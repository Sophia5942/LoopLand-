using System;
using System.Collections.Generic;
using System.IO;
using DrinkSystem;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;
using Object = UnityEngine.Object;

namespace DrinkSystem.EditorTools
{
    /// <summary>
    /// One-click builder: Tools > Drink System > Build Drink Machine In Scene.
    /// Places the Drink_Machine_1 model on a counter, adds a cup slot + push-button UI under each of its 6 nozzles,
    /// a stack of cups, a respawn button, and generates the materials, meshes and sound effects into
    /// Assets/DrinkSystem/Generated.
    /// </summary>
    public static class DrinkMachineBuilder
    {
        private const string Root = "Assets/DrinkSystem";
        private const string Gen = Root + "/Generated";
        private const string ModelPath = "Packages/com.drinksystem/Models/Drink_Machine_1.fbx";
        private const string RootName = "Drink Machine";

        // Drink_Machine_1 is modeled about 2x real size: this brings the nozzles to ~13 cm apart.
        private const float MachineScale = 0.55f;
        private const float CounterHeight = 0.9f;
        private const int CupCount = 6;
        private const float CupHeight = 0.15f;
        private const float CupBottomRadius = 0.032f;
        private const float CupTopRadius = 0.043f;

        // Where things are on Drink_Machine_1, as fractions of its bounds (measured from the model).
        // Width: left to right. Height: bottom to top. Depth: from the front edge backwards.
        private static readonly float[] NozzleWidth = { 0.069f, 0.208f, 0.346f, 0.654f, 0.792f, 0.932f };
        private const float NozzleHeight = 0.63f;
        private const float NozzleDepth = 0.668f;
        private const float TrayHeight = 0.189f;
        private const float PanelHeight = 0.715f;
        private const float PanelDepth = 0.372f;

        private struct FlavorDef
        {
            public string Name, Main, Foam, Edge;
            public float Transparency, FoamHeight, EdgeStrength;
            public FlavorDef(string name, string main, string foam, string edge, float transparency, float foamHeight, float edgeStrength)
            {
                Name = name; Main = main; Foam = foam; Edge = edge;
                Transparency = transparency; FoamHeight = foamHeight; EdgeStrength = edgeStrength;
            }
        }

        private static readonly FlavorDef[] FlavorDefs =
        {
            new FlavorDef("Cola", "2E1206", "C9A27A", "6B3518", 0.10f, 0.05f, 1.0f),
            new FlavorDef("Lemon Lime", "D8F08C", "F4FFE0", "FFFFFF", 0.60f, 0.03f, 1.4f),
            new FlavorDef("Orange", "FF7A12", "FFD39A", "FFB347", 0.25f, 0.04f, 1.0f),
            new FlavorDef("Root Beer", "3B1A0A", "E8CFA8", "7A3E1C", 0.10f, 0.09f, 0.9f),
            new FlavorDef("Grape", "5B1466", "C79BD0", "A040B0", 0.25f, 0.04f, 1.1f),
            new FlavorDef("Blue Raspberry", "1E6BFF", "BFD8FF", "7FD0FF", 0.35f, 0.04f, 1.3f),
        };

        private static TMP_FontAsset font;
        private static Texture2D dot;
        private static Texture2D ring;
        private static Sprite roundSprite;
        private static Sprite knobSprite;
        private static readonly List<UdonSharpBehaviour> made = new List<UdonSharpBehaviour>();
        private static readonly System.Random rng = new System.Random(11);

