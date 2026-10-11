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
    /// Builds a black and gold bar drink station: drink cards with pour buttons, gold nozzles with warm light, a garnish
    /// drawer, a shelf of glowing logo glasses, an options tower (froth / strength / spirit / fruit / straw), a cup
    /// customizer kiosk and a neon sign. Everything (meshes, textures, icons, materials, sounds) is generated into
    /// Assets/DrinkSystem/Generated. Select your bar counter first to put the station on top of it.
    /// </summary>
    public static class DrinkMachineBuilder
    {
        // ---- easy to change
        private const string Brand = "GOLDEN HOUR";
        private const string CupLogoText = "G";
        private const string NeonText = "Fancy a\nDrink?";
        private const int CupCount = 8;

        private const string Root = "Assets/DrinkSystem";
        private const string Gen = Root + "/Generated";
        private const string RootName = "Drink Machine";
        private const float PxScale = 0.0005f; // UI canvases: 1 px = 0.5 mm

        // ---- layout (meters, the bar top is y = 0, players stand on +Z)
        private const float MainW = 1.05f, Depth = 0.42f, Front = 0.21f, Back = -0.21f;
        private const float BaseTop = 0.10f, GrateTop = 0.11f, BodyBottom = 0.38f, BodyTop = 0.64f;
        private const float NozzleZ = 0.055f, SlotSpacing = 0.175f;
        private const float TowerX0 = 0.56f, TowerX1 = 1.30f, TowerTop = 0.66f;
        private const float KioskX = -0.78f, KioskW = 0.40f;
        private const float CupH = 0.15f, CupRB = 0.028f, CupRT = 0.036f;

        private struct Drink
        {
            public string Name; public int Glass, Garnish; public string Main, Foam, Edge; public float Transparency, FoamHeight;
            public Drink(string name, int glass, int garnish, string main, string foam, string edge, float transparency, float foamHeight)
            { Name = name; Glass = glass; Garnish = garnish; Main = main; Foam = foam; Edge = edge; Transparency = transparency; FoamHeight = foamHeight; }
        }

        // Glass: 0 highball, 1 goblet, 2 martini. Garnish: 0 orange, 1 berries, 2 lime + mint, 3 lemon, 4 coffee beans
        private static readonly Drink[] Drinks =
        {
            new Drink("Golden Hour Spritz", 1, 0, "FF8A1E", "FFD9A0", "FFC060", 0.35f, 0.03f),
            new Drink("Berry Bliss", 0, 1, "B0102A", "F2A0B0", "FF5070", 0.25f, 0.03f),
            new Drink("Tropical Mule", 0, 2, "E8C25A", "FFF2C8", "FFE08A", 0.40f, 0.04f),
            new Drink("Citrus Smash", 0, 3, "C8D94A", "F4FFD0", "F0FF90", 0.45f, 0.03f),
            new Drink("Espresso Martini", 2, 4, "2A140A", "D9B98C", "6B3A1E", 0.05f, 0.16f),
        };

        private struct Fruit
        {
            public string Name, Liquid, Rind, Flesh; public int Style; public float Transparency;
            public Fruit(string name, string liquid, string rind, string flesh, int style, float transparency)
            { Name = name; Liquid = liquid; Rind = rind; Flesh = flesh; Style = style; Transparency = transparency; }
        }

        // Slice style: 0 citrus segments, 1 berry seeds, 2 pineapple core, 3 dragon fruit seeds, 4 apple star, 5 grape
        private static readonly Fruit[] Fruits =
        {
            new Fruit("Citrus", "F7D23A", "F2C200", "FFF2A0", 0, 0.45f),
            new Fruit("Berry", "D81E3C", "B0122E", "F2506A", 1, 0.30f),
            new Fruit("Tropical", "FFB02E", "D98A10", "FFD27A", 2, 0.35f),
            new Fruit("Exotic", "E83E9A", "D2207F", "FAF4F4", 3, 0.30f),
            new Fruit("Apple", "A8D85A", "5FA82A", "F4F0C8", 4, 0.45f),
            new Fruit("Grape", "6B2A8C", "4A1766", "B48ACF", 5, 0.25f),
        };

        private static readonly string[] StrawNames = { "Gold Metal", "Black Metal", "Glass", "Bamboo", "Paper", "Custom" };
        private static readonly string[] TintNames = { "Clear", "Smoke", "Amber", "Rose", "Violet", "Emerald" };
        private static readonly Color[] Tints =
        {
            new Color(0.92f, 0.96f, 1f, 0.10f), new Color(0.25f, 0.25f, 0.28f, 0.40f), new Color(1f, 0.65f, 0.25f, 0.25f),
            new Color(1f, 0.5f, 0.66f, 0.22f), new Color(0.6f, 0.42f, 1f, 0.25f), new Color(0.24f, 0.84f, 0.55f, 0.22f),
        };
        private static readonly string[] LogoNames = { "GOLD", "SILVER", "NONE" };
        private static readonly Color[] LogoColors = { new Color(1f, 0.76f, 0.29f, 1f), new Color(0.9f, 0.92f, 0.96f, 1f), new Color(1f, 1f, 1f, 0f) };

        private static readonly Color Gold = new Color(0.79f, 0.59f, 0.18f, 1f);
        private static readonly Color GoldText = new Color(0.93f, 0.74f, 0.38f, 1f);
        private static readonly Color Panel = new Color(0.07f, 0.065f, 0.085f, 0.97f);
        private static readonly Color Card = new Color(0.09f, 0.085f, 0.11f, 1f);
        private static readonly Color OptionOff = new Color(0.12f, 0.11f, 0.15f, 1f);

        private static TMP_FontAsset font;
        private static Sprite roundSprite, knobSprite, ringSprite;
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

            // Selected bar counter = where the station goes
            GameObject target = Selection.activeGameObject;
            if (target != null && target.name == RootName) target = null;
            Bounds targetBounds = default;
            bool onTarget = target != null && TryBounds(target, out targetBounds);

            GameObject old = GameObject.Find(RootName);
            if (old != null)
            {
                if (!EditorUtility.DisplayDialog("Drink System", "A '" + RootName + "' object already exists in this scene. Replace it?", "Replace", "Cancel")) return;
                Undo.DestroyObjectImmediate(old);
            }

            made.Clear();
            try
            {
                EditorUtility.DisplayProgressBar("Drink System", "Preparing UdonSharp programs...", 0.05f);
                EnsurePrograms();
                EditorUtility.DisplayProgressBar("Drink System", "Painting icons, building the station...", 0.3f);
                GameObject root = BuildAll(!onTarget);
                if (onTarget) root.transform.position = new Vector3(targetBounds.center.x - (TowerX1 + KioskX - KioskW * 0.5f) * 0.5f, targetBounds.max.y, targetBounds.center.z);
                else
                {
                    Vector3 p = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : new Vector3(0f, 0f, 3f);
                    root.transform.position = new Vector3(p.x, 1.0f, p.z);
                }
                EditorUtility.DisplayProgressBar("Drink System", "Wiring Udon behaviours...", 0.9f);
                foreach (UdonSharpBehaviour b in made) UdonSharpEditorUtility.CopyProxyToUdon(b);
                Undo.RegisterCreatedObjectUndo(root, "Build Drink Machine");
                EditorSceneManager.MarkSceneDirty(root.scene);
                AssetDatabase.SaveAssets();
                PrefabUtility.SaveAsPrefabAssetAndConnect(root, Root + "/Drink Machine.prefab", InteractionMode.AutomatedAction);
                Selection.activeGameObject = root;
                Debug.Log("[DrinkSystem] Drink machine built" + (onTarget ? " on top of '" + target.name + "'" : "") + ". Prefab: " + Root + "/Drink Machine.prefab");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static bool TryBounds(GameObject go, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return any;
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

        private static GameObject BuildAll(bool withCounter)
        {
            roundSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            knobSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            ringSprite = SaveSprite("Gauge_Ring", RingTex());

            // --- materials
            Material body = LitMat("Body_Black", Hex("0E0D11"), 0.4f, 0.9f, Color.black);
            Material trim = LitMat("Trim_Gold", Hex("C9962E"), 1f, 0.8f, Hex("FFB347") * 0.9f);
            Material metal = LitMat("Metal_Gold", Hex("D4A64A"), 1f, 0.85f, new Color(0.22f, 0.15f, 0.04f));
            Material warm = LitMat("Light_Warm", Hex("FFD9A0"), 0f, 0.5f, Hex("FFC070") * 2.5f);
            Material counterMat = LitMat("Counter", Hex("16141B"), 0.2f, 0.85f, Color.black);
            Material grate = LitMat("Grate", Hex("B88A35"), 1f, 0.7f, new Color(0.1f, 0.07f, 0.02f), GrateTex());
            grate.mainTextureScale = new Vector2(8f, 1f);
            Material liquidMat = ShaderMat("Liquid", "DrinkSystem/Liquid");
            Material streamMat = ShaderMat("Stream", "DrinkSystem/Stream");
            Material cupMat = ShaderMat("Cup_Glass", "DrinkSystem/Cup");
            Texture2D logo = TextTexture("Cup_Logo", CupLogoText, 256, 256);
            cupMat.SetTexture("_LogoTex", logo);
            Material darkGlass = ShaderMat("Drawer_Glass", "DrinkSystem/Cup");
            darkGlass.SetColor("_Color", new Color(0.06f, 0.05f, 0.04f, 0.28f));
            darkGlass.SetColor("_LogoColor", new Color(0f, 0f, 0f, 0f));
            Material coneMat = AdditiveMat("Light_Cone", ConeTex(), new Color(1f, 0.72f, 0.38f, 0.22f));
            Material poolMat = AdditiveMat("Light_Pool", DotTex(), new Color(1f, 0.7f, 0.35f, 0.5f));
            Material guideMat = AdditiveMat("Slot_Guide", RingTex(), new Color(1f, 0.75f, 0.35f, 0.8f));
            Material splashMat = AdditiveMat("Splash", DotTex(), new Color(0.6f, 0.6f, 0.6f, 0.6f));

            var fruitSprites = new Sprite[Fruits.Length];
            var garnishMats = new Material[Fruits.Length];
            for (int i = 0; i < Fruits.Length; i++)
            {
                Fruit f = Fruits[i];
                fruitSprites[i] = SaveSprite("Fruit_" + f.Name, SliceTex(Hex(f.Rind), Hex(f.Flesh), f.Style));
                garnishMats[i] = LitMat("Garnish_" + f.Name, Color.white, 0f, 0.6f, new Color(0.08f, 0.08f, 0.08f), fruitSprites[i].texture);
            }
            Material orangeMat = LitMat("Garnish_Orange", Color.white, 0f, 0.6f, new Color(0.08f, 0.08f, 0.08f), SaveSprite("Fruit_Orange", SliceTex(Hex("F07A10"), Hex("FFB347"), 0)).texture);
            Material limeMat = LitMat("Garnish_Lime", Color.white, 0f, 0.6f, new Color(0.08f, 0.08f, 0.08f), SaveSprite("Fruit_Lime", SliceTex(Hex("3E9A1E"), Hex("B8E07A"), 0)).texture);
            Material berryMat = LitMat("Decor_Berry", Hex("C8102E"), 0f, 0.7f, new Color(0.15f, 0f, 0.02f));
            Material grapeMat = LitMat("Decor_Grape", Hex("5A1E78"), 0f, 0.7f, new Color(0.06f, 0f, 0.08f));
            Material beanMat = LitMat("Decor_Bean", Hex("3A1E0E"), 0f, 0.6f, Color.black);

            var strawMats = new Material[]
            {
                LitMat("Straw_GoldMetal", Hex("E0B04A"), 1f, 0.9f, new Color(0.25f, 0.17f, 0.04f)),
                LitMat("Straw_BlackMetal", Hex("1A1A1E"), 1f, 0.9f, Color.black),
                ShaderMat("Straw_Glass", "DrinkSystem/Cup"),
                LitMat("Straw_Bamboo", Color.white, 0f, 0.4f, Color.black, StrawTex(3)),
                LitMat("Straw_Paper", Color.white, 0f, 0.3f, Color.black, StrawTex(4)),
                LitMat("Straw_Custom", Hex("9B4DFF"), 0.3f, 0.8f, Hex("6A2BD8") * 0.8f),
            };
            strawMats[2].SetColor("_Color", new Color(0.9f, 0.95f, 1f, 0.3f));
            strawMats[2].SetColor("_LogoColor", new Color(0f, 0f, 0f, 0f));
            foreach (Material m in new[] { grate, cupMat, darkGlass, strawMats[2] }) EditorUtility.SetDirty(m);

            // --- meshes
            Mesh cupMesh = Frustum("Cup", CupRB, CupRT, 0f, CupH, 24, true, false);
            Mesh liquidMesh = Frustum("Liquid", CupRB * 0.92f, CupRT * 0.93f, 0.012f, CupH - 0.006f, 24, true, true, 0f, CupH);
            Mesh streamMesh = Tube("Stream", 0.0042f, 0.0034f, -1f, 10);
            Mesh strawMesh = Tube("Straw", 0.0035f, 0.0035f, 0.19f, 10);
            Mesh discMesh = Frustum("Garnish", 0.021f, 0.021f, 0f, 0.005f, 20, true, true);
            Mesh coneMesh = Tube("Light_Cone", 0.006f, 0.065f, -(BodyBottom - 0.035f - GrateTop), 16);
            Mesh sphereMesh = LowSphere("Low_Sphere", 10, 6);

            // --- root + machine brain
            var root = new GameObject(RootName);
            Transform rt = root.transform;
            var mb = UdonSharpUndo.AddComponent<DrinkMachine>(root);
            made.Add(mb);
            Sounds(mb);

            var stat = new GameObject("Station");
            stat.transform.SetParent(rt, false);
            Transform st = stat.transform;
            if (withCounter)
            {
                float cx = (KioskX - KioskW * 0.5f + TowerX1) * 0.5f;
                Box(st, "Counter", new Vector3(cx, -0.5f, 0.02f), new Vector3(TowerX1 - KioskX + KioskW * 0.5f + 0.3f, 1f, 0.75f), counterMat, true);
                Box(st, "Counter Trim", new Vector3(cx, -0.004f, 0.395f), new Vector3(TowerX1 - KioskX + KioskW * 0.5f + 0.3f, 0.008f, 0.006f), trim);
            }
            MainUnit(st, body, trim, metal, warm, grate, darkGlass, coneMat, poolMat, coneMesh, sphereMesh, discMesh,
                orangeMat, limeMat, garnishMats[0], berryMat, grapeMat, beanMat);
            Tower(st, body, trim);
            Kiosk(st, body, trim, metal, warm, grate, coneMat, poolMat, coneMesh);
            Neon(rt);
            foreach (Transform t in st.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;

            // --- flavors: 5 drinks then the 6 fruits (FruitFlavorStart = 5)
            var flavorsParent = new GameObject("Flavors");
            flavorsParent.transform.SetParent(rt, false);
            foreach (Drink d in Drinks) Flavor(flavorsParent.transform, d.Name, Hex(d.Main), Hex(d.Foam), Hex(d.Edge), d.Transparency, d.FoamHeight);
            foreach (Fruit f in Fruits) Flavor(flavorsParent.transform, f.Name, Hex(f.Liquid), Color.Lerp(Hex(f.Liquid), Color.white, 0.7f), Hex(f.Flesh), f.Transparency, 0.03f);

            // --- slots: 6 nozzles with their cards, plus the customizer pad
            var slotsParent = new GameObject("Slots");
            slotsParent.transform.SetParent(rt, false);
            RectTransform bg = Canvas(rt, "Drink Panel", new Vector3(0f, (BodyBottom + BodyTop) * 0.5f, Front + 0.0015f), new Vector2(2040f, 500f), false);
            Img(bg, "Background", Vector2.zero, new Vector2(2040f, 500f), roundSprite, Panel, true);
            Label(bg, "Brand", Brand, new Vector2(-800f, 210f), new Vector2(380f, 40f), 26f, GoldText, TextAlignmentOptions.Left);
            Label(bg, "Title", "CHOOSE YOUR DRINK", new Vector2(0f, 210f), new Vector2(900f, 52f), 38f, Color.white);
            Img(bg, "Line", new Vector2(0f, 180f), new Vector2(1960f, 2f), null, new Color(Gold.r, Gold.g, Gold.b, 0.5f), false);
            for (int i = 0; i < 6; i++)
            {
                float x = -SlotSpacing * 2.5f + i * SlotSpacing;
                bool custom = i == 5;
                Sprite icon = custom ? SaveSprite("Icon_Custom", CustomIcon()) : SaveSprite("Icon_" + i, DrinkIcon(Drinks[i]));
                NozzleSlot(slotsParent.transform, i, x, custom, icon, mb, guideMat, streamMat, streamMesh, splashMat);
            }
            DrinkSlot customizer = CustomizerSlot(slotsParent.transform, mb, guideMat);

            // --- cups on the top shelf
            var cupsParent = new GameObject("Cups");
            cupsParent.transform.SetParent(rt, false);
            float cupStep = (MainW - 0.14f) / (CupCount - 1);
            for (int i = 0; i < CupCount; i++)
            {
                var pos = new Vector3(-MainW * 0.5f + 0.07f + i * cupStep, BodyTop + 0.004f, -0.02f);
                Cup(cupsParent.transform, i, pos, cupMesh, liquidMesh, strawMesh, discMesh, cupMat, liquidMat);
            }

            // --- options + customizer UI
            OptionsPanel(rt, mb, fruitSprites);
            KioskPanel(rt, mb, customizer);

            mb.FlavorsParent = flavorsParent;
            mb.SlotsParent = slotsParent;
            mb.CupsParent = cupsParent;
            mb.SnapRadius = 0.09f;
            mb.FruitFlavorStart = Drinks.Length;
            mb.FruitNames = Array.ConvertAll(Fruits, f => f.Name);
            mb.StrawNames = StrawNames;
            mb.StrawMaterials = strawMats;
            mb.GarnishMaterials = garnishMats;
            mb.CustomizerSlot = customizer;
            mb.CupTints = Tints;
            mb.LogoColors = LogoColors;
            mb.OptionOn = Gold;
            mb.OptionOff = OptionOff;
            return root;
        }

        private static void Flavor(Transform parent, string name, Color main, Color foam, Color edge, float transparency, float foamHeight)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var f = UdonSharpUndo.AddComponent<DrinkFlavor>(go);
            f.DisplayName = name;
            f.FillSpeed = 0.2f;
            f.Transparency = transparency;
            f.ColorMain = main;
            f.ColorFoam = foam;
            f.ColorEdge = edge;
            f.EdgePower = 3f;
            f.EdgeStrength = 1.1f;
            f.FoamHeight = foamHeight;
            f.FoamSmoothness = 0.03f;
            made.Add(f);
        }

        // ------------------------------------------------------------------ station geometry

        private static void MainUnit(Transform st, Material body, Material trim, Material metal, Material warm, Material grate, Material darkGlass,
            Material coneMat, Material poolMat, Mesh coneMesh, Mesh sphere, Mesh disc,
            Material orange, Material lime, Material lemon, Material berry, Material grape, Material bean)
        {
            var m = new GameObject("Main Unit").transform;
            m.SetParent(st, false);
            float w = MainW;
            // base with a glass-front garnish drawer
            float drawerBack = Front - 0.13f;
            Box(m, "Base", new Vector3(0f, BaseTop * 0.5f, (Back + drawerBack) * 0.5f), new Vector3(w, BaseTop, drawerBack - Back), body, true);
            Box(m, "Drawer Floor", new Vector3(0f, 0.004f, (drawerBack + Front) * 0.5f), new Vector3(w, 0.008f, Front - drawerBack), body, true);
            Box(m, "Drawer Top", new Vector3(0f, BaseTop - 0.004f, (drawerBack + Front) * 0.5f), new Vector3(w, 0.008f, Front - drawerBack), body, true);
            for (int k = 0; k <= 6; k++)
                Box(m, "Drawer Divider", new Vector3(-w * 0.5f + 0.0025f + k * (w - 0.005f) / 6f, BaseTop * 0.5f, (drawerBack + Front) * 0.5f), new Vector3(0.005f, BaseTop, Front - drawerBack), trim);
            var glass = Box(m, "Drawer Glass", new Vector3(0f, BaseTop * 0.5f, Front + 0.001f), new Vector3(w, BaseTop - 0.008f, 0.002f), darkGlass);
            Shadowless(glass.GetComponent<Renderer>());
            Box(m, "Drawer Light", new Vector3(0f, BaseTop - 0.011f, Front - 0.01f), new Vector3(w - 0.01f, 0.003f, 0.004f), warm);
            Box(m, "Trim Base", new Vector3(0f, BaseTop + 0.001f, Front + 0.002f), new Vector3(w + 0.004f, 0.004f, 0.006f), trim);
            Box(m, "Grate", new Vector3(0f, (BaseTop + GrateTop) * 0.5f, NozzleZ + 0.01f), new Vector3(w - 0.04f, GrateTop - BaseTop, 0.25f), grate, true);
            // back column and the upper body (nozzles hang under it)
            Box(m, "Back", new Vector3(0f, (BaseTop + BodyBottom) * 0.5f, Back + 0.06f), new Vector3(w, BodyBottom - BaseTop, 0.12f), body, true);
            Box(m, "Upper", new Vector3(0f, (BodyBottom + BodyTop) * 0.5f, 0f), new Vector3(w, BodyTop - BodyBottom, Depth), body, true);
            Box(m, "Trim Top", new Vector3(0f, BodyTop, Front + 0.002f), new Vector3(w + 0.004f, 0.005f, 0.006f), trim);
            Box(m, "Trim Bottom", new Vector3(0f, BodyBottom, Front + 0.002f), new Vector3(w + 0.004f, 0.005f, 0.006f), trim);
            Box(m, "Trim Left", new Vector3(-w * 0.5f - 0.002f, (BodyBottom + BodyTop) * 0.5f, Front + 0.002f), new Vector3(0.005f, BodyTop - BodyBottom, 0.006f), trim);
            Box(m, "Trim Right", new Vector3(w * 0.5f + 0.002f, (BodyBottom + BodyTop) * 0.5f, Front + 0.002f), new Vector3(0.005f, BodyTop - BodyBottom, 0.006f), trim);
            Box(m, "Under Light", new Vector3(0f, BodyBottom - 0.002f, Front - 0.015f), new Vector3(w - 0.02f, 0.003f, 0.006f), warm);
            // glowing shelf for the glasses on top
            Box(m, "Shelf Glow", new Vector3(0f, BodyTop + 0.002f, -0.02f), new Vector3(w - 0.04f, 0.003f, 0.07f), warm);
            Box(m, "Shelf Rail", new Vector3(0f, BodyTop + 0.012f, 0.03f), new Vector3(w - 0.02f, 0.02f, 0.004f), trim);

            for (int i = 0; i < 6; i++)
            {
                float x = -SlotSpacing * 2.5f + i * SlotSpacing;
                // nozzle
                Cyl(m, "Nozzle " + (i + 1), new Vector3(x, BodyBottom - 0.0175f, NozzleZ), 0.026f, 0.035f, metal);
                Cyl(m, "Nozzle Ring", new Vector3(x, BodyBottom - 0.002f, NozzleZ), 0.036f, 0.004f, trim);
                Cyl(m, "Nozzle Light", new Vector3(x, BodyBottom - 0.0355f, NozzleZ), 0.016f, 0.002f, warm);
                LightCone(m, new Vector3(x, BodyBottom - 0.035f, NozzleZ), coneMesh, coneMat, poolMat);
                // garnish drawer bin
                float bx = -w * 0.5f + (i + 0.5f) * (w / 6f);
                Vector3 bc = new Vector3(bx, 0.008f, (drawerBack + Front) * 0.5f);
                switch (i)
                {
                    case 0: Slices(m, bc, disc, orange, 5); break;
                    case 1: Balls(m, bc, sphere, berry, 9, 0.016f, new Vector3(1f, 0.9f, 1f)); break;
                    case 2: Slices(m, bc, disc, lime, 5); break;
                    case 3: Slices(m, bc, disc, lemon, 5); break;
                    case 4: Balls(m, bc, sphere, bean, 16, 0.011f, new Vector3(1.3f, 0.8f, 1f)); break;
                    default: Balls(m, bc, sphere, grape, 10, 0.014f, Vector3.one); break;
                }
            }
        }

        private static void Tower(Transform st, Material body, Material trim)
        {
            var t = new GameObject("Options Tower").transform;
            t.SetParent(st, false);
            float cx = (TowerX0 + TowerX1) * 0.5f, w = TowerX1 - TowerX0;
            Box(t, "Body", new Vector3(cx, TowerTop * 0.5f, Front - 0.15f), new Vector3(w, TowerTop, 0.30f), body, true);
            Box(t, "Trim Top", new Vector3(cx, TowerTop, Front + 0.002f), new Vector3(w + 0.004f, 0.005f, 0.006f), trim);
            Box(t, "Trim Left", new Vector3(TowerX0 - 0.002f, TowerTop * 0.5f, Front + 0.002f), new Vector3(0.005f, TowerTop, 0.006f), trim);
            Box(t, "Trim Right", new Vector3(TowerX1 + 0.002f, TowerTop * 0.5f, Front + 0.002f), new Vector3(0.005f, TowerTop, 0.006f), trim);
            Box(t, "Trim Base", new Vector3(cx, 0.003f, Front + 0.002f), new Vector3(w + 0.004f, 0.005f, 0.006f), trim);
        }

        private static void Kiosk(Transform st, Material body, Material trim, Material metal, Material warm, Material grate, Material coneMat, Material poolMat, Mesh coneMesh)
        {
            var k = new GameObject("Cup Kiosk").transform;
            k.SetParent(st, false);
            float w = KioskW;
            Box(k, "Base", new Vector3(KioskX, BaseTop * 0.5f, 0f), new Vector3(w, BaseTop, Depth), body, true);
            Box(k, "Pad", new Vector3(KioskX, (BaseTop + GrateTop) * 0.5f, NozzleZ + 0.01f), new Vector3(0.16f, GrateTop - BaseTop, 0.2f), grate, true);
            Box(k, "Back", new Vector3(KioskX, (BaseTop + 0.40f) * 0.5f, Back + 0.06f), new Vector3(w, 0.40f - BaseTop, 0.12f), body, true);
            Box(k, "Hood", new Vector3(KioskX, (0.40f + TowerTop) * 0.5f, 0f), new Vector3(w, TowerTop - 0.40f, Depth), body, true);
            Box(k, "Trim Top", new Vector3(KioskX, TowerTop, Front + 0.002f), new Vector3(w + 0.004f, 0.005f, 0.006f), trim);
            Box(k, "Trim Hood", new Vector3(KioskX, 0.40f, Front + 0.002f), new Vector3(w + 0.004f, 0.005f, 0.006f), trim);
            Box(k, "Trim Base", new Vector3(KioskX, BaseTop + 0.001f, Front + 0.002f), new Vector3(w + 0.004f, 0.004f, 0.006f), trim);
            Cyl(k, "Spot", new Vector3(KioskX, 0.398f, NozzleZ), 0.03f, 0.004f, warm);
            LightCone(k, new Vector3(KioskX, 0.396f, NozzleZ), coneMesh, coneMat, poolMat);
        }

        private static void LightCone(Transform parent, Vector3 top, Mesh coneMesh, Material coneMat, Material poolMat)
        {
            var cone = new GameObject("Light Cone");
            cone.transform.SetParent(parent, false);
            cone.transform.localPosition = top;
            cone.AddComponent<MeshFilter>().sharedMesh = coneMesh;
            var r = cone.AddComponent<MeshRenderer>();
            r.sharedMaterial = coneMat;
            Shadowless(r);
            var pool = Prim(PrimitiveType.Quad, "Light Pool", parent, new Vector3(top.x, GrateTop + 0.0015f, top.z), Vector3.one * 0.13f, poolMat);
            pool.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Shadowless(pool.GetComponent<Renderer>());
        }

        private static void Slices(Transform parent, Vector3 c, Mesh disc, Material mat, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Slice");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = c + new Vector3(-0.05f + i * 0.025f, 0.022f, (float)(rng.NextDouble() - 0.5) * 0.04f);
                go.transform.localRotation = Quaternion.Euler(0f, (float)(rng.NextDouble() - 0.5) * 30f, 70f + (float)rng.NextDouble() * 20f);
                go.AddComponent<MeshFilter>().sharedMesh = disc;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        private static void Balls(Transform parent, Vector3 c, Mesh sphere, Material mat, int count, float size, Vector3 shape)
        {
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Fruit");
                go.transform.SetParent(parent, false);
                float a = i * 2.39996f, r = 0.012f * Mathf.Sqrt(i);
                go.transform.localPosition = c + new Vector3(Mathf.Cos(a) * r * 1.6f, size * 0.5f + (i % 3) * size * 0.35f, Mathf.Sin(a) * r * 0.8f);
                go.transform.localRotation = Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, 0f);
                go.transform.localScale = Vector3.Scale(Vector3.one * size, shape);
                go.AddComponent<MeshFilter>().sharedMesh = sphere;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        private static void Neon(Transform rt)
        {
            var go = new GameObject("Neon Sign", typeof(RectTransform));
            go.transform.SetParent(rt, false);
            go.transform.localPosition = new Vector3(KioskX, 0.98f, -0.1f);
            var t = go.AddComponent<TextMeshPro>();
            t.font = font;
            t.text = NeonText;
            t.fontStyle = FontStyles.Italic | FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.color = new Color(1f, 0.78f, 0.45f, 1f);
            t.enableAutoSizing = true;
            t.fontSizeMin = 0.2f;
            t.fontSizeMax = 2.4f;
            t.rectTransform.sizeDelta = new Vector2(0.55f, 0.4f);
            Shader s = Shader.Find("TextMeshPro/Mobile/Distance Field");
            if (s != null && font.material != null)
            {
                Material m = SaveMat("Neon_Text", s, mat =>
                {
                    mat.CopyPropertiesFromMaterial(font.material);
                    mat.EnableKeyword("UNDERLAY_ON");
                    mat.SetColor("_UnderlayColor", new Color(1f, 0.45f, 0.08f, 0.85f));
                    mat.SetFloat("_UnderlaySoftness", 1f);
                    mat.SetFloat("_UnderlayDilate", 0.6f);
                    mat.SetFloat("_FaceDilate", 0.15f);
                });
                t.fontSharedMaterial = m;
            }
        }

        // ------------------------------------------------------------------ slots

        private static void NozzleSlot(Transform parent, int i, float x, bool custom, Sprite icon, DrinkMachine mb,
            Material guideMat, Material streamMat, Mesh streamMesh, Material splashMat)
        {
            var go = new GameObject("Slot " + (i + 1) + (custom ? " (Custom)" : ""));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(x, GrateTop, NozzleZ);

            var snap = new GameObject("SnapPoint").transform;
            snap.SetParent(go.transform, false);
            GameObject guide = Guide(go.transform, guideMat);

            var nozzle = new GameObject("Nozzle").transform;
            nozzle.SetParent(go.transform, false);
            nozzle.localPosition = new Vector3(0f, BodyBottom - 0.036f - GrateTop, 0f);
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
            slot.FlavorIndex = custom ? Drinks.Length : i;
            slot.UseFruitFlavor = custom;
            slot.SnapPoint = snap;
            slot.Highlight = guide;
            slot.Nozzle = nozzle;
            slot.Stream = streamR;
            slot.Splash = splash;
            slot.LoopSource = loop;
            slot.OneShotSource = shot;
            made.Add(slot);

            // Drink card: icon, name, status, pour button, fill bar
            const float W = 310f, H = 400f;
            RectTransform card = Canvas(go.transform.parent.parent, "Card " + (i + 1), new Vector3(x, (BodyBottom + BodyTop) * 0.5f - 0.016f, Front + 0.0035f), new Vector2(W, H), true);
            card.SetParent(go.transform, true);
            Img(card, "Background", Vector2.zero, new Vector2(W, H), roundSprite, custom ? new Color(0.17f, 0.08f, 0.26f, 1f) : Card, true);
            Img(card, "Icon", new Vector2(0f, 88f), new Vector2(190f, 190f), icon, Color.white, false).preserveAspect = true;
            Label(card, "Name", custom ? "Custom Drink" : Drinks[i].Name, new Vector2(0f, -36f), new Vector2(W - 30f, 56f), 26f, Color.white);
            slot.StatusText = Label(card, "Status", "POUR", new Vector2(0f, -84f), new Vector2(W - 30f, 26f), 19f, new Color(0.85f, 0.79f, 0.66f));
            slot.StatusLight = Img(card, "Status Light", new Vector2(W * 0.5f - 22f, H * 0.5f - 22f), new Vector2(14f, 14f), knobSprite, new Color(0.25f, 0.25f, 0.3f), false);

            RectTransform hit = Img(card, "Pour Button", new Vector2(0f, -132f), new Vector2(230f, 104f), null, new Color(1f, 1f, 1f, 0f), false).rectTransform;
            Image ring = Img(hit, "Ring", new Vector2(0f, -8f), new Vector2(76f, 76f), knobSprite, Gold, false);
            Img(ring.rectTransform, "Face", Vector2.zero, new Vector2(64f, 64f), knobSprite, new Color(0.05f, 0.045f, 0.06f, 1f), false);
            Img(ring.rectTransform, "Glow", new Vector2(0f, 0f), new Vector2(22f, 22f), knobSprite, new Color(1f, 0.75f, 0.35f, 0.9f), false);
            Button(hit.gameObject, slot, "Press", false, 0, ring.transform, shot, mb.ClipButtonClick);

            Img(card, "Fill Back", new Vector2(0f, -186f), new Vector2(230f, 7f), roundSprite, new Color(1f, 1f, 1f, 0.1f), false);
            Image fill = Img(card, "Fill", new Vector2(0f, -186f), new Vector2(230f, 7f), roundSprite, Color.white, false);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            slot.FillBar = fill;
        }

        private static DrinkSlot CustomizerSlot(Transform parent, DrinkMachine mb, Material guideMat)
        {
            var go = new GameObject("Customizer Pad");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(KioskX, GrateTop, NozzleZ);
            var snap = new GameObject("SnapPoint").transform;
            snap.SetParent(go.transform, false);
            var slot = UdonSharpUndo.AddComponent<DrinkSlot>(go);
            slot.IsCustomizer = true;
            slot.SnapPoint = snap;
            slot.Highlight = Guide(go.transform, guideMat);
            slot.OneShotSource = Audio(go, 0.9f, 6f);
            made.Add(slot);
            return slot;
        }

        private static GameObject Guide(Transform parent, Material mat)
        {
            var guide = Prim(PrimitiveType.Quad, "Placement Guide", parent, new Vector3(0f, 0.002f, 0f), Vector3.one * (CupRT * 3f), mat);
            guide.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Shadowless(guide.GetComponent<Renderer>());
            guide.SetActive(false);
            return guide;
        }

        private static void Cup(Transform parent, int i, Vector3 pos, Mesh cupMesh, Mesh liquidMesh, Mesh strawMesh, Mesh discMesh, Material cupMat, Material liquidMat)
        {
            var go = new GameObject("Cup " + (i + 1));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;

            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = cupMesh;
            col.convex = true;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.3f;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var pickup = go.AddComponent<VRCPickup>();
            pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;
            pickup.UseText = "Drink";
            pickup.InteractionText = "Glass";
            pickup.proximity = 0.4f;
            pickup.DisallowTheft = true;
            pickup.orientation = VRC_Pickup.PickupOrientation.Any;

            var liquid = Part(go.transform, "Liquid", liquidMesh, liquidMat, Vector3.zero, Quaternion.identity);
            liquid.enabled = false;
            var body = Part(go.transform, "Glass", cupMesh, cupMat, Vector3.zero, Quaternion.identity);
            var straw = Part(go.transform, "Straw", strawMesh, cupMat, new Vector3(0.004f, 0.014f, -0.004f), Quaternion.Euler(4f, 0f, 8f));
            straw.enabled = false;
            var garnish = Part(go.transform, "Garnish", discMesh, cupMat, new Vector3(0.026f, CupH - 0.012f, 0.022f), Quaternion.Euler(0f, 50f, 90f));
            garnish.enabled = false;

            var cup = UdonSharpUndo.AddComponent<DrinkCup>(go);
            cup.LiquidRenderer = liquid;
            cup.BodyRenderer = body;
            cup.StrawRenderer = straw;
            cup.GarnishRenderer = garnish;
            cup.OneShotSource = Audio(go, 0.9f, 6f);
            cup.LoopSource = Audio(go, 0.6f, 4f);
            made.Add(cup);
        }

        private static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            Shadowless(r);
            return r;
        }

        // ------------------------------------------------------------------ options + kiosk UI

        private static void OptionsPanel(Transform rt, DrinkMachine mb, Sprite[] fruitSprites)
        {
            const float W = 1400f, H = 1080f;
            float cx = (TowerX0 + TowerX1) * 0.5f;
            RectTransform c = Canvas(rt, "Options Panel", new Vector3(cx, 0.355f, Front + 0.0015f), new Vector2(W, H), true);
            AudioSource click = Audio(c.gameObject, 0.9f, 6f);
            Img(c, "Background", Vector2.zero, new Vector2(W, H), roundSprite, Panel, true);

            string[] titles = { "FROTH", "STRENGTH", "SPIRIT" };
            string[] subs = { "ADJUST FROTH", "ADJUST STRENGTH", "CHOOSE STRENGTH" };
            string[] lows = { "LIGHT", "LIGHT", "LIGHT" };
            string[] highs = { "THICK", "STRONG", "STRONG" };
            string[] sections = { "HEIGHT", "STRENGTH", "STRENGTH" };
            string[] events = { "OptionFroth", "OptionStrength", "OptionSpirit" };
            var gauges = new Image[3];
            var groups = new Image[3][];
            for (int k = 0; k < 3; k++)
            {
                float x = -570f + k * 225f;
                Img(c, titles[k] + " Panel", new Vector2(x, 250f), new Vector2(210f, 580f), roundSprite, Card, true);
                Label(c, titles[k], titles[k], new Vector2(x, 500f), new Vector2(200f, 40f), 32f, GoldText);
                Label(c, titles[k] + " Sub", subs[k], new Vector2(x, 462f), new Vector2(200f, 22f), 15f, new Color(1f, 1f, 1f, 0.55f));
                Image track = Img(c, titles[k] + " Track", new Vector2(x, 345f), new Vector2(160f, 160f), ringSprite, new Color(1f, 1f, 1f, 0.12f), false);
                Gauge(track, 0.75f);
                gauges[k] = Img(c, titles[k] + " Gauge", new Vector2(x, 345f), new Vector2(160f, 160f), ringSprite, Gold, false);
                Gauge(gauges[k], 0.5f);
                Label(c, "Low", lows[k], new Vector2(x - 52f, 250f), new Vector2(80f, 20f), 13f, new Color(1f, 1f, 1f, 0.5f));
                Label(c, "High", highs[k], new Vector2(x + 52f, 250f), new Vector2(80f, 20f), 13f, new Color(1f, 1f, 1f, 0.5f));
                Label(c, "Section", sections[k], new Vector2(x, 192f), new Vector2(200f, 24f), 17f, Color.white);
                string[] names = { "LOW", "MED", k == 0 ? "HIGH" : "STRONG" };
                groups[k] = new Image[3];
                for (int j = 0; j < 3; j++)
                {
                    Image b = Img(c, names[j], new Vector2(x - 64f + j * 64f, 128f), new Vector2(58f, 44f), roundSprite, OptionOff, true);
                    Label(b.rectTransform, "Label", names[j], Vector2.zero, new Vector2(54f, 30f), 13f, Color.white);
                    Button(b.gameObject, mb, events[k], true, j, b.transform, click, mb.ClipButtonClick);
                    groups[k][j] = b;
                }
            }
            mb.FrothGauge = gauges[0];
            mb.StrengthGauge = gauges[1];
            mb.SpiritGauge = gauges[2];
            mb.FrothButtons = groups[0];
            mb.StrengthButtons = groups[1];
            mb.SpiritButtons = groups[2];

            Img(c, "Summary Panel", new Vector2(-345f, -300f), new Vector2(660f, 440f), roundSprite, Card, true);
            Label(c, "Summary Title", "YOUR DRINK", new Vector2(-345f, -125f), new Vector2(600f, 40f), 30f, GoldText);
            Label(c, "Summary Hint", "These are added to the next drink you pour", new Vector2(-345f, -165f), new Vector2(600f, 24f), 16f, new Color(1f, 1f, 1f, 0.5f));
            mb.SummaryText = Label(c, "Summary", "", new Vector2(-345f, -315f), new Vector2(560f, 230f), 30f, Color.white);
            Image respawn = Img(c, "Respawn Glasses", new Vector2(-345f, -478f), new Vector2(340f, 56f), roundSprite, new Color(0.2f, 0.17f, 0.1f, 1f), true);
            Label(respawn.rectTransform, "Label", "RESPAWN GLASSES", Vector2.zero, new Vector2(320f, 34f), 20f, GoldText);
            Button(respawn.gameObject, mb, "RespawnAllCups", false, 0, respawn.transform, click, mb.ClipButtonClick);

            // FRUIT
            Img(c, "Fruit Panel", new Vector2(140f, 0f), new Vector2(230f, 1060f), roundSprite, Card, true);
            Label(c, "Fruit", "FRUIT", new Vector2(140f, 500f), new Vector2(220f, 40f), 32f, GoldText);
            Label(c, "Fruit Sub", "CHOOSE YOUR FLAVOUR", new Vector2(140f, 462f), new Vector2(220f, 22f), 14f, new Color(1f, 1f, 1f, 0.55f));
            var fruitButtons = new Image[Fruits.Length];
            for (int i = 0; i < Fruits.Length; i++)
            {
                Image b = Img(c, Fruits[i].Name, new Vector2(140f, 360f - i * 148f), new Vector2(210f, 132f), roundSprite, OptionOff, true);
                Img(b.rectTransform, "Icon", new Vector2(-58f, 0f), new Vector2(84f, 84f), fruitSprites[i], Color.white, false);
                Label(b.rectTransform, "Label", Fruits[i].Name.ToUpperInvariant(), new Vector2(36f, 0f), new Vector2(110f, 30f), 18f, Color.white, TextAlignmentOptions.Left);
                Button(b.gameObject, mb, "OptionFruit", true, i, b.transform, click, mb.ClipButtonClick);
                fruitButtons[i] = b;
            }
            mb.FruitButtons = fruitButtons;

            // STRAW
            Img(c, "Straw Panel", new Vector2(470f, 0f), new Vector2(420f, 1060f), roundSprite, Card, true);
            Label(c, "Straw", "STRAW", new Vector2(470f, 500f), new Vector2(400f, 40f), 32f, GoldText);
            Label(c, "Straw Sub", "CHOOSE YOUR STYLE", new Vector2(470f, 462f), new Vector2(400f, 22f), 14f, new Color(1f, 1f, 1f, 0.55f));
            var strawButtons = new Image[StrawNames.Length];
            for (int i = 0; i < StrawNames.Length; i++)
            {
                Vector2 pos = new Vector2(470f + (i % 2 == 0 ? -100f : 100f), 290f - (i / 2) * 285f);
                Image b = Img(c, StrawNames[i], pos, new Vector2(192f, 270f), roundSprite, OptionOff, true);
                Img(b.rectTransform, "Swatch", new Vector2(0f, 22f), new Vector2(170f, 190f), SaveSprite("Straw_" + i, StrawSwatch(i)), Color.white, false);
                Label(b.rectTransform, "Label", StrawNames[i].ToUpperInvariant(), new Vector2(0f, -106f), new Vector2(180f, 26f), 16f, Color.white);
                Button(b.gameObject, mb, "OptionStraw", true, i, b.transform, click, mb.ClipButtonClick);
                strawButtons[i] = b;
            }
            mb.StrawButtons = strawButtons;
        }

        private static void KioskPanel(Transform rt, DrinkMachine mb, DrinkSlot slot)
        {
            const float W = 720f, H = 440f;
            RectTransform c = Canvas(rt, "Customize Cup Panel", new Vector3(KioskX, (0.40f + TowerTop) * 0.5f, Front + 0.0015f), new Vector2(W, H), true);
            Img(c, "Background", Vector2.zero, new Vector2(W, H), roundSprite, new Color(0.13f, 0.07f, 0.2f, 0.98f), true);
            Label(c, "Title", "CUSTOMIZE CUP", new Vector2(0f, 175f), new Vector2(600f, 44f), 34f, Color.white);
            slot.StatusText = Label(c, "Status", "PLACE A CUP BELOW", new Vector2(0f, 133f), new Vector2(600f, 26f), 18f, GoldText);
            slot.StatusLight = Img(c, "Status Light", new Vector2(W * 0.5f - 26f, H * 0.5f - 26f), new Vector2(16f, 16f), knobSprite, new Color(0.25f, 0.25f, 0.3f), false);
            AudioSource click = slot.OneShotSource;

            Label(c, "Glass", "GLASS", new Vector2(-300f, 90f), new Vector2(120f, 24f), 16f, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Left);
            var tintButtons = new Image[Tints.Length];
            for (int i = 0; i < Tints.Length; i++)
            {
                var pos = new Vector2(-265f + i * 106f, 30f);
                Image ring = Img(c, TintNames[i], pos, new Vector2(84f, 84f), knobSprite, OptionOff, false);
                Color sw = Tints[i];
                sw.a = 1f;
                Img(ring.rectTransform, "Swatch", Vector2.zero, new Vector2(68f, 68f), knobSprite, i == 0 ? new Color(0.85f, 0.9f, 0.95f) : sw, false);
                Label(ring.rectTransform, "Name", TintNames[i].ToUpperInvariant(), new Vector2(0f, -56f), new Vector2(100f, 20f), 12f, new Color(1f, 1f, 1f, 0.7f));
                Button(ring.gameObject, mb, "CupTint", true, i, ring.transform, click, mb.ClipButtonClick);
                tintButtons[i] = ring;
            }
            Label(c, "Logo", "LOGO", new Vector2(-300f, -100f), new Vector2(120f, 24f), 16f, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Left);
            var logoButtons = new Image[LogoNames.Length];
            for (int i = 0; i < LogoNames.Length; i++)
            {
                Image b = Img(c, LogoNames[i], new Vector2(-210f + i * 210f, -160f), new Vector2(190f, 64f), roundSprite, OptionOff, true);
                Label(b.rectTransform, "Label", LogoNames[i], Vector2.zero, new Vector2(170f, 34f), 20f, i == 0 ? GoldText : Color.white);
                Button(b.gameObject, mb, "CupLogo", true, i, b.transform, click, mb.ClipButtonClick);
                logoButtons[i] = b;
            }
            mb.TintButtons = tintButtons;
            mb.LogoButtons = logoButtons;
        }

        private static void Gauge(Image img, float fill)
        {
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Bottom;
            img.fillClockwise = true;
            img.fillAmount = fill;
            // start at bottom-left, sweep 270 degrees over the top
            img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
        }

        // ------------------------------------------------------------------ helpers: UI

        private static RectTransform Canvas(Transform parent, string name, Vector3 localPos, Vector2 size, bool interactive)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            rt.sizeDelta = size;
            rt.localPosition = localPos;
            // the readable side of a world space canvas is its -Z side: face the players on +Z
            rt.localRotation = Quaternion.Euler(0f, 180f, 0f);
            rt.localScale = Vector3.one * PxScale;
            if (interactive)
            {
                go.AddComponent<GraphicRaycaster>();
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(size.x, size.y, 2f);
                go.AddComponent<VRCUiShape>();
            }
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

        private static TextMeshProUGUI Label(RectTransform parent, string name, string text, Vector2 pos, Vector2 size, float maxSize, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
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
            t.alignment = align;
            t.enableAutoSizing = true;
            t.fontSizeMax = maxSize;
            t.fontSizeMin = maxSize * 0.4f;
            t.fontSize = maxSize;
            t.raycastTarget = false;
            return t;
        }

        private static void Button(GameObject hit, UdonSharpBehaviour target, string evt, bool sendArg, int arg, Transform pressVisual, AudioSource audio, AudioClip click)
        {
            hit.GetComponent<Image>().raycastTarget = true;
            var btn = hit.AddComponent<UnityEngine.UI.Button>();
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var db = UdonSharpUndo.AddComponent<DrinkButton>(hit);
            db.Target = target;
            db.EventName = evt;
            db.SendArg = sendArg;
            db.Arg = arg;
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

        private static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material m, bool collider = false)
        {
            return Prim(PrimitiveType.Cube, name, parent, pos, size, m, collider);
        }

        private static GameObject Cyl(Transform parent, string name, Vector3 pos, float diameter, float height, Material m)
        {
            return Prim(PrimitiveType.Cylinder, name, parent, pos, new Vector3(diameter, height * 0.5f, diameter), m);
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
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
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
                    if (tex != null && m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", tex);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
                else m.DisableKeyword("_EMISSION");
            });
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

        private static Texture2D SavePng(string name, Texture2D tex, bool repeat = false)
        {
            Dir(Gen + "/Textures");
            string path = Gen + "/Textures/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                imp.alphaIsTransparency = true;
                imp.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Sprite SaveSprite(string name, Texture2D tex)
        {
            Dir(Gen + "/Textures");
            string path = Gen + "/Textures/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>Renders text with TextMesh Pro into a transparent texture (used for the glass logo).</summary>
        private static Texture2D TextTexture(string name, string text, int w, int h)
        {
            const int layer = 31;
            var holder = new GameObject("TextRender") { hideFlags = HideFlags.HideAndDontSave };
            holder.transform.position = new Vector3(0f, -5000f, 0f);
            var tgo = new GameObject("Text", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            tgo.transform.SetParent(holder.transform, false);
            tgo.layer = layer;
            var t = tgo.AddComponent<TextMeshPro>();
            t.font = font;
            t.text = text;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.enableAutoSizing = true;
            t.fontSizeMin = 1f;
            t.fontSizeMax = 100f;
            t.rectTransform.sizeDelta = new Vector2(1f, 1f);
            t.ForceMeshUpdate();
            var cgo = new GameObject("Cam") { hideFlags = HideFlags.HideAndDontSave };
            cgo.transform.SetParent(holder.transform, false);
            cgo.transform.localPosition = new Vector3(0f, 0f, -5f);
            var cam = cgo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 0.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(1f, 1f, 1f, 0f);
            cam.cullingMask = 1 << layer;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20f;
            var rtex = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rtex;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rtex;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            Object.DestroyImmediate(rtex);
            Object.DestroyImmediate(holder);
            // keep the shape in alpha, colour comes from the shader
            Color[] px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++) px[i] = new Color(1f, 1f, 1f, px[i].a);
            tex.SetPixels(px);
            tex.Apply();
            return SavePng(name, tex);
        }

        // ------------------------------------------------------------------ painted textures

        private sealed class Painter
        {
            public readonly int W, H;
            private readonly Color[] px;

            public Painter(int w, int h)
            {
                W = w; H = h;
                px = new Color[w * h];
                for (int i = 0; i < px.Length; i++) px[i] = new Color(1f, 1f, 1f, 0f);
            }

            public void Fill(Func<Vector2, float> sdf, Func<Vector2, Color> col, float alpha = 1f)
            {
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        float cov = Mathf.Clamp01(0.5f - sdf(p)) * alpha;
                        if (cov <= 0f) continue;
                        Color c = col(p);
                        float a = cov * c.a;
                        if (a <= 0f) continue;
                        int i = y * W + x;
                        Color d = px[i];
                        float oa = a + d.a * (1f - a);
                        Color o = new Color(
                            (c.r * a + d.r * d.a * (1f - a)) / oa,
                            (c.g * a + d.g * d.a * (1f - a)) / oa,
                            (c.b * a + d.b * d.a * (1f - a)) / oa, oa);
                        px[i] = o;
                    }
            }

            public void Fill(Func<Vector2, float> sdf, Color c, float alpha = 1f) { Fill(sdf, _ => c, alpha); }
            public void Stroke(Func<Vector2, float> sdf, float width, Color c) { Fill(p => Mathf.Abs(sdf(p)) - width * 0.5f, c); }

            public Texture2D Texture()
            {
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                tex.SetPixels(px);
                tex.Apply();
                return tex;
            }
        }

        private static float Circle(Vector2 p, Vector2 c, float r) { return (p - c).magnitude - r; }

        private static float RBox(Vector2 p, Vector2 c, Vector2 half, float r)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + new Vector2(r, r);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        private static float Seg(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        private static float Ellipse(Vector2 p, Vector2 c, Vector2 r)
        {
            Vector2 q = new Vector2((p.x - c.x) / r.x, (p.y - c.y) / r.y);
            return (q.magnitude - 1f) * Mathf.Min(r.x, r.y);
        }

        /// <summary>Convex polygon (either winding): positive outside.</summary>
        private static float Poly(Vector2 p, Vector2[] v)
        {
            float area = 0f;
            for (int i = 0; i < v.Length; i++) { Vector2 a = v[i], b = v[(i + 1) % v.Length]; area += a.x * b.y - b.x * a.y; }
            float s = area > 0f ? 1f : -1f;
            float d = float.MinValue;
            for (int i = 0; i < v.Length; i++)
            {
                Vector2 a = v[i], e = v[(i + 1) % v.Length] - a;
                Vector2 n = new Vector2(e.y, -e.x).normalized * s;
                d = Mathf.Max(d, Vector2.Dot(p - a, n));
            }
            return d;
        }

        private static void Wheel(Painter pt, Vector2 c, float r, Color rind, Color flesh, int style)
        {
            pt.Fill(p => Circle(p, c, r), rind);
            pt.Fill(p => Circle(p, c, r * 0.86f), flesh);
            Color line = Color.Lerp(flesh, Color.white, 0.55f);
            Color seed = new Color(0.12f, 0.1f, 0.08f);
            switch (style)
            {
                case 0: // citrus segments
                    for (int k = 0; k < 9; k++)
                    {
                        float a = k / 9f * Mathf.PI * 2f;
                        Vector2 e = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.84f;
                        pt.Fill(p => Seg(p, c, e, r * 0.025f), line);
                    }
                    pt.Fill(p => Circle(p, c, r * 0.1f), line);
                    break;
                case 1: // berry: lighter heart and seeds
                    pt.Fill(p => Circle(p, c, r * 0.5f), Color.Lerp(flesh, Color.white, 0.35f));
                    for (int k = 0; k < 12; k++)
                    {
                        float a = k / 12f * Mathf.PI * 2f;
                        Vector2 s = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.7f;
                        pt.Fill(p => Circle(p, s, r * 0.045f), new Color(1f, 0.9f, 0.5f));
                    }
                    break;
                case 2: // pineapple core and rays
                    for (int k = 0; k < 14; k++)
                    {
                        float a = k / 14f * Mathf.PI * 2f;
                        Vector2 e = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.82f;
                        pt.Fill(p => Seg(p, c, e, r * 0.018f), Color.Lerp(flesh, rind, 0.35f));
                    }
                    pt.Fill(p => Circle(p, c, r * 0.22f), Color.Lerp(flesh, Color.white, 0.5f));
                    break;
                case 3: // dragon fruit seeds
                    for (int k = 0; k < 40; k++)
                    {
                        float a = k * 2.39996f, d = r * 0.78f * Mathf.Sqrt((k + 0.5f) / 40f);
                        Vector2 s = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                        pt.Fill(p => Circle(p, s, r * 0.035f), seed);
                    }
                    break;
                case 4: // apple seed star
                    for (int k = 0; k < 5; k++)
                    {
                        float a = k / 5f * Mathf.PI * 2f + Mathf.PI * 0.5f;
                        Vector2 s = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.2f;
                        pt.Fill(p => Ellipse(p, s, new Vector2(r * 0.06f, r * 0.06f)), new Color(0.35f, 0.2f, 0.08f));
                    }
                    pt.Stroke(p => Circle(p, c, r * 0.3f), r * 0.03f, line);
                    break;
                default: // grape: soft highlight and two pips
                    pt.Fill(p => Circle(p, c + new Vector2(-r * 0.3f, r * 0.3f), r * 0.3f), new Color(1f, 1f, 1f, 0.3f));
                    pt.Fill(p => Ellipse(p, c + new Vector2(-r * 0.12f, 0f), new Vector2(r * 0.07f, r * 0.12f)), new Color(0.3f, 0.2f, 0.1f));
                    pt.Fill(p => Ellipse(p, c + new Vector2(r * 0.12f, 0f), new Vector2(r * 0.07f, r * 0.12f)), new Color(0.3f, 0.2f, 0.1f));
                    break;
            }
        }

        private static Texture2D SliceTex(Color rind, Color flesh, int style)
        {
            var pt = new Painter(128, 128);
            Wheel(pt, new Vector2(64f, 64f), 60f, rind, flesh, style);
            return pt.Texture();
        }

        private static void Glow(Painter pt, Vector2 c, float r, Color col)
        {
            pt.Fill(p => (p - c).magnitude - r, p => new Color(col.r, col.g, col.b, col.a * Mathf.Pow(Mathf.Clamp01(1f - (p - c).magnitude / r), 2f)));
        }

        private static Texture2D DrinkIcon(Drink d)
        {
            var pt = new Painter(256, 256);
            Color liquid = Hex(d.Main), foam = Hex(d.Foam);
            Color deep = Color.Lerp(liquid, Color.black, 0.4f);
            Color glassFill = new Color(1f, 1f, 1f, 0.07f), glassLine = new Color(1f, 1f, 1f, 0.6f);
            Func<Vector2, Color> grad = p => Color.Lerp(deep, liquid, Mathf.Clamp01((p.y - 40f) / 150f));
            Glow(pt, new Vector2(128f, 118f), 118f, new Color(1f, 0.68f, 0.3f, 0.28f));
            float rimY;
            Vector2 garnishAt;
            if (d.Glass == 1)
            {
                // goblet (spritz)
                var cc = new Vector2(128f, 152f);
                Func<Vector2, float> bowl = p => Mathf.Max(Circle(p, cc, 62f), p.y - 206f);
                pt.Fill(bowl, glassFill);
                pt.Fill(p => Mathf.Max(Circle(p, cc, 56f), p.y - 184f), grad);
                Ice(pt, new[] { new Vector2(110f, 150f), new Vector2(146f, 140f), new Vector2(124f, 118f) });
                Bubbles(pt, 100f, 160f, 110f, 180f);
                pt.Stroke(bowl, 3f, glassLine);
                pt.Fill(p => Seg(p, new Vector2(128f, 90f), new Vector2(128f, 40f), 3.5f), glassLine);
                pt.Fill(p => RBox(p, new Vector2(128f, 37f), new Vector2(38f, 4f), 4f), glassLine);
                rimY = 206f;
                garnishAt = new Vector2(176f, 192f);
            }
            else if (d.Glass == 2)
            {
                // martini
                Vector2[] cone = { new Vector2(52f, 210f), new Vector2(204f, 210f), new Vector2(128f, 116f) };
                Vector2[] fill = { new Vector2(66f, 199f), new Vector2(190f, 199f), new Vector2(128f, 126f) };
                pt.Fill(p => Poly(p, cone), glassFill);
                pt.Fill(p => Poly(p, fill), p => p.y > 186f ? foam : Color.Lerp(deep, liquid, Mathf.Clamp01((p.y - 126f) / 60f)));
                pt.Stroke(p => Poly(p, cone), 3f, glassLine);
                pt.Fill(p => Seg(p, new Vector2(128f, 116f), new Vector2(128f, 42f), 3.5f), glassLine);
                pt.Fill(p => RBox(p, new Vector2(128f, 39f), new Vector2(38f, 4f), 4f), glassLine);
                rimY = 210f;
                garnishAt = new Vector2(128f, 194f);
            }
            else
            {
                // highball
                Func<Vector2, float> outer = p => RBox(p, new Vector2(128f, 118f), new Vector2(46f, 92f), 10f);
                pt.Fill(outer, glassFill);
                pt.Fill(p => Mathf.Max(RBox(p, new Vector2(128f, 121f), new Vector2(40f, 86f), 7f), p.y - 176f), grad);
                Ice(pt, new[] { new Vector2(112f, 150f), new Vector2(146f, 132f), new Vector2(118f, 104f), new Vector2(142f, 84f) });
                Bubbles(pt, 92f, 164f, 40f, 170f);
                pt.Fill(p => Seg(p, new Vector2(110f, 150f), new Vector2(88f, 240f), 4f), new Color(0.08f, 0.08f, 0.09f, 1f));
                pt.Stroke(outer, 3f, glassLine);
                pt.Fill(p => RBox(p, new Vector2(102f, 122f), new Vector2(2.5f, 68f), 2.5f), new Color(1f, 1f, 1f, 0.22f));
                rimY = 210f;
                garnishAt = new Vector2(172f, 200f);
            }
            switch (d.Garnish)
            {
                case 0: Wheel(pt, garnishAt, 30f, Hex("F07A10"), Hex("FFB347"), 0); break;
                case 1:
                    foreach (var b in new[] { new Vector2(150f, rimY - 6f), new Vector2(174f, rimY + 4f), new Vector2(160f, rimY + 22f) })
                    {
                        pt.Fill(p => Circle(p, b, 14f), Hex("C8102E"));
                        pt.Fill(p => Circle(p, b + new Vector2(-4f, 5f), 4f), new Color(1f, 1f, 1f, 0.5f));
                    }
                    break;
                case 2:
                    Wheel(pt, garnishAt, 26f, Hex("3E9A1E"), Hex("B8E07A"), 0);
                    pt.Fill(p => Ellipse(p, garnishAt + new Vector2(-34f, 18f), new Vector2(16f, 8f)), Hex("2E8B3A"));
                    pt.Fill(p => Ellipse(p, garnishAt + new Vector2(-22f, 30f), new Vector2(8f, 15f)), Hex("3FA34A"));
                    break;
                case 3: Wheel(pt, garnishAt, 28f, Hex("F2C200"), Hex("FFF2A0"), 0); break;
                default:
                    for (int k = -1; k <= 1; k++)
                    {
                        Vector2 b = garnishAt + new Vector2(k * 15f, Mathf.Abs(k) * -2f);
                        pt.Fill(p => Ellipse(p, b, new Vector2(7f, 5f)), Hex("3A1E0E"));
                        pt.Fill(p => Seg(p, b - new Vector2(4f, 0f), b + new Vector2(4f, 0f), 0.8f), Hex("1A0A04"));
                    }
                    break;
            }
            return pt.Texture();
        }

        private static void Ice(Painter pt, Vector2[] at)
        {
            foreach (Vector2 c in at)
            {
                pt.Fill(p => RBox(p, c, new Vector2(13f, 13f), 4f), new Color(1f, 1f, 1f, 0.28f));
                pt.Fill(p => RBox(p, c + new Vector2(-4f, 4f), new Vector2(4f, 4f), 2f), new Color(1f, 1f, 1f, 0.35f));
            }
        }

        private static void Bubbles(Painter pt, float x0, float x1, float y0, float y1)
        {
            for (int k = 0; k < 10; k++)
            {
                var c = new Vector2(Mathf.Lerp(x0, x1, (float)rng.NextDouble()), Mathf.Lerp(y0, y1, (float)rng.NextDouble()));
                float r = 1.5f + (float)rng.NextDouble() * 2f;
                pt.Stroke(p => Circle(p, c, r), 1.2f, new Color(1f, 1f, 1f, 0.5f));
            }
        }

        private static Texture2D CustomIcon()
        {
            var pt = new Painter(256, 256);
            var c = new Vector2(128f, 128f);
            Glow(pt, c, 118f, new Color(0.7f, 0.4f, 1f, 0.3f));
            pt.Stroke(p => Circle(p, c, 70f), 5f, Hex("E8C27A"));
            pt.Fill(p => RBox(p, c, new Vector2(34f, 3f), 2f), Hex("E8C27A"));
            pt.Fill(p => RBox(p, c, new Vector2(3f, 34f), 2f), Hex("E8C27A"));
            return pt.Texture();
        }

        private static Texture2D StrawSwatch(int style)
        {
            var pt = new Painter(170, 190);
            for (int k = 0; k < 5; k++)
            {
                Vector2 a = new Vector2(30f + k * 27f, 14f), b = new Vector2(44f + k * 27f, 178f);
                Func<Vector2, float> s = p => Seg(p, a, b, 7f);
                switch (style)
                {
                    case 0: pt.Fill(s, p => Color.Lerp(Hex("8A5E18"), Hex("FFD77A"), Mathf.Clamp01(0.5f + (p.x - Mathf.Lerp(a.x, b.x, p.y / 190f)) / 10f))); break;
                    case 1: pt.Fill(s, p => Color.Lerp(Hex("0C0C0E"), Hex("5A5A62"), Mathf.Clamp01(0.5f + (p.x - Mathf.Lerp(a.x, b.x, p.y / 190f)) / 10f))); break;
                    case 2:
                        pt.Fill(s, new Color(1f, 1f, 1f, 0.15f));
                        pt.Stroke(s, 1.6f, new Color(1f, 1f, 1f, 0.75f));
                        break;
                    case 3: pt.Fill(s, p => Mathf.Repeat(p.y, 30f) < 3f ? Hex("8A6A30") : Hex("D9B46A")); break;
                    case 4: pt.Fill(s, p => Mathf.Repeat(p.x + p.y * 0.5f, 14f) < 7f ? Hex("D82A2A") : Hex("F6F2EA")); break;
                    default: pt.Fill(s, p => Color.Lerp(Hex("6A2BD8"), Hex("C79BFF"), Mathf.Clamp01(0.5f + (p.x - Mathf.Lerp(a.x, b.x, p.y / 190f)) / 10f))); break;
                }
            }
            return pt.Texture();
        }

        private static Texture2D StrawTex(int style)
        {
            const int w = 32, h = 256;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color c = style == 3
                        ? (y % 48 < 4 ? Hex("8A6A30") : Hex("D9B46A"))
                        : (Mathf.Repeat(x * 4f + y, 32f) < 16f ? Hex("D82A2A") : Hex("F6F2EA"));
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            return SavePng(style == 3 ? "Straw_Bamboo" : "Straw_Paper", tex, true);
        }

        private static Texture2D GrateTex()
        {
            const int w = 64, h = 16;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    tex.SetPixel(x, y, (x % 8) < 5 ? Color.white : new Color(0.15f, 0.12f, 0.08f));
            tex.Apply();
            return SavePng("Grate", tex, true);
        }

        private static Texture2D RingTex()
        {
            var pt = new Painter(256, 256);
            pt.Fill(p => Mathf.Abs(Circle(p, new Vector2(128f, 128f), 112f)) - 12f, Color.white);
            return pt.Texture();
        }

        private static Texture2D DotTex()
        {
            var pt = new Painter(64, 64);
            Glow(pt, new Vector2(32f, 32f), 32f, Color.white);
            return pt.Texture();
        }

        private static Texture2D ConeTex()
        {
            const int w = 8, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            {
                // mesh v = 0 at the nozzle (bright), 1 at the grate
                float a = Mathf.Pow(1f - y / (h - 1f), 1.5f);
                for (int x = 0; x < w; x++) tex.SetPixel(x, y, new Color(a, a, a, a));
            }
            tex.Apply();
            return SavePng("Light_Cone", tex);
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

        /// <summary>Tapered cylinder around the Y axis (pivot = bottom center). UV: u around (0.25 faces +Z), v up.</summary>
        private static Mesh Frustum(string name, float rBottom, float rTop, float y0, float y1, int sides, bool capBottom, bool capTop, float rangeY0 = 0f, float rangeY1 = 0f)
        {
            // rangeY0/1: the radii are given at these heights (default: y0..y1), so an inner mesh follows the cup's taper
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

        /// <summary>Open tube from y = 0 (radius r0) to y = length (radius r1). Negative length points down. UV v: 0 at y = 0.</summary>
        private static Mesh Tube(string name, float r0, float r1, float length, int sides)
        {
            var v = new List<Vector3>();
            var nrm = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(dir * r0); nrm.Add(dir); uv.Add(new Vector2(i / (float)sides, 0f));
                v.Add(dir * r1 + Vector3.up * length); nrm.Add(dir); uv.Add(new Vector2(i / (float)sides, 1f));
            }
            for (int i = 0; i < sides; i++)
            {
                int k = i * 2;
                // keep the outside facing out whichever way the tube points
                if (length > 0f) tri.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 });
                else tri.AddRange(new[] { k, k + 3, k + 1, k, k + 2, k + 3 });
            }
            var mesh = new Mesh();
            mesh.SetVertices(v);
            mesh.SetNormals(nrm);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0);
            return SaveMesh(name, mesh);
        }

        /// <summary>Low poly unit-diameter sphere for the decorative fruit (cheaper than Unity's sphere).</summary>
        private static Mesh LowSphere(string name, int seg, int rings)
        {
            var v = new List<Vector3>();
            var tri = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float phi = r / (float)rings * Mathf.PI;
                for (int s = 0; s <= seg; s++)
                {
                    float th = s / (float)seg * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th)) * 0.5f);
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + seg + 1;
                    tri.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            var mesh = new Mesh();
            mesh.SetVertices(v);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals();
            return SaveMesh(name, mesh);
        }

        // ------------------------------------------------------------------ sounds
        // Procedural stand-ins in the spirit of a fast food drink machine. Swap in your own clips on the
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
                // glass touching the grate, then the holder clicking shut
                float hit = Noise() * Env(t, 0.0005f, 0.006f) * 0.7f;
                float body = (Sin(1650f, t) * 0.45f + Sin(2740f, t) * 0.3f + Sin(4100f, t) * 0.15f) * Env(t, 0.001f, 0.05f);
                float thud = Sin(160f - 60f * t, t) * Env(t, 0.002f, 0.04f) * 0.5f;
                float latch = t > 0.05f ? (Noise() * 0.5f + Sin(3200f, t) * 0.45f) * Env(t - 0.05f, 0.0005f, 0.007f) : 0f;
                return (hit + body + thud + latch) * 0.75f;
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
            // drink stream: band-passed rush, a bubbling gurgle and carbonation crackle
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
            if (importer != null && name.Contains("loop"))
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
