using UdonSharp;
using UnityEngine;

namespace LoopLand.Drinks {
	/// <summary>
	/// UI push button. A Unity UI Button calls Press() (laser pointer on PC VR / Quest, mouse on desktop),
	/// and the DrinkMachine calls it when a VR index finger pokes the button. Plays the click, squashes the
	/// visual and sends EventName to Target. No networking: the target decides what to sync.
	/// </summary>
	[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
	public class DrinkButton : UdonSharpBehaviour {
		public UdonSharpBehaviour Target;
		public string EventName = "Press";
		[Tooltip("Scaled down while pressed (usually the round button image).")]
		public Transform PressVisual;
		public AudioSource Audio;
		public AudioClip ClickClip;
		[Range(0.05f, 1.0f)] public float Cooldown = 0.3f;
		// Internal
		private float LastPress = -10f;
		private Vector3 RestScale;
		private bool HasRest;

		public void Press() {
			if(Time.time - LastPress < Cooldown) return;
			LastPress = Time.time;
			if(Audio != null && ClickClip != null) Audio.PlayOneShot(ClickClip);
			if(PressVisual != null) {
				if(!HasRest) { RestScale = PressVisual.localScale; HasRest = true; }
				PressVisual.localScale = RestScale * 0.86f;
				SendCustomEventDelayedSeconds(nameof(_Release), 0.11f);
			}
			if(Target != null) Target.SendCustomEvent(EventName);
		}
		public void _Release() {
			if(HasRest) PressVisual.localScale = RestScale;
		}
	}
}
