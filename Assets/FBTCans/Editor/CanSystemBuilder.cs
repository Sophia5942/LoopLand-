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
using VRC.SDK3.Components;
using VRC.SDKBase;
using Object = UnityEngine.Object;

/// <summary>
/// FBT Mayfair > Build Can Fridge + Bin.
/// Six drink rows for your fridge (Mayfair Energy, Velvet Rouge, Emerald G&T, Blanc Spritz, After Hours, Passion):
/// each has a click box over a row of display cans, and clicking it puts that drink in front of the row. Cans use your
/// can model with an animated ring-pull; your trash can is the bin.
/// It's built where your Scene view is looking; move the "FBT Can Fridge + Bin" object wherever you like.
/// </summary>
public static class CanSystemBuilder
{
    private const string Root = "Assets/FBTCans";
    private const string Gen = Root + "/Generated";
    private const string RootName = "FBT Can Fridge + Bin";
    private const string BinModelName = "Trashcanfbx_2";
    private const string CanModelName = "MayfairEnergyCan";
    private const int PoolPerDrink = 4;     // cans of each drink that can be out at once (the oldest is reused after that)
    private const int DisplayPerRow = 4;    // display cans standing in each row

    private struct Drink
    {
        public string Key, Name, Texture;
        public Drink(string key, string name, string texture) { Key = key; Name = name; Texture = texture; }
    }

    // six drinks, all FBT Mayfair house brands
    private static readonly Drink[] Drinks =
    {
        new Drink("MayfairEnergy", "Mayfair Energy", "MayfairEnergy_Can"),
        new Drink("VelvetRouge", "Velvet Rouge", "VelvetRouge_Can"),
        new Drink("Emerald", "Emerald Gin & Tonic", "Emerald_Can"),
        new Drink("Blanc", "Blanc Spritz", "Blanc_Can"),
        new Drink("AfterHours", "After Hours Espresso Martini", "AfterHours_Can"),
        new Drink("Passion", "Passion Martini", "Passion_Can"),
    };

    private static Mesh mouthMesh;
    private static Material mouthMat;
    // your can model, used for every drink
    private static GameObject canModel;
    private static Material tabMat;
    private static Mesh pullMesh, rivetMesh;
    private static float modelTop, modelRadius;

    private static readonly List<UdonSharpBehaviour> made = new List<UdonSharpBehaviour>();
    private static readonly List<CanFridge> rows = new List<CanFridge>();
    private static TMP_FontAsset font;

