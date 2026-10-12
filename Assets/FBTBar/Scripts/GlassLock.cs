using UdonSharp;
using UnityEngine;

/// <summary>
/// Put this next to LiquidGlass + TransformSync on a glass. When you let go of the glass near a spout, it locks
/// upright onto the drip tray right under the spout (with a click), so the pour lands in it.
/// TransformSync then sends the new position to everyone, as usual.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class GlassLock : UdonSharpBehaviour {
	[Tooltip("Parent of the spot under each spout (its children are the lock points).")]
	public Transform SnapPointsParent;
	[Tooltip("How close (meters, sideways) the glass must be to a spot to lock into it.")]
	public float SnapRadius = 0.12f;
	[Tooltip("The glass mesh renderer: the lock lines up its visible bottom with the spot (works even if the pivot is elsewhere).")]
	public Renderer Body;
	public AudioSource Sound;
	public AudioClip LockClip;
	public AudioClip UnlockClip;
	// Internal
	private Transform[] Points;
	private bool Locked;

	private void Start() {
		int n = SnapPointsParent != null ? SnapPointsParent.childCount : 0;
		Points = new Transform[n];
		for(int i = 0; i < n; i++) Points[i] = SnapPointsParent.GetChild(i);
	}
	public override void OnDrop() {
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
	private Vector3 Bottom() {
		if(Body == null) return transform.position;
		Bounds b = Body.bounds;
		return new Vector3(b.center.x, b.min.y, b.center.z);
	}
	public override void OnPickup() {
		if(Locked && Sound != null && UnlockClip != null) Sound.PlayOneShot(UnlockClip);
		Locked = false;
	}
}
