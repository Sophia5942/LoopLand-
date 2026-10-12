using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// A drink can. The fridge puts it on the shelf (it stays put there until someone grabs it), the first Use opens it (the tab lifts, "pssht"), holding Use after that sips,
/// dropping it on the floor clanks, and dropping it in the bin cleans it up. Cans are reused (pooled): a cleaned-up can is just hidden until the
/// fridge needs it again.
/// Sync: Manual, three bools (in use / opened / held), sent only when they change. Movement is synced by TransformSync
/// (your script) on the same object, also Manual.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class FBTCan : UdonSharpBehaviour {
	// References
	public GameObject Visual;
	public Transform Tab;
	public Collider CanCollider;
	public Transform HideSpot;
	public AudioSource Sound;
	public AudioSource SipSound;
	public AudioClip OpenClip;
	public AudioClip TrashClip;
	public AudioClip SpawnClip;
	public AudioClip DropClip;
	[Tooltip("How hard the can has to hit something to clank (m/s).")]
	public float DropSoundSpeed = 1.2f;
	[Tooltip("A can left alone is cleaned up by itself after this many seconds. 0 = never.")]
	public float CleanupAfterSeconds = 300f;
	// Sync
	[UdonSynced] private bool Active;
	[UdonSynced] private bool Opened;
	[UdonSynced] private bool Held;
	// Read by the fridge
	[HideInInspector()] public float SpawnTime;
	// Internal
	private VRC_Pickup Pickup;
	private Rigidbody RB;
	private bool AppliedActive;
	private bool AppliedOpened;
	private bool Ready;
	private float TabT;
	private float CleanupDue;
	private float NextClank;
	private bool Parked;

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
		if(SipSound != null) SipSound.Play();
	}
	public override void OnPickupUseUp() {
		if(SipSound != null) SipSound.Stop();
	}
	// Hitting the floor (only the owner simulates the can, so only the owner hears the hit and tells everyone)
	private void OnCollisionEnter(Collision collision) {
		if(!Active || IsHeld() || DropClip == null || Time.time < NextClank) return;
		if(collision.relativeVelocity.magnitude < DropSoundSpeed || !Networking.IsOwner(gameObject)) return;
		NextClank = Time.time + 0.3f;
		SendCustomNetworkEvent(NetworkEventTarget.All, nameof(Clank));
	}
	public void Clank() {
		if(!Active || Sound == null || DropClip == null) return;
		Sound.pitch = Random.Range(0.92f, 1.08f);
		Sound.PlayOneShot(DropClip);
	}
	// Networking
	public override void OnDeserialization() { ApplyState(true); }
	public override void OnOwnershipTransferred(VRCPlayerApi player) { UpdatePhysics(); }
	// Delayed events
	public void _CleanupCheck() {
		if(!Active || !Networking.IsOwner(gameObject) || IsHeld() || Time.time < CleanupDue) return;
		Despawn(false);
	}
	public void _TabStep() {
		TabT = Mathf.Clamp01(TabT + Time.deltaTime / 0.25f);
		SetTab(TabT);
		if(TabT < 1f) SendCustomEventDelayedFrames(nameof(_TabStep), 1);
	}
	// Internal
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
		if(!Active && SipSound != null) SipSound.Stop();
		if(Opened != AppliedOpened) {
			AppliedOpened = Opened;
			if(Opened && effects) {
				if(Sound != null && OpenClip != null) { Sound.pitch = 1f; Sound.PlayOneShot(OpenClip); }
				TabT = 0f;
				SendCustomEventDelayedFrames(nameof(_TabStep), 1);
			}
			else SetTab(Opened ? 1f : 0f);
		}
		UpdatePhysics();
	}
	private void UpdatePhysics() {
		// only the owner simulates; everyone else follows TransformSync. A can waiting in the fridge doesn't move.
		if(RB != null) RB.isKinematic = !Active || Parked || !Networking.IsOwner(gameObject);
	}
	private void SetTab(float t) {
		if(Tab == null) return;
		float e = 1f - (1f - t) * (1f - t);
		Tab.localRotation = Quaternion.Euler(-75f * e, 0f, 0f);
	}
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
