using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// A drink can. The fridge puts it on the shelf (it stays put there until someone grabs it), Use opens it (animated: the ring-pull lifts, the can jolts,
/// the hole punches open, the tab settles back down, "pssht"), Use again swishes it round in your hand,
/// dropping it on the floor clunks, and dropping it in the bin cleans it up. Cans are reused (pooled): a cleaned-up can is just hidden until the
/// fridge needs it again.
/// Anti-abuse (hacked clients): ownership of a can is only handed over when the current owner agrees. They refuse if
/// the can is in their hand, or if the asking player spawned a can too recently (everyone tracks spawns via SpawnCount).
/// The clunk is the only network event and it ignores spam; a swish only plays when the last one has finished. Everything internal starts with _ so it can't be called
/// over the network.
/// Sync: Manual, three bools (in use / opened / held) and two counters (spawn, swish), sent only when they change. Movement is synced by TransformSync
/// (your script) on the same object, also Manual.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class FBTCan : UdonSharpBehaviour {
	// References
	[Tooltip("The fridge that keeps track of who spawned recently (anti-spam).")]
	public CanFridge Fridge;
	public GameObject Visual;
	public Transform Tab;
	[Tooltip("The dark opening on the lid, shown once the can is open.")]
	public GameObject Mouth;
	public Collider CanCollider;
	public Transform HideSpot;
	public AudioSource Sound;
	public AudioClip OpenClip;
	public AudioClip TrashClip;
	public AudioClip SpawnClip;
	public AudioClip DropClip;
	public AudioClip SwishClip;
	[Tooltip("How hard the can has to hit something to clank (m/s).")]
	public float DropSoundSpeed = 1.2f;
	[Tooltip("A can left alone is cleaned up by itself after this many seconds. 0 = never.")]
	public float CleanupAfterSeconds = 300f;
	// Sync
	[UdonSynced] private bool Active;
	[UdonSynced] private bool Opened;
	[UdonSynced] private bool Held;
	[UdonSynced] private int SpawnCount;
	[UdonSynced] private int SwishCount;
	// Read by the fridge
	[HideInInspector()] public float SpawnTime;
	// Internal
	private VRC_Pickup Pickup;
	private Rigidbody RB;
	private bool AppliedActive;
	private bool AppliedOpened;
	private bool Ready;
	private float TabT;
	// opening animation length (seconds)
	private const float OpenTime = 0.75f;
	private float CleanupDue;
	private float NextClank;
	private bool Parked;
	private int AppliedSpawnCount;
	private float LastClankHeard;
	private int AppliedSwishCount;
	private bool GotFirstSync;
	private bool Swishing;
	private float SwishT;
	// swish animation length (seconds)
	private const float SwishTime = 0.7f;

	private void Start() {
		Pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
		RB = GetComponent<Rigidbody>();
		Ready = true;
		ApplyState(false);
	}
	// Fridge
	public bool IsActive() { return Active; }
	public bool IsHeld() { return Held || (Pickup != null && Pickup.IsHeld); }
	public void Spawn(Vector3 pos, Quaternion rot) {
		TakeOwnership();
		if(Pickup != null && Pickup.IsHeld) Pickup.Drop();
		Active = true;
		Opened = false;
		Held = false;
		Parked = true;
		SpawnCount++;
		AppliedSpawnCount = SpawnCount;
		if(Fridge != null) Fridge._NoteSpawn(Networking.LocalPlayer.playerId);
		SpawnTime = Time.time;
		transform.SetPositionAndRotation(pos, rot);
		if(RB != null) { RB.velocity = Vector3.zero; RB.angularVelocity = Vector3.zero; }
		RequestSerialization();
		ApplyState(true);
		ScheduleCleanup();
	}
	// Bin
	public void _Trash() {
		if(!Active || !Networking.IsOwner(gameObject) || IsHeld()) return;
		Despawn(true);
	}
	// Pickup
	public override void OnPickup() {
		Parked = false;
		TakeOwnership();
		Held = true;
		RequestSerialization();
	}
	public override void OnDrop() {
		Held = false;
		RequestSerialization();
		ScheduleCleanup();
	}
	public override void OnPickupUseDown() {
		if(!Opened) {
			Opened = true;
			RequestSerialization();
			ApplyState(true);
			return;
		}
		// opened: swish it round (once the opening animation and the last swish are done)
		if(Swishing || TabT < 1f) return;
		SwishCount++;
		AppliedSwishCount = SwishCount;
		RequestSerialization();
		StartSwish();
	}
	// Hitting the floor (only the owner simulates the can, so only the owner hears the hit and tells everyone)
	private void OnCollisionEnter(Collision collision) {
		if(!Active || IsHeld() || DropClip == null || Time.time < NextClank) return;
		if(collision.relativeVelocity.magnitude < DropSoundSpeed || !Networking.IsOwner(gameObject)) return;
		NextClank = Time.time + 0.3f;
		SendCustomNetworkEvent(NetworkEventTarget.All, nameof(Clank));
	}
	public void Clank() {
		// anyone can send this event, so ignore spam
		if(!Active || Sound == null || DropClip == null || Time.time - LastClankHeard < 0.25f) return;
		LastClankHeard = Time.time;
		Sound.pitch = Random.Range(0.92f, 1.08f);
		Sound.PlayOneShot(DropClip);
	}
	// Networking
	public override void OnDeserialization() {
		if(SpawnCount != AppliedSpawnCount) {
			// someone spawned this can: remember who, for the anti-spam check
			AppliedSpawnCount = SpawnCount;
			SpawnTime = Time.time;
			VRCPlayerApi owner = Networking.GetOwner(gameObject);
			if(Fridge != null && owner != null && Active) Fridge._NoteSpawn(owner.playerId);
		}
		if(SwishCount != AppliedSwishCount) {
			AppliedSwishCount = SwishCount;
			// joining: don't replay an old swish
			if(GotFirstSync && Active && Opened) StartSwish();
		}
		GotFirstSync = true;
		ApplyState(true);
	}
	public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner) {
		// runs on the current owner: a modified client can skip its own checks, but not ours
		if(requestingPlayer == null || requestedOwner == null || requestingPlayer != requestedOwner) return false;
		if(Networking.IsOwner(gameObject) && Pickup != null && Pickup.IsHeld) return false;
		// taking a can nobody is using = spawning it: only once per cooldown (with some slack for network lag)
		if(!Active && Fridge != null && Fridge._SpawnedWithin(requestingPlayer.playerId, Fridge.CooldownSeconds * 0.7f)) return false;
		return true;
	}
	public override void OnOwnershipTransferred(VRCPlayerApi player) { UpdatePhysics(); }
	// Delayed events
	public void _CleanupCheck() {
		if(!Active || !Networking.IsOwner(gameObject) || IsHeld() || Time.time < CleanupDue) return;
		Despawn(false);
	}
	public void _TabStep() {
		TabT = Mathf.Clamp01(TabT + Time.deltaTime / OpenTime);
		SetTab(TabT);
		if(TabT < 1f) SendCustomEventDelayedFrames(nameof(_TabStep), 1);
	}
	public void _SwishStep() {
		if(!Active || Visual == null) { StopSwish(); return; }
		SwishT += Time.deltaTime / SwishTime;
		if(SwishT >= 1f) { StopSwish(); return; }
		// swirl the can round in small circles, building up then easing off
		float env = Mathf.Sin(SwishT * Mathf.PI);
		float ph = SwishT * SwishTime * Mathf.PI * 2f * 2.6f;
		Visual.transform.localRotation = Quaternion.Euler(Mathf.Sin(ph) * 13f * env, 0f, Mathf.Cos(ph) * 13f * env);
		SendCustomEventDelayedFrames(nameof(_SwishStep), 1);
	}
	// Internal
	private void StartSwish() {
		if(Swishing || Visual == null) return;
		Swishing = true;
		SwishT = 0f;
		if(Sound != null && SwishClip != null) { Sound.pitch = 1f; Sound.PlayOneShot(SwishClip); }
		SendCustomEventDelayedFrames(nameof(_SwishStep), 1);
	}
	private void StopSwish() {
		Swishing = false;
		if(Visual != null) Visual.transform.localRotation = Quaternion.identity;
	}
	private void Despawn(bool trashed) {
		Active = false;
		Opened = false;
		Held = false;
		RequestSerialization();
		ApplyState(trashed);
		if(HideSpot != null) transform.SetPositionAndRotation(HideSpot.position, HideSpot.rotation);
	}
	private void ApplyState(bool effects) {
		if(!Ready) return;
		if(Active != AppliedActive) {
			AppliedActive = Active;
			if(effects && Sound != null) {
				AudioClip clip = Active ? SpawnClip : TrashClip;
				if(clip != null) { Sound.pitch = 1f; Sound.PlayOneShot(clip); }
			}
		}
		if(Visual != null && Visual.activeSelf != Active) Visual.SetActive(Active);
		if(CanCollider != null) CanCollider.enabled = Active;
		if(Pickup != null) Pickup.pickupable = Active;
		if(Opened != AppliedOpened) {
			AppliedOpened = Opened;
			if(Opened && effects) {
				if(Sound != null && OpenClip != null) { Sound.pitch = 1f; Sound.PlayOneShot(OpenClip); }
				TabT = 0f;
				SendCustomEventDelayedFrames(nameof(_TabStep), 1);
			}
			else { TabT = Opened ? 1f : 0f; SetTab(TabT); }
		}
		UpdatePhysics();
	}
	private void UpdatePhysics() {
		// only the owner simulates; everyone else follows TransformSync. A can waiting in the fridge doesn't move.
		if(RB != null) RB.isKinematic = !Active || Parked || !Networking.IsOwner(gameObject);
	}
	/// <summary>Opening animation, t = 0..1: pull the ring up, the can jolts, the hole opens, the tab settles back down.</summary>
	private void SetTab(float t) {
		float sec = t * OpenTime;
		float angle;
		if(sec < 0.2f) angle = -80f * Ease(sec / 0.2f);                       // finger lifts the ring
		else if(sec < 0.35f) angle = -80f;                                     // hold: the lid gives way
		else angle = -80f + 65f * Ease(Mathf.Clamp01((sec - 0.35f) / 0.35f)); // push it back down
		if(Tab != null) Tab.localRotation = Quaternion.Euler(angle, 0f, 0f);
		if(Mouth != null) {
			bool show = t > 0f && sec >= 0.17f;
			if(Mouth.activeSelf != show) Mouth.SetActive(show);
		}
		if(Visual != null) {
			// small jolt when the seal breaks
			float j = sec > 0.15f && sec < 0.45f ? Mathf.Sin((sec - 0.15f) * 70f) * 4f * (1f - (sec - 0.15f) / 0.3f) : 0f;
			Visual.transform.localRotation = Quaternion.Euler(j * 0.5f, 0f, j);
		}
	}
	private float Ease(float x) { return x * x * (3f - 2f * x); }
	private void ScheduleCleanup() {
		if(CleanupAfterSeconds <= 0f) return;
		CleanupDue = Time.time + CleanupAfterSeconds;
		SendCustomEventDelayedSeconds(nameof(_CleanupCheck), CleanupAfterSeconds + 0.1f);
	}
	private void TakeOwnership() {
		if(!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
		UpdatePhysics();
	}
}
