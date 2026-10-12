using UdonSharp;
using UnityEngine;

/// <summary>RESET GLASSES button: puts every glass nobody is holding back on the tray, empty (Interact).</summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class GlassRespawnButton : UdonSharpBehaviour {
	public GlassLock[] Glasses;
	public AudioSource ClickSound;
	public Transform PressVisual;
	// Internal
	private Vector3 RestPos;
	private bool HasRest;

	public override void Interact() {
		for(int i = 0; i < Glasses.Length; i++) {
			if(Glasses[i] != null) Glasses[i].ForceRespawn();
		}
		if(ClickSound != null) ClickSound.Play();
		if(PressVisual != null) {
			if(!HasRest) { RestPos = PressVisual.localPosition; HasRest = true; }
			PressVisual.localPosition = RestPos - Vector3.forward * 0.006f;
			SendCustomEventDelayedSeconds(nameof(_Release), 0.12f);
		}
	}
	public void _Release() {
		if(HasRest) PressVisual.localPosition = RestPos;
	}
}
