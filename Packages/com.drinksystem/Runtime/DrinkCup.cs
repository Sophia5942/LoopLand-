using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace DrinkSystem {
	/// <summary>
	/// A cup (LiquidGlass + TransformSync in one). It remembers the options it was poured with (froth, strength,
	/// spirit, fruit garnish, straw) and its customizer style (glass tint, logo). It locks into a machine slot when you drop it there, fills while
	/// its slot pours, you drink it with Use, and it respawns when it is lost or left behind.
	///
	/// Networking (manual sync, the owner writes, nobody sends anything while nothing changes):
	///  - Locked in a slot: no position is sent at all. Every client puts it on the slot.
	///  - Held: the offset to the holder's hand bone is sent once (re-sent only if it changes). Everyone else
	///    attaches it to that hand locally, so it's smooth without streaming positions.
	///  - Dropped: positions are sent a few times per second while it falls, then once more when it settles.
	///  - Pouring / drinking: only the start and the stop are sent. Every client simulates the fill in between,
	///    and the final value fixes any drift.
	///  - The drink mix is one byte per flavor.
	/// </summary>
	[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
	public class DrinkCup : UdonSharpBehaviour {
		// References
		[Tooltip("The liquid mesh inside the cup (child). Its pivot must be on the cup's axis.")]
		public Renderer LiquidRenderer;
		[Tooltip("Optional respawn spot. Empty = where the cup starts.")]
		public Transform SpawnPoint;
		public AudioSource OneShotSource;
		public AudioSource LoopSource;
		[Tooltip("The glass itself (tint and logo are set on it).")]
		public Renderer BodyRenderer;
		public Renderer StrawRenderer;
		public Renderer GarnishRenderer;
		// Synced state
		[HideInInspector(), UdonSynced] public float FillAmount;
		[HideInInspector(), UdonSynced] public sbyte SlotIndex = -1;
		[HideInInspector(), UdonSynced] public bool Pouring;
		[HideInInspector(), UdonSynced] public bool Drinking;
		[HideInInspector(), UdonSynced] public byte HeldHand; // 0 = not held, 1 = left, 2 = right
		[HideInInspector(), UdonSynced] public byte Froth = 1;
		[HideInInspector(), UdonSynced] public byte Strength = 1;
		[HideInInspector(), UdonSynced] public byte Spirit;
		[HideInInspector(), UdonSynced] public sbyte Fruit = -1;
		[HideInInspector(), UdonSynced] public sbyte Straw = -1;
		[HideInInspector(), UdonSynced] public byte Tint;
		[HideInInspector(), UdonSynced] public byte Logo;
		[UdonSynced] private byte RespawnCount;
		[UdonSynced] private byte[] Mix = new byte[0];
		[UdonSynced] private Vector3 SyncPos;                  // world pose, or the hand offset while held
		[UdonSynced] private Quaternion SyncRot = Quaternion.identity;
		// Read by the machine / slots
		[HideInInspector()] public int CupId;
		[HideInInspector()] public float VisualFill;
		[HideInInspector()] public float SurfaceY;
		[HideInInspector()] public float LastActive;
		// Internal: setup
		private DrinkMachine MB;
		private VRC_Pickup Pickup;
		private Rigidbody RB;
		private Transform LiquidTransform;
		private MaterialPropertyBlock Props;
		private int IdFillHeight;
		private int IdGlassRemap;
		private int IdColorMain;
		private int IdFoamColor;
		private int IdEdgeColor;
		private int IdTransparency;
		private int IdFoamHeight;
		private int IdFoamSmoothness;
		private int IdEdgeStrength;
		private int IdEdgePower;
		private int IdWobbleX;
		private int IdWobbleZ;
		private int IdColor;
		private int IdLogoColor;
		private MaterialPropertyBlock BodyProps;
		private int AppliedLooks = -1;
		private Vector3 LocalBottom;
		private Vector3 LocalTop;
		private float RadiusBottom;
		private float RadiusTop;
		private Vector3 HomePos;
		private Quaternion HomeRot;
		private bool Ready;
		private bool IsOwner;
		private bool Received;
		private bool HadState;
		// Internal: drink
		private float[] Amounts;
		private bool MixDirty;
		private float Effect1;
		private float Effect2;
		private float Effect3;
		private int PourFlavor;
		private float PourRate;
		private bool WantDrink;
		private float LastVisual = -1f;
		// Internal: applied state (what this client has already shown)
		private int AppliedSlot = -1;
		private bool AppliedPouring;
		private bool AppliedDrinking;
		private byte AppliedHand;
		private byte AppliedRespawn;
		// Internal: motion
		private VRCPlayerApi Holder;
		private VRC_Pickup.PickupHand LastHand;
		private Vector3 HandPos;
		private Quaternion HandRot;
		private Vector3 PrevPos;
		private Vector3 ThrowVel;
		private bool Snapping;
		private float SnapT;
		private Vector3 SnapFromPos;
		private Vector3 SnapToPos;
		private Quaternion SnapFromRot;
		private Quaternion SnapToRot;
		private bool LockHaptic;
		private bool PendingPhysics;
		private bool PhysicsOn;
		private float PhysicsStart;
		private float RestTime;
		private float NextSend;
		private Vector3 LastSentPos;
		private Quaternion LastSentRot;
		private bool Interpolating;
		private int Highlighted = -1;
		private float IdleDue;
		// Internal: wobble
		private float WobX;
		private float WobZ;
		private float WobVelX;
		private float WobVelZ;
		private Vector3 WobPrevPos;
		private Vector3 WobPrevVel;
		private bool WobWasMoving;
		private bool Wobbling;

		public DrinkCup Init(DrinkMachine mb, int id) {
			MB = mb;
			CupId = id;
			Pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
			RB = GetComponent<Rigidbody>();
			LiquidTransform = LiquidRenderer.transform;
			Props = new MaterialPropertyBlock();
			IdFillHeight = VRCShader.PropertyToID("_FillHeight");
			IdGlassRemap = VRCShader.PropertyToID("_GlassRemap");
			IdColorMain = VRCShader.PropertyToID("_ColorMain");
			IdFoamColor = VRCShader.PropertyToID("_FoamColor");
			IdEdgeColor = VRCShader.PropertyToID("_EdgeColor");
			IdTransparency = VRCShader.PropertyToID("_Transparency");
			IdFoamHeight = VRCShader.PropertyToID("_FoamHeight");
			IdFoamSmoothness = VRCShader.PropertyToID("_FoamLineSmoothness");
			IdEdgeStrength = VRCShader.PropertyToID("_EdgeStrength");
			IdEdgePower = VRCShader.PropertyToID("_EdgePower");
			IdWobbleX = VRCShader.PropertyToID("_WobbleX");
			IdWobbleZ = VRCShader.PropertyToID("_WobbleZ");
			IdColor = VRCShader.PropertyToID("_Color");
			IdLogoColor = VRCShader.PropertyToID("_LogoColor");
			BodyProps = new MaterialPropertyBlock();
			MeasureLiquid();
			Transform home = SpawnPoint != null ? SpawnPoint : transform;
			HomePos = home.position;
			HomeRot = home.rotation;
			Amounts = new float[MB.FlavorCount];
			IsOwner = Networking.IsOwner(gameObject);
			if(IsOwner && !Received) {
				SyncPos = HomePos;
				SyncRot = HomeRot;
				Mix = new byte[MB.FlavorCount];
			}
			RB.isKinematic = true;
			LastActive = Time.time;
			Ready = true;
			if(Received) OnDeserialization();
			else {
				MixDirty = true;
				RefreshLiquid(false, 0f);
				ApplyState(false);
			}
			return this;
		}
		private void Update() {
			if(!Ready) return;
			float dt = Time.deltaTime;
			// Pouring (simulated on every client, the owner decides when it stops)
			if(AppliedPouring) {
				float add = Mathf.Min(PourRate * dt, 1f - FillAmount);
				if(add > 0f) {
					FillAmount += add;
					Amounts[PourFlavor] += add;
					MixDirty = true;
				}
				if(IsOwner && FillAmount >= 0.999f) {
					FillAmount = 1f;
					Pouring = false;
					Commit();
				}
			}
			// Drinking (same idea)
			if(AppliedDrinking && FillAmount > 0f) {
				float amountDrinked = Mathf.Min(MB.DrinkingSpeed * dt, FillAmount);
				float keep = (FillAmount - amountDrinked) / FillAmount;
				int n = Amounts.Length;
				for(int i = 0; i < n; i++) Amounts[i] *= keep;
				FillAmount -= amountDrinked;
				if(IsOwner) {
					MB.AddIntoxication(Effect1, Effect2, Effect3, amountDrinked);
					if(FillAmount <= 0.0005f) {
						FillAmount = 0f;
						Drinking = false;
						Commit();
					}
				}
			}
			if(VisualFill != FillAmount) {
				VisualFill = IsOwner ? FillAmount : Mathf.MoveTowards(VisualFill, FillAmount, 1.5f * dt);
			}
			// Motion
			if(Snapping) StepSnap(dt);
			else if(AppliedHand != 0) {
				if(IsOwner) OwnerHeldTick(dt);
				else FollowHolder();
			}
			else if(IsOwner) {
				if(PhysicsOn) PhysicsTick(dt);
			}
			else if(Interpolating) Interpolate(dt);
			// Visuals: only when something changed
			bool moving = Snapping || AppliedHand != 0 || PhysicsOn || Interpolating;
			if(moving || MixDirty || Wobbling || VisualFill != LastVisual) RefreshLiquid(moving, dt);
			if(AppliedPouring && AppliedSlot >= 0) MB.Slots[AppliedSlot]._PourTick(SurfaceY, VisualFill);
		}
		// Pickup events (only on the player holding it)
		public override void OnPickup() {
			TakeOwnership();
			PendingPhysics = false;
			PhysicsOn = false;
			Snapping = false;
			Interpolating = false;
			WantDrink = false;
			LastHand = Pickup.currentHand;
			SlotIndex = -1;
			Pouring = false;
			Drinking = false;
			HeldHand = LastHand == VRC_Pickup.PickupHand.Left ? (byte)1 : (byte)2;
			PrevPos = transform.position;
			ThrowVel = Vector3.zero;
			CaptureHoldOffset(true);
			NextSend = Time.time + 1f;
			Commit();
			// The pickup settles into the hand over the next frames: send the final offset once it has
			SendCustomEventDelayedFrames(nameof(_RecaptureHold), 3);
			if(MB.KeepCupReady) MB._EnsureCupReady(this);
		}
		public override void OnDrop() {
			if(!IsOwner) return;
			WantDrink = false;
			if(Highlighted >= 0) { MB.SetHighlight(Highlighted, false); Highlighted = -1; }
			HeldHand = 0;
			Drinking = false;
			int slot = MB.FindSnapSlot(this);
			if(slot >= 0) {
				SlotIndex = (sbyte)slot;
				LockHaptic = true;
				Commit();
			}
			else {
				SyncPos = transform.position;
				SyncRot = transform.rotation;
				PendingPhysics = true;
				Commit();
				SendCustomEventDelayedFrames(nameof(_BeginPhysics), 1);
			}
			ScheduleIdle();
		}
		public override void OnPickupUseDown() { WantDrink = true; }
		public override void OnPickupUseUp() { WantDrink = false; }
		// Networking
		public override void OnPreSerialization() {
			if(Amounts == null) return;
			int n = Amounts.Length;
			if(Mix == null || Mix.Length != n) Mix = new byte[n];
			float total = 0f;
			for(int i = 0; i < n; i++) total += Amounts[i];
			for(int i = 0; i < n; i++) Mix[i] = (byte)(total > 0f ? Mathf.RoundToInt(Mathf.Clamp01(Amounts[i] / total) * 255f) : 0);
		}
		public override void OnDeserialization() {
			Received = true;
			if(!Ready) return;
			// Rebuild the flavor amounts from the synced mix
			int n = Amounts.Length;
			int m = Mix != null ? Mix.Length : 0;
			float total = 0f;
			for(int i = 0; i < m && i < n; i++) total += Mix[i];
			for(int i = 0; i < n; i++) Amounts[i] = (i < m && total > 0f) ? Mix[i] / total * FillAmount : 0f;
			MixDirty = true;
			bool effects = HadState;
			if(!HadState) AppliedRespawn = RespawnCount; // late join: don't replay an old respawn
			ApplyState(effects);
			if(SlotIndex < 0 && HeldHand == 0 && ValidPose()) {
				if(effects) Interpolating = true;
				else transform.SetPositionAndRotation(SyncPos, SyncRot);
			}
			if(!effects) VisualFill = FillAmount;
			HadState = true;
		}
		public override void OnOwnershipTransferred(VRCPlayerApi player) {
			IsOwner = Utilities.IsValid(player) && player.isLocal;
			if(!Ready) return;
			if(!IsOwner) {
				PendingPhysics = false;
				WantDrink = false;
				if(PhysicsOn) { PhysicsOn = false; RB.isKinematic = true; }
				if(Highlighted >= 0) { MB.SetHighlight(Highlighted, false); Highlighted = -1; }
				if(AppliedHand != 0) Holder = player;
				return;
			}
			Pickup.pickupable = true;
			// We inherited a cup whose holder left the world: drop it where it is
			if(AppliedHand != 0 && !Pickup.IsHeld) {
				HeldHand = 0;
				Drinking = false;
				SyncPos = transform.position;
				SyncRot = transform.rotation;
				PendingPhysics = true;
				Commit();
				SendCustomEventDelayedFrames(nameof(_BeginPhysics), 1);
			}
			ScheduleIdle();
		}
		// Interfaces
		public void _TogglePour() {
			if(SlotIndex < 0) return;
			TakeOwnership();
			if(Pouring) Pouring = false;
			else {
				if(FillAmount >= 0.999f) return;
				Pouring = true;
				// The cup takes the options picked on the panel right now
				Froth = MB.Froth;
				Strength = MB.Strength;
				Spirit = MB.Spirit;
				Fruit = MB.Fruit;
				Straw = MB.Straw;
			}
			Commit();
		}
		public void ForceRespawn() {
			if(HeldHand != 0) return;
			if(IsAtHome() && FillAmount <= 0f) return;
			TakeOwnership();
			_Respawn();
		}
		public void _SetStyle(int tint, int logo) {
			TakeOwnership();
			Tint = (byte)Mathf.Clamp(tint, 0, 255);
			Logo = (byte)Mathf.Clamp(logo, 0, 255);
			Commit();
			PlayOneShot(MB.ClipCupLock);
		}
		public bool IsAtHome() {
			return SlotIndex < 0 && HeldHand == 0 && !PhysicsOn && (transform.position - HomePos).sqrMagnitude < 0.0004f;
		}
		public void _Yield() {
			if(IsOwner) SendCustomEventDelayedFrames(nameof(_Eject), 1);
		}
		// Delayed events
		public void _Eject() {
			if(!IsOwner || SlotIndex < 0 || MB.GetSlotCup(SlotIndex) == this) return;
			SlotIndex = -1;
			Pouring = false;
			Snapping = false;
			transform.position += Vector3.up * 0.05f;
			SyncPos = transform.position;
			SyncRot = transform.rotation;
			PendingPhysics = true;
			Commit();
			SendCustomEventDelayedFrames(nameof(_BeginPhysics), 1);
		}
		public void _RecaptureHold() {
			if(IsOwner && AppliedHand != 0) CaptureHoldOffset(false);
		}
		public void _BeginPhysics() {
			if(!PendingPhysics || !IsOwner || AppliedHand != 0 || AppliedSlot >= 0 || Snapping) return;
			PendingPhysics = false;
			RB.isKinematic = false;
			RB.velocity = ThrowVel;
			RB.WakeUp();
			PhysicsOn = true;
			PhysicsStart = Time.time;
			RestTime = 0f;
			NextSend = Time.time + MB.MoveSendInterval;
			LastSentPos = transform.position;
			LastSentRot = transform.rotation;
		}
		public void _IdleCheck() {
			if(!IsOwner || Time.time < IdleDue) return;
			if(HeldHand != 0 || Pouring || Snapping || PhysicsOn || IsAtHome()) return;
			_Respawn();
		}
		public void _Respawn() {
			PendingPhysics = false;
			PhysicsOn = false;
			Snapping = false;
			Interpolating = false;
			WantDrink = false;
			RB.isKinematic = true;
			SlotIndex = -1;
			Pouring = false;
			Drinking = false;
			HeldHand = 0;
			FillAmount = 0f;
			Froth = 1;
			Strength = 1;
			Spirit = 0;
			Fruit = -1;
			Straw = -1;
			Tint = 0;
			Logo = 0;
			int n = Amounts.Length;
			for(int i = 0; i < n; i++) Amounts[i] = 0f;
			MixDirty = true;
			RespawnCount = (byte)((RespawnCount + 1) & 255);
			SyncPos = HomePos;
			SyncRot = HomeRot;
			Commit();
		}
		// Internal: state
		private void Commit() {
			RequestSerialization();
			ApplyState(true);
		}
		/// <summary>Shows the current synced state on this client (same path for the owner and everyone else).</summary>
		private void ApplyState(bool effects) {
			LastActive = Time.time;
			bool respawned = RespawnCount != AppliedRespawn;
			AppliedRespawn = RespawnCount;
			// Pour stopped (or the cup left the slot)
			if(AppliedPouring && (!Pouring || SlotIndex != AppliedSlot)) {
				AppliedPouring = false;
				if(AppliedSlot >= 0) MB.Slots[AppliedSlot]._StopPour(effects && !respawned, FillAmount >= 0.999f);
				if(effects && !respawned && FillAmount > 0f) PlayOneShot(MB.ClipFizz);
			}
			// Locked / unlocked
			if(SlotIndex != AppliedSlot) {
				int old = AppliedSlot;
				AppliedSlot = SlotIndex;
				if(old >= 0) MB._ClearSlotCup(old, this);
				if(AppliedSlot >= 0) {
					MB._SetSlotCup(AppliedSlot, this);
					BeginSnap(effects);
				}
				else {
					Snapping = false;
					if(effects && !respawned && old >= 0) PlayOneShot(MB.ClipCupUnlock);
				}
			}
			// Pour started
			if(Pouring && !AppliedPouring && AppliedSlot >= 0) {
				AppliedPouring = true;
				DrinkSlot slot = MB.Slots[AppliedSlot];
				PourFlavor = slot.GetPourFlavor(Fruit);
				PourRate = MB.FFillSpeed[PourFlavor] * MB.FillSpeedMultiplier;
				slot._StartPour(effects, PourFlavor);
			}
			// Held / released
			if(HeldHand != AppliedHand) {
				AppliedHand = HeldHand;
				Holder = HeldHand != 0 ? Networking.GetOwner(gameObject) : null;
				if(!IsOwner) Pickup.pickupable = HeldHand == 0;
			}
			// Drinking sound
			if(Drinking != AppliedDrinking) {
				AppliedDrinking = Drinking;
				if(LoopSource != null) {
					if(Drinking && MB.ClipSlurpLoop != null) {
						LoopSource.clip = MB.ClipSlurpLoop;
						LoopSource.loop = true;
						LoopSource.Play();
					}
					else LoopSource.Stop();
				}
			}
			if(respawned) {
				transform.SetPositionAndRotation(HomePos, HomeRot);
				Interpolating = false;
				VisualFill = FillAmount;
				WobX = 0f; WobZ = 0f; WobVelX = 0f; WobVelZ = 0f;
				LastVisual = -1f;
				if(effects) PlayOneShot(MB.ClipCupRespawn);
			}
			int looks = Froth | (Strength << 2) | (Spirit << 4) | ((Fruit + 1) << 6) | ((Straw + 1) << 11) | (Tint << 16) | (Logo << 24);
			if(looks != AppliedLooks) {
				AppliedLooks = looks;
				ApplyLooks();
			}
			if(AppliedSlot >= 0) MB.Slots[AppliedSlot].RefreshUI();
		}
		private void ApplyLooks() {
			if(StrawRenderer != null) {
				bool on = Straw >= 0 && MB.StrawMaterials != null && Straw < MB.StrawMaterials.Length;
				if(on) StrawRenderer.sharedMaterial = MB.StrawMaterials[Straw];
				StrawRenderer.enabled = on;
			}
			if(GarnishRenderer != null) {
				bool on = Fruit >= 0 && MB.GarnishMaterials != null && Fruit < MB.GarnishMaterials.Length;
				if(on) GarnishRenderer.sharedMaterial = MB.GarnishMaterials[Fruit];
				GarnishRenderer.enabled = on;
			}
			if(BodyRenderer != null) {
				if(MB.CupTints != null && Tint < MB.CupTints.Length) BodyProps.SetColor(IdColor, MB.CupTints[Tint]);
				if(MB.LogoColors != null && Logo < MB.LogoColors.Length) BodyProps.SetColor(IdLogoColor, MB.LogoColors[Logo]);
				BodyRenderer.SetPropertyBlock(BodyProps);
			}
			MixDirty = true;
		}
		private void TakeOwnership() {
			if(IsOwner) return;
			Networking.SetOwner(Networking.LocalPlayer, gameObject);
			IsOwner = true;
		}
		private void ScheduleIdle() {
			if(!IsOwner) return;
			float delay = MB.RespawnIdleSeconds;
			if(FillAmount <= 0.0005f && SlotIndex < 0 && MB.RespawnEmptySeconds > 0f) delay = MB.RespawnEmptySeconds;
			if(delay <= 0f) return;
			IdleDue = Time.time + delay;
			SendCustomEventDelayedSeconds(nameof(_IdleCheck), delay + 0.1f);
		}
		private bool ValidPose() {
			return SyncRot.x != 0f || SyncRot.y != 0f || SyncRot.z != 0f || SyncRot.w != 0f;
		}
		// Internal: motion
		private void BeginSnap(bool animate) {
			PendingPhysics = false;
			PhysicsOn = false;
			Interpolating = false;
			RB.isKinematic = true;
			Transform snap = MB.Slots[AppliedSlot].SnapPoint;
			SnapToPos = snap.position;
			SnapToRot = snap.rotation;
			if(!animate) {
				Snapping = false;
				transform.SetPositionAndRotation(SnapToPos, SnapToRot);
				return;
			}
			SnapFromPos = transform.position;
			SnapFromRot = transform.rotation;
			SnapT = 0f;
			Snapping = true;
		}
		private void StepSnap(float dt) {
			SnapT += dt / Mathf.Max(0.01f, MB.SnapTime);
			Vector3 lift = Vector3.up * 0.01f;
			if(SnapT < 1f) {
				// Glide over the holder...
				float e = 1f - (1f - SnapT) * (1f - SnapT) * (1f - SnapT);
				transform.SetPositionAndRotation(Vector3.Lerp(SnapFromPos, SnapToPos + lift, e), Quaternion.Slerp(SnapFromRot, SnapToRot, e));
			}
			else if(SnapT < 1.5f) {
				// ...then drop into it
				float e = (SnapT - 1f) * 2f;
				transform.SetPositionAndRotation(SnapToPos + lift * (1f - e * e), SnapToRot);
			}
			else {
				transform.SetPositionAndRotation(SnapToPos, SnapToRot);
				Snapping = false;
				PlayOneShot(MB.ClipCupLock);
				if(LockHaptic) {
					LockHaptic = false;
					if(MB.IsVR) Networking.LocalPlayer.PlayHapticEventInHand(LastHand, 0.08f, 0.7f, 120f);
				}
			}
		}
		private void OwnerHeldTick(float dt) {
			Vector3 p = transform.position;
			if(dt > 0f) ThrowVel = Vector3.Lerp(ThrowVel, (p - PrevPos) / dt, 0.5f);
			PrevPos = p;
			// Drink while Use is held and the cup is at your mouth
			bool drink = WantDrink && FillAmount > 0f && NearMouth();
			if(drink != Drinking) {
				Drinking = drink;
				Commit();
			}
			// Show where the cup will lock
			int slot = MB.FindSnapSlot(this);
			if(slot != Highlighted) {
				MB.SetHighlight(Highlighted, false);
				MB.SetHighlight(slot, true);
				Highlighted = slot;
			}
			if(Time.time >= NextSend) {
				NextSend = Time.time + 1f;
				CaptureHoldOffset(false);
			}
		}
		private void CaptureHoldOffset(bool force) {
			if(!GetHandPose(Networking.LocalPlayer, HeldHand)) return;
			Quaternion inv = Quaternion.Inverse(HandRot);
			Vector3 offset = inv * (transform.position - HandPos);
			Quaternion rotOffset = inv * transform.rotation;
			if(force || (offset - SyncPos).sqrMagnitude > 0.0001f || Quaternion.Angle(rotOffset, SyncRot) > 3f) {
				SyncPos = offset;
				SyncRot = rotOffset;
				if(!force) RequestSerialization();
			}
		}
		private void FollowHolder() {
			if(!Utilities.IsValid(Holder)) return;
			if(!GetHandPose(Holder, AppliedHand)) return;
			transform.SetPositionAndRotation(HandPos + HandRot * SyncPos, HandRot * SyncRot);
		}
		private bool GetHandPose(VRCPlayerApi player, byte hand) {
			if(!Utilities.IsValid(player) || hand == 0) return false;
			HumanBodyBones bone = hand == 1 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
			HandPos = player.GetBonePosition(bone);
			if(HandPos != Vector3.zero) {
				HandRot = player.GetBoneRotation(bone);
				return true;
			}
			// Avatar without hand bones: use the tracked hand
			VRCPlayerApi.TrackingData td = player.GetTrackingData(hand == 1 ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand);
			HandPos = td.position;
			HandRot = td.rotation;
			return HandPos != Vector3.zero;
		}
		private void PhysicsTick(float dt) {
			Vector3 p = transform.position;
			if(p.y < HomePos.y - MB.FallRespawnDistance) { _Respawn(); return; }
			bool still = RB.velocity.sqrMagnitude < 0.0025f && RB.angularVelocity.sqrMagnitude < 0.05f;
			RestTime = still ? RestTime + dt : 0f;
			if(RestTime > 0.3f || Time.time - PhysicsStart > 8f) {
				// Settled: freeze it and send the final pose
				RB.isKinematic = true;
				PhysicsOn = false;
				SendPose();
				ScheduleIdle();
				LastActive = Time.time;
				return;
			}
			if(Time.time >= NextSend) {
				NextSend = Time.time + MB.MoveSendInterval;
				// Same thresholds idea as TransformSync
				if((LastSentPos - p).sqrMagnitude > 0.0004f || Quaternion.Angle(LastSentRot, transform.rotation) > 2f) SendPose();
			}
		}
		private void SendPose() {
			SyncPos = transform.position;
			SyncRot = transform.rotation;
			LastSentPos = SyncPos;
			LastSentRot = SyncRot;
			RequestSerialization();
		}
		private void Interpolate(float dt) {
			float k = 1f - Mathf.Exp(-14f * dt);
			Vector3 p = Vector3.Lerp(transform.position, SyncPos, k);
			if((p - SyncPos).sqrMagnitude < 0.000001f) {
				transform.SetPositionAndRotation(SyncPos, SyncRot);
				Interpolating = false;
				return;
			}
			transform.SetPositionAndRotation(p, Quaternion.Slerp(transform.rotation, SyncRot, k));
		}
		private bool NearMouth() {
			if(!MB.IsVR) return true;
			Vector3 head = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
			Vector3 rim = LiquidTransform.TransformPoint(LocalTop);
			return (rim - head).sqrMagnitude < MB.MouthDistance * MB.MouthDistance;
		}
		// Internal: liquid visuals
		private void MeasureLiquid() {
			// Done once: the liquid is treated as a (tapered) cylinder, so the fill line costs a few math ops per frame
			// instead of transforming every vertex every frame.
			Mesh mesh = LiquidTransform.GetComponent<MeshFilter>().sharedMesh;
			Bounds b = mesh.bounds;
			Vector3[] vertices = mesh.vertices;
			float minY = b.min.y;
			float h = Mathf.Max(0.0001f, b.size.y);
			float cx = b.center.x;
			float cz = b.center.z;
			// Mesh without Read/Write enabled (vertices unavailable in builds): use its bounds
			RadiusBottom = vertices.Length == 0 ? Mathf.Max(b.extents.x, b.extents.z) : 0f;
			RadiusTop = RadiusBottom;
			foreach(Vector3 v in vertices) {
				float r = Mathf.Sqrt((v.x - cx) * (v.x - cx) + (v.z - cz) * (v.z - cz));
				float t = (v.y - minY) / h;
				if(t < 0.15f && r > RadiusBottom) RadiusBottom = r;
				if(t > 0.85f && r > RadiusTop) RadiusTop = r;
			}
			LocalBottom = new Vector3(cx, b.min.y, cz);
			LocalTop = new Vector3(cx, b.max.y, cz);
		}
		private void RefreshLiquid(bool moving, float dt) {
			LastVisual = VisualFill;
			if(MixDirty) RecomputeMix();
			bool show = VisualFill > 0.001f;
			if(LiquidRenderer.enabled != show) LiquidRenderer.enabled = show;
			if(!show) {
				Wobbling = false;
				WobWasMoving = false;
				return;
			}
			if(MB.WobbleAmount > 0f) UpdateWobble(moving, dt);
			// Same fill line as LiquidGlass.UpdateFillHeight, from the cylinder instead of every vertex
			Vector3 bottom = LiquidTransform.TransformPoint(LocalBottom);
			Vector3 top = LiquidTransform.TransformPoint(LocalTop);
			Vector3 axis = top - bottom;
			float len = axis.magnitude;
			float ay = len > 0f ? axis.y / len : 1f;
			float s = Mathf.Sqrt(Mathf.Max(0f, 1f - ay * ay)) * LiquidTransform.lossyScale.x;
			float rb = RadiusBottom * s;
			float rt = RadiusTop * s;
			float minY = Mathf.Min(bottom.y - rb, top.y - rt);
			float maxY = Mathf.Max(bottom.y + rb, top.y + rt);
			SurfaceY = minY + VisualFill * (maxY - minY - MB.BrimFullOffset);
			Props.SetVector(IdGlassRemap, new Vector4(minY, maxY, 0f, 0f));
			Props.SetFloat(IdFillHeight, SurfaceY);
			LiquidRenderer.SetPropertyBlock(Props);
		}
		private void RecomputeMix() {
			MixDirty = false;
			int n = Amounts.Length;
			float total = 0f;
			for(int i = 0; i < n; i++) total += Amounts[i];
			if(total <= 0f) return;
			Color[] fMain = MB.FColorMain;
			Color[] fFoam = MB.FColorFoam;
			Color[] fEdge = MB.FColorEdge;
			float[] fTransparency = MB.FTransparency;
			float[] fFoamHeight = MB.FFoamHeight;
			float[] fFoamSmoothness = MB.FFoamSmoothness;
			float[] fEdgeStrength = MB.FEdgeStrength;
			float[] fEdgePower = MB.FEdgePower;
			float[] fE1 = MB.FEffect1;
			float[] fE2 = MB.FEffect2;
			float[] fE3 = MB.FEffect3;
			// Weighted average by amount (the same result as LiquidGlass' running lerp)
			Color main = Color.black;
			Color foam = Color.black;
			Color edge = Color.black;
			float transparency = 0f;
			float foamHeight = 0f;
			float foamSmoothness = 0f;
			float edgeStrength = 0f;
			float edgePower = 0f;
			float e1 = 0f;
			float e2 = 0f;
			float e3 = 0f;
			for(int i = 0; i < n; i++) {
				float w = Amounts[i] / total;
				if(w <= 0f) continue;
				main += fMain[i] * w;
				foam += fFoam[i] * w;
				edge += fEdge[i] * w;
				transparency += fTransparency[i] * w;
				foamHeight += fFoamHeight[i] * w;
				foamSmoothness += fFoamSmoothness[i] * w;
				edgeStrength += fEdgeStrength[i] * w;
				edgePower += fEdgePower[i] * w;
				e1 += fE1[i] * w;
				e2 += fE2[i] * w;
				e3 += fE3[i] * w;
			}
			// Options the cup was poured with
			float[] frothLevels = MB.FrothLevels;
			if(frothLevels != null && Froth < frothLevels.Length) foamHeight *= frothLevels[Froth];
			if(Strength == 0) {
				main = Color.Lerp(main, Color.white, 0.25f);
				transparency = Mathf.Lerp(transparency, 1f, 0.3f);
			}
			else if(Strength == 2) {
				main *= 0.72f;
				transparency *= 0.5f;
			}
			if(Fruit >= 0) {
				int fruitFlavor = Mathf.Clamp(MB.FruitFlavorStart + Fruit, 0, n - 1);
				main = Color.Lerp(main, fMain[fruitFlavor], 0.12f);
			}
			float[] spiritLevels = MB.SpiritLevels;
			if(spiritLevels != null && Spirit < spiritLevels.Length) {
				float spirit = spiritLevels[Spirit];
				e1 = Mathf.Clamp01(e1 + spirit);
				e2 = Mathf.Clamp01(e2 + spirit * 0.6f);
				e3 = Mathf.Clamp01(e3 + spirit * 0.4f);
			}
			main.a = 1f;
			foam.a = 1f;
			edge.a = 1f;
			Props.SetColor(IdColorMain, main);
			Props.SetColor(IdFoamColor, foam);
			Props.SetColor(IdEdgeColor, edge);
			Props.SetFloat(IdTransparency, transparency);
			Props.SetFloat(IdFoamHeight, foamHeight);
			Props.SetFloat(IdFoamSmoothness, foamSmoothness);
			Props.SetFloat(IdEdgeStrength, edgeStrength);
			Props.SetFloat(IdEdgePower, edgePower);
			Effect1 = e1;
			Effect2 = e2;
			Effect3 = e3;
		}
		private void UpdateWobble(bool moving, float dt) {
			Vector3 p = LiquidTransform.position;
			if(moving && dt > 0f) {
				Vector3 vel = (p - WobPrevPos) / dt;
				if(WobWasMoving) {
					// The liquid lags behind when the cup speeds up or stops
					Vector3 dv = vel - WobPrevVel;
					WobVelX -= Mathf.Clamp(dv.x, -2f, 2f) * MB.WobbleAmount * 0.5f;
					WobVelZ -= Mathf.Clamp(dv.z, -2f, 2f) * MB.WobbleAmount * 0.5f;
				}
				WobPrevVel = vel;
			}
			WobWasMoving = moving;
			WobPrevPos = p;
			// Damped spring back to flat
			WobVelX -= WobX * 90f * dt;
			WobVelZ -= WobZ * 90f * dt;
			float damp = Mathf.Clamp01(1f - 5f * dt);
			WobVelX *= damp;
			WobVelZ *= damp;
			WobX = Mathf.Clamp(WobX + WobVelX * dt, -0.5f, 0.5f);
			WobZ = Mathf.Clamp(WobZ + WobVelZ * dt, -0.5f, 0.5f);
			Wobbling = Mathf.Abs(WobX) + Mathf.Abs(WobZ) + Mathf.Abs(WobVelX) + Mathf.Abs(WobVelZ) > 0.002f;
			if(!Wobbling) { WobX = 0f; WobZ = 0f; WobVelX = 0f; WobVelZ = 0f; }
			Props.SetFloat(IdWobbleX, WobX);
			Props.SetFloat(IdWobbleZ, WobZ);
		}
		private void PlayOneShot(AudioClip clip) {
			if(clip != null && OneShotSource != null) OneShotSource.PlayOneShot(clip);
		}
	}
}