    [MenuItem("FBT Mayfair/Build Can Fridge + Bin", priority = 30)]
    public static void Build()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorUtility.DisplayDialog("FBT Mayfair", "Unity is still compiling or importing. Wait until it finishes, then try again.", "OK");
            return;
        }
        // the admin panel's text uses TextMesh Pro (same as the bar)
        font = TMP_Settings.instance != null ? TMP_Settings.defaultFontAsset : null;
        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font == null)
        {
            if (EditorUtility.DisplayDialog("FBT Mayfair", "TextMesh Pro Essential Resources are missing.\nImport them, then click Build again.", "Import now", "Cancel"))
                EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources");
            return;
        }
        GameObject old = GameObject.Find(RootName);
        if (old != null)
        {
            if (!EditorUtility.DisplayDialog("FBT Mayfair", "'" + RootName + "' already exists in this scene. Replace it?", "Replace", "Cancel")) return;
            Undo.DestroyObjectImmediate(old);
        }
        made.Clear();
        rows.Clear();
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
            AdminPanel(root.transform, p);
            Wire();
            Undo.RegisterCreatedObjectUndo(root, "Build Can Fridge + Bin");
            EditorSceneManager.MarkSceneDirty(root.scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("FBT Mayfair", "Done! '" + RootName + "' is in your scene.\n\n" +
                "1. There are 6 drink rows ('Get Mayfair Energy', 'Get Velvet Rouge', ...). Move each one onto a shelf inside your fridge,\n" +
                "   with its blue arrow pointing out of the fridge (towards the glass).\n" +
                "   The see-through box is the click area and the see-through can shows where the new drink appears. Both are invisible in game.\n" +
                "2. Move 'Trash Bin' and 'Drinks Admin Panel' where you want them.\n" +
                "   The panel works for the instance owner; add more admins in its Admin Names list.\n3. Save the scene (Ctrl+S).", "OK");
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
        Material ghost = SaveMat("Editor_Ghost", Shader.Find("Legacy Shaders/Transparent/Diffuse"), m => m.color = new Color(0.3f, 0.9f, 1f, 0.3f));
        canModel = FindModel(CanModelName);
        if (canModel == null) throw new Exception("The can model '" + CanModelName + ".fbx' is missing. Make sure the whole FBTCans folder was imported (FBTCans/Models).");
        mouthMat = LitMat("Can_Mouth", Hex("101010"), 0f, 0.9f, Color.black);
        tabMat = LitMat("Can_PullTab", Hex("D2B97F"), 1f, 0.8f, Color.black);
        pullMesh = PullTab("Can_PullTab");
        rivetMesh = Disc("Can_Rivet", 0.0022f, 0.0009f, 10);
        mouthMesh = Stadium("Can_Opening", 0.0035f, 0.0195f, 0.0075f, 0f);   // at the front, above the logo
        Mesh hull = null;

        AudioClip open = Clip("Can_Open"), trash = Clip("Can_Trash"), drop = Clip("Can_Drop");

        var root = new GameObject(RootName);
        Transform rt = root.transform;
        var cansParent = new GameObject("Cans (pool)").transform;
        cansParent.SetParent(rt, false);

        for (int d = 0; d < Drinks.Length; d++)
        {
            Drink drink = Drinks[d];
            Material mat = LitMat("Can_" + drink.Key, Color.white, 0.55f, 0.75f, Color.black, FindTexture(drink.Texture));

            // --- "Get <drink>": a click box over a row of display cans; the new can appears in front of the row.
            // Laid out 3 x 2 to start with: move each row onto a shelf in your fridge.
            var spawner = new GameObject("Get " + drink.Name);
            spawner.transform.SetParent(rt, false);
            spawner.transform.localPosition = new Vector3(((d % 3) - 1) * 0.4f, (d / 3) * 0.3f, 0f);
            Transform sp = spawner.transform;
            var box = spawner.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 0.1f, -0.06f);
            box.size = new Vector3(0.34f, 0.2f, 0.14f);
            var spawnPoint = new GameObject("Can Spawn Point").transform;
            spawnPoint.SetParent(sp, false);
            spawnPoint.localPosition = new Vector3(0f, 0.002f, 0.07f);
            var hide = new GameObject("Can Hide Spot").transform;
            hide.SetParent(sp, false);
            hide.localPosition = new Vector3(0f, 0f, -0.06f);
            // the row of display cans (just looks: no scripts, no colliders, static so they batch)
            var row = new GameObject("Display Row").transform;
            row.SetParent(sp, false);
            for (int k = 0; k < DisplayPerRow; k++)
            {
                var dc = ModelVisual(row, "Display " + drink.Name, mat, true);
                dc.transform.localPosition = new Vector3((k - (DisplayPerRow - 1) * 0.5f) * 0.078f, 0f, -0.06f);
                dc.transform.localRotation = Quaternion.Euler(0f, (k * 37 % 30) - 15f, 0f);
                foreach (Transform t in dc.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
            }
            // see-through helpers, tagged EditorOnly so they are not in the uploaded world
            var gb = Box(sp, "Click Area (editor only)", box.center, box.size, ghost);
            gb.tag = "EditorOnly";
            var gc = ModelVisual(spawnPoint, "New Can Appears Here (editor only)", ghost, false);
            foreach (Transform t in gc.GetComponentsInChildren<Transform>(true)) t.gameObject.tag = "EditorOnly";

            // --- this drink's cans (pool)
            var cans = new FBTCan[PoolPerDrink];
            for (int i = 0; i < PoolPerDrink; i++)
            {
                var go = new GameObject(drink.Name + " " + (i + 1));
                go.transform.SetParent(cansParent, false);
                go.transform.position = hide.position;
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 0.35f;
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                GameObject vis = ModelVisual(go.transform, "Visual", mat, true);
                if (hull == null) hull = Hull("Can_Collider", modelRadius, modelTop, 14);
                var col = go.AddComponent<MeshCollider>();
                col.sharedMesh = hull;
                col.convex = true;
                var pickup = go.AddComponent<VRCPickup>();
                pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;
                pickup.UseText = "Open / Swish";
                pickup.InteractionText = drink.Name;
                pickup.proximity = 0.5f;
                pickup.orientation = VRC_Pickup.PickupOrientation.Any;
                pickup.DisallowTheft = true; // nobody can snatch a can out of your hand
                var sound = AudioOn(go, 0.9f, 6f);
                made.Add(Add<TransformSync>(go));
                var can = Add<FBTCan>(go);
                can.Visual = vis;
                can.Tab = vis.transform.Find("Lid Parts/Tab Pivot");
                can.Mouth = vis.transform.Find("Mouth").gameObject;
                can.CanCollider = col;
                can.HideSpot = hide;
                can.Sound = sound;
                can.OpenClip = open;
                can.TrashClip = trash;
                can.SpawnClip = null;  // no sound when it appears, like a real fridge
                can.SwishClip = null;  // only the recorded sounds: open, floor, bin
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
            rows.Add(fridgeScript);
            var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(fridgeScript);
            if (ub != null)
            {
                ub.interactText = "Get " + drink.Name;
                ub.proximity = 2f;
            }
        }
        return root;
    }

    /// <summary>Your can model, standing with its bottom at y = 0, plus the ring-pull, rivet and opening on its lid.</summary>
    private static GameObject ModelVisual(Transform parent, string name, Material mat, bool withTab)
    {
        // build at the world origin first so measuring is simple, then move it under its parent
        var v = new GameObject(name);
        var m = (GameObject)Object.Instantiate(canModel);
        m.name = "Can Model";
        m.transform.SetParent(v.transform, false);
        foreach (Collider c in m.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (Renderer r in m.GetComponentsInChildren<Renderer>(true))
        {
            var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int k = 0; k < mats.Length; k++) mats[k] = mat;
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }
        Bounds b = BoundsOf(m);
        // if the import units are off, bring it back to a real 500 ml can size
        if (b.size.y < 0.08f || b.size.y > 0.4f)
        {
            m.transform.localScale *= 0.167f / Mathf.Max(0.0001f, b.size.y);
            b = BoundsOf(m);
        }
        m.transform.localPosition += new Vector3(-b.center.x, -b.min.y, -b.center.z);
        modelTop = b.size.y;
        modelRadius = Mathf.Max(b.extents.x, b.extents.z);
        if (withTab)
        {
            // ring-pull: rotates up around its rivet when opened. Turned round so the drinking hole is at the front
            // (+Z, above the logo) and the finger ring at the back.
            var lid = new GameObject("Lid Parts").transform;
            lid.SetParent(v.transform, false);
            lid.localPosition = new Vector3(0f, modelTop + 0.0004f, 0.001f);
            lid.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var pivot = new GameObject("Tab Pivot").transform;
            pivot.SetParent(lid, false);
            Part(pivot, "Tab", pullMesh, tabMat);
            Part(pivot, "Rivet", rivetMesh, tabMat);
            // the opening, in front of the rivet; hidden until the can is opened
            Part(v.transform, "Mouth", mouthMesh, mouthMat);
            Transform mouth = v.transform.Find("Mouth");
            mouth.localPosition = new Vector3(0f, modelTop + 0.00015f, 0f);
            mouth.gameObject.SetActive(false);
        }
        v.transform.SetParent(parent, false);
        v.transform.localPosition = Vector3.zero;
        v.transform.localRotation = Quaternion.identity;
        return v;
    }

    /// <summary>Wall panel for admins: lock / unlock drinks and reset all drinks. Front faces +Z.</summary>
    private static void AdminPanel(Transform rt, Vector3 near)
    {
        Material plate = LitMat("Admin_Plate", Hex("16100E"), 0.2f, 0.6f, Color.black);
        Material goldMat = LitMat("Admin_Gold", Hex("C9A04A"), 1f, 0.8f, new Color(0.12f, 0.08f, 0.02f));
        Material capMat = LitMat("Admin_Button", Hex("5A1424"), 0.3f, 0.7f, Color.black);
        var panel = new GameObject("Drinks Admin Panel");
        panel.transform.SetParent(rt, false);
        panel.transform.position = near + new Vector3(-1f, 1.3f, 0f);
        Transform pt = panel.transform;
        Box(pt, "Plate", new Vector3(0f, 0f, 0f), new Vector3(0.46f, 0.3f, 0.02f), plate);
        // gold frame
        Box(pt, "Trim Top", new Vector3(0f, 0.145f, 0.011f), new Vector3(0.46f, 0.01f, 0.006f), goldMat);
        Box(pt, "Trim Bottom", new Vector3(0f, -0.145f, 0.011f), new Vector3(0.46f, 0.01f, 0.006f), goldMat);
        Box(pt, "Trim Left", new Vector3(-0.225f, 0f, 0.011f), new Vector3(0.01f, 0.3f, 0.006f), goldMat);
        Box(pt, "Trim Right", new Vector3(0.225f, 0f, 0.011f), new Vector3(0.01f, 0.3f, 0.006f), goldMat);
        Label3D(pt, "Title", "FBT  MAYFAIR", new Vector3(0f, 0.105f, 0.012f), new Vector2(0.38f, 0.045f), Hex("D9B867"));
        Label3D(pt, "Subtitle", "DRINKS  ADMIN", new Vector3(0f, 0.07f, 0.012f), new Vector2(0.3f, 0.025f), Hex("BFA88A"));
        TextMeshPro status = Label3D(pt, "Status", "DRINKS  OPEN", new Vector3(0f, 0.025f, 0.012f), new Vector2(0.36f, 0.035f), Hex("E6C780"));
        Label3D(pt, "Footer", "INSTANCE OWNER & ADMINS ONLY", new Vector3(0f, -0.122f, 0.012f), new Vector2(0.36f, 0.018f), Hex("8F7A5E"));

        var admin = Add<DrinksAdmin>(panel);
        TextMeshPro lockLabel;
        Collider lockCol = AdminBtn(pt, admin, 0, "Lock Button", "LOCK", new Vector3(-0.1f, -0.06f, 0f), capMat, out lockLabel);
        TextMeshPro resetLabel;
        Collider resetCol = AdminBtn(pt, admin, 1, "Reset Button", "RESET ALL", new Vector3(0.1f, -0.06f, 0f), capMat, out resetLabel);
        admin.Fridges = rows.ToArray();
        admin.InstanceOwnerIsAdmin = true;
        admin.AdminNames = new string[0];
        admin.Buttons = new[] { lockCol, resetCol };
        admin.StatusText = status;
        admin.LockLabel = lockLabel;
        made.Add(admin);
        foreach (CanFridge f in rows) f.Admin = admin;
    }

    private static Collider AdminBtn(Transform parent, DrinksAdmin admin, int action, string name, string text, Vector3 pos, Material capMat, out TextMeshPro label)
    {
        var b = new GameObject(name);
        b.transform.SetParent(parent, false);
        b.transform.localPosition = pos;
        var col = b.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0f, 0.02f);
        col.size = new Vector3(0.17f, 0.07f, 0.04f);
        // the cap and its label move in together when pressed
        var press = new GameObject("Press").transform;
        press.SetParent(b.transform, false);
        Box(press, "Cap", new Vector3(0f, 0f, 0.018f), new Vector3(0.16f, 0.06f, 0.016f), capMat);
        label = Label3D(press, "Label", text, new Vector3(0f, 0f, 0.0275f), new Vector2(0.14f, 0.032f), Hex("F0DDB0"));
        var btn = Add<AdminButton>(b);
        btn.Panel = admin;
        btn.Action = action;
        btn.Cap = press;
        made.Add(btn);
        var ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(btn);
        if (ub != null)
        {
            ub.interactText = action == 0 ? "Lock / unlock drinks" : "Reset all drinks";
            ub.proximity = 2f;
        }
        return col;
    }

    private static TextMeshPro Label3D(Transform parent, string name, string text, Vector3 pos, Vector2 size, Color color)
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
        return t;
    }

    private static GameObject FindModel(string name)
    {
        foreach (string g in AssetDatabase.FindAssets(name + " t:Model"))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
            if (go != null && go.name == name) return go;
        }
        return null;
    }

    private static Texture2D FindTexture(string name)
    {
        foreach (string g in AssetDatabase.FindAssets(name + " t:Texture2D"))
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g));
            if (t != null && t.name == name) return t;
        }
        Debug.LogWarning("[FBT Mayfair] Texture '" + name + "' not found. Make sure the FBTCans/Textures folder was imported.");
        return null;
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
        Type[] types = { typeof(FBTCan), typeof(CanFridge), typeof(TrashBin), typeof(TransformSync), typeof(DrinksAdmin), typeof(AdminButton) };
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

    // --- ring-pull and opening shapes (flat, in the XZ plane; ring end towards +Z)

    private static float SegDist(Vector2 p, float z0, float z1)
    {
        float z = Mathf.Clamp(p.y, z0, z1);
        return new Vector2(p.x, p.y - z).magnitude;
    }

    /// <summary>Where a ray from c (inside) leaves a stadium (segment on the Z axis from z0 to z1, radius r).</summary>
    private static Vector2 StadiumHit(Vector2 c, Vector2 dir, float z0, float z1, float r)
    {
        float lo = 0f, hi = 0.06f;
        for (int k = 0; k < 40; k++)
        {
            float mid = (lo + hi) * 0.5f;
            if (SegDist(c + dir * mid, z0, z1) < r) lo = mid; else hi = mid;
        }
        return c + dir * lo;
    }

    /// <summary>Adds a triangle, flipped if needed so its front faces 'want'.</summary>
    private static void Tri(List<int> t, List<Vector3> v, int a, int b, int c, Vector3 want)
    {
        if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), want) < 0f) { int x = b; b = c; c = x; }
        t.Add(a); t.Add(b); t.Add(c);
    }

    /// <summary>Flat stadium plate (the opening): from zMin to zMax, half width r, at height y.</summary>
    private static Mesh Stadium(string name, float zMin, float zMax, float r, float y)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        float z0 = zMin + r, z1 = zMax - r;
        var c = new Vector2(0f, (zMin + zMax) * 0.5f);
        const int n = 28;
        v.Add(new Vector3(c.x, y, c.y));
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            Vector2 p = StadiumHit(c, new Vector2(Mathf.Cos(a), Mathf.Sin(a)), z0, z1, r);
            v.Add(new Vector3(p.x, y, p.y));
        }
        for (int i = 0; i < n; i++) Tri(t, v, 0, 1 + i, 1 + (i + 1) % n, Vector3.up);
        var mesh = new Mesh();
        mesh.SetVertices(v); mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        return SaveMesh(name, mesh);
    }

    /// <summary>The ring-pull: a rounded plate with a finger hole, 0.8 mm thick. The rivet is at the origin.</summary>
    private static Mesh PullTab(string name)
    {
        const float h = 0.0004f;                       // half thickness
        const float oz0 = -0.0002f, oz1 = 0.0142f, or = 0.0068f;   // outline: z -0.007 .. 0.021
        const float iz0 = 0.0122f, iz1 = 0.0128f, ir = 0.0047f;    // finger hole
        var c = new Vector2(0f, 0.0125f);
        const int n = 36;
        var outer = new Vector2[n]; var inner = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            outer[i] = StadiumHit(c, d, oz0, oz1, or);
            inner[i] = StadiumHit(c, d, iz0, iz1, ir);
        }
        var v = new List<Vector3>(); var t = new List<int>();
        // top and bottom faces (ring between outline and hole)
        foreach (float side in new[] { 1f, -1f })
        {
            int b0 = v.Count;
            for (int i = 0; i < n; i++) { v.Add(new Vector3(outer[i].x, h * side, outer[i].y)); v.Add(new Vector3(inner[i].x, h * side, inner[i].y)); }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int o0 = b0 + i * 2, i0 = o0 + 1, o1 = b0 + j * 2, i1 = o1 + 1;
                Tri(t, v, o0, o1, i1, Vector3.up * side);
                Tri(t, v, o0, i1, i0, Vector3.up * side);
            }
        }
        // outer edge and hole edge
        foreach (bool isOuter in new[] { true, false })
        {
            Vector2[] loop = isOuter ? outer : inner;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int b0 = v.Count;
                v.Add(new Vector3(loop[i].x, h, loop[i].y)); v.Add(new Vector3(loop[j].x, h, loop[j].y));
                v.Add(new Vector3(loop[j].x, -h, loop[j].y)); v.Add(new Vector3(loop[i].x, -h, loop[i].y));
                Vector2 mid = (loop[i] + loop[j]) * 0.5f - c;
                Vector3 want = new Vector3(mid.x, 0f, mid.y) * (isOuter ? 1f : -1f);
                Tri(t, v, b0, b0 + 1, b0 + 2, want);
                Tri(t, v, b0, b0 + 2, b0 + 3, want);
            }
        }
        var mesh = new Mesh();
        mesh.SetVertices(v); mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
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