        [MenuItem("Tools/Drink System/Build Drink Machine In Scene")]
        public static void Build()
        {
            font = TMP_Settings.instance != null ? TMP_Settings.defaultFontAsset : null;
            if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (font == null)
            {
                if (EditorUtility.DisplayDialog("Drink System", "TextMesh Pro Essential Resources are missing.\nImport them, then run Tools > Drink System > Build Drink Machine In Scene again.", "Import now", "Cancel"))
                    EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources");
                return;
            }

            GameObject old = GameObject.Find(RootName);
            if (old != null)
            {
                if (!EditorUtility.DisplayDialog("Drink System", "A '" + RootName + "' object already exists in this scene. Replace it?", "Replace", "Cancel")) return;
                Undo.DestroyObjectImmediate(old);
            }

            made.Clear();
            try
            {
                EditorUtility.DisplayProgressBar("Drink System", "Preparing UdonSharp programs...", 0.1f);
                EnsurePrograms();
                EditorUtility.DisplayProgressBar("Drink System", "Generating sounds, meshes and materials...", 0.3f);
                GameObject root = BuildAll();
                EditorUtility.DisplayProgressBar("Drink System", "Wiring Udon behaviours...", 0.85f);
                foreach (UdonSharpBehaviour b in made) UdonSharpEditorUtility.CopyProxyToUdon(b);
                Undo.RegisterCreatedObjectUndo(root, "Build Drink Machine");
                EditorSceneManager.MarkSceneDirty(root.scene);
                AssetDatabase.SaveAssets();
                PrefabUtility.SaveAsPrefabAssetAndConnect(root, Root + "/Drink Machine.prefab", InteractionMode.AutomatedAction);
                Selection.activeGameObject = root;
                Debug.Log("[DrinkSystem] Drink machine built! Prefab saved to " + Root + "/Drink Machine.prefab.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ------------------------------------------------------------------ programs

        private static void EnsurePrograms()
        {
            Type[] types = { typeof(DrinkMachine), typeof(DrinkSlot), typeof(DrinkCup), typeof(DrinkFlavor), typeof(DrinkButton) };
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
                if (script == null) throw new Exception("[DrinkSystem] Could not find the script for " + t.Name);
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = script;
                Dir(Gen + "/Programs");
                AssetDatabase.CreateAsset(asset, Gen + "/Programs/" + t.Name + ".asset");
                created = true;
            }
            if (!created) return;
            AssetDatabase.SaveAssets();
            UdonSharpProgramAsset.CompileAllCsPrograms(true);
        }

        // ------------------------------------------------------------------ scene

        private static GameObject BuildAll()
        {
            dot = Dot();
            ring = Ring();
            roundSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            knobSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            Material liquidMat = ShaderMat("Drink_Liquid", "DrinkSystem/Liquid");
            Material cupMat = ShaderMat("Drink_Cup", "DrinkSystem/Cup");
            Material streamMat = ShaderMat("Drink_Stream", "DrinkSystem/Stream");
            Material sleeveMat = LitMat("Drink_CupSleeve", Hex("D83A2E"));
            Material counterMat = LitMat("Drink_Counter", Hex("2B2A33"));
            Material machineMat = LitMat("Drink_MachineFallback", Hex("B9BEC6"));
            Material ringMat = AdditiveMat("Drink_SlotGuide", ring, new Color(0.2f, 0.9f, 1f, 0.8f));
            Material splashMat = AdditiveMat("Drink_Splash", dot, new Color(0.6f, 0.6f, 0.6f, 0.6f));

            Mesh cupMesh = Frustum("Cup", CupBottomRadius, CupTopRadius, 0f, CupHeight, 20, true, false);
            Mesh sleeveMesh = Frustum("CupSleeve", CupBottomRadius + 0.0022f, CupTopRadius + 0.0022f, 0.045f, 0.105f, 20, false, false, 0f, CupHeight);
            Mesh liquidMesh = Frustum("Liquid", CupBottomRadius * 0.93f, CupTopRadius * 0.93f, 0.004f, CupHeight - 0.006f, 20, true, true, 0.004f, CupHeight - 0.006f);
            Mesh streamMesh = Stream("Stream", 0.0042f, 10);

            var root = new GameObject(RootName);
            Transform rt = root.transform;
            var mb = UdonSharpUndo.AddComponent<DrinkMachine>(root);
            made.Add(mb);
            Sounds(mb);

            // machine on a counter, front facing +Z (players stand on the +Z side)
            Bounds b;
            Machine(rt, machineMat, out b);
            float W = b.size.x, H = b.size.y, D = b.size.z;
            float spacing = (NozzleWidth[1] - NozzleWidth[0]) * W;
            float cupArea = 0.5f;
            float counterLeft = b.min.x - 0.1f;
            float counterRight = b.max.x + cupArea;
            float counterFront = b.max.z + 0.12f;
            float counterBack = b.min.z - 0.05f;
            var counter = Prim(PrimitiveType.Cube, "Counter", rt, new Vector3((counterLeft + counterRight) * 0.5f, CounterHeight * 0.5f, (counterFront + counterBack) * 0.5f),
                new Vector3(counterRight - counterLeft, CounterHeight, counterFront - counterBack), counterMat, true);
            counter.isStatic = true;

            var hum = new GameObject("Machine Hum");
            hum.transform.SetParent(rt, false);
            hum.transform.position = new Vector3(b.center.x, b.center.y, b.center.z);
            AudioSource humSrc = Audio(hum, 0.18f, 6f);
            humSrc.clip = Loop("drink_machine_hum", 2f, 0.25f, HumSample());
            humSrc.loop = true;
            humSrc.playOnAwake = true;

            // flavors
            var flavorsParent = new GameObject("Flavors");
            flavorsParent.transform.SetParent(rt, false);
            for (int i = 0; i < FlavorDefs.Length; i++)
            {
                FlavorDef d = FlavorDefs[i];
                var go = new GameObject(d.Name);
                go.transform.SetParent(flavorsParent.transform, false);
                var f = UdonSharpUndo.AddComponent<DrinkFlavor>(go);
                f.DisplayName = d.Name;
                f.FillSpeed = 0.2f;
                f.Transparency = d.Transparency;
                f.ColorMain = Hex(d.Main);
                f.ColorFoam = Hex(d.Foam);
                f.ColorEdge = Hex(d.Edge);
                f.EdgePower = 3f;
                f.EdgeStrength = d.EdgeStrength;
                f.FoamHeight = d.FoamHeight;
                f.FoamSmoothness = 0.03f;
                made.Add(f);
            }

            // slots, one under each nozzle
            var slotsParent = new GameObject("Slots");
            slotsParent.transform.SetParent(rt, false);
            for (int i = 0; i < NozzleWidth.Length; i++)
            {
                float x = b.min.x + NozzleWidth[i] * W;
                var nozzlePos = new Vector3(x, b.min.y + NozzleHeight * H, b.max.z - NozzleDepth * D);
                var snapPos = new Vector3(x, b.min.y + TrayHeight * H, nozzlePos.z);
                var panelPos = new Vector3(x, b.min.y + PanelHeight * H, b.max.z - PanelDepth * D + 0.006f);
                Slot(slotsParent.transform, i, nozzlePos, snapPos, panelPos, spacing, mb, ringMat, streamMat, streamMesh, splashMat);
            }

            // cups on the counter next to the machine
            var cupsParent = new GameObject("Cups");
            cupsParent.transform.SetParent(rt, false);
            for (int i = 0; i < CupCount; i++)
            {
                int col = i % 3, row = i / 3;
                var pos = new Vector3(b.max.x + 0.11f + col * 0.115f, CounterHeight, counterFront - 0.1f - row * 0.115f);
                Cup(cupsParent.transform, i, pos, cupMesh, sleeveMesh, liquidMesh, cupMat, sleeveMat, liquidMat);
            }

            // respawn button on the counter front, under the cups
            RespawnPanel(rt, new Vector3(b.max.x + cupArea * 0.5f, CounterHeight - 0.09f, counterFront + 0.004f), mb);

            mb.FlavorsParent = flavorsParent;
            mb.SlotsParent = slotsParent.gameObject;
            mb.CupsParent = cupsParent;
            mb.SnapRadius = Mathf.Min(0.11f, spacing * 0.75f);

            // put it where the scene view is looking
            Vector3 place = new Vector3(0f, 0f, 4f);
            if (SceneView.lastActiveSceneView != null)
            {
                place = SceneView.lastActiveSceneView.pivot;
                place.y = 0f;
            }
            rt.position = place;
            return root;
        }

        private static Transform Machine(Transform parent, Material fallbackMat, out Bounds b)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (prefab == null)
            {
                foreach (string g in AssetDatabase.FindAssets("Drink_Machine_1 t:Model"))
                {
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                    if (prefab != null) break;
                }
            }
            if (prefab == null)
            {
                Debug.LogWarning("[DrinkSystem] Drink_Machine_1.fbx not found, building a simple stand-in machine.");
                return FallbackMachine(parent, fallbackMat, out b);
            }

            var m = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            m.name = "Machine Model";
            m.transform.SetParent(parent, false);
            m.transform.localScale = Vector3.one * MachineScale;
            // The model's front (drip tray side) faces its -X: turn it to face +Z
            m.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            b = RendererBounds(m);
            if (b.size.x < b.size.z)
            {
                m.transform.localRotation = Quaternion.identity;
                b = RendererBounds(m);
            }
            m.transform.position += new Vector3(-b.center.x, CounterHeight - b.min.y, -b.center.z);
            b = RendererBounds(m);
            foreach (MeshFilter mf in m.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.GetComponent<Collider>() == null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
            return m.transform;
        }

        private static Transform FallbackMachine(Transform parent, Material mat, out Bounds b)
        {
            float W = 0.97f, H = 0.55f, D = 0.32f;
            var m = new GameObject("Machine Model (stand-in)").transform;
            m.SetParent(parent, false);
            float y0 = CounterHeight;
            float front = D * 0.5f;
            Prim(PrimitiveType.Cube, "Tray", m, new Vector3(0f, y0 + TrayHeight * H * 0.5f, 0f), new Vector3(W, TrayHeight * H, D), mat, true);
            float backDepth = D * 0.2f;
            Prim(PrimitiveType.Cube, "Back", m, new Vector3(0f, y0 + H * 0.5f, -front + backDepth * 0.5f), new Vector3(W, H, backDepth), mat, true);
            float panelZ = front - PanelDepth * D;
            float upperDepth = panelZ + front;
            Prim(PrimitiveType.Cube, "Upper", m, new Vector3(0f, y0 + (NozzleHeight + 1f) * 0.5f * H, -front + upperDepth * 0.5f),
                new Vector3(W, (1f - NozzleHeight) * H, upperDepth), mat, true);
            foreach (float w in NozzleWidth)
                Prim(PrimitiveType.Cylinder, "Nozzle", m, new Vector3(-W * 0.5f + w * W, y0 + NozzleHeight * H + 0.01f, front - NozzleDepth * D), new Vector3(0.025f, 0.01f, 0.025f), mat);
            b = new Bounds(new Vector3(0f, y0 + H * 0.5f, 0f), new Vector3(W, H, D));
            return m;
        }

        private static void Slot(Transform parent, int i, Vector3 nozzlePos, Vector3 snapPos, Vector3 panelPos, float spacing, DrinkMachine mb,
            Material ringMat, Material streamMat, Mesh streamMesh, Material splashMat)
        {
            var go = new GameObject("Slot " + (i + 1));
            go.transform.SetParent(parent, false);
            go.transform.position = snapPos;

            var snap = new GameObject("SnapPoint").transform;
            snap.SetParent(go.transform, false);

            var guide = Prim(PrimitiveType.Quad, "Placement Guide", go.transform, new Vector3(0f, 0.002f, 0f), Vector3.one * (CupTopRadius * 2.8f), ringMat);
            guide.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Shadowless(guide.GetComponent<Renderer>());
            guide.SetActive(false);

            var nozzle = new GameObject("Nozzle").transform;
            nozzle.SetParent(go.transform, false);
            nozzle.position = nozzlePos;
            var stream = new GameObject("Stream");
            stream.transform.SetParent(nozzle, false);
            stream.AddComponent<MeshFilter>().sharedMesh = streamMesh;
            var streamR = stream.AddComponent<MeshRenderer>();
            streamR.sharedMaterial = streamMat;
            Shadowless(streamR);
            streamR.enabled = false;

            ParticleSystem splash = Splash(go.transform, splashMat);
            AudioSource loop = Audio(nozzle.gameObject, 0.55f, 5f);
            AudioSource shot = Audio(nozzle.gameObject, 0.9f, 6f);

            var slot = UdonSharpUndo.AddComponent<DrinkSlot>(go);
            slot.FlavorIndex = i % FlavorDefs.Length;
            slot.SnapPoint = snap;
            slot.Highlight = guide;
            slot.Nozzle = nozzle;
            slot.Stream = streamR;
            slot.Splash = splash;
            slot.LoopSource = loop;
            slot.OneShotSource = shot;
            made.Add(slot);

            // Push-button panel (world space UI: laser pointer, mouse and VR finger presses)
            const float pxW = 240f, pxH = 190f;
            float scale = spacing * 0.9f / pxW;
            RectTransform canvas = Canvas(go.transform, "Button Panel", panelPos, new Vector2(pxW, pxH), scale);
            Img(canvas, "Background", Vector2.zero, new Vector2(pxW, pxH), roundSprite, new Color(0.07f, 0.07f, 0.1f, 0.94f), true);
            slot.NameText = Label(canvas, "Name", FlavorDefs[slot.FlavorIndex].Name.ToUpperInvariant(), new Vector2(0f, 70f), new Vector2(pxW - 50f, 36f), 26f, Color.white);
            slot.StatusLight = Img(canvas, "Status Light", new Vector2(pxW * 0.5f - 22f, 70f), new Vector2(18f, 18f), knobSprite, new Color(0.25f, 0.25f, 0.3f, 1f), false);

            RectTransform hit = Img(canvas, "Push Button", new Vector2(0f, 2f), new Vector2(150f, 104f), null, new Color(1f, 1f, 1f, 0f), false).rectTransform;
            Image cap = Img(hit, "Cap", Vector2.zero, new Vector2(98f, 98f), knobSprite, Hex(FlavorDefs[slot.FlavorIndex].Main), false);
            Img(cap.rectTransform, "Shine", new Vector2(-14f, 16f), new Vector2(34f, 22f), knobSprite, new Color(1f, 1f, 1f, 0.35f), false);
            Label(cap.rectTransform, "Push", "PUSH", Vector2.zero, new Vector2(84f, 34f), 24f, Color.white);
            slot.ButtonImage = cap;
            Button(hit.gameObject, slot, "Press", cap.transform, shot, mb.ClipButtonClick);

            slot.StatusText = Label(canvas, "Status", "PLACE CUP", new Vector2(0f, -64f), new Vector2(pxW - 30f, 26f), 19f, new Color(1f, 0.9f, 0.6f));
            Img(canvas, "Fill Back", new Vector2(0f, -84f), new Vector2(pxW - 40f, 9f), roundSprite, new Color(1f, 1f, 1f, 0.12f), false);
            Image fill = Img(canvas, "Fill", new Vector2(0f, -84f), new Vector2(pxW - 40f, 9f), roundSprite, Color.white, false);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            slot.FillBar = fill;
        }

        private static void Cup(Transform parent, int i, Vector3 pos, Mesh cupMesh, Mesh sleeveMesh, Mesh liquidMesh, Material cupMat, Material sleeveMat, Material liquidMat)
        {
            var go = new GameObject("Cup " + (i + 1));
            go.transform.SetParent(parent, false);
            go.transform.position = pos;

            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = cupMesh;
            col.convex = true;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.25f;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var pickup = go.AddComponent<VRCPickup>();
            pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;
            pickup.UseText = "Drink";
            pickup.InteractionText = "Cup";
            pickup.proximity = 0.4f;
            pickup.DisallowTheft = true;
            pickup.orientation = VRC_Pickup.PickupOrientation.Any;

            var body = new GameObject("Body");
            body.transform.SetParent(go.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = cupMesh;
            var bodyR = body.AddComponent<MeshRenderer>();
            bodyR.sharedMaterial = cupMat;
            Shadowless(bodyR);

            var sleeve = new GameObject("Sleeve");
            sleeve.transform.SetParent(go.transform, false);
            sleeve.AddComponent<MeshFilter>().sharedMesh = sleeveMesh;
            sleeve.AddComponent<MeshRenderer>().sharedMaterial = sleeveMat;

            var liquid = new GameObject("Liquid");
            liquid.transform.SetParent(go.transform, false);
            liquid.AddComponent<MeshFilter>().sharedMesh = liquidMesh;
            var liquidR = liquid.AddComponent<MeshRenderer>();
            liquidR.sharedMaterial = liquidMat;
            Shadowless(liquidR);
            liquidR.enabled = false;

            var cup = UdonSharpUndo.AddComponent<DrinkCup>(go);
            cup.LiquidRenderer = liquidR;
            cup.OneShotSource = Audio(go, 0.9f, 6f);
            cup.LoopSource = Audio(go, 0.6f, 4f);
            made.Add(cup);
        }

        private static void RespawnPanel(Transform parent, Vector3 pos, DrinkMachine mb)
        {
            const float pxW = 360f, pxH = 110f;
            var holder = new GameObject("Respawn Cups");
            holder.transform.SetParent(parent, false);
            holder.transform.position = pos;
            RectTransform canvas = Canvas(holder.transform, "Button Panel", pos, new Vector2(pxW, pxH), 0.24f / pxW);
            AudioSource src = Audio(holder, 0.9f, 6f);
            RectTransform hit = Img(canvas, "Push Button", Vector2.zero, new Vector2(pxW, pxH), roundSprite, new Color(0.07f, 0.07f, 0.1f, 0.94f), false).rectTransform;
            Image cap = Img(hit, "Cap", Vector2.zero, new Vector2(pxW - 16f, pxH - 16f), roundSprite, Hex("1FA36B"), false);
            Label(cap.rectTransform, "Label", "RESPAWN CUPS", new Vector2(0f, 10f), new Vector2(pxW - 40f, 44f), 36f, Color.white);
            Label(cap.rectTransform, "Hint", "puts every cup nobody is holding back on the stack", new Vector2(0f, -24f), new Vector2(pxW - 40f, 22f), 15f, new Color(1f, 1f, 1f, 0.8f));
            Button(hit.gameObject, mb, "RespawnAllCups", cap.transform, src, mb.ClipButtonClick);
        }

        // ------------------------------------------------------------------ helpers: UI

        private static RectTransform Canvas(Transform parent, string name, Vector3 worldPos, Vector2 size, float scale)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            rt.sizeDelta = size;
            rt.position = worldPos;
            // the readable side of a world space canvas is its -Z side: face the players on +Z
            rt.rotation = Quaternion.Euler(0f, 180f, 0f);
            rt.localScale = Vector3.one * scale;
            go.AddComponent<GraphicRaycaster>();
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(size.x, size.y, 2f);
            go.AddComponent<VRCUiShape>();
            go.layer = 0;
            return rt;
        }

        private static Image Img(RectTransform parent, string name, Vector2 pos, Vector2 size, Sprite sprite, Color color, bool sliced)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI Label(RectTransform parent, string name, string text, Vector2 pos, Vector2 size, float maxSize, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.text = text;
            t.color = color;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.enableAutoSizing = true;
            t.fontSizeMax = maxSize;
            t.fontSizeMin = maxSize * 0.4f;
            t.fontSize = maxSize;
            t.raycastTarget = false;
            return t;
        }

        private static void Button(GameObject hit, UdonSharpBehaviour target, string evt, Transform pressVisual, AudioSource audio, AudioClip click)
        {
            hit.GetComponent<Image>().raycastTarget = true;
            var btn = hit.AddComponent<UnityEngine.UI.Button>();
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var db = UdonSharpUndo.AddComponent<DrinkButton>(hit);
            db.Target = target;
            db.EventName = evt;
            db.PressVisual = pressVisual;
            db.Audio = audio;
            db.ClickClip = click;
            made.Add(db);
            UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(db);
            UnityEventTools.AddStringPersistentListener(btn.onClick, new UnityAction<string>(ub.SendCustomEvent), "Press");
        }

        // ------------------------------------------------------------------ helpers: objects

        private static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material m, bool keepCollider = false)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static void Shadowless(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
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

        private static ParticleSystem Splash(Transform parent, Material mat)
        {
            var go = new GameObject("Splash");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.003f, 0.007f);
            main.gravityModifier = 0.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            var em = ps.emission;
            em.rateOverTime = 70f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Hemisphere;
            sh.radius = 0.008f;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            Shadowless(r);
            return ps;
        }

        private static Bounds RendererBounds(GameObject go)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>();
            Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------------ helpers: assets

        private static void Dir(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Dir(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static Material SaveMat(string name, Shader shader, Action<Material> setup)
        {
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

        private static Material ShaderMat(string name, string shaderName)
        {
            Shader s = Shader.Find(shaderName);
            if (s == null)
            {
                Debug.LogWarning("[DrinkSystem] Shader " + shaderName + " not found, using Standard for " + name);
                s = Shader.Find("Standard");
            }
            return SaveMat(name, s, m => { });
        }

        private static Material LitMat(string name, Color c)
        {
            // Quest-friendly when the VRChat mobile shaders are present
            Shader s = Shader.Find("VRChat/Mobile/Standard Lite");
            if (s == null) s = Shader.Find("Standard");
            return SaveMat(name, s, m => { m.color = c; });
        }

        private static Material AdditiveMat(string name, Texture tex, Color tint)
        {
            Shader s = Shader.Find("Legacy Shaders/Particles/Additive");
            if (s == null) s = Shader.Find("Mobile/Particles/Additive");
            return SaveMat(name, s, m =>
            {
                m.mainTexture = tex;
                if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", tint);
            });
        }

        private static Texture2D SavePng(string name, Texture2D tex)
        {
            Dir(Gen + "/Textures");
            string path = Gen + "/Textures/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Texture2D Dot()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            tex.Apply();
            return SavePng("Drink_Dot", tex);
        }

        private static Texture2D Ring()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) / 0.12f) + Mathf.Clamp01(0.75f - d) * 0.25f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
                }
            tex.Apply();
            return SavePng("Drink_Ring", tex);
        }

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

