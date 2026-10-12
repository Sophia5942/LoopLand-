using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDKBase;
using Object = UnityEngine.Object;

/// <summary>
/// FBT Mayfair > Build Spirits Machine.
/// Builds a simple bar spirits dispenser that runs on YOUR scripts:
///   - every spout is a LiquidParticles stream (your script), switched on/off by a "PUSH" button (BarTapButton),
///   - your Glasses2025 glasses (LiquidGlass + TransformSync) go on a tray next to it, each with a GlassLock so it
///     locks under a spout when you put it down,
///   - one ManagerBar (your script) connects the taps and the glasses and drives the drunk effect.
/// Select your bar counter first to build on top of it.
/// </summary>
public static class BarMachineBuilder
{
    private const string Root = "Assets/FBTBar";
    private const string Gen = Root + "/Generated";
    private const string RootName = "FBT Spirits Machine";
    private const string GlassesPrefabName = "Glasses2025";

    private struct Spirit
    {
        public string Name; public Color Liquid, Bottle; public float Transparency, E1, E2, E3;
        public Spirit(string name, string liquid, string bottle, float transparency, float e1, float e2, float e3)
        { Name = name; Liquid = Hex(liquid); Bottle = Hex(bottle); Transparency = transparency; E1 = e1; E2 = e2; E3 = e3; }
    }

    private static readonly Spirit[] Spirits =
    {
        new Spirit("VODKA", "E8F0FF", "C9D8E8", 0.85f, 0.5f, 0.2f, 0.1f),
        new Spirit("GIN", "D8F2DE", "4E8F6A", 0.80f, 0.3f, 0.4f, 0.1f),
        new Spirit("RUM", "7A3A12", "3A1E0E", 0.30f, 0.4f, 0.3f, 0.3f),
        new Spirit("WHISKEY", "C06A1E", "6E3A12", 0.25f, 0.6f, 0.4f, 0.2f),
        new Spirit("TEQUILA", "F2C860", "B88A2E", 0.45f, 0.3f, 0.6f, 0.5f),
        new Spirit("RED WINE", "6E0A1C", "2A0A10", 0.20f, 0.2f, 0.3f, 0.6f),
    };

    // layout (meters; the counter top is y = 0, players stand on +Z)
    private const float Spacing = 0.17f, Width = 1.08f, TrayTop = 0.06f, SpoutZ = 0.06f;
    private static float SpoutTip = 0.34f;

    private static TMP_FontAsset font;
    private static readonly List<UdonSharpBehaviour> made = new List<UdonSharpBehaviour>();
    private static readonly System.Random rng = new System.Random(5);

