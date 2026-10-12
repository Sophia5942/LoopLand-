using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Put this next to LiquidGlass + TransformSync on a glass.
///  - Lock: let go of the glass near a spout and it locks upright onto the drip tray under it (with a click).
///  - Respawn: a glass left alone away from its spot goes back to the tray, empty, after RespawnAfterSeconds,
///    or straight away with the RESET GLASSES button. Glasses someone is holding are never taken away.
/// TransformSync sends the new position and LiquidGlass the empty fill to everyone, as usual.
/// Manual sync (one bool) to match LiquidGlass + TransformSync: VRChat needs every UdonBehaviour on one object to use
/// the same sync method.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class GlassLock : UdonSharpBehaviour {
	[Tooltip("Parent of the spot under each spout (its children are the lock points).")]
	public Transform SnapPointsParent;
	[Tooltip("How close (meters, sideways) the glass must be to a spot to lock into it.")]
	public float SnapRadius = 0.12f;
	[Tooltip("The glass mesh renderer: the lock lines up its visible bottom with the spot (works even if the pivot is elsewhere).")]
	public Renderer Body;
	[Tooltip("The LiquidGlass on this glass (emptied on respawn).")]
	public LiquidGlass Glass;
	[Tooltip("A glass left alone away from its spot goes back after this many seconds. 0 = never.")]
	public float RespawnAfterSeconds = 180f;
	public AudioSource Sound;
	public AudioClip LockClip;
	public AudioClip UnlockClip;
	public AudioClip RespawnClip;
	// Sync
	[UdonSynced] private bool Held;
	// Internal
	private Transform[] Points;
	private bool Locked;
	private Vector3 HomePos;
	private Quaternion HomeRot;
	private float RespawnDue;

	private void Start() {
		int n = SnapPointsParent != null ? SnapPointsParent.childCount : 0;
		Points = new Transform[n];
		for(int i = 0; i < n; i++) Points[i] = SnapPointsParent.GetChild(i);
		HomePos = transform.position;
		HomeRot = transform.rotation;
	}
	public override void OnPickup() {
		// The holder owns the glass, so LiquidGlass (drinking, filling) and TransformSync sync from them
		if(!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
		Held = true;
		RequestSerialization();
		if(Locked && Sound != null && UnlockClip != null) Sound.PlayOneShot(UnlockClip);
		Locked = false;
	}
	public override void OnDrop() {
		Held = false;
		RequestSerialization();
		ScheduleRespawn();
		Vector3 bottom = Bottom();
		float best = SnapRadius * SnapRadius;
		int bestIndex = -1;
		for(int i = 0; i < Points.Length; i++) {
			Vector3 d = Points[i].position - bottom;
			float sideways = d.x * d.x + d.z * d.z;
			if(Mathf.Abs(d.y) < 0.35f && sideways < best) { best = sideways; bestIndex = i; }
		}
		if(bestIndex < 0) { Locked = false; return; }
		// stand it upright, then move its visible bottom-center onto the spot
		transform.rotation = Points[bestIndex].rotation;
		transform.position += Points[bestIndex].position - Bottom();
		Locked = true;
		if(Sound != null && LockClip != null) Sound.PlayOneShot(LockClip);
	}
	// RESET GLASSES button
	public void ForceRespawn() {
		if(Held) return;
		if(AtHome() && (Glass == null || Glass.FillAmount <= 0f)) return;
		if(!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
		Respawn();
	}
	public void _RespawnCheck() {
		if(!Networking.IsOwner(gameObject) || Held || Time.time < RespawnDue || AtHome()) return;
		Respawn();
	}
	// Internal
	private void ScheduleRespawn() {
		if(RespawnAfterSeconds <= 0f) return;
		RespawnDue = Time.time + RespawnAfterSeconds;
		SendCustomEventDelayedSeconds(nameof(_RespawnCheck), RespawnAfterSeconds + 0.1f);
	}
	private void Respawn() {
		Locked = false;
		transform.SetPositionAndRotation(HomePos, HomeRot);
		if(Glass != null && Glass.FillAmount > 0f) Glass.ChangeFillAmount(-1f, null);
		if(Sound != null && RespawnClip != null) Sound.PlayOneShot(RespawnClip);
	}
	private bool AtHome() {
		return (transform.position - HomePos).sqrMagnitude < 0.0004f;
	}
	private Vector3 Bottom() {
		if(Body == null) return transform.position;
		Bounds b = Body.bounds;
		return new Vector3(b.center.x, b.min.y, b.center.z);
	}
}
