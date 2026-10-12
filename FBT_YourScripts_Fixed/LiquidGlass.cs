using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class LiquidGlass : UdonSharpBehaviour {
	// References
	[HideInInspector()] public Transform LiquidTransform;
	[HideInInspector()] public Renderer LiquidRenderer;
	[HideInInspector()] public Mesh LiquidMesh;
	// Drink visuals
	[HideInInspector(), UdonSynced] public float FillAmount;
	[HideInInspector(), UdonSynced] public Color CurrentColorMain;
	[HideInInspector(), UdonSynced] public Color CurrentColorFoam;
	[HideInInspector(), UdonSynced] public Color CurrentColorEdge;
	[HideInInspector(), UdonSynced] public float CurrentTransparency;
	[HideInInspector(), UdonSynced] public float CurrentFoamHeight;
	[HideInInspector(), UdonSynced] public float CurrentFoamSmoothness;
	[HideInInspector(), UdonSynced] public float CurrentEdgeStrength;
	[HideInInspector(), UdonSynced] public float CurrentEdgePower;
	// PP effects
	[UdonSynced] public float CurrentEffect1Intensity;
	[UdonSynced] public float CurrentEffect2Intensity;
	[UdonSynced] public float CurrentEffect3Intensity;
	// Internal
	private ManagerBar MB;
	private bool IsBeingDrinked;
	private Material LiquidMaterial;     // FBT FIX: cached (renderer.material every call is wasteful)
	private Vector3[] LiquidVertices;    // FBT FIX: copied once (mesh.vertices makes a new copy every call)
	private Vector3 LastPos;             // FBT FIX: last state the fill line was computed for
	private Quaternion LastRot;
	private float LastFill = -1f;

	private void Start() {
		LiquidTransform = transform.GetChild(0);
		LiquidRenderer = LiquidTransform.GetComponent<Renderer>();
		LiquidMesh = LiquidTransform.GetComponent<MeshFilter>().sharedMesh;
		LiquidVertices = LiquidMesh.vertices;
		LiquidMaterial = LiquidRenderer.material;
		FillAmount = 0;
		IsBeingDrinked = false;
	}
	private void Update() {
		if(Networking.IsOwner(gameObject)) {
			if(IsBeingDrinked) {
				float deltaTime = Time.deltaTime;
				float amountDrinked = ChangeFillAmount(-MB.DrinkingSpeed * deltaTime);
				float intoxicationFactor = MB.IntoxicationFactor * amountDrinked;
				MB.CurrentIntoxication[0] += CurrentEffect1Intensity * intoxicationFactor;
				MB.CurrentIntoxication[1] += CurrentEffect2Intensity * intoxicationFactor;
				MB.CurrentIntoxication[2] += CurrentEffect3Intensity * intoxicationFactor;
			}
		}
	}
	public float ChangeFillAmount(float amount, LiquidParticles lp = null) {
		float newFillAmount = Mathf.Clamp(FillAmount + amount, 0, 1);
		if(amount > 0) {
			if(FillAmount == 0) {
				// Visuals
				CurrentColorMain = lp.ColorMain;
				CurrentColorFoam = lp.ColorFoam;
				CurrentColorEdge = lp.ColorEdge;
				CurrentTransparency = lp.Transparency;
				CurrentFoamHeight = lp.FoamHeight;
				CurrentFoamSmoothness = lp.FoamSmoothness;
				CurrentEdgeStrength = lp.EdgeStrength;
				CurrentEdgePower = lp.EdgePower;
				// Effects
				CurrentEffect1Intensity = lp.Effect1Intensity;
				CurrentEffect2Intensity = lp.Effect2Intensity;
				CurrentEffect3Intensity = lp.Effect3Intensity;
			}
			else {
				float ratio = 1.0f / newFillAmount * amount;
				Color newColor;
				// Main color
				newColor = Color.Lerp(CurrentColorMain, lp.ColorMain, ratio);
				newColor.a = 1.0f;
				CurrentColorMain = newColor;
				// Foam color
				newColor = Color.Lerp(CurrentColorFoam, lp.ColorFoam, ratio);
				newColor.a = 1.0f;
				CurrentColorFoam = newColor;
				// Edge color
				newColor = Color.Lerp(CurrentColorEdge, lp.ColorEdge, ratio);
				newColor.a = 1.0f;
				CurrentColorEdge = newColor;
				// Transparency
				CurrentTransparency = Mathf.Lerp(CurrentTransparency, lp.Transparency, ratio);
				// Foam height
				CurrentFoamHeight = Mathf.Lerp(CurrentFoamHeight, lp.FoamHeight, ratio);
				// Foam smoothness
				CurrentFoamSmoothness = Mathf.Lerp(CurrentFoamSmoothness, lp.FoamSmoothness, ratio);
				// Edge strength
				CurrentEdgeStrength = Mathf.Lerp(CurrentEdgeStrength, lp.EdgeStrength, ratio);
				// Edge power
				CurrentEdgePower = Mathf.Lerp(CurrentEdgePower, lp.EdgePower, ratio);
				// Effects
				CurrentEffect1Intensity = Mathf.Lerp(CurrentEffect1Intensity, lp.Effect1Intensity, ratio);
				CurrentEffect2Intensity = Mathf.Lerp(CurrentEffect2Intensity, lp.Effect2Intensity, ratio);
				CurrentEffect3Intensity = Mathf.Lerp(CurrentEffect3Intensity, lp.Effect3Intensity, ratio);
			}
		}
		float change = FillAmount - newFillAmount;
		FillAmount = newFillAmount;
		RequestSerialization();
		UpdateAllMaterialParameters();
		return change;
	}
	public override void OnDeserialization() {
		UpdateAllMaterialParameters();
	}
	public void UpdateFillHeight(float brimFullOffset) {
		if(LiquidMesh != null && LiquidTransform != null) {
			// FBT FIX: nothing moved and the fill didn't change -> the fill line is still right, skip the vertex loop
			Vector3 pos = LiquidTransform.position;
			Quaternion rot = LiquidTransform.rotation;
			if(pos == LastPos && rot == LastRot && FillAmount == LastFill) return;
			LastPos = pos;
			LastRot = rot;
			LastFill = FillAmount;
			Vector2 minMaxY = FindMinMax(LiquidMesh, LiquidTransform);
			LiquidMaterial.SetVector("_GlassRemap", minMaxY);
			float lineY = minMaxY.x + FillAmount * (minMaxY.y - minMaxY.x - brimFullOffset);
			LiquidMaterial.SetFloat("_FillHeight", lineY);
		}
	}
	public LiquidGlass Init(ManagerBar mb) {
		MB = mb;
		return this;
	}
	private Vector2 FindMinMax(Mesh targetMesh, Transform targetTransform) {
		Vector3[] vertices = LiquidVertices; // FBT FIX: cached copy
		float min = float.MaxValue;
		float max = float.MinValue;
		foreach(Vector3 v in vertices) {
			Vector3 vt = targetTransform.TransformPoint(v);
			if(vt.y < min) min = vt.y;
			if(vt.y > max) max = vt.y;
		}
		return new Vector2(min, max);
	}
	private void UpdateAllMaterialParameters() {
		if(LiquidMaterial == null) LiquidMaterial = LiquidRenderer.material; // FBT FIX: can arrive before Start
		LiquidMaterial.SetColor("_ColorMain", CurrentColorMain);
		LiquidMaterial.SetColor("_FoamColor", CurrentColorFoam);
		LiquidMaterial.SetColor("_EdgeColor", CurrentColorEdge);
		LiquidMaterial.SetFloat("_Transparency", CurrentTransparency);
		LiquidMaterial.SetFloat("_FoamHeight", CurrentFoamHeight);
		LiquidMaterial.SetFloat("_FoamLineSmoothness", CurrentFoamSmoothness);
		LiquidMaterial.SetFloat("_EdgeStrength", CurrentEdgeStrength);
		LiquidMaterial.SetFloat("_EdgePower", CurrentEdgePower);
		LastFill = -1f; // FBT FIX: make the fill line update next frame
	}
	public override void OnPickupUseUp() { IsBeingDrinked = false; }
	public override void OnPickupUseDown() { IsBeingDrinked = true; }
}