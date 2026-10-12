using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

/// <summary>
/// The "Push" button above a tap. Press it (Interact: VR hand / laser, desktop click, Quest) to start or stop the pour.
/// It switches your LiquidParticles stream on/off and syncs that for everyone (one bool, sent only on press).
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class BarTapButton : UdonSharpBehaviour {
	// References
	public LiquidParticles Tap;
	public Renderer Lamp;
	public Material LampOn;
	public Material LampOff;
	public Transform PressVisual;
	public AudioSource PourSound;
	public AudioSource ClickSound;
	// Sync
	[UdonSynced] private bool IsOn;
	// Internal
	private bool LocalOn;
	private bool Ready;
	private Vector3 RestPos;
	private bool HasRest;

	private void Start() {
		// Give LiquidParticles a moment to run its own Start (it sets up the emission module there)
		SendCustomEventDelayedFrames(nameof(_Ready), 2);
	}
	public void _Ready() {
		Ready = true;
		Apply();
	}
	public override void Interact() {
		if(!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
		IsOn = !IsOn;
		RequestSerialization();
		Apply();
		if(ClickSound != null) ClickSound.Play();
		if(PressVisual != null) {
			if(!HasRest) { RestPos = PressVisual.localPosition; HasRest = true; }
			PressVisual.localPosition = RestPos - PressVisual.parent.InverseTransformDirection(transform.forward) * 0.006f;
			SendCustomEventDelayedSeconds(nameof(_Release), 0.12f);
		}
	}
	public override void OnDeserialization() { Apply(); }
	public void _Release() {
		if(HasRest) PressVisual.localPosition = RestPos;
	}
	private void Apply() {
		if(!Ready || Tap == null || IsOn == LocalOn) return;
		LocalOn = IsOn;
		Tap.ToggleEmission();
		if(Lamp != null) Lamp.sharedMaterial = IsOn ? LampOn : LampOff;
		if(PourSound != null) {
			if(IsOn) PourSound.Play();
			else PourSound.Stop();
		}
	}
}
