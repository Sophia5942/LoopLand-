using System;
using System.Collections.Generic;
using System.IO;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDK3.Components;
using VRC.SDKBase;
using Object = UnityEngine.Object;

/// <summary>
/// FBT Mayfair > Build Can Fridge + Bin.
/// A mini fridge ("Get a new can"), a pool of cans (Use opens them with an animation) and your trash can as the bin.
/// It's built where your Scene view is looking; move the "FBT Can Fridge + Bin" object wherever you like.
/// </summary>
public static class CanSystemBuilder
{
    private const string Root = "Assets/FBTCans";
    private const string Gen = Root + "/Generated";
    private const string RootName = "FBT Can Fridge + Bin";
    private const string BinModelName = "Trashcanfbx_2";
    private const int PoolSize = 8;

    // can size (a standard 330 ml can)
    private const float CanR = 0.033f, CanBody = 0.112f, CanH = 0.122f, NeckR = 0.0275f;

    private struct Design
    {
        public string Name, Base, Band, Accent, Text;
        public Design(string name, string b, string band, string accent, string text) { Name = name; Base = b; Band = band; Accent = accent; Text = text; }
    }

    private static readonly Design[] Designs =
    {
        new Design("Cola", "B0102A", "FFFFFF", "1A1A1A", "FFFFFF"),
        new Design("Energy", "1E3A8C", "C9CED6", "D8202E", "FFFFFF"),
        new Design("Lime", "3FAE4A", "F5E04A", "1F5E26", "FFFFFF"),
    };

    private static Mesh mouthMesh;
    private static Material mouthMat;

    private static readonly List<UdonSharpBehaviour> made = new List<UdonSharpBehaviour>();

