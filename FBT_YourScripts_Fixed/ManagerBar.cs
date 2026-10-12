using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

public class ManagerBar : UdonSharpBehaviour {
	public GameObject ParticlesParent;
	public GameObject GlassesParent;
	[Range(0.0f, 0.01f)] public float BrimFullOffset;
	public Transform DrunkEffectTransform;

	[Range(0.0f, 1.0f)] public float[] CurrentIntoxication;
	[Range(2.0f, 5.0f)] public float IntoxicationFactor;
	[Range(0.0f, 1.0f)] public float DetoxicationSpeed;
	[Range(0.0f, 1.0f)] public float DrinkingSpeed;
	// Internal
	private LiquidGlass[] GlassesGO;
	private LiquidParticles[] LiquidParticleSpawners;
	private Material DrunkEffectMaterial;
	private float LastPP1 = -1f; // FBT FIX: last values sent to the effect material
	private float LastPP2 = -1f;
	private float LastPP3 = -1f;

	private void Start() {
		// Glasses
		LiquidGlass[] lgs = GlassesParent.GetComponentsInChildren<LiquidGlass>();
		GlassesGO = new LiquidGlass[lgs.Length];
		for(int i = 0; i < lgs.Length; i++) GlassesGO[i] = lgs[i].Init(this);
		// Particle Spawners
		LiquidParticles[] pps = ParticlesParent.GetComponentsInChildren<LiquidParticles>();
		LiquidParticleSpawners = new LiquidParticles[pps.Length];
		for(int i = 0; i < pps.Length; i++) LiquidParticleSpawners[i] = pps[i].Init(this);
		// Effect
		DrunkEffectMaterial = DrunkEffectTransform.GetComponent<Renderer>().material;
	}
	private void Update() {
		if(Networking.LocalPlayer != null) {
			if(GlassesGO != null) {
				int count = GlassesGO.Length;
				// FBT FIX: each glass now skips the work when it hasn't moved and its fill hasn't changed
				for(int i = 0; i < count; i++) GlassesGO[i].UpdateFillHeight(BrimFullOffset);
			}
			// Drunk effect
			VRCPlayerApi.TrackingData localPlayerTD = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
			DrunkEffectTransform.position = localPlayerTD.position;
			for(int i = 0; i < 3; i++) {
				CurrentIntoxication[i] = Mathf.Clamp(CurrentIntoxication[i] - DetoxicationSpeed * Time.deltaTime, 0.0f, 1.0f);
			}
			// FBT FIX: only touch the material when the values actually change (the effect still works exactly the same)
			if(CurrentIntoxication[0] != LastPP1 || CurrentIntoxication[1] != LastPP2 || CurrentIntoxication[2] != LastPP3) {
				LastPP1 = CurrentIntoxication[0];
				LastPP2 = CurrentIntoxication[1];
				LastPP3 = CurrentIntoxication[2];
				UpdatePP(CurrentIntoxication[0], CurrentIntoxication[1], CurrentIntoxication[2]);
			}
		}
	}
	// Interfaces
	public void AddFillAmount(GameObject hitGO, LiquidParticles lp, float amount) {
		int countGlasses = GlassesGO.Length;
		for(int i = 0; i < countGlasses; i++) {
			if(GlassesGO[i].LiquidTransform == hitGO.transform) {
				// FBT FIX: check the owner of the GLASS (the object with LiquidGlass), not its liquid child.
				// The liquid child never changes owner, so before only the world master could fill glasses.
				if(Networking.IsOwner(GlassesGO[i].gameObject)) GlassesGO[i].ChangeFillAmount(amount, lp);
				break;
			}
		}
	}
	public void UpdatePP(float value1, float value2, float value3) {
		DrunkEffectMaterial.SetFloat("Chromatic_Aberration", value1 > 0 ? 2 : 0);
		DrunkEffectMaterial.SetFloat("_CA_amplitude", value1 * 0.4f);

		DrunkEffectMaterial.SetFloat("Distorsion", value2 > 0 ? 1 : 0);
		DrunkEffectMaterial.SetVector("_DistorsionWave", new Vector4(value2 * 0.02f, value2 * 0.02f, 1, 0));

		DrunkEffectMaterial.SetFloat("Neon", value3 > 0 ? 1 : 0);
		DrunkEffectMaterial.SetFloat("_NeonColorAlpha", value3 * 0.4f);
	}
}
