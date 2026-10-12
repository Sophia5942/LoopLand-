using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// Opens a curtain when someone walks up to it, by animating its blend shape ("Key 1") from ClosedWeight (0) to
/// OpenWeight (50), with a curtain sound. It closes again when everyone has walked away.
/// No networking needed: every player's game checks where all players are, so everyone sees the same curtain.
/// Optimized: it checks 4 times a second and only animates while the curtain is actually moving (no Update).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CurtainOpener : UdonSharpBehaviour {
	[Header("Curtain")]
	public SkinnedMeshRenderer Curtain;
	[Tooltip("Index of the blend shape to animate (\"Key 1\" on your curtain is 0).")]
	public int BlendShapeIndex = 0;
	public float ClosedWeight = 0f;
	public float OpenWeight = 50f;
	[Header("Behaviour")]
	[Tooltip("Someone closer than this (meters) opens the curtain.")]
	public float OpenDistance = 3f;
	[Tooltip("Seconds it takes to open or close.")]
	public float OpenSeconds = 2.5f;
	[Tooltip("On: any player near it opens it for everyone. Off: it only opens for you when YOU are near.")]
	public bool AnyPlayerOpensIt = true;
	[Tooltip("Where distance is measured from (empty = the curtain's center).")]
	public Transform Center;
	[Header("Sound")]
	public AudioSource Sound;
	public AudioClip OpenClip;
	public AudioClip CloseClip;
	// Internal
	private VRCPlayerApi[] Players = new VRCPlayerApi[100];
	private Vector3 CenterPos;
	private bool WantOpen;
	private bool Moving;
	private float Progress; // 0 = closed, 1 = open

	private void Start() {
		CenterPos = Center != null ? Center.position : (Curtain != null ? Curtain.bounds.center : transform.position);
		Progress = 0f;
		Apply();
		SendCustomEventDelayedSeconds(nameof(_Check), 0.25f);
	}
	public void _Check() {
		bool near = SomeoneNear();
		if(near != WantOpen) {
			WantOpen = near;
			if(Sound != null) {
				AudioClip clip = near ? OpenClip : CloseClip;
				if(clip != null) {
					Sound.Stop();
					Sound.clip = clip;
					Sound.Play();
				}
			}
			if(!Moving) {
				Moving = true;
				SendCustomEventDelayedFrames(nameof(_Step), 1);
			}
		}
		SendCustomEventDelayedSeconds(nameof(_Check), 0.25f);
	}
	public void _Step() {
		float speed = Time.deltaTime / Mathf.Max(0.05f, OpenSeconds);
		Progress = Mathf.Clamp01(Progress + (WantOpen ? speed : -speed));
		Apply();
		if((WantOpen && Progress >= 1f) || (!WantOpen && Progress <= 0f)) {
			Moving = false;
			return;
		}
		SendCustomEventDelayedFrames(nameof(_Step), 1);
	}
	private void Apply() {
		if(Curtain == null) return;
		// ease in and out, so it starts and stops gently like real fabric
		float eased = Progress * Progress * (3f - 2f * Progress);
		Curtain.SetBlendShapeWeight(BlendShapeIndex, Mathf.Lerp(ClosedWeight, OpenWeight, eased));
	}
	private bool SomeoneNear() {
		float range = OpenDistance * OpenDistance;
		if(!AnyPlayerOpensIt) {
			VRCPlayerApi local = Networking.LocalPlayer;
			return Utilities.IsValid(local) && Flat(local.GetPosition()) < range;
		}
		int count = VRCPlayerApi.GetPlayerCount();
		if(Players.Length < count) Players = new VRCPlayerApi[count];
		VRCPlayerApi.GetPlayers(Players);
		for(int i = 0; i < count; i++) {
			if(Utilities.IsValid(Players[i]) && Flat(Players[i].GetPosition()) < range) return true;
		}
		return false;
	}
	private float Flat(Vector3 p) {
		// distance on the floor (ignores height), squared
		float dx = p.x - CenterPos.x;
		float dz = p.z - CenterPos.z;
		return dx * dx + dz * dz;
	}
}