    [MenuItem("FBT Mayfair/Build Can Fridge + Bin", priority = 30)]
    public static void Build()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorUtility.DisplayDialog("FBT Mayfair", "Unity is still compiling or importing. Wait until it finishes, then try again.", "OK");
            return;
        }
        GameObject old = GameObject.Find(RootName);
        if (old != null)
        {
            if (!EditorUtility.DisplayDialog("FBT Mayfair", "'" + RootName + "' already exists in this scene. Replace it?", "Replace", "Cancel")) return;
            Undo.DestroyObjectImmediate(old);
        }
        made.Clear();
        try
        {
            EditorUtility.DisplayProgressBar("FBT Mayfair", "Compiling UdonSharp programs...", 0.1f);
            EnsurePrograms();
            // put it in your fridge if it's selected, otherwise where the Scene view is looking
            GameObject sel = Selection.activeGameObject;
            Vector3 p;
            if (sel != null && sel.scene.IsValid() && sel.GetComponentInChildren<Renderer>(true) != null) p = BoundsOf(sel).center;
            else
            {
                p = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
                if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 20f)) p = hit.point;
            }
            EditorUtility.DisplayProgressBar("FBT Mayfair", "Building the can spawner, cans and bin...", 0.4f);
            GameObject root = BuildAll();
            root.transform.position = p;
            Bin(root.transform, p);
            Wire();
            Undo.RegisterCreatedObjectUndo(root, "Build Can Fridge + Bin");
            EditorSceneManager.MarkSceneDirty(root.scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("FBT Mayfair", "Done! '" + RootName + "' is in your scene.\n\n" +
                "1. Move 'Get A New Can' onto a shelf inside your fridge, with the blue arrow pointing out of the fridge (towards the glass).\n" +
                "   The see-through can shows where new cans appear; the see-through box is the click area. Both are invisible in game.\n" +
                "2. Move 'Trash Bin' where you want it.\n3. Save the scene (Ctrl+S).", "OK");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("FBT Mayfair - build failed", e.Message + "\n\nPlease screenshot this and send it.", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ------------------------------------------------------------------ building

    private static GameObject BuildAll()
    {
        Material alu = LitMat("Can_Aluminium", Hex("C8CDD3"), 1f, 0.85f, Color.black);
        Material aluDark = LitMat("Can_Top", Hex("9AA0A8"), 1f, 0.7f, Color.black);
        Material ghost = SaveMat("Editor_Ghost", Shader.Find("Legacy Shaders/Transparent/Diffuse"), m => m.color = new Color(0.3f, 0.9f, 1f, 0.3f));
        var labels = new Material[Designs.Length];
        for (int i = 0; i < Designs.Length; i++) labels[i] = LitMat("Can_" + Designs[i].Name, Color.white, 0.6f, 0.75f, Color.black, LabelTex(Designs[i]));

        Mesh body = Tube("Can_Body", CanR, CanR, 0.006f, CanBody, 20);
        Mesh neck = Tube("Can_Neck", CanR, NeckR, CanBody, CanH, 20);
        Mesh bottom = Tube("Can_Bottom", CanR * 0.85f, CanR, 0f, 0.006f, 20);
        Mesh top = Disc("Can_Lid", NeckR, CanH - 0.0015f, 20);
        mouthMesh = Disc("Can_Mouth", 0.009f, CanH - 0.0011f, 14);
        mouthMat = LitMat("Can_Mouth", Hex("101010"), 0f, 0.9f, Color.black);
        Mesh hull = Hull("Can_Collider", CanR, CanH, 12);

        AudioClip open = Clip("Can_Open"), trash = Clip("Can_Trash"), drop = Clip("Can_Drop");

        var root = new GameObject(RootName);
        Transform rt = root.transform;

        // --- "Get A New Can": a click box that goes on a shelf inside your fridge; the can appears in front of it
        var spawner = new GameObject("Get A New Can");
        spawner.transform.SetParent(rt, false);
        Transform sp = spawner.transform;
        var box = spawner.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = new Vector3(0f, 0.15f, -0.1f);
        box.size = new Vector3(0.5f, 0.3f, 0.2f);
        var spawnPoint = new GameObject("Can Spawn Point").transform;
        spawnPoint.SetParent(sp, false);
        spawnPoint.localPosition = new Vector3(0f, 0.002f, 0.07f);
        var hide = new GameObject("Can Hide Spot").transform;
        hide.SetParent(sp, false);
        hide.localPosition = new Vector3(0f, 0f, -0.1f);
        // see-through helpers so you can see where things are; tagged EditorOnly, so they are not in the uploaded world
        var gb = Box(sp, "Click Area (editor only)", box.center, box.size, ghost);
        gb.tag = "EditorOnly";
        var gc = CanVisual(spawnPoint, "New Can Appears Here (editor only)", Vector3.zero, body, neck, bottom, top, ghost, ghost, ghost, false);
        foreach (Transform t in gc.GetComponentsInChildren<Transform>()) t.gameObject.tag = "EditorOnly";

        // --- cans (pool)
        var cansParent = new GameObject("Cans (pool)").transform;
        cansParent.SetParent(rt, false);
        var cans = new FBTCan[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            var go = new GameObject("Can " + (i + 1) + " (" + Designs[i % Designs.Length].Name + ")");
            go.transform.SetParent(cansParent, false);
            go.transform.position = hide.position;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.35f;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = hull;
            col.convex = true;
            var pickup = go.AddComponent<VRCPickup>();
            pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;
            pickup.UseText = "Open";
            pickup.InteractionText = "Can";
            pickup.proximity = 0.5f;
            pickup.orientation = VRC_Pickup.PickupOrientation.Any;
            pickup.DisallowTheft = true; // nobody can snatch a can out of your hand
            GameObject vis = CanVisual(go.transform, "Visual", Vector3.zero, body, neck, bottom, top, labels[i % labels.Length], alu, aluDark, true);
            var sound = AudioOn(go, 0.9f, 6f);
            made.Add(Add<TransformSync>(go));
            var can = Add<FBTCan>(go);
            can.Visual = vis;
            can.Tab = vis.transform.Find("Tab Pivot");
            can.Mouth = vis.transform.Find("Mouth").gameObject;
            can.CanCollider = col;
            can.HideSpot = hide;
            can.Sound = sound;
            can.OpenClip = open;
            can.TrashClip = trash;
            can.SpawnClip = null; // no sound when it appears, like a real fridge
            can.DropClip = drop;
            can.DropSoundSpeed = 1.2f;
            can.CleanupAfterSeconds = 300f;
            made.Add(can);
            cans[i] = can;
        }

        var fridgeScript = Add<CanFridge>(spawner);
        fridgeScript.Cans = cans;
        fridgeScript.SpawnPoint = spawnPoint;
        fridgeScript.CooldownSeconds = 10f;
        foreach (FBTCan c in cans) c.Fridge = fridgeScript;
        made.Add(fridgeScript);
        var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(fridgeScript);
        if (ub != null)
        {
            ub.interactText = "Get A New Can";
            ub.proximity = 2f;
        }

        return root;
    }

    private static GameObject CanVisual(Transform parent, string name, Vector3 pos, Mesh body, Mesh neck, Mesh bottom, Mesh top, Material label, Material alu, Material aluDark, bool withTab)
    {
        var v = new GameObject(name);
        v.transform.SetParent(parent, false);
        v.transform.localPosition = pos;
        Part(v.transform, "Body", body, label);
        Part(v.transform, "Neck", neck, alu);
        Part(v.transform, "Bottom", bottom, alu);
        Part(v.transform, "Lid", top, aluDark);
        // the ring-pull: rotates up around its rivet when opened
        var pivot = new GameObject("Tab Pivot").transform;
        pivot.SetParent(v.transform, false);
        pivot.localPosition = new Vector3(0f, CanH - 0.0005f, -0.004f);
        var tab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tab.name = "Tab";
        Object.DestroyImmediate(tab.GetComponent<Collider>());
        tab.transform.SetParent(pivot, false);
        tab.transform.localPosition = new Vector3(0f, 0.001f, 0.011f);
        tab.transform.localScale = new Vector3(0.013f, 0.002f, 0.026f);
        tab.GetComponent<Renderer>().sharedMaterial = alu;
        if (!withTab) pivot.name = "Tab";
        else
        {
            // the hole in the lid, behind the rivet; hidden until the can is opened
            Part(v.transform, "Mouth", mouthMesh, mouthMat);
            Transform mouth = v.transform.Find("Mouth");
            mouth.localPosition = new Vector3(0f, 0f, -0.015f);
            mouth.localScale = new Vector3(1f, 1f, 0.75f);
            mouth.gameObject.SetActive(false);
        }
        return v;
    }

    private static void Bin(Transform rt, Vector3 near)
    {
        GameObject prefab = null;
        foreach (string g in AssetDatabase.FindAssets(BinModelName + " t:Model"))
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
            if (prefab != null) break;
        }
        GameObject bin;
        if (prefab != null)
        {
            bin = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            bin.name = "Trash Bin";
            bin.transform.SetParent(rt, false);
        }
        else
        {
            Debug.LogWarning("[FBT Mayfair] Trash can model '" + BinModelName + "' not found, using a simple bin.");
            bin = new GameObject("Trash Bin");
            bin.transform.SetParent(rt, false);
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            c.transform.SetParent(bin.transform, false);
            c.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
            c.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            Object.DestroyImmediate(c.GetComponent<Collider>());
        }
        // stand it on the floor, next to the fridge
        Vector3 at = near + Vector3.right * 1f;
        if (Physics.Raycast(at + Vector3.up * 0.1f, Vector3.down, out RaycastHit floor, 20f)) at = floor.point;
        Bounds b = BoundsOf(bin);
        bin.transform.position += new Vector3(at.x - b.center.x, at.y - b.min.y, at.z - b.center.z);
        b = BoundsOf(bin);
        // walls cans can hit
        foreach (MeshFilter mf in bin.GetComponentsInChildren<MeshFilter>())
            if (mf.GetComponent<Collider>() == null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        foreach (Transform t in bin.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
        // trigger inside: a can that drops in is cleaned up
        var trig = new GameObject("Bin Trigger");
        trig.transform.SetParent(bin.transform, true);
        trig.transform.position = new Vector3(b.center.x, b.min.y + b.size.y * 0.55f, b.center.z);
        trig.transform.rotation = Quaternion.identity;
        var box = trig.AddComponent<BoxCollider>();
        box.isTrigger = true;
        Vector3 ls = trig.transform.lossyScale;
        box.size = new Vector3(b.size.x * 0.7f / Mathf.Max(0.0001f, ls.x), b.size.y * 0.8f / Mathf.Max(0.0001f, ls.y), b.size.z * 0.7f / Mathf.Max(0.0001f, ls.z));
        made.Add(Add<TrashBin>(trig));
    }

    // ------------------------------------------------------------------ programs

    private static T Add<T>(GameObject go) where T : UdonSharpBehaviour
    {
        T c = UdonSharpUndo.AddComponent<T>(go);
        if (c == null) throw new Exception("UdonSharp could not add " + typeof(T).Name + ". Wait until Unity has finished compiling, check the Console for UdonSharp errors, then try again.");
        return c;
    }

    private static void EnsurePrograms()
    {
        Type[] types = { typeof(FBTCan), typeof(CanFridge), typeof(TrashBin), typeof(TransformSync) };
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

    // brand-new scripts may still be compiling: then the settings are applied by themselves a moment later
    private static int tries;

    private static void Wire()
    {
        try { CopyAll(); }
        catch (Exception e)
        {
            if (!IsOutdated(e)) throw;
            Debug.Log("[FBT Mayfair] UdonSharp is still compiling the new scripts. The cans are built; their settings will be applied in a moment.");
            tries = 0;
            EditorApplication.update -= Retry;
            EditorApplication.update += Retry;
        }
    }

    private static void Retry()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || ++tries % 30 != 0) return;
        try
        {
            CopyAll();
            EditorApplication.update -= Retry;
            GameObject root = GameObject.Find(RootName);
            if (root != null) EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("[FBT Mayfair] Can fridge + bin ready. Save your scene (Ctrl+S).");
        }
        catch (Exception e)
        {
            if (IsOutdated(e) && tries < 3600) return;
            EditorApplication.update -= Retry;
            Debug.LogWarning("[FBT Mayfair] Could not apply the can settings (" + e.Message + "). Build again once Unity is idle.");
        }
    }

    private static void CopyAll()
    {
        foreach (UdonSharpBehaviour b in made) if (b != null) UdonSharpEditorUtility.CopyProxyToUdon(b);
    }

    private static bool IsOutdated(Exception e)
    {
        return e.Message.IndexOf("outdated", StringComparison.OrdinalIgnoreCase) >= 0 || e.Message.IndexOf("compile", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // ------------------------------------------------------------------ helpers

    private static Bounds BoundsOf(GameObject go)
    {
        Bounds b = new Bounds(go.transform.position, Vector3.zero);
        bool any = false;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    private static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material m)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    private static void Part(Transform parent, string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    private static AudioSource AudioOn(GameObject go, float volume, float maxDistance)
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

    private static AudioClip Clip(string name)
    {
        foreach (string g in AssetDatabase.FindAssets(name + " t:AudioClip"))
        {
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g));
            if (c != null && c.name == name) return c;
        }
        Debug.LogWarning("[FBT Mayfair] Sound '" + name + "' not found. Make sure the FBTCans/Sounds folder was imported.");
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

    // ------------------------------------------------------------------ can label (painted: color bands + "FBT")

    private static Texture2D LabelTex(Design d)
    {
        const int w = 256, h = 128;
        Color basec = Hex(d.Base), band = Hex(d.Band), accent = Hex(d.Accent), text = Hex(d.Text);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = basec;
                float wave = 30f + 8f * Mathf.Sin(x / (float)w * Mathf.PI * 4f);
                if (y > wave && y < wave + 12f) c = band;
                if (y > wave + 12f && y < wave + 16f) c = accent;
                if (y < 6 || y > h - 7) c = accent;
                px[y * w + x] = c;
            }
        // "FBT" on the front (u = 0.25) and back (u = 0.75)
        foreach (int cx in new[] { 64, 192 }) Letters(px, w, cx - 36, 62, text);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
        tex.SetPixels(px);
        tex.Apply();
        Dir(Gen + "/Textures");
        string path = Gen + "/Textures/Can_" + d.Name + ".png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void Letters(Color[] px, int w, int x0, int y0, Color c)
    {
        // block letters, 20 x 34 px each, 6 px apart; the texture wraps around the can, so draw them mirrored in X
        // (u runs the other way when you look at the front of the can)
        int lw = 20, lh = 34, t = 5;
        // F
        Rect(px, w, x0, y0, t, lh, c); Rect(px, w, x0, y0 + lh - t, lw, t, c); Rect(px, w, x0, y0 + lh / 2 - 2, lw - 5, t, c);
        // B
        int bx = x0 + lw + 6;
        Rect(px, w, bx, y0, t, lh, c); Rect(px, w, bx, y0 + lh - t, lw - 4, t, c); Rect(px, w, bx, y0 + lh / 2 - 2, lw - 4, t, c); Rect(px, w, bx, y0, lw - 4, t, c);
        Rect(px, w, bx + lw - t, y0 + 3, t, lh / 2 - 4, c); Rect(px, w, bx + lw - t, y0 + lh / 2 + 1, t, lh / 2 - 4, c);
        // T
        int tx = bx + lw + 6;
        Rect(px, w, tx, y0 + lh - t, lw, t, c); Rect(px, w, tx + lw / 2 - t / 2, y0, t, lh, c);
        // mirror the 3 letters in place so they read correctly on the can
        int span = tx + lw - x0;
        for (int y = y0; y < y0 + lh; y++)
            for (int i = 0; i < span / 2; i++)
            {
                int a = y * w + x0 + i, b = y * w + x0 + span - 1 - i;
                Color tmp = px[a]; px[a] = px[b]; px[b] = tmp;
            }
    }

    private static void Rect(Color[] px, int w, int x, int y, int rw, int rh, Color c)
    {
        int h = px.Length / w;
        for (int j = y; j < y + rh; j++)
            for (int i = x; i < x + rw; i++)
                if (i >= 0 && i < w && j >= 0 && j < h) px[j * w + i] = c;
    }

    // ------------------------------------------------------------------ meshes

    private static Mesh SaveMesh(string name, Mesh mesh)
    {
        Dir(Gen + "/Meshes");
        string path = Gen + "/Meshes/" + name + ".asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        mesh.name = name;
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    /// <summary>Open tapered tube from y0 (radius r0) to y1 (radius r1). UV: u around (0.25 faces +Z), v up.</summary>
    private static Mesh Tube(string name, float r0, float r1, float y0, float y1, int sides)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        float slope = (r0 - r1) / (y1 - y0);
        for (int i = 0; i <= sides; i++)
        {
            float a = i / (float)sides * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 nn = (dir + Vector3.up * slope).normalized;
            v.Add(dir * r0 + Vector3.up * y0); n.Add(nn); uv.Add(new Vector2(i / (float)sides, 0f));
            v.Add(dir * r1 + Vector3.up * y1); n.Add(nn); uv.Add(new Vector2(i / (float)sides, 1f));
        }
        for (int i = 0; i < sides; i++)
        {
            int k = i * 2;
            tri.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 });
        }
        var mesh = new Mesh();
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
        return SaveMesh(name, mesh);
    }

    private static Mesh Disc(string name, float r, float y, int sides)
    {
        var v = new List<Vector3> { new Vector3(0f, y, 0f) }; var n = new List<Vector3> { Vector3.up }; var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) }; var tri = new List<int>();
        for (int i = 0; i <= sides; i++)
        {
            float a = i / (float)sides * Mathf.PI * 2f;
            v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r)); n.Add(Vector3.up); uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
        }
        for (int i = 0; i < sides; i++) tri.AddRange(new[] { 0, i + 2, i + 1 });
        var mesh = new Mesh();
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
        return SaveMesh(name, mesh);
    }

    /// <summary>Closed cylinder used as the can's (convex) collider.</summary>
    private static Mesh Hull(string name, float r, float h, int sides)
    {
        var v = new List<Vector3>(); var tri = new List<int>();
        for (int i = 0; i < sides; i++)
        {
            float a = i / (float)sides * Mathf.PI * 2f;
            v.Add(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            v.Add(new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r));
        }
        for (int i = 0; i < sides; i++)
        {
            int k = i * 2, m = ((i + 1) % sides) * 2;
            tri.AddRange(new[] { k, k + 1, m + 1, k, m + 1, m });
            if (i > 0 && i < sides - 1) { tri.AddRange(new[] { 0, m, k }); tri.AddRange(new[] { 1, k + 1, m + 1 }); }
        }
        var mesh = new Mesh();
        mesh.SetVertices(v); mesh.SetTriangles(tri, 0);
        mesh.RecalculateNormals();
        return SaveMesh(name, mesh);
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }
}
