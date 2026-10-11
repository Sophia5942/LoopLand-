using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace DrinkSystem {
	/// <summary>
	/// The drink machine manager (like ManagerBar). Finds the flavors, slots and cups under their parents, keeps the
	/// local "which cup is locked in which slot" table, handles VR finger presses on the UI buttons, cup respawning,
	/// the pour options (froth / strength), the finishing pad (straw, fruit, glass style) and the drunk screen effect.
	/// Synced: only the 2 option bytes (sent when someone taps an option). Every glass syncs its own small state.
	/// Don't move the machine at runtime (slot positions are cached).
	/// </summary>
	[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
	public class DrinkMachine : UdonSharpBehaviour {
		[Header("Parents (filled from their children)")]
		public GameObject FlavorsParent;
		public GameObject SlotsParent;
		public GameObject CupsParent;

		[Header("Cups")]
		[Range(0.0f, 0.01f)] public float BrimFullOffset = 0.004f;
		[Tooltip("How close (meters) a dropped cup must be to a slot to lock into it.")]
		public float SnapRadius = 0.11f;
		[Tooltip("Seconds the lock-in animation takes.")]
		public float SnapTime = 0.12f;
		[Range(0.1f, 3.0f)] public float FillSpeedMultiplier = 1f;
		[Range(0.0f, 1.0f)] public float DrinkingSpeed = 0.2f;
		[Tooltip("VR: the cup's rim must be this close (meters) to your head to drink. Desktop always drinks.")]
		public float MouthDistance = 0.3f;
		[Range(0.0f, 10.0f)] public float WobbleAmount = 3f;
		[Tooltip("While a dropped cup is still moving, its position is sent at most this often (seconds).")]
		public float MoveSendInterval = 0.2f;

		[Header("Cup respawn")]
		[Tooltip("A cup left alone away from its spawn is respawned after this many seconds (0 = never).")]
		public float RespawnIdleSeconds = 300f;
		[Tooltip("An EMPTY cup left alone (not in a slot) is respawned after this many seconds (0 = use the idle time).")]
		public float RespawnEmptySeconds = 20f;
		[Tooltip("A cup that falls this far below its spawn is respawned right away.")]
		public float FallRespawnDistance = 3f;
		[Tooltip("When someone takes the last cup from the stack, recycle the oldest abandoned cup back onto it.")]
		public bool KeepCupReady = true;
		[Tooltip("A cup must have been left alone this long before it can be recycled.")]
		public float RecycleMinIdleSeconds = 30f;

		[Header("VR finger presses")]
		public bool EnableFingerPoke = true;
		[Tooltip("Finger presses are only checked when you are this close to the machine.")]
		public float PokeRange = 2.5f;
		[Tooltip("How thick (meters) the press zone in front of a button is.")]
		public float PokeDepth = 0.025f;

		[Header("Sounds")]
		public AudioClip ClipButtonClick;
		public AudioClip ClipButtonDeny;
		public AudioClip ClipCupLock;
		public AudioClip ClipCupUnlock;
		public AudioClip ClipPourStart;
		public AudioClip ClipPourLoop;
		public AudioClip ClipPourStop;
		public AudioClip ClipPourFull;
		public AudioClip ClipFizz;
		public AudioClip ClipSlurpLoop;
		public AudioClip ClipCupRespawn;

		[Header("Step 3: pour options (synced, picked on the drink panel)")]
		[UdonSynced] public byte Froth = 1;
		[UdonSynced] public byte Strength = 1;
		[Tooltip("Foam height multiplier for LOW / MEDIUM / HIGH froth.")]
		public float[] FrothLevels = new float[] { 0.35f, 1f, 2.4f };
		[Tooltip("How hard the drink hits (drunk post-processing intensity) for LOW / MEDIUM / STRONG.")]
		public float[] StrengthLevels = new float[] { 0.15f, 0.45f, 0.9f };
		public Image[] FrothButtons;
		public Image[] StrengthButtons;
		public Color OptionOn = new Color(0.79f, 0.59f, 0.18f, 1f);
		public Color OptionOff = new Color(0.12f, 0.11f, 0.15f, 1f);

		[Header("Step 4: finishing pad (straw, fruit, glass style)")]
		public DrinkSlot CustomizerSlot;
		[Tooltip("Index of the first fruit flavor under FlavorsParent (used for a slight fruit tint).")]
		public int FruitFlavorStart = 6;
		[Tooltip("Straw look per STRAW option.")]
		public Material[] StrawMaterials;
		[Tooltip("Rim garnish per FRUIT option.")]
		public Material[] GarnishMaterials;
		[Tooltip("Glass tint per GLASS option. The first one means: leave the glass material as it is.")]
		public Color[] CupTints;
		public Color[] LogoColors;
		public Image[] StrawButtons;
		public Image[] FruitButtons;
		public Image[] TintButtons;
		public Image[] LogoButtons;

		[Header("Drunk effects (optional, same as ManagerBar)")]
		public Transform DrunkEffectTransform;
		[Range(0.0f, 1.0f)] public float[] CurrentIntoxication = new float[3];
		[Range(2.0f, 5.0f)] public float IntoxicationFactor = 3f;
		[Range(0.0f, 1.0f)] public float DetoxicationSpeed = 0.05f;

		// Read by cups and slots
		[HideInInspector()] public DrinkSlot[] Slots;
		[HideInInspector()] public DrinkCup[] Cups;
		[HideInInspector()] public int FlavorCount;
		[HideInInspector()] public bool IsVR;
		[HideInInspector()] public int PressedArg;
		[HideInInspector()] public string[] FName;
		[HideInInspector()] public float[] FFillSpeed;
		[HideInInspector()] public float[] FTransparency;
		[HideInInspector()] public Color[] FColorMain;
		[HideInInspector()] public Color[] FColorFoam;
		[HideInInspector()] public Color[] FColorEdge;
		[HideInInspector()] public float[] FEdgePower;
		[HideInInspector()] public float[] FEdgeStrength;
		[HideInInspector()] public float[] FFoamHeight;
		[HideInInspector()] public float[] FFoamSmoothness;
		[HideInInspector()] public float[] FEffect1;
		[HideInInspector()] public float[] FEffect2;
		[HideInInspector()] public float[] FEffect3;
		// Internal
		private VRCPlayerApi Local;
		private DrinkCup[] SlotCups;
		private Vector3[] SnapPositions;
		private DrinkButton[] Buttons;
		private Transform[] ButtonTransforms;
		private Vector4[] ButtonRects;
		private float[] ButtonDepths;
		private bool[] PokeLeft;
		private bool[] PokeRight;
		private bool PokeWasNear;
		private Material DrunkEffectMaterial;
		private Renderer DrunkEffectRenderer;
		private bool IntoxicationActive;
		private bool IdsReady;
		private float NextPPUpdate;
		private bool PPOn1;
		private bool PPOn2;
		private bool PPOn3;
		private float PPLast1 = -1f;
		private float PPLast2 = -1f;
		private float PPLast3 = -1f;

		private void Start() {
			Local = Networking.LocalPlayer;
			IsVR = Utilities.IsValid(Local) && Local.IsUserInVR();
			// Flavors: copy their values into arrays once, so cups can mix them cheaply
			DrinkFlavor[] fl = FlavorsParent.GetComponentsInChildren<DrinkFlavor>();
			FlavorCount = fl.Length;
			FName = new string[FlavorCount];
			FFillSpeed = new float[FlavorCount];
			FTransparency = new float[FlavorCount];
			FColorMain = new Color[FlavorCount];
			FColorFoam = new Color[FlavorCount];
			FColorEdge = new Color[FlavorCount];
			FEdgePower = new float[FlavorCount];
			FEdgeStrength = new float[FlavorCount];
			FFoamHeight = new float[FlavorCount];
			FFoamSmoothness = new float[FlavorCount];
			FEffect1 = new float[FlavorCount];
			FEffect2 = new float[FlavorCount];
			FEffect3 = new float[FlavorCount];
			for(int i = 0; i < FlavorCount; i++) {
				DrinkFlavor f = fl[i];
				FName[i] = f.DisplayName;
				FFillSpeed[i] = f.FillSpeed;
				FTransparency[i] = f.Transparency;
				FColorMain[i] = f.ColorMain;
				FColorFoam[i] = f.ColorFoam;
				FColorEdge[i] = f.ColorEdge;
				FEdgePower[i] = f.EdgePower;
				FEdgeStrength[i] = f.EdgeStrength;
				FFoamHeight[i] = f.FoamHeight;
				FFoamSmoothness[i] = f.FoamSmoothness;
				FEffect1[i] = f.Effect1Intensity;
				FEffect2[i] = f.Effect2Intensity;
				FEffect3[i] = f.Effect3Intensity;
			}
			// Slots
			DrinkSlot[] sl = SlotsParent.GetComponentsInChildren<DrinkSlot>();
			Slots = new DrinkSlot[sl.Length];
			SlotCups = new DrinkCup[sl.Length];
			SnapPositions = new Vector3[sl.Length];
			for(int i = 0; i < sl.Length; i++) {
				Slots[i] = sl[i].Init(this, i);
				SnapPositions[i] = sl[i].SnapPoint.position;
			}
			// Cups (after slots: a cup that is already locked registers itself in its slot)
			DrinkCup[] cl = CupsParent.GetComponentsInChildren<DrinkCup>();
			Cups = new DrinkCup[cl.Length];
			for(int i = 0; i < cl.Length; i++) Cups[i] = cl[i].Init(this, i);
			// Buttons: cache their press zones for VR finger pokes
			Buttons = GetComponentsInChildren<DrinkButton>(true);
			int bc = Buttons.Length;
			ButtonTransforms = new Transform[bc];
			ButtonRects = new Vector4[bc];
			ButtonDepths = new float[bc];
			PokeLeft = new bool[bc];
			PokeRight = new bool[bc];
			for(int i = 0; i < bc; i++) {
				RectTransform rt = Buttons[i].GetComponent<RectTransform>();
				ButtonTransforms[i] = Buttons[i].transform;
				if(rt != null) {
					Rect r = rt.rect;
					ButtonRects[i] = new Vector4(r.center.x, r.center.y, r.width * 0.5f, r.height * 0.5f);
				}
				ButtonDepths[i] = PokeDepth / Mathf.Max(0.00001f, ButtonTransforms[i].lossyScale.z);
			}
			RefreshOptionsUI();
			_RefreshCustomizerUI();
			IdsReady = true;
			// Effect (hidden until someone actually drinks something with effects)
			if(DrunkEffectTransform != null) {
				DrunkEffectRenderer = DrunkEffectTransform.GetComponent<Renderer>();
				if(DrunkEffectRenderer != null) {
					DrunkEffectMaterial = DrunkEffectRenderer.material;
					DrunkEffectRenderer.enabled = false;
				}
			}
		}
		private void Update() {
			if(!Utilities.IsValid(Local)) return;
			if(IsVR && EnableFingerPoke) PokeTick();
			if(IntoxicationActive) IntoxicationTick();
		}
		public override void OnDeserialization() {
			RefreshOptionsUI();
		}
		// Step 3 buttons (PressedArg = which choice)
		public void OptionFroth() { TakeOwnership(); Froth = (byte)Mathf.Clamp(PressedArg, 0, 2); CommitOptions(); }
		public void OptionStrength() { TakeOwnership(); Strength = (byte)Mathf.Clamp(PressedArg, 0, 2); CommitOptions(); }
		// Step 4 buttons: change the glass on the finishing pad right away (tap the picked straw / fruit again to remove it)
		public void FinishStraw() {
			DrinkCup cup = PadCup();
			if(cup != null) cup._SetFinish(cup.Straw == PressedArg ? -1 : PressedArg, cup.Fruit, cup.Tint, cup.Logo);
		}
		public void FinishFruit() {
			DrinkCup cup = PadCup();
			if(cup != null) cup._SetFinish(cup.Straw, cup.Fruit == PressedArg ? -1 : PressedArg, cup.Tint, cup.Logo);
		}
		public void CupTint() {
			DrinkCup cup = PadCup();
			if(cup != null) cup._SetFinish(cup.Straw, cup.Fruit, PressedArg, cup.Logo);
		}
		public void CupLogo() {
			DrinkCup cup = PadCup();
			if(cup != null) cup._SetFinish(cup.Straw, cup.Fruit, cup.Tint, PressedArg);
		}
		public void _RefreshCustomizerUI() {
			DrinkCup cup = CustomizerSlot != null ? CustomizerSlot.Cup : null;
			SetChoice(StrawButtons, cup != null ? cup.Straw : -1);
			SetChoice(FruitButtons, cup != null ? cup.Fruit : -1);
			SetChoice(TintButtons, cup != null ? cup.Tint : -1);
			SetChoice(LogoButtons, cup != null ? cup.Logo : -1);
		}
		// Interfaces: slots
		public int FindSnapSlot(DrinkCup cup) {
			Transform t = cup.transform;
			if(Vector3.Dot(t.up, Vector3.up) < 0.6f) return -1;
			Vector3 p = t.position;
			float best = SnapRadius * SnapRadius;
			int bestIndex = -1;
			int count = SnapPositions.Length;
			for(int i = 0; i < count; i++) {
				if(SlotCups[i] != null && SlotCups[i] != cup) continue;
				float d = (SnapPositions[i] - p).sqrMagnitude;
				if(d < best) { best = d; bestIndex = i; }
			}
			return bestIndex;
		}
		public void SetHighlight(int slot, bool on) {
			if(slot < 0 || slot >= Slots.Length) return;
			GameObject h = Slots[slot].Highlight;
			if(h != null) h.SetActive(on);
		}
		public DrinkCup GetSlotCup(int slot) {
			return slot >= 0 && slot < SlotCups.Length ? SlotCups[slot] : null;
		}
		public void _SetSlotCup(int slot, DrinkCup cup) {
			DrinkCup current = SlotCups[slot];
			if(current == null || current == cup) {
				SlotCups[slot] = cup;
				Slots[slot].Cup = cup;
			}
			// Two players locked a cup into the same slot at the same time: the lower cup id keeps it
			else if(cup.CupId < current.CupId) {
				SlotCups[slot] = cup;
				Slots[slot].Cup = cup;
				current._Yield();
			}
			else cup._Yield();
			Slots[slot].RefreshUI();
		}
		public void _ClearSlotCup(int slot, DrinkCup cup) {
			if(SlotCups[slot] != cup) return;
			SlotCups[slot] = null;
			Slots[slot].Cup = null;
			Slots[slot].RefreshUI();
		}
		// Interfaces: respawn
		public void RespawnAllCups() {
			int count = Cups.Length;
			for(int i = 0; i < count; i++) Cups[i].ForceRespawn();
		}
		public void _EnsureCupReady(DrinkCup taken) {
			int count = Cups.Length;
			for(int i = 0; i < count; i++) {
				if(Cups[i] != taken && Cups[i].IsAtHome()) return;
			}
			// The stack is empty: bring back the cup that has been abandoned the longest
			DrinkCup oldest = null;
			float oldestTime = Time.time - RecycleMinIdleSeconds;
			for(int i = 0; i < count; i++) {
				DrinkCup c = Cups[i];
				if(c == taken || c.HeldHand != 0 || c.SlotIndex >= 0 || c.Pouring) continue;
				if(c.LastActive < oldestTime) { oldestTime = c.LastActive; oldest = c; }
			}
			if(oldest != null) oldest.ForceRespawn();
		}
		// Interfaces: effects (same as ManagerBar)
		public void AddIntoxication(float e1, float e2, float e3, float amountDrinked) {
			if(DrunkEffectMaterial == null) return;
			float factor = IntoxicationFactor * amountDrinked;
			CurrentIntoxication[0] = Mathf.Clamp01(CurrentIntoxication[0] + e1 * factor);
			CurrentIntoxication[1] = Mathf.Clamp01(CurrentIntoxication[1] + e2 * factor);
			CurrentIntoxication[2] = Mathf.Clamp01(CurrentIntoxication[2] + e3 * factor);
			if(!IntoxicationActive && CurrentIntoxication[0] + CurrentIntoxication[1] + CurrentIntoxication[2] > 0f) {
				IntoxicationActive = true;
				DrunkEffectRenderer.enabled = true;
			}
		}
		public void UpdatePP(float value1, float value2, float value3) {
			// Toggles only change when an effect switches on/off; amounts only when they visibly change
			bool on1 = value1 > 0f;
			bool on2 = value2 > 0f;
			bool on3 = value3 > 0f;
			if(on1 != PPOn1 || !IdsReady) { PPOn1 = on1; DrunkEffectMaterial.SetFloat("Chromatic_Aberration", on1 ? 2 : 0); }
			if(on2 != PPOn2 || !IdsReady) { PPOn2 = on2; DrunkEffectMaterial.SetFloat("Distorsion", on2 ? 1 : 0); }
			if(on3 != PPOn3 || !IdsReady) { PPOn3 = on3; DrunkEffectMaterial.SetFloat("Neon", on3 ? 1 : 0); }
			if(Mathf.Abs(value1 - PPLast1) > 0.005f || !on1) { PPLast1 = value1; DrunkEffectMaterial.SetFloat("_CA_amplitude", value1 * 0.4f); }
			if(Mathf.Abs(value2 - PPLast2) > 0.005f || !on2) { PPLast2 = value2; DrunkEffectMaterial.SetVector("_DistorsionWave", new Vector4(value2 * 0.02f, value2 * 0.02f, 1, 0)); }
			if(Mathf.Abs(value3 - PPLast3) > 0.005f || !on3) { PPLast3 = value3; DrunkEffectMaterial.SetFloat("_NeonColorAlpha", value3 * 0.4f); }
		}
		// Internal
		private void TakeOwnership() {
			if(!Networking.IsOwner(gameObject)) Networking.SetOwner(Local, gameObject);
		}
		private void CommitOptions() {
			RequestSerialization();
			RefreshOptionsUI();
		}
		private void RefreshOptionsUI() {
			SetChoice(FrothButtons, Froth);
			SetChoice(StrengthButtons, Strength);
		}
		private DrinkCup PadCup() {
			if(CustomizerSlot == null) return null;
			if(CustomizerSlot.Cup == null) CustomizerSlot._Deny("PUT YOUR GLASS ON THE PAD FIRST");
			return CustomizerSlot.Cup;
		}
		private void SetChoice(Image[] buttons, int choice) {
			if(buttons == null) return;
			for(int i = 0; i < buttons.Length; i++) {
				if(buttons[i] != null) buttons[i].color = i == choice ? OptionOn : OptionOff;
			}
		}
		private void IntoxicationTick() {
			VRCPlayerApi.TrackingData head = Local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
			DrunkEffectTransform.position = head.position;
			float dt = Time.deltaTime;
			bool any = false;
			for(int i = 0; i < 3; i++) {
				CurrentIntoxication[i] = Mathf.Clamp(CurrentIntoxication[i] - DetoxicationSpeed * dt, 0.0f, 1.0f);
				if(CurrentIntoxication[i] > 0f) any = true;
			}
			// The material only needs ~10 updates a second (the position follows the head every frame)
			if(!any || Time.time >= NextPPUpdate) {
				NextPPUpdate = Time.time + 0.1f;
				UpdatePP(CurrentIntoxication[0], CurrentIntoxication[1], CurrentIntoxication[2]);
			}
			// Sober again: switch the full-screen effect off (it is expensive, especially on Quest)
			if(!any) {
				IntoxicationActive = false;
				DrunkEffectRenderer.enabled = false;
			}
		}
		private void PokeTick() {
			int count = ButtonTransforms.Length;
			if(count == 0) return;
			bool near = (Local.GetPosition() - transform.position).sqrMagnitude < PokeRange * PokeRange;
			if(!near) {
				if(PokeWasNear) {
					PokeWasNear = false;
					for(int i = 0; i < count; i++) { PokeLeft[i] = false; PokeRight[i] = false; }
				}
				return;
			}
			PokeWasNear = true;
			Vector3 left = Fingertip(true);
			Vector3 right = Fingertip(false);
			bool hasLeft = left != Vector3.zero;
			bool hasRight = right != Vector3.zero;
			for(int i = 0; i < count; i++) {
				Transform bt = ButtonTransforms[i];
				if(!bt.gameObject.activeInHierarchy) continue;
				Vector4 r = ButtonRects[i];
				float depth = ButtonDepths[i];
				bool inLeft = false;
				bool inRight = false;
				// The visible side of a world space canvas is local -Z, so a finger presses from -Z towards 0
				if(hasLeft) {
					Vector3 lp = bt.InverseTransformPoint(left);
					inLeft = Mathf.Abs(lp.x - r.x) < r.z && Mathf.Abs(lp.y - r.y) < r.w && lp.z > -depth && lp.z < depth * 0.6f;
				}
				if(hasRight) {
					Vector3 lp = bt.InverseTransformPoint(right);
					inRight = Mathf.Abs(lp.x - r.x) < r.z && Mathf.Abs(lp.y - r.y) < r.w && lp.z > -depth && lp.z < depth * 0.6f;
				}
				if(inLeft && !PokeLeft[i]) Poke(i, VRC_Pickup.PickupHand.Left);
				else if(inRight && !PokeRight[i]) Poke(i, VRC_Pickup.PickupHand.Right);
				PokeLeft[i] = inLeft;
				PokeRight[i] = inRight;
			}
		}
		private void Poke(int index, VRC_Pickup.PickupHand hand) {
			Local.PlayHapticEventInHand(hand, 0.06f, 0.5f, 170f);
			Buttons[index].Press();
		}
		private Vector3 Fingertip(bool left) {
			Vector3 p = Local.GetBonePosition(left ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal);
			if(p != Vector3.zero) return p;
			return Local.GetBonePosition(left ? HumanBodyBones.LeftIndexIntermediate : HumanBodyBones.RightIndexIntermediate);
		}
	}
}
