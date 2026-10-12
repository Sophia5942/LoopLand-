using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// The fridge door. Click it: the door swings open (with a sound) and a fresh can is waiting on the shelf inside.
/// Click it again to close it; it also closes by itself after a few seconds. Uses a free can from the pool; if all
/// are out, it reuses the oldest one nobody is holding. Optional cooldown per player.
/// Sync: Manual, one bool (door open), sent only when it changes.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class CanFridge : UdonSharpBehaviour {
	public FBTCan[] Cans;
	[Tooltip("Where the new can appears (on the shelf, inside the fridge).")]
	public Transform SpawnPoint;
	[Tooltip("Seconds before the same player can take another can. 0 = no cooldown. The door still opens during the cooldown.")]
	public float CooldownSeconds = 5f;
	[Tooltip("The door closes by itself after this many seconds. 0 = stays open until clicked.")]
	public float AutoCloseSeconds = 8f;
	[Tooltip("How far the door swings open (degrees).")]
	public float OpenAngle = -105f;
	public AudioSource Sound;
	public AudioClip OpenClip;
	public AudioClip CloseClip;
	// Sync
	[UdonSynced] private bool DoorOpen;
	// Internal
	private float NextAllowed;
	private int Spread;
	private float CloseDue;
	private float DoorT;
	private bool Animating;
	private bool AppliedOpen;

	public override void Interact() {
		if(!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
		if(DoorOpen) {
			DoorOpen = false;
		} else {
			DoorOpen = true;
			GiveCan();
			ScheduleClose();
		}
		RequestSerialization();
		ApplyDoor();
	}
	public override void OnDeserialization() { ApplyDoor(); }
	public override void OnOwnershipTransferred(VRCPlayerApi player) {
		// the old owner left with the door open: the new owner closes it
		if(Networking.IsOwner(gameObject) && DoorOpen) ScheduleClose();
	}
	// Delayed events
	public void _AutoClose() {
		if(!DoorOpen || !Networking.IsOwner(gameObject) || Time.time < CloseDue) return;
		DoorOpen = false;
		RequestSerialization();
		ApplyDoor();
	}
	public void _DoorStep() {
		float target = DoorOpen ? 1f : 0f;
		// opens quick, closes a bit slower
		DoorT = Mathf.MoveTowards(DoorT, target, Time.deltaTime / (DoorOpen ? 0.45f : 0.6f));
		float e = DoorT * DoorT * (3f - 2f * DoorT);
		transform.localRotation = Quaternion.Euler(0f, OpenAngle * e, 0f);
		if(DoorT != target) SendCustomEventDelayedFrames(nameof(_DoorStep), 1);
		else Animating = false;
	}
	// Internal
	private void ApplyDoor() {
		if(DoorOpen != AppliedOpen) {
			AppliedOpen = DoorOpen;
			if(Sound != null) {
				AudioClip clip = DoorOpen ? OpenClip : CloseClip;
				if(clip != null) Sound.PlayOneShot(clip);
			}
		}
		if(!Animating) {
			Animating = true;
			SendCustomEventDelayedFrames(nameof(_DoorStep), 1);
		}
	}
	private void ScheduleClose() {
		if(AutoCloseSeconds <= 0f) return;
		CloseDue = Time.time + AutoCloseSeconds;
		SendCustomEventDelayedSeconds(nameof(_AutoClose), AutoCloseSeconds + 0.1f);
	}
	private void GiveCan() {
		if(Time.time < NextAllowed || Cans == null || Cans.Length == 0 || SpawnPoint == null) return;
		FBTCan can = null;
		for(int i = 0; i < Cans.Length; i++) {
			if(Cans[i] != null && !Cans[i].IsActive()) { can = Cans[i]; break; }
		}
		if(can == null) {
			// all out: reuse the oldest one nobody is holding
			float oldest = float.MaxValue;
			for(int i = 0; i < Cans.Length; i++) {
				if(Cans[i] == null || Cans[i].IsHeld()) continue;
				if(Cans[i].SpawnTime < oldest) { oldest = Cans[i].SpawnTime; can = Cans[i]; }
			}
		}
		if(can == null) return;
		// spread cans along the shelf so they don't land inside each other
		Spread = (Spread + 1) % 3;
		Vector3 pos = SpawnPoint.position + SpawnPoint.right * ((Spread - 1) * 0.09f);
		can.Spawn(pos, SpawnPoint.rotation);
		NextAllowed = Time.time + CooldownSeconds;
	}
}
