using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

/// <summary>
/// Admin panel for the drinks: lock / unlock getting new drinks (locking also clears every can), and reset (clear away)
/// every can. The panel pops up in front of an admin, only on their screen: hold Tab on desktop, or in VR grab with both
/// hands (as if picking something up) and pull them apart. Same again (or the X) closes it. The panel is a world-space UI
/// canvas (point at it with the VRChat laser / mouse); its buttons call _ToggleLock, _ResetAll and _ClosePanel here.
/// Admins = the instance owner (if ticked) and anyone whose display name is in Admin Names.
/// Anti-abuse: only an admin can take ownership of the panel (the current owner refuses everyone else), and every
/// player only accepts a lock or reset when the panel's owner is an admin, so a hacked client can't fake either.
/// Non-admins never see the panel, and the buttons do nothing for them anyway.
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
	public TextMeshProUGUI StatusText;
	public TextMeshProUGUI LockLabel;
	[Header("Pop-up")]
	[Tooltip("The panel itself; only ever shown to the admin who opens it (it's local, nobody else sees it).")]
	public GameObject PanelUI;
	[Tooltip("Desktop: hold this key to open / close the panel.")]
	public KeyCode ToggleKey = KeyCode.Tab;
	[Tooltip("Desktop: how long to hold the key (seconds).")]
	public float KeyHoldSeconds = 0.4f;
	[Tooltip("VR: grab with both hands and pull them this far apart (metres) to open / close the panel.")]
	public float VRPullDistance = 0.25f;
	[Tooltip("How far in front of you the panel appears (metres).")]
	public float PanelDistance = 0.55f;
	// Sync
	[UdonSynced] private bool Locked;
	[UdonSynced] private int ResetCount;
	// Internal
	private bool AcceptedLocked;
	private int AppliedReset;
	private bool GotFirstSync;
	private bool LocalAdmin;
	private bool LeftGrab, RightGrab, GestureFired, KeyFired;
	private float StartSpread, KeySince;

	private void Start() {
		LocalAdmin = _IsAdmin(Networking.LocalPlayer);
		KeySince = -1f;
		if(PanelUI != null) PanelUI.SetActive(false);
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

	// Opening the panel
	private void Update() {
		if(!LocalAdmin) return;
		// desktop: hold the key
		if(Input.GetKey(ToggleKey)) {
			if(KeySince < 0f) KeySince = Time.time;
			if(!KeyFired && Time.time - KeySince >= KeyHoldSeconds) { KeyFired = true; _TogglePanel(); }
		} else { KeySince = -1f; KeyFired = false; }
		// VR: both hands grabbing (nothing in them), pulled apart
		if(LeftGrab && RightGrab && !GestureFired) {
			VRCPlayerApi me = Networking.LocalPlayer;
			if(me == null || me.GetPickupInHand(VRC_Pickup.PickupHand.Left) != null || me.GetPickupInHand(VRC_Pickup.PickupHand.Right) != null) return;
			if(HandSpread(me) - StartSpread >= VRPullDistance) { GestureFired = true; _TogglePanel(); }
		}
	}
	public override void InputGrab(bool value, UdonInputEventArgs args) {
		if(!LocalAdmin) return;
		bool before = LeftGrab && RightGrab;
		if(args.handType == HandType.LEFT) LeftGrab = value; else RightGrab = value;
		bool both = LeftGrab && RightGrab;
		VRCPlayerApi me = Networking.LocalPlayer;
		if(both && !before && me != null && me.IsUserInVR()) StartSpread = HandSpread(me);
		if(!both) GestureFired = false;
	}
	private float HandSpread(VRCPlayerApi me) {
		Vector3 l = me.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position;
		Vector3 r = me.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position;
		return Vector3.Distance(l, r);
	}
	public void _TogglePanel() {
		if(PanelUI == null || !LocalAdmin) return;
		if(PanelUI.activeSelf) { PanelUI.SetActive(false); return; }
		// put it in front of your head, facing you (a UI canvas is read from its back, so it looks the same way you do)
		VRCPlayerApi me = Networking.LocalPlayer;
		VRCPlayerApi.TrackingData head = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
		Vector3 fwd = head.rotation * Vector3.forward;
		fwd.y = 0f;
		if(fwd.sqrMagnitude < 0.001f) fwd = me.GetRotation() * Vector3.forward;
		fwd.Normalize();
		PanelUI.transform.SetPositionAndRotation(head.position + fwd * PanelDistance + Vector3.down * 0.12f, Quaternion.LookRotation(fwd));
		PanelUI.SetActive(true);
		Refresh();
	}
	public void _ClosePanel() { if(PanelUI != null) PanelUI.SetActive(false); }

	// Buttons
	public void _ToggleLock() {
		if(!TakeControl()) return;
		Locked = !AcceptedLocked;
		AcceptedLocked = Locked;
		if(Locked) {
			// locking clears every drink too
			ResetCount++;
			AppliedReset = ResetCount;
			ClearMyCans();
		}
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