        /// <summary>Tapered cylinder around the Y axis (pivot = bottom center of the cup).</summary>
        private static Mesh Frustum(string name, float rBottom, float rTop, float y0, float y1, int sides, bool capBottom, bool capTop, float rangeY0 = 0f, float rangeY1 = 0f)
        {
            // rangeY0/1: the radii are given at these heights (default: y0..y1), so a sleeve hugs the cup's taper
            if (rangeY1 <= rangeY0) { rangeY0 = y0; rangeY1 = y1; }
            float r0 = Mathf.Lerp(rBottom, rTop, (y0 - rangeY0) / (rangeY1 - rangeY0));
            float r1 = Mathf.Lerp(rBottom, rTop, (y1 - rangeY0) / (rangeY1 - rangeY0));
            var v = new List<Vector3>();
            var nrm = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            float slope = (r0 - r1) / (y1 - y0);
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 n = (dir + Vector3.up * slope).normalized;
                v.Add(dir * r0 + Vector3.up * y0); nrm.Add(n); uv.Add(new Vector2(i / (float)sides, 0f));
                v.Add(dir * r1 + Vector3.up * y1); nrm.Add(n); uv.Add(new Vector2(i / (float)sides, 1f));
            }
            for (int i = 0; i < sides; i++)
            {
                int k = i * 2;
                tri.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 });
            }
            if (capBottom) Cap(v, nrm, uv, tri, y0, r0, sides, false);
            if (capTop) Cap(v, nrm, uv, tri, y1, r1, sides, true);
            var mesh = new Mesh();
            mesh.SetVertices(v);
            mesh.SetNormals(nrm);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0);
            return SaveMesh(name, mesh);
        }

        private static void Cap(List<Vector3> v, List<Vector3> nrm, List<Vector2> uv, List<int> tri, float y, float r, int sides, bool up)
        {
            int c = v.Count;
            Vector3 n = up ? Vector3.up : Vector3.down;
            v.Add(new Vector3(0f, y, 0f)); nrm.Add(n); uv.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r)); nrm.Add(n); uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
            }
            for (int i = 0; i < sides; i++)
            {
                if (up) tri.AddRange(new[] { c, c + i + 2, c + i + 1 });
                else tri.AddRange(new[] { c, c + i + 1, c + i + 2 });
            }
        }

        /// <summary>Thin open cylinder, pivot at the top, 1 unit long pointing down.</summary>
        private static Mesh Stream(string name, float radius, int sides)
        {
            var v = new List<Vector3>();
            var nrm = new List<Vector3>();
            var tri = new List<int>();
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(dir * radius); nrm.Add(dir);
                v.Add(dir * radius * 0.8f + Vector3.down); nrm.Add(dir);
            }
            for (int i = 0; i < sides; i++)
            {
                int k = i * 2;
                tri.AddRange(new[] { k, k + 3, k + 1, k, k + 2, k + 3 });
            }
            var mesh = new Mesh();
            mesh.SetVertices(v);
            mesh.SetNormals(nrm);
            mesh.SetTriangles(tri, 0);
            return SaveMesh(name, mesh);
        }

        // ------------------------------------------------------------------ sounds
        // Procedural placeholders in the spirit of a fast food soda fountain. Swap in your own clips on the
        // DrinkMachine (Sounds section) any time; everything plays from those fields.

        private static void Sounds(DrinkMachine mb)
        {
            mb.ClipButtonClick = Wav("drink_button", 0.14f, t =>
                (Noise() * 0.5f + Sin(2600f, t) * 0.45f) * Env(t, 0.0005f, 0.005f)
                + Sin(240f, t) * Env(t, 0.001f, 0.02f) * 0.45f
                + (t > 0.075f ? (Noise() * 0.3f + Sin(3100f, t) * 0.25f) * Env(t - 0.075f, 0.0005f, 0.004f) : 0f));

            mb.ClipButtonDeny = Wav("drink_deny", 0.34f, t =>
            {
                float f = t < 0.14f ? 330f : 247f;
                float lt = t < 0.14f ? t : t - 0.16f;
                return lt < 0f ? 0f : Mathf.Sign(Sin(f, lt)) * 0.16f * Env(lt, 0.004f, 0.07f);
            });

            mb.ClipCupLock = Wav("drink_cup_lock", 0.24f, t =>
            {
                // plastic cup hitting the grate, then the holder clicking shut
                float hit = Noise() * Env(t, 0.0005f, 0.006f) * 0.8f;
                float body = (Sin(820f, t) * 0.5f + Sin(1370f, t) * 0.3f + Sin(2400f, t) * 0.15f) * Env(t, 0.001f, 0.03f);
                float thud = Sin(160f - 60f * t, t) * Env(t, 0.002f, 0.04f) * 0.6f;
                float latch = t > 0.05f ? (Noise() * 0.5f + Sin(3200f, t) * 0.45f) * Env(t - 0.05f, 0.0005f, 0.007f) : 0f;
                return (hit + body + thud + latch) * 0.8f;
            });

            float scrape = 0f;
            mb.ClipCupUnlock = Wav("drink_cup_unlock", 0.2f, t =>
            {
                scrape += 0.3f * (Noise() - scrape);
                float click = (Noise() * 0.4f + Sin(2900f, t) * 0.4f) * Env(t, 0.0005f, 0.006f);
                return click + scrape * Env(t, 0.01f, 0.05f) * 0.5f;
            });

            float lpA = 0f, lpB = 0f;
            mb.ClipPourStart = Wav("drink_pour_start", 0.4f, t =>
            {
                float n = Noise();
                lpA += 0.08f * (n - lpA);
                lpB += 0.45f * (n - lpB);
                float hiss = (lpB - lpA) * (1f - Mathf.Exp(-t / 0.05f)) * Env(t, 0.02f, 0.25f);
                float valve = Sin(110f, t) * Env(t, 0.002f, 0.035f) * 0.7f + Noise() * Env(t, 0.0005f, 0.004f) * 0.5f;
                return valve + hiss * 0.9f;
            });

            mb.ClipPourLoop = Loop("drink_pour_loop", 2.4f, 0.4f, PourSample());

            mb.ClipPourStop = Wav("drink_pour_stop", 0.75f, t =>
            {
                float valve = (Noise() * 0.4f + Sin(1900f, t) * 0.35f) * Env(t, 0.0005f, 0.006f) + Sin(130f, t) * Env(t, 0.002f, 0.03f) * 0.5f;
                return valve + Drip(t, 0.16f) + Drip(t, 0.34f) * 0.7f + Drip(t, 0.58f) * 0.45f;
            });

            mb.ClipPourFull = Wav("drink_full", 0.9f, t => Notes(t, new[] { 1318.5f, 1760f }, 0.12f, 0.35f) * 0.7f);

            float crackle = 0f;
            mb.ClipFizz = Wav("drink_fizz", 1.6f, t =>
            {
                float density = 0.02f * (1f - t / 1.6f) * (1f - t / 1.6f);
                if (rng.NextDouble() < density) crackle += (float)rng.NextDouble();
                crackle *= 0.88f;
                return crackle * Noise() * 0.35f;
            });

            mb.ClipSlurpLoop = Loop("drink_slurp_loop", 1.2f, 0.25f, SlurpSample());

            mb.ClipCupRespawn = Wav("drink_cup_respawn", 0.2f, t =>
                Sin(700f - 2500f * Mathf.Min(t, 0.09f), t) * Env(t, 0.001f, 0.035f) * 0.6f + Noise() * Env(t, 0.0005f, 0.004f) * 0.3f);
        }

        private static float Drip(float t, float at)
        {
            float lt = t - at;
            if (lt < 0f || lt > 0.06f) return 0f;
            return Sin(900f + 2200f * lt / 0.04f, lt) * Env(lt, 0.001f, 0.015f) * 0.35f;
        }

        private static float[] PourSample()
        {
            // soda stream: band-passed rush, a bubbling gurgle and carbonation crackle
            int count = (int)(44100 * 2.8f);
            var s = new float[count];
            float lp1 = 0f, lp2 = 0f, crack = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / 44100f;
                float n = Noise();
                lp1 += 0.06f * (n - lp1);
                lp2 += 0.5f * (n - lp2);
                float hiss = lp2 - lp1;
                float gurgle = Sin(170f + 70f * Sin(3.1f, t) + 30f * Sin(7.7f, t), t) * 0.07f * (0.5f + 0.5f * Sin(4.3f, t));
                if (rng.NextDouble() < 0.004) crack += (float)rng.NextDouble();
                crack *= 0.9f;
                s[i] = (lp1 * 1.6f + hiss * 0.8f + gurgle + crack * Noise() * 0.18f) * 0.55f;
            }
            return s;
        }

        private static float[] SlurpSample()
        {
            int count = (int)(44100 * 1.45f);
            var s = new float[count];
            float lp1 = 0f, lp2 = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / 44100f;
                float n = Noise();
                lp1 += 0.12f * (n - lp1);
                lp2 += 0.6f * (n - lp2);
                float wobble = 0.55f + 0.45f * Sin(6.5f + 2f * Sin(0.9f, t), t);
                float blub = Sin(220f + 90f * Sin(11f, t), t) * 0.12f * Mathf.Max(0f, Sin(3.7f, t));
                s[i] = ((lp2 - lp1) * 0.9f * wobble + blub) * 0.6f;
            }
            return s;
        }

        private static float[] HumSample()
        {
            int count = (int)(44100 * 2.25f);
            var s = new float[count];
            float lp = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / 44100f;
                lp += 0.02f * (Noise() - lp);
                s[i] = Sin(60f, t) * 0.18f + Sin(120f, t) * 0.12f + Sin(180f, t) * 0.04f + lp * 0.5f;
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

        /// <summary>Seamless loop: the last 'fade' seconds of the source are crossfaded into its start.</summary>
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
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer != null && (name.Contains("loop") || name.Contains("hum")))
            {
                // loops stay uncompressed so they loop without a gap (they're small)
                AudioImporterSampleSettings st = importer.defaultSampleSettings;
                st.loadType = AudioClipLoadType.DecompressOnLoad;
                st.compressionFormat = AudioCompressionFormat.PCM;
                importer.defaultSampleSettings = st;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        private static float Sin(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);
        private static float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);
        private static float Env(float t, float attack, float decay) => t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);

        private static float Notes(float t, float[] hz, float step, float decay)
        {
            int k = Mathf.Min((int)(t / step), hz.Length - 1);
            float lt = t - k * step;
            return (Sin(hz[k], lt) * 0.55f + Sin(hz[k] * 2f, lt) * 0.18f) * Env(lt, 0.004f, decay);
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color c);
            return c;
        }
    }
}