    [MenuItem("FBT Mayfair/Build Spirits Machine", priority = 0)]
    public static void Build()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorUtility.DisplayDialog("FBT Mayfair", "Unity is still compiling or importing (spinner bottom-right).\n\nWait until it finishes, then click Build again.", "OK");
            return;
        }
        font = TMP_Settings.instance != null ? TMP_Settings.defaultFontAsset : null;
        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font == null)
        {
            if (EditorUtility.DisplayDialog("FBT Mayfair", "TextMesh Pro Essential Resources are missing.\nImport them, then click Build again.", "Import now", "Cancel"))
                EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources");
            return;
        }

        GameObject target = Selection.activeGameObject;
        if (target != null && target.name == RootName) target = null;
        Bounds tb = default;
        bool onTarget = target != null && TryBounds(target, out tb);

        GameObject old = GameObject.Find(RootName);
        if (old != null)
        {
            if (!EditorUtility.DisplayDialog("FBT Mayfair", "A '" + RootName + "' already exists in this scene. Replace it?", "Replace", "Cancel")) return;
            Undo.DestroyObjectImmediate(old);
        }

        made.Clear();
        GameObject root = null;
        try
        {
            EditorUtility.DisplayProgressBar("FBT Mayfair", "Compiling UdonSharp programs...", 0.1f);
            EnsurePrograms();
            EditorUtility.DisplayProgressBar("FBT Mayfair", "Building the spirits machine...", 0.4f);
            root = BuildAll(!onTarget);
            if (onTarget) root.transform.position = new Vector3(tb.center.x - 0.3f, tb.max.y, tb.center.z);
            else
            {
                Vector3 p = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : new Vector3(0f, 0f, 3f);
                root.transform.position = new Vector3(p.x, 1.0f, p.z);
            }
            EditorUtility.DisplayProgressBar("FBT Mayfair", "Wiring Udon behaviours...", 0.9f);
            Wire();
            Undo.RegisterCreatedObjectUndo(root, "Build Spirits Machine");
            EditorSceneManager.MarkSceneDirty(root.scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("FBT Mayfair", "Done! '" + RootName + "' is in your scene" + (onTarget ? " on top of '" + target.name + "'" : "") +
                ", with " + glassCount + " of your glasses on the tray next to it.\n\nSave your scene (Ctrl+S). Then press Play: pick up a glass, put it under a spout (it locks in), press PUSH to pour, press PUSH again to stop, hold Use to drink.", "OK");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("FBT Mayfair - build failed", "The machine could not be built:\n\n" + e.Message +
                "\n\nPlease screenshot this message (and the red error in Window > General > Console) and send it.", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ------------------------------------------------------------------ building

    private static GameObject BuildAll(bool withCounter)
    {
        Material wood = LitMat("Wood_Dark", Hex("2A1A12"), 0.1f, 0.75f, Color.black);
        Material black = LitMat("Black_Gloss", Hex("0E0D11"), 0.3f, 0.9f, Color.black);
        Material brass = LitMat("Brass", Hex("C9A04A"), 1f, 0.85f, new Color(0.18f, 0.12f, 0.03f));
        Material chrome = LitMat("Chrome", Hex("D8DCE2"), 1f, 0.92f, Color.black);
        Material grate = LitMat("Grate", Hex("2A2A2E"), 0.8f, 0.6f, Color.black, GrateTex());
        grate.mainTextureScale = new Vector2(10f, 1f);
        EditorUtility.SetDirty(grate);
        Material warm = LitMat("Light_Warm", Hex("FFD9A0"), 0f, 0.5f, Hex("FFC070") * 2f);
        Material lampOff = LitMat("Lamp_Off", Hex("3A2A10"), 0f, 0.6f, Color.black);
        Material lampOn = LitMat("Lamp_On", Hex("FFB040"), 0f, 0.6f, Hex("FFB040") * 3f);
        Material counterMat = LitMat("Counter", Hex("16141B"), 0.2f, 0.85f, Color.black);
        Material particleMat = ParticleMat();
        AudioClip pour = Loop("bar_pour_loop", 2.4f, 0.4f, PourSample());
        AudioClip click = Wav("bar_button", 0.14f, t => (Noise() * 0.5f + Sin(2600f, t) * 0.45f) * Env(t, 0.0005f, 0.005f) + Sin(240f, t) * Env(t, 0.001f, 0.02f) * 0.45f);
        AudioClip lockClip = Wav("bar_glass_lock", 0.24f, t =>
            (Noise() * Env(t, 0.0005f, 0.006f) * 0.7f + (Sin(1650f, t) * 0.45f + Sin(2740f, t) * 0.3f) * Env(t, 0.001f, 0.05f) + Sin(160f - 60f * t, t) * Env(t, 0.002f, 0.04f) * 0.5f
             + (t > 0.05f ? (Noise() * 0.5f + Sin(3200f, t) * 0.45f) * Env(t - 0.05f, 0.0005f, 0.007f) : 0f)) * 0.75f);
        AudioClip respawnClip = Wav("bar_glass_respawn", 0.2f, t => Sin(700f - 2500f * Mathf.Min(t, 0.09f), t) * Env(t, 0.001f, 0.035f) * 0.6f + Noise() * Env(t, 0.0005f, 0.004f) * 0.3f);
        AudioClip unlockClip = Wav("bar_glass_unlock", 0.15f, t => (Noise() * 0.4f + Sin(2900f, t) * 0.4f) * Env(t, 0.0005f, 0.006f));

        var root = new GameObject(RootName);
        Transform rt = root.transform;

        // --- your glasses (first: the spouts are placed high enough for the tallest one)
        GameObject glasses = InstantiateGlasses(rt, out float tallest);
        SpoutTip = Mathf.Max(0.32f, TrayTop + tallest + 0.06f);
        float railY = SpoutTip + 0.04f, labelY = railY + 0.08f, bottleBase = labelY + 0.08f;
        float topY = bottleBase + 0.30f;

        // --- body
        var body = new GameObject("Machine").transform;
        body.SetParent(rt, false);
        if (withCounter) Box(body, "Counter", new Vector3(0.25f, -0.5f, 0f), new Vector3(Width + 1.0f, 1f, 0.7f), counterMat, true);
        Box(body, "Drip Tray", new Vector3(0f, TrayTop * 0.5f, SpoutZ), new Vector3(Width, TrayTop, 0.24f), black, true);
        Box(body, "Grate", new Vector3(0f, TrayTop + 0.002f, SpoutZ), new Vector3(Width - 0.04f, 0.004f, 0.2f), grate);
        Box(body, "Tray Trim", new Vector3(0f, TrayTop, SpoutZ + 0.121f), new Vector3(Width + 0.004f, 0.006f, 0.004f), brass);
        Box(body, "Back", new Vector3(0f, topY * 0.5f, -0.12f), new Vector3(Width, topY, 0.12f), wood, true);
        Box(body, "Back Trim", new Vector3(0f, topY, -0.06f), new Vector3(Width + 0.004f, 0.008f, 0.008f), brass);
        Box(body, "Spout Rail", new Vector3(0f, railY, SpoutZ - 0.03f), new Vector3(Width - 0.02f, 0.03f, 0.03f), chrome);
        Box(body, "Rail Arm L", new Vector3(-Width * 0.5f + 0.02f, railY, (SpoutZ - 0.06f - 0.06f) * 0.5f), new Vector3(0.02f, 0.02f, 0.12f), chrome);
        Box(body, "Rail Arm R", new Vector3(Width * 0.5f - 0.02f, railY, (SpoutZ - 0.06f - 0.06f) * 0.5f), new Vector3(0.02f, 0.02f, 0.12f), chrome);
        Box(body, "Label Board", new Vector3(0f, labelY, -0.055f), new Vector3(Width, 0.12f, 0.012f), black, true);
        Box(body, "Glow", new Vector3(0f, bottleBase - 0.005f, -0.04f), new Vector3(Width - 0.04f, 0.004f, 0.03f), warm);
        Box(body, "Glass Tray", new Vector3(Width * 0.5f + 0.32f, 0.006f, 0.02f), new Vector3(0.52f, 0.012f, 0.3f), black, true);

        // brand sign on top
        Texture2D logo = FindLogo();
        if (logo != null)
        {
            var sign = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sign.name = "FBT Mayfair Logo";
            Object.DestroyImmediate(sign.GetComponent<Collider>());
            sign.transform.SetParent(body, false);
            float h = 0.14f, w = h * logo.width / Mathf.Max(1f, logo.height);
            sign.transform.localPosition = new Vector3(0f, topY + 0.08f, -0.06f);
            sign.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            sign.transform.localScale = new Vector3(w, h, 1f);
            sign.GetComponent<Renderer>().sharedMaterial = SaveMat("Logo", Shader.Find("Unlit/Transparent"), m => m.mainTexture = logo);
            Box(body, "Sign Post", new Vector3(0f, topY + 0.04f, -0.07f), new Vector3(0.04f, 0.08f, 0.01f), brass);
        }

        // --- taps: bottle, spout, your LiquidParticles stream, label and PUSH button
        var taps = new GameObject("Taps (LiquidParticles)").transform;
        taps.SetParent(rt, false);
        var snaps = new GameObject("Glass Lock Points").transform;
        snaps.SetParent(rt, false);
        for (int i = 0; i < Spirits.Length; i++)
        {
            Spirit s = Spirits[i];
            float x = -Spacing * 2.5f + i * Spacing;
            // upside-down bottle in the optic
            Material bottleMat = LitMat("Bottle_" + i, s.Bottle, 0.2f, 0.95f, s.Bottle * 0.15f);
            Cyl(body, s.Name + " Bottle", new Vector3(x, bottleBase + 0.07f + 0.1f, -0.03f), 0.085f, 0.2f, bottleMat);
            Cyl(body, s.Name + " Neck", new Vector3(x, bottleBase + 0.035f, -0.03f), 0.03f, 0.07f, bottleMat);
            Cyl(body, s.Name + " Optic", new Vector3(x, bottleBase - 0.002f, -0.03f), 0.05f, 0.012f, chrome);
            // spout
            Cyl(body, s.Name + " Spout", new Vector3(x, railY - 0.03f, SpoutZ), 0.022f, 0.06f, brass);
            // stream (your LiquidParticles)
            var tapGo = new GameObject(s.Name + " Tap");
            tapGo.transform.SetParent(taps, false);
            tapGo.transform.localPosition = new Vector3(x, SpoutTip, SpoutZ);
            tapGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // emit straight down
            Stream(tapGo, particleMat);
            var lp = Add<LiquidParticles>(tapGo);
            lp.FillSpeed = 0.12f;
            lp.Transparency = s.Transparency;
            lp.ColorMain = s.Liquid;
            lp.ColorFoam = Color.Lerp(s.Liquid, Color.white, 0.6f);
            lp.ColorEdge = Color.Lerp(s.Liquid, Color.white, 0.3f);
            lp.EdgePower = 2f;
            lp.EdgeStrength = 0.6f;
            lp.FoamHeight = 0.003f;
            lp.FoamSmoothness = 0.002f;
            lp.Effect1Intensity = s.E1;
            lp.Effect2Intensity = s.E2;
            lp.Effect3Intensity = s.E3;
            made.Add(lp);
            AudioSource pourSrc = Audio(tapGo, 0.5f, 5f);
            pourSrc.clip = pour;
            pourSrc.loop = true;
            // lock point on the tray under the spout
            var snap = new GameObject(s.Name + " Lock Point").transform;
            snap.SetParent(snaps, false);
            snap.localPosition = new Vector3(x, TrayTop + 0.004f, SpoutZ);
            // label + PUSH button
            Label3D(body, s.Name + " Label", s.Name, new Vector3(x, labelY + 0.03f, -0.0485f), new Vector2(0.15f, 0.04f), Color.Lerp(s.Liquid, Color.white, 0.4f));
            PushButton(body, s, x, labelY - 0.025f, lp, lampOn, lampOff, black, brass, click, pourSrc);
        }

        // --- every glass locks under the spouts and respawns on the tray
        var locks = new List<GlassLock>();
        foreach (Transform g in glasses.transform)
        {
            if (g.GetComponent<LiquidGlass>() == null) continue;
            var gl = Add<GlassLock>(g.gameObject);
            gl.Glass = g.GetComponent<LiquidGlass>();
            gl.RespawnAfterSeconds = 180f;
            gl.RespawnClip = respawnClip;
            locks.Add(gl);
            gl.SnapPointsParent = snaps;
            gl.SnapRadius = 0.08f;
            gl.Body = g.GetComponent<Renderer>();
            gl.Sound = Audio(g.gameObject, 0.9f, 5f);
            gl.LockClip = lockClip;
            gl.UnlockClip = unlockClip;
            made.Add(gl);
        }

        // --- RESET GLASSES button on the front of the glass tray
        ResetButton(body, locks.ToArray(), brass, click);

        // --- your ManagerBar connects taps and glasses
        var mgrGo = new GameObject("Bar Manager (ManagerBar)");
        mgrGo.transform.SetParent(rt, false);
        var mb = Add<ManagerBar>(mgrGo);
        mb.ParticlesParent = taps.gameObject;
        mb.GlassesParent = glasses;
        mb.BrimFullOffset = 0.003f;
        mb.CurrentIntoxication = new float[3];
        mb.IntoxicationFactor = 3f;
        mb.DetoxicationSpeed = 0.05f;
        mb.DrinkingSpeed = 0.2f;
        mb.DrunkEffectTransform = DrunkEffect(rt);
        made.Add(mb);
        return root;
    }

    private static int glassCount;

    private static GameObject InstantiateGlasses(Transform rt, out float tallest)
    {
        tallest = 0.2f;
        GameObject prefab = null;
        foreach (string g in AssetDatabase.FindAssets(GlassesPrefabName + " t:Prefab"))
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
            if (prefab != null) break;
        }
        if (prefab == null) throw new Exception("Could not find your '" + GlassesPrefabName + "' prefab in the project. Import it, then click Build again.");
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        inst.name = "Glasses (LiquidGlass)";
        inst.transform.SetParent(rt, false);
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.identity;
        glassCount = 0;
        foreach (Transform g in inst.transform) if (g.GetComponent<LiquidGlass>() != null) glassCount++;
        if (glassCount == 0) throw new Exception("Your '" + GlassesPrefabName + "' prefab has no glasses with a LiquidGlass script on them.");
        // stand them on the glass tray, by what you actually see (the models may sit away from their pivots)
        if (TryBounds(inst, out Bounds b))
        {
            Vector3 trayCenter = rt.TransformPoint(new Vector3(Width * 0.5f + 0.32f, 0.012f, 0.02f));
            inst.transform.position += new Vector3(trayCenter.x - b.center.x, trayCenter.y - b.min.y, trayCenter.z - b.center.z);
            if (b.size.x > 3f || b.size.z > 3f)
                Debug.LogWarning("[FBT Mayfair] Your glasses are spread over " + b.size.x.ToString("0.0") + " m. Move them onto the tray if they look scattered.");
            tallest = 0f;
            foreach (Transform g in inst.transform)
                if (g.GetComponent<LiquidGlass>() != null && TryBounds(g.gameObject, out Bounds gb)) tallest = Mathf.Max(tallest, gb.size.y);
        }
        return inst;
    }

    /// <summary>Uses the drunk effect of a ManagerBar already in the scene (and offers to turn that one off), or a harmless placeholder.</summary>
    private static Transform DrunkEffect(Transform rt)
    {
        foreach (ManagerBar other in Object.FindObjectsByType<ManagerBar>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (other == null || other.DrunkEffectTransform == null || other.transform.IsChildOf(rt)) continue;
            if (EditorUtility.DisplayDialog("FBT Mayfair", "Found your existing ManagerBar on '" + other.name + "'.\n\n" +
                "Use its drunk effect for the new machine and switch the old ManagerBar script off? (Recommended: two ManagerBars would fight over the same effect. Only that script is switched off, nothing is deleted.)",
                "Yes, use it", "No"))
            {
                var oldUdon = UdonSharpEditorUtility.GetBackingUdonBehaviour(other);
                if (oldUdon != null)
                {
                    Undo.RecordObject(oldUdon, "Disable old ManagerBar");
                    oldUdon.enabled = false;
                }
                Debug.LogWarning("[FBT Mayfair] Switched off the old ManagerBar on '" + other.name + "'. Turn it back on (tick its UdonBehaviour) if you still need it.", other);
                return other.DrunkEffectTransform;
            }
            break;
        }
        // ManagerBar needs an effect object with a renderer; this one draws nothing (no mesh)
        var go = new GameObject("Drunk Effect (placeholder - put your effect here)");
        go.transform.SetParent(rt, false);
        go.AddComponent<MeshRenderer>().sharedMaterial = SaveMat("DrunkEffect_Placeholder", Shader.Find("Unlit/Color"), m => { });
        Debug.LogWarning("[FBT Mayfair] No drunk effect found. Drag your drunk effect object into 'Drunk Effect Transform' on 'Bar Manager (ManagerBar)'.");
        return go.transform;
    }

    private static void Stream(GameObject go, Material mat)
    {
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.duration = 1f;
        main.startLifetime = 0.8f;
        main.startSpeed = 0.6f;
        main.startSize = 0.012f;
        main.gravityModifier = 0.8f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;
        var em = ps.emission;
        em.rateOverTime = 140f;
        em.enabled = false; // off until PUSH
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Cone;
        sh.angle = 1f;
        sh.radius = 0.002f;
        var col = ps.collision;
        col.enabled = true;
        col.type = ParticleSystemCollisionType.World;
        col.mode = ParticleSystemCollisionMode.Collision3D;
        col.dampen = 1f;
        col.bounce = 0f;
        col.lifetimeLoss = 1f;
        col.sendCollisionMessages = true;
        col.quality = ParticleSystemCollisionQuality.Medium; // plenty for a pour, cheaper than High
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.04f;
        r.lengthScale = 1.5f;
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    private static void ResetButton(Transform parent, GlassLock[] locks, Material brass, AudioClip click)
    {
        var go = new GameObject("RESET GLASSES");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(Width * 0.5f + 0.32f, 0.02f, 0.02f + 0.15f + 0.012f);
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(0.16f, 0.045f, 0.03f);
        var cap = new GameObject("Cap").transform;
        cap.SetParent(go.transform, false);
        Box(cap, "Button", new Vector3(0f, 0f, 0f), new Vector3(0.15f, 0.036f, 0.012f), brass);
        Label3D(cap, "Text", "RESET GLASSES", new Vector3(0f, 0f, 0.0065f), new Vector2(0.14f, 0.026f), Hex("1A1206"));
        var btn = Add<GlassRespawnButton>(go);
        btn.Glasses = locks;
        btn.PressVisual = cap;
        btn.ClickSound = Audio(go, 0.9f, 5f);
        btn.ClickSound.clip = click;
        made.Add(btn);
        var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(btn);
        if (ub != null)
        {
            ub.interactText = "Put all glasses back";
            ub.proximity = 2f;
        }
    }

    private static void PushButton(Transform parent, Spirit s, float x, float y, LiquidParticles tap, Material lampOn, Material lampOff, Material black, Material brass, AudioClip click, AudioSource pour)
    {
        var go = new GameObject(s.Name + " PUSH");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(x, y, -0.045f);
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(0.11f, 0.045f, 0.03f);
        var cap = new GameObject("Cap").transform;
        cap.SetParent(go.transform, false);
        Box(cap, "Button", new Vector3(0f, 0f, 0.006f), new Vector3(0.1f, 0.036f, 0.012f), brass);
        Label3D(cap, "Text", "PUSH", new Vector3(-0.012f, 0f, 0.0125f), new Vector2(0.06f, 0.026f), Hex("1A1206"));
        var lamp = Cyl(cap, "Lamp", new Vector3(0.033f, 0f, 0.0125f), 0.014f, 0.002f, lampOff);
        lamp.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var btn = Add<BarTapButton>(go);
        btn.Tap = tap;
        btn.Lamp = lamp.GetComponent<Renderer>();
        btn.LampOn = lampOn;
        btn.LampOff = lampOff;
        btn.PressVisual = cap;
        btn.PourSound = pour;
        btn.ClickSound = Audio(go, 0.9f, 5f);
        btn.ClickSound.clip = click;
        made.Add(btn);
        var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(btn);
        if (ub != null)
        {
            ub.interactText = "Pour " + s.Name.ToLowerInvariant();
            ub.proximity = 2f;
        }
    }

    // ------------------------------------------------------------------ programs

    private static T Add<T>(GameObject go) where T : UdonSharpBehaviour
    {
        T c = UdonSharpUndo.AddComponent<T>(go);
        if (c == null)
            throw new Exception("UdonSharp could not add " + typeof(T).Name + ". Wait until Unity has finished compiling, check the Console for UdonSharp errors, then click Build again.");
        return c;
    }

    private static void EnsurePrograms()
    {
        Type[] types = { typeof(ManagerBar), typeof(LiquidParticles), typeof(LiquidGlass), typeof(TransformSync), typeof(BarTapButton), typeof(GlassLock), typeof(GlassRespawnButton) };
        var existing = new HashSet<Type>();
        foreach (string g in AssetDatabase.FindAssets("t:UdonSharpProgramAsset"))
        {
            var pa = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AssetDatabase.GUIDToAssetPath(g));
            if (pa != null && pa.sourceCsScript != null && pa.sourceCsScript.GetClass() != null) existing.Add(pa.sourceCsScript.GetClass());
        }
        bool created = false;
        foreach (Type t in types)
        {
            if (existing.Contains(t)) continue;
            MonoScript script = null;
            foreach (string g in AssetDatabase.FindAssets(t.Name + " t:MonoScript"))
            {
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(g));
                if (ms != null && ms.GetClass() == t) { script = ms; break; }
            }
            if (script == null) throw new Exception("Could not find the script " + t.Name + ".cs in your project.");
            var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = script;
            Dir(Gen + "/Programs");
            AssetDatabase.CreateAsset(asset, Gen + "/Programs/" + t.Name + ".asset");
            created = true;
        }
        if (created) AssetDatabase.SaveAssets();
        UdonSharpProgramAsset.CompileAllCsPrograms(true);
    }

    /// <summary>Copies every behaviour's settings into its Udon program. Brand-new scripts may still be compiling in
    /// the background ("outdated script version"): then it finishes this step by itself once UdonSharp is ready.</summary>
    private static void Wire()
    {
        try
        {
            CopyAll();
        }
        catch (Exception e)
        {
            if (!IsOutdated(e)) throw;
            Debug.Log("[FBT Mayfair] UdonSharp is still compiling the new scripts. The machine is built; its settings will be applied automatically in a moment.");
            pendingTries = 0;
            nextTry = EditorApplication.timeSinceStartup + 1.0;
            EditorApplication.update -= RetryWire;
            EditorApplication.update += RetryWire;
        }
    }

    private static int pendingTries;
    private static double nextTry;

    private static void RetryWire()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextTry) return;
        nextTry = EditorApplication.timeSinceStartup + 1.0;
        try
        {
            CopyAll();
            EditorApplication.update -= RetryWire;
            GameObject root = GameObject.Find(RootName);
            if (root != null) EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("[FBT Mayfair] All settings applied. Save your scene (Ctrl+S), then press Play.");
        }
        catch (Exception e)
        {
            if (IsOutdated(e) && ++pendingTries < 120)
            {
                if (pendingTries % 10 == 0) UdonSharpProgramAsset.CompileAllCsPrograms(true);
                return;
            }
            EditorApplication.update -= RetryWire;
            Debug.LogWarning("[FBT Mayfair] Could not apply the settings yet (" + e.Message + "). Save the scene and click FBT Mayfair > Build Spirits Machine again once Unity is idle.");
        }
    }

    private static void CopyAll()
    {
        foreach (UdonSharpBehaviour b in made)
            if (b != null) UdonSharpEditorUtility.CopyProxyToUdon(b);
    }

    private static bool IsOutdated(Exception e)
    {
        return e.Message.IndexOf("outdated", StringComparison.OrdinalIgnoreCase) >= 0 || e.Message.IndexOf("compile", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // ------------------------------------------------------------------ helpers

    private static bool TryBounds(GameObject go, out Bounds b)
    {
        b = default;
        bool any = false;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return any;
    }

    private static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material m, bool collider = false)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    private static GameObject Cyl(Transform parent, string name, Vector3 pos, float diameter, float height, Material m)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
        go.GetComponent<Renderer>().sharedMaterial = m;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    private static void Label3D(Transform parent, string name, string text, Vector3 pos, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var t = go.AddComponent<TextMeshPro>();
        t.font = font;
        t.text = text;
        t.color = color;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.enableAutoSizing = true;
        t.fontSizeMin = 0.05f;
        t.fontSizeMax = 2f;
        t.rectTransform.sizeDelta = size;
    }

    private static AudioSource Audio(GameObject go, float volume, float maxDistance)
    {
        var a = go.AddComponent<AudioSource>();
        a.playOnAwake = false;
        a.volume = volume;
        a.spatialBlend = 1f;
        a.minDistance = 0.4f;
        a.maxDistance = maxDistance;
        a.rolloffMode = AudioRolloffMode.Linear;
        a.dopplerLevel = 0f;
        return a;
    }

    private static Texture2D FindLogo()
    {
        foreach (string g in AssetDatabase.FindAssets("FBT_Mayfair_Logo t:Texture2D"))
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g));
            if (t != null) return t;
        }
        return null;
    }

    private static void Dir(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        Dir(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static Material SaveMat(string name, Shader shader, Action<Material> setup)
    {
        if (shader == null) shader = Shader.Find("Standard");
        Dir(Gen + "/Materials");
        string path = Gen + "/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool isNew = m == null;
        if (isNew) m = new Material(shader);
        else m.shader = shader;
        setup(m);
        if (isNew) AssetDatabase.CreateAsset(m, path);
        else EditorUtility.SetDirty(m);
        return m;
    }

    private static Material LitMat(string name, Color c, float metal, float smooth, Color emission, Texture tex = null)
    {
        // Quest-friendly when the VRChat mobile shaders are present
        Shader s = Shader.Find("VRChat/Mobile/Standard Lite");
        if (s == null) s = Shader.Find("Standard");
        return SaveMat(name, s, m =>
        {
            m.color = c;
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (tex != null) m.mainTexture = tex;
            if (emission.maxColorComponent > 0.001f && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else m.DisableKeyword("_EMISSION");
        });
    }

    private static Material ParticleMat()
    {
        Shader s = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (s == null) s = Shader.Find("Mobile/Particles/Alpha Blended");
        if (s == null) s = Shader.Find("Particles/Standard Unlit");
        Texture2D dot = DotTex();
        return SaveMat("Pour_Stream", s, m =>
        {
            m.mainTexture = dot;
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        });
    }

    private static Texture2D SavePng(string name, Texture2D tex, bool repeat)
    {
        Dir(Gen + "/Textures");
        string path = Gen + "/Textures/" + name + ".png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        if (AssetImporter.GetAtPath(path) is TextureImporter imp)
        {
            imp.alphaIsTransparency = true;
            imp.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Texture2D DotTex()
    {
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - n * 0.5f) * (x + 0.5f - n * 0.5f) + (y + 0.5f - n * 0.5f) * (y + 0.5f - n * 0.5f)) / (n * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();
        return SavePng("Pour_Dot", tex, false);
    }

    private static Texture2D GrateTex()
    {
        const int w = 64, h = 16;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, (x % 8) < 5 ? Color.white : new Color(0.1f, 0.1f, 0.1f));
        tex.Apply();
        return SavePng("Grate", tex, true);
    }

    // ------------------------------------------------------------------ sounds (procedural stand-ins)

    private static float[] PourSample()
    {
        int count = (int)(44100 * 2.8f);
        var s = new float[count];
        float lp1 = 0f, lp2 = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / 44100f;
            float n = Noise();
            lp1 += 0.06f * (n - lp1);
            lp2 += 0.5f * (n - lp2);
            float gurgle = Sin(170f + 70f * Sin(3.1f, t) + 30f * Sin(7.7f, t), t) * 0.07f * (0.5f + 0.5f * Sin(4.3f, t));
            s[i] = (lp1 * 1.6f + (lp2 - lp1) * 0.8f + gurgle) * 0.55f;
        }
        return s;
    }

    private static AudioClip Wav(string name, float dur, Func<float, float> f)
    {
        int count = (int)(44100 * dur);
        var s = new float[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / 44100f;
            s[i] = f(t) * Mathf.Clamp01((dur - t) / 0.015f);
        }
        return WriteWav(name, s);
    }

    private static AudioClip Loop(string name, float dur, float fade, float[] src)
    {
        int count = (int)(44100 * dur);
        int x = (int)(44100 * fade);
        var s = new float[count];
        for (int i = 0; i < count; i++)
        {
            float v = src[i];
            if (i < x && count + i < src.Length)
            {
                float k = i / (float)x;
                v = src[i] * k + src[count + i] * (1f - k);
            }
            s[i] = v;
        }
        return WriteWav(name, s);
    }

    private static AudioClip WriteWav(string name, float[] samples)
    {
        Dir(Gen + "/Audio");
        string path = Gen + "/Audio/" + name + ".wav";
        const int sr = 44100;
        using (var bw = new BinaryWriter(File.Create(path)))
        {
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + samples.Length * 2);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)1);
            bw.Write(sr);
            bw.Write(sr * 2);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(samples.Length * 2);
            foreach (float v in samples) bw.Write((short)(Mathf.Clamp(v, -1f, 1f) * 30000f));
        }
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    private static float Sin(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);
    private static float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);
    private static float Env(float t, float attack, float decay) => t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }
}
