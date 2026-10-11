using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace DrinkSystem {
	/// <summary>
	/// One place a cup locks into: a nozzle with its drink card (pour button), or the cup customizer pad.
	/// Nothing here is synced. The cup locked in the slot carries the state (locked / pouring / fill / style) and
	/// drives these visuals on every client.
	/// </summary>
	[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
	public class DrinkSlot : UdonSharpBehaviour {
		[Header("Drink")]
		[Tooltip("Index of the DrinkFlavor this nozzle pours (child order under the machine's FlavorsParent).")]
		public int FlavorIndex;
		[Tooltip("Custom drink: pours the fruit flavor of the glass's FRUIT (picked on the finishing pad) instead of FlavorIndex.")]
		public bool UseFruitFlavor;
		[Tooltip("Finishing pad: no nozzle, the finishing buttons change the glass locked here (straw, fruit, tint, logo).")]
		public bool IsCustomizer;
		[Header("Placement")]
		[Tooltip("Where a locked cup sits. The cup's pivot is the bottom of the cup.")]
		public Transform SnapPoint;
		[Tooltip("Shown while you hold a cup close enough to lock it here.")]
		public GameObject Highlight;
		[Header("Pour visuals")]
		public Transform Nozzle;
		[Tooltip("Stream mesh under the nozzle: pivot at the top, 1 unit long, pointing down. Scaled to reach the drink.")]
		public Renderer Stream;
		public ParticleSystem Splash;
		[Header("Audio")]
		public AudioSource LoopSource;
		public AudioSource OneShotSource;
		[Header("UI")]
		public TextMeshProUGUI StatusText;
		public Image FillBar;
		public Image StatusLight;

		[HideInInspector()] public int Index;
		[HideInInspector()] public DrinkCup Cup;
		// Internal
		private DrinkMachine MB;
		private MaterialPropertyBlock StreamProps;
		private int IdColor;
		private Transform StreamTransform;
		private Transform SplashTransform;
		private float StreamParentScale = 1f;
		private Vector3 NozzlePos;
		private bool Pouring;
		private string LastLabel = "";
		private float LastBar = -1f;
		private float DenyUntil;

		public DrinkSlot Init(DrinkMachine mb, int index) {
			MB = mb;
			Index = index;
			FlavorIndex = Mathf.Clamp(FlavorIndex, 0, MB.FlavorCount - 1);
			if(FillBar != null) FillBar.color = Color.Lerp(MB.FColorMain[FlavorIndex], Color.white, 0.3f);
			if(Nozzle != null) NozzlePos = Nozzle.position;
			IdColor = VRCShader.PropertyToID("_Color");
			if(Stream != null) {
				StreamProps = new MaterialPropertyBlock();
				StreamTransform = Stream.transform;
				if(StreamTransform.parent != null) StreamParentScale = Mathf.Max(0.0001f, StreamTransform.parent.lossyScale.y);
				Stream.enabled = false;
			}
			if(Splash != null) {
				SplashTransform = Splash.transform;
				Splash.Stop();
			}
			if(Highlight != null) Highlight.SetActive(false);
			RefreshUI();
			return this;
		}
		// Button (UI / finger poke)
		public void Press() {
			if(IsCustomizer) return;
			if(Cup == null) { _Deny("PUT A GLASS HERE FIRST"); return; }
			if(!Cup.Pouring) {
				if(Cup.FillAmount >= 0.999f) { _Deny("FULL! GO TO STEP 4"); return; }
				if(UseFruitFlavor && Cup.Fruit < 0) { _Deny("PICK A FRUIT FIRST"); return; }
			}
			Cup._TogglePour();
		}
		public int GetPourFlavor(int fruit) {
			if(UseFruitFlavor) return Mathf.Clamp(MB.FruitFlavorStart + Mathf.Max(0, fruit), 0, MB.FlavorCount - 1);
			return FlavorIndex;
		}
		// Called by the cup on every client
		public void _StartPour(bool effects, int flavor) {
			Pouring = true;
			Color c = MB.FColorMain[flavor];
			if(Stream != null) {
				c.a = Mathf.Lerp(0.95f, 0.55f, MB.FTransparency[flavor]);
				StreamProps.SetColor(IdColor, c);
				Stream.SetPropertyBlock(StreamProps);
				Stream.enabled = true;
			}
			if(Splash != null) {
				ParticleSystem.MainModule mm = Splash.main;
				mm.startColor = Color.Lerp(MB.FColorMain[flavor], Color.white, 0.55f);
				Splash.Play();
			}
			if(FillBar != null) FillBar.color = Color.Lerp(MB.FColorMain[flavor], Color.white, 0.3f);
			if(LoopSource != null && MB.ClipPourLoop != null) {
				LoopSource.clip = MB.ClipPourLoop;
				LoopSource.loop = true;
				LoopSource.Play();
			}
			if(effects) PlayOneShot(MB.ClipPourStart);
			RefreshUI();
		}
		public void _StopPour(bool effects, bool full) {
			Pouring = false;
			if(Stream != null) Stream.enabled = false;
			if(Splash != null) Splash.Stop();
			if(LoopSource != null) LoopSource.Stop();
			if(effects) {
				PlayOneShot(MB.ClipPourStop);
				if(full) PlayOneShot(MB.ClipPourFull);
			}
			RefreshUI();
		}
		public void _PourTick(float surfaceY, float fill) {
			if(!Pouring) return;
			float length = Mathf.Max(0.005f, NozzlePos.y - surfaceY);
			if(StreamTransform != null) StreamTransform.localScale = new Vector3(1f, length / StreamParentScale, 1f);
			if(SplashTransform != null) SplashTransform.position = new Vector3(NozzlePos.x, surfaceY, NozzlePos.z);
			// The pour rises in pitch as the cup fills, like a real cup
			if(LoopSource != null) LoopSource.pitch = 0.9f + fill * 0.35f;
			SetBar(fill);
		}
		public void RefreshUI() {
			if(IsCustomizer) MB._RefreshCustomizerUI();
			if(Time.time < DenyUntil) return;
			Color light;
			if(Cup == null) {
				SetLabel(IsCustomizer ? "PUT YOUR GLASS ON THE PAD" : "PLACE A GLASS HERE");
				light = new Color(0.25f, 0.25f, 0.3f, 1f);
				SetBar(0f);
			}
			else if(IsCustomizer) {
				SetLabel("TAP A STRAW, FRUIT & GLASS STYLE");
				light = new Color(1f, 0.78f, 0.3f, 1f);
			}
			else {
				if(Cup.Pouring) { SetLabel("POURING... (PRESS TO STOP)"); light = new Color(1f, 0.6f, 0.15f, 1f); }
				else if(Cup.FillAmount >= 0.999f) { SetLabel("FULL! GO TO STEP 4"); light = new Color(0.3f, 1f, 0.45f, 1f); }
				else { SetLabel("PRESS POUR"); light = new Color(1f, 0.78f, 0.3f, 1f); }
				SetBar(Cup.VisualFill);
			}
			if(StatusLight != null) StatusLight.color = light;
		}
		public void _Deny(string msg) {
			PlayOneShot(MB.ClipButtonDeny);
			SetLabel(msg);
			if(StatusLight != null) StatusLight.color = new Color(1f, 0.2f, 0.2f, 1f);
			DenyUntil = Time.time + 0.9f;
			SendCustomEventDelayedSeconds(nameof(_EndDeny), 0.95f);
		}
		public void _EndDeny() { RefreshUI(); }
		// Internal
		private void SetLabel(string label) {
			if(StatusText == null || label == LastLabel) return;
			LastLabel = label;
			StatusText.text = label;
		}
		private void SetBar(float fill) {
			// Only touch the UI when it visibly changes (each change rebuilds the canvas)
			if(FillBar == null || fill == LastBar) return;
			if(Mathf.Abs(fill - LastBar) < 0.01f && fill > 0f && fill < 1f) return;
			LastBar = fill;
			FillBar.fillAmount = fill;
		}
		private void PlayOneShot(AudioClip clip) {
			if(clip != null && OneShotSource != null) OneShotSource.PlayOneShot(clip);
		}
	}
}
