using UdonSharp;
using UnityEngine;

namespace DrinkSystem {
	/// <summary>
	/// One drink type (like LiquidParticles): how the liquid looks, how fast it pours and its optional screen effects.
	/// Put these under the machine's FlavorsParent. A DrinkSlot picks one by index.
	/// </summary>
	[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
	public class DrinkFlavor : UdonSharpBehaviour {
		public string DisplayName = "Cola";
		[Tooltip("Fraction of a cup filled per second (0.2 = a full cup in 5 seconds).")]
		[Range(0.02f, 1.0f)] public float FillSpeed = 0.2f;
		[Range(0.0f, 1.0f)] public float Transparency = 0.1f;
		public Color ColorMain = new Color(0.18f, 0.07f, 0.02f, 1f);
		public Color ColorFoam = new Color(0.79f, 0.64f, 0.48f, 1f);
		public Color ColorEdge = new Color(0.42f, 0.21f, 0.09f, 1f);
		public float EdgePower = 3f;
		public float EdgeStrength = 1f;
		[Tooltip("Foam thickness as a fraction of the cup height.")]
		public float FoamHeight = 0.05f;
		public float FoamSmoothness = 0.03f;

		[Range(0.0f, 1.0f)] public float Effect1Intensity;
		[Range(0.0f, 1.0f)] public float Effect2Intensity;
		[Range(0.0f, 1.0f)] public float Effect3Intensity;
	}
}
