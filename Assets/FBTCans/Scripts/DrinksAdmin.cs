using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Admin panel for the drinks: lock / unlock getting new drinks, and reset (clear away) every can.
/// Admins = the instance owner (if ticked) and anyone whose display name is in Admin Names.
/// Anti-abuse: only an admin can take ownership of the panel (the current owner refuses everyone else), and every
/// player only accepts a lock or reset when the panel's owner is an admin, so a hacked client can't fake either.
/// Non-admins can't even click the buttons (their colliders are switched off for them).
/// Sync: Manual, one bool (locked) and one counter (resets), sent only when an admin presses a button.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class DrinksAdmin : UdonSharpBehaviour {
	[Tooltip("Every drink row (CanFridge) this panel controls.")]
	public CanFridge[] Fridges;
	[Tooltip("The instance owner is an admin.")]
	public bool InstanceOwnerIsAdmin = true;
	[Tooltip("Display names of extra admins (exact VRChat names, not case sensitive).")]
	public string[] AdminNames;
	[Tooltip("The button colliders; switched off for players who aren't admins.")]
	public Collider[] Buttons;
	public TextMeshPro StatusText;
	public TextMeshPro LockLabel;
	// Sync
	[UdonSynced] private bool Locked;
	[UdonSynced] private int ResetCount;
	// Internal
	private bool AcceptedLocked;
	private int AppliedReset;
	private bool GotFirstSync;

	private void Start() {
		bool admin = _IsAdmin(Networking.LocalPlayer);
		if(Buttons != null) {
			for(int i = 0; i < Buttons.Length; i++) if(Buttons[i] != null) Buttons[i].enabled = admin;
		}
		Refresh();
	}

	// Read by the drink rows and cans
	public bool IsLocked() { return AcceptedLocked; }
	public bool _IsAdmin(VRCPlayerApi p) {
		if(p == null || !Utilities.IsValid(p)) return false;
		if(InstanceOwnerIsAdmin && p.isInstanceOwner) return true;
		if(AdminNames == null) return false;
		string n = p.displayName.Trim().ToLower();
		for(int i = 0; i < AdminNames.Length; i++) {
			if(AdminNames[i] != null && AdminNames[i].Trim().ToLower() == n) return true;
		}
		return false;
	}

	// Buttons
	public void _ToggleLock() {
		if(!TakeControl()) return;
		Locked = !AcceptedLocked;
		AcceptedLocked = Locked;
		RequestSerialization();
		Refresh();
	}
	public void _ResetAll() {
		if(!TakeControl()) return;
		Locked = AcceptedLocked;
		ResetCount++;
		AppliedReset = ResetCount;
		RequestSerialization();
		ClearMyCans();
		Refresh();
	}

	// Networking
	public override void OnDeserialization() {
		VRCPlayerApi owner = Networking.GetOwner(gameObject);
		if(!_IsAdmin(owner)) {
			// not sent by an admin (e.g. the admin left and the panel passed to someone else): never reset, and only
			// take the lock state when joining, so a new player still sees what the admin set
			if(!GotFirstSync) { AcceptedLocked = Locked; Refresh(); }
			AppliedReset = ResetCount;
			GotFirstSync = true;
			return;
		}
		AcceptedLocked = Locked;
		if(ResetCount != AppliedReset) {
			AppliedReset = ResetCount;
			// joining: don't replay an old reset
			if(GotFirstSync) ClearMyCans();
		}
		GotFirstSync = true;
		Refresh();
	}
	public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner) {
		return requestingPlayer != null && requestingPlayer == requestedOwner && _IsAdmin(requestingPlayer);
	}

	// Internal
	private bool TakeControl() {
		VRCPlayerApi me = Networking.LocalPlayer;
		if(!_IsAdmin(me)) return false;
		if(!Networking.IsOwner(gameObject)) Networking.SetOwner(me, gameObject);
		return true;
	}
	private void ClearMyCans() {
		// every player clears the cans they own, so together every can is cleared
		if(Fridges == null) return;
		for(int i = 0; i < Fridges.Length; i++) if(Fridges[i] != null) Fridges[i]._AdminClear();
	}
	private void Refresh() {
		if(Fridges != null) {
			for(int i = 0; i < Fridges.Length; i++) if(Fridges[i] != null) Fridges[i]._SetLocked(AcceptedLocked);
		}
		if(StatusText != null) StatusText.text = AcceptedLocked ? "DRINKS  LOCKED" : "DRINKS  OPEN";
		if(StatusText != null) StatusText.color = AcceptedLocked ? new Color(0.85f, 0.35f, 0.35f) : new Color(0.9f, 0.78f, 0.5f);
		if(LockLabel != null) LockLabel.text = AcceptedLocked ? "UNLOCK" : "LOCK";
	}
}
