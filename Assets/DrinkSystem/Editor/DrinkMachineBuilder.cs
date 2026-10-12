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
    /// Builds the FBT Mayfair drink station as one clear line, left to right:
    ///   1 TAKE A GLASS (glass rack)  >  2 PLACE IT under a drink  >  3 STRENGTH + FROTH, POUR  >  4 FINISH IT (straw,
    ///   fruit, glass tint, logo)  >  5 HOLD USE TO DRINK.
    /// The glasses come from your Glasses2025 prefab (your models and materials), turned into networked DrinkCups.
    /// Everything else (meshes, icons, materials, sounds) is generated into Assets/DrinkSystem/Generated.
    /// Select your bar counter first to put the station on top of it.
    /// </summary>
    public static class DrinkMachineBuilder
    {
        // ---- easy to change
        private const string GlassesPrefabName = "Glasses2025";   // your glasses prefab (Glass_* children with a Liquid_* child)
        private const int CopiesPerGlass = 2;                      // how many of each glass on the rack
        private const string LogoPath = "Assets/DrinkSystem/Textures/FBT_Mayfair_Logo.png";

        private const string Root = "Assets/DrinkSystem";
        private const string Gen = Root + "/Generated";
        private const string RootName = "Drink Machine";
        private const float PxScale = 0.0005f; // UI canvases: 1 px = 0.5 mm

        // ---- layout (meters, the bar top is y = 0, players stand on +Z)
        private const float MainW = 1.05f, Depth = 0.42f, Front = 0.21f, Back = -0.21f;
        private const float BaseTop = 0.10f, GrateTop = 0.11f;
        private const float NozzleZ = 0.055f, SlotSpacing = 0.175f;
        private const float RackX0 = -1.34f, RackX1 = -0.60f;
        private const float FinishX0 = 0.56f, FinishX1 = 1.30f;
        private const float CupH = 0.15f, CupRB = 0.028f, CupRT = 0.036f;
        // set from the tallest glass, so every glass fits under the nozzles
        private static float BodyBottom = 0.42f, BodyTop = 0.72f;

        private struct Drink
        {
            public string Name; public int Glass, Garnish; public string Main, Foam, Edge; public float Transparency, FoamHeight;
            public Drink(string name, int glass, int garnish, string main, string foam, string edge, float transparency, float foamHeight)
            { Name = name; Glass = glass; Garnish = garnish; Main = main; Foam = foam; Edge = edge; Transparency = transparency; FoamHeight = foamHeight; }
        }

        // Glass: 0 highball, 1 goblet, 2 martini. Garnish: 0 orange, 1 berries, 2 lime + mint, 3 lemon, 4 coffee beans
        private static readonly Drink[] Drinks =
        {
            new Drink("Mayfair Spritz", 1, 0, "FF8A1E", "FFD9A0", "FFC060", 0.35f, 0.03f),
            new Drink("Berry Bliss", 0, 1, "B0102A", "F2A0B0", "FF5070", 0.25f, 0.03f),
            new Drink("Tropical Mule", 0, 2, "E8C25A", "FFF2C8", "FFE08A", 0.40f, 0.04f),
            new Drink("Citrus Smash", 0, 3, "C8D94A", "F4FFD0", "F0FF90", 0.45f, 0.03f),
            new Drink("Espresso Martini", 2, 4, "2A140A", "D9B98C", "6B3A1E", 0.05f, 0.16f),
            new Drink("Rose Fizz", 1, 1, "F28AA0", "FFE0E8", "FFC0D0", 0.45f, 0.05f),
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
        private static readonly string[] TintNames = { "Original", "Smoke", "Amber", "Rose", "Violet", "Emerald" };
        private static readonly Color[] Tints =
        {
            new Color(1f, 1f, 1f, 1f), new Color(0.25f, 0.25f, 0.28f, 0.45f), new Color(1f, 0.65f, 0.25f, 0.35f),
            new Color(1f, 0.5f, 0.66f, 0.32f), new Color(0.6f, 0.42f, 1f, 0.35f), new Color(0.24f, 0.84f, 0.55f, 0.32f),
        };
        private static readonly string[] LogoNames = { "FBT GOLD", "FBT SILVER", "NO LOGO" };
        private static readonly Color[] LogoColors = { new Color(1f, 1f, 1f, 1f), new Color(0.88f, 0.9f, 0.95f, 1f), new Color(1f, 1f, 1f, 0f) };

        private static readonly Color Gold = new Color(0.79f, 0.59f, 0.18f, 1f);
        private static readonly Color GoldText = new Color(0.93f, 0.74f, 0.38f, 1f);
        private static readonly Color Panel = new Color(0.07f, 0.065f, 0.085f, 0.97f);
        private static readonly Color Card = new Color(0.09f, 0.085f, 0.11f, 1f);
        private static readonly Color OptionOff = new Color(0.12f, 0.11f, 0.15f, 1f);
        private static readonly Color Hint = new Color(1f, 1f, 1f, 0.6f);

        private static TMP_FontAsset font;
        private static Sprite roundSprite, knobSprite, logoSprite;
        private static readonly List<UdonSharpBehaviour> made = new List<UdonSharpBehaviour>();
        private static readonly Dictionary<string, Mesh> logoMeshes = new Dictionary<string, Mesh>();
        private static readonly System.Random rng = new System.Random(11);

        [MenuItem("Drink System/Build Drink Machine In Scene", priority = 0)]
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
            logoMeshes.Clear();
            Debug.Log("[DrinkSystem] Building the drink station...");
            try
            {
                EditorUtility.DisplayProgressBar("Drink System", "Preparing UdonSharp programs...", 0.05f);
                EnsurePrograms();
                EditorUtility.DisplayProgressBar("Drink System", "Building the station...", 0.3f);
                GameObject root = BuildAll(!onTarget);
                if (onTarget) root.transform.position = new Vector3(targetBounds.center.x - (RackX0 + FinishX1) * 0.5f, targetBounds.max.y, targetBounds.center.z);
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
                if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
                Debug.Log("[DrinkSystem] FBT Mayfair drink station built" + (onTarget ? " on top of '" + target.name + "'" : "") + ". Prefab: " + Root + "/Drink Machine.prefab");
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("FBT Mayfair Drink System",
                    "Done! The drink station is in your scene as '" + RootName + "'" + (onTarget ? ", on top of '" + target.name + "'" : "") +
                    " and the Scene view is now looking at it.\n\nPress Play to try it.", "OK");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Drink System - build failed",
                    "The drink station could not be built:\n\n" + e.Message +
                    "\n\nPlease screenshot this message (and the red error in Window > General > Console) and send it.", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static T Add<T>(GameObject go) where T : UdonSharpBehaviour
        {
            T c = UdonSharpUndo.AddComponent<T>(go);
            if (c == null)
                throw new Exception("UdonSharp could not add " + typeof(T).Name + ". Wait until Unity has finished compiling (no spinner bottom-right), " +
                    "check the Console for UdonSharp errors, then click Build again.");
            return c;
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
            logoSprite = Logo();

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
            cupMat.SetColor("_LogoColor", new Color(0f, 0f, 0f, 0f));
            Material logoMat = ShaderMat("Glass_Logo", "DrinkSystem/Cup");
            logoMat.SetColor("_Color", new Color(0f, 0f, 0f, 0f));
            logoMat.SetColor("_RimColor", new Color(0f, 0f, 0f, 0f));
            logoMat.SetFloat("_Highlight", 0f);
            logoMat.SetTexture("_LogoTex", logoSprite.texture);
            logoMat.SetVector("_LogoRect", new Vector4(0.5f, 0.5f, 1f, 1f));
            logoMat.SetColor("_LogoColor", LogoColors[0]);
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
            foreach (Material m in new[] { grate, cupMat, logoMat, darkGlass, strawMats[2] }) EditorUtility.SetDirty(m);

            // --- meshes
            Mesh strawMesh = Tube("Straw", 0.0035f, 0.0035f, 1f, 10);
            Mesh discMesh = Frustum("Garnish", 0.021f, 0.021f, 0f, 0.005f, 20, true, true);
            Mesh sphereMesh = LowSphere("Low_Sphere", 10, 6);
            Mesh streamMesh = Tube("Stream", 0.0042f, 0.0034f, -1f, 10);

            // --- root + machine brain
            var root = new GameObject(RootName);
            Transform rt = root.transform;
            var mb = Add<DrinkMachine>(root);
            made.Add(mb);
            Sounds(mb);
            WireDrunkEffect(mb);

            // --- STEP 1: glasses (first, so the machine can be made tall enough for the tallest one)
            var cupsParent = new GameObject("Glasses");
            cupsParent.transform.SetParent(rt, false);
            float tallest = Glasses(cupsParent.transform, cupMat, liquidMat, logoMat, strawMesh, discMesh);
            BodyBottom = Mathf.Max(0.42f, GrateTop + tallest + 0.09f);
            BodyTop = BodyBottom + 0.30f;
            Mesh coneMesh = Tube("Light_Cone", 0.006f, 0.065f, -(BodyBottom - 0.035f - GrateTop), 16);

            var stat = new GameObject("Station");
            stat.transform.SetParent(rt, false);
            Transform st = stat.transform;
            if (withCounter)
            {
                float cx = (RackX0 + FinishX1) * 0.5f, w = FinishX1 - RackX0 + 0.3f;
                Box(st, "Counter", new Vector3(cx, -0.5f, 0.02f), new Vector3(w, 1f, 0.75f), counterMat, true);
                Box(st, "Counter Trim", new Vector3(cx, -0.004f, 0.395f), new Vector3(w, 0.008f, 0.006f), trim);
            }
            Rack(st, rt, body, trim, warm);
            MainUnit(st, body, trim, metal, warm, grate, darkGlass, coneMat, poolMat, coneMesh, sphereMesh, discMesh,
                orangeMat, limeMat, garnishMats[0], berryMat, grapeMat, beanMat);
            FinishUnit(st, body, trim, warm, grate, coneMat, poolMat, coneMesh);
            Sign(st, rt, body, trim, warm);
            foreach (Transform t in st.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;

            // --- flavors: the 6 drinks, then the 6 fruits (slight fruit tint)
            var flavorsParent = new GameObject("Flavors");
            flavorsParent.transform.SetParent(rt, false);
            foreach (Drink d in Drinks) Flavor(flavorsParent.transform, d.Name, Hex(d.Main), Hex(d.Foam), Hex(d.Edge), d.Transparency, d.FoamHeight);
            foreach (Fruit f in Fruits) Flavor(flavorsParent.transform, f.Name, Hex(f.Liquid), Color.Lerp(Hex(f.Liquid), Color.white, 0.7f), Hex(f.Flesh), f.Transparency, 0.03f);

            // --- STEP 2 + 3: drink panel, cards, nozzles
            var slotsParent = new GameObject("Slots");
            slotsParent.transform.SetParent(rt, false);
            DrinkPanel(rt, mb);
            for (int i = 0; i < Drinks.Length; i++)
            {
                float x = -SlotSpacing * 2.5f + i * SlotSpacing;
                NozzleSlot(slotsParent.transform, i, x, SaveSprite("Icon_" + i, DrinkIcon(Drinks[i])), mb, guideMat, streamMat, streamMesh, splashMat);
            }

            // --- STEP 4 + 5: finishing pad and its panel
            DrinkSlot pad = FinishPad(slotsParent.transform, guideMat);
            FinishPanel(rt, mb, pad, fruitSprites);

            mb.FlavorsParent = flavorsParent;
            mb.SlotsParent = slotsParent;
            mb.CupsParent = cupsParent;
            mb.SnapRadius = 0.09f;
            mb.FruitFlavorStart = Drinks.Length;
            mb.StrawMaterials = strawMats;
            mb.GarnishMaterials = garnishMats;
            mb.CustomizerSlot = pad;
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
            var f = Add<DrinkFlavor>(go);
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

        private static Sprite Logo()
        {
            // find the logo wherever the DrinkSystem folder is
            string path = LogoPath;
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
            {
                foreach (string g in AssetDatabase.FindAssets("FBT_Mayfair_Logo t:Texture2D"))
                {
                    path = AssetDatabase.GUIDToAssetPath(g);
                    break;
                }
            }
            // make it a sprite (always re-applied, so a bad import setting can't stop the build)
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
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
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s == null)
            {
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (o is Sprite sp) { s = sp; break; }
                }
            }
            if (s == null)
            {
                // last resort: build a sprite asset from the texture
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null)
                {
                    Dir(Gen + "/Textures");
                    string spritePath = Gen + "/Textures/FBT_Mayfair_Logo_Sprite.asset";
                    s = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                    if (s == null)
                    {
                        s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                        s.name = "FBT_Mayfair_Logo";
                        AssetDatabase.CreateAsset(s, spritePath);
                    }
                }
            }
            if (s == null) throw new Exception("Could not find the FBT Mayfair logo (FBT_Mayfair_Logo.png). Make sure the whole DrinkSystem folder was imported, including Textures.");
            return s;
        }

        /// <summary>Uses the drunk post-processing from an existing ManagerBar in the scene, so STRENGTH drives it.</summary>
        private static void WireDrunkEffect(DrinkMachine mb)
        {
            foreach (MonoBehaviour c in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c == null || c.GetType().Name != "ManagerBar") continue;
                SerializedProperty p = new SerializedObject(c).FindProperty("DrunkEffectTransform");
                if (p != null && p.objectReferenceValue is Transform t)
                {
                    mb.DrunkEffectTransform = t;
                    Debug.LogWarning("[DrinkSystem] Using the drunk effect '" + t.name + "' from your ManagerBar. Disable or remove that ManagerBar: " +
                        "the drink station now drives the effect (two scripts setting it every frame would fight).", c);
                    return;
                }
            }
            Debug.LogWarning("[DrinkSystem] No drunk effect found. Assign your post-processing effect object to 'Drunk Effect Transform' on '" + RootName +
                "' so STRENGTH controls how drunk people get.");
        }

        // ------------------------------------------------------------------ STEP 1: glasses

        /// <summary>Builds the rack glasses from your Glasses2025 prefab (or simple glasses if it isn't in the project). Returns the tallest height.</summary>
        private static float Glasses(Transform parent, Material cupMat, Material liquidMat, Material logoMat, Mesh strawMesh, Mesh discMesh)
        {
            var sources = new List<Transform>();
            GameObject prefab = null;
            foreach (string g in AssetDatabase.FindAssets(GlassesPrefabName + " t:Prefab"))
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                if (prefab != null) break;
            }
            if (prefab != null)
            {
                // Make the glass models readable: the liquid line is measured from the mesh
                var paths = new HashSet<string>();
                foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) paths.Add(AssetDatabase.GetAssetPath(mf.sharedMesh));
                foreach (string p in paths)
                {
                    if (AssetImporter.GetAtPath(p) is ModelImporter mi && !mi.isReadable)
                    {
                        mi.isReadable = true;
                        mi.SaveAndReimport();
                    }
                }
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(prefab));
                foreach (Transform t in prefab.transform)
                {
                    if (!t.name.StartsWith("Glass_") || t.GetComponent<MeshFilter>() == null) continue;
                    foreach (Transform c in t)
                        if (c.GetComponent<MeshFilter>() != null) { sources.Add(t); break; }
                }
            }
            if (sources.Count == 0) Debug.LogWarning("[DrinkSystem] '" + GlassesPrefabName + "' prefab not found (or it has no Glass_* with a Liquid_* child). Using simple glasses.");

            Mesh fbGlass = null, fbLiquid = null;
            if (sources.Count == 0)
            {
                fbGlass = Frustum("Cup", CupRB, CupRT, 0f, CupH, 24, true, false);
                fbLiquid = Frustum("Liquid", CupRB * 0.92f, CupRT * 0.93f, 0.012f, CupH - 0.006f, 24, true, true, 0f, CupH);
            }
            int types = sources.Count > 0 ? sources.Count : 5;
            float step = (RackX1 - RackX0 - 0.12f) / Mathf.Max(1, types - 1);
            float tallest = 0f;
            int id = 0;
            for (int copy = 0; copy < CopiesPerGlass; copy++)
            {
                // front tier, then the raised back tier
                float y = copy == 0 ? 0.06f : 0.13f, z = copy == 0 ? 0.11f : -0.09f;
                for (int k = 0; k < types; k++)
                {
                    var pos = new Vector3(RackX0 + 0.06f + k * step, y, z);
                    float h;
                    if (sources.Count > 0)
                    {
                        Transform src = sources[k];
                        Transform liq = null;
                        foreach (Transform c in src) if (c.GetComponent<MeshFilter>() != null) { liq = c; break; }
                        h = MakeGlass(parent, src.name.Replace("Glass_", "") + " " + (copy + 1), pos, src.GetComponent<MeshFilter>().sharedMesh,
                            src.GetComponent<MeshRenderer>().sharedMaterials, src.localRotation, src.localScale,
                            liq.GetComponent<MeshFilter>().sharedMesh, liq.GetComponent<MeshRenderer>().sharedMaterials,
                            liq.localPosition, liq.localRotation, liq.localScale, logoMat, strawMesh, discMesh, id);
                    }
                    else
                    {
                        h = MakeGlass(parent, "Glass " + (copy + 1) + "-" + (k + 1), pos, fbGlass, new[] { cupMat }, Quaternion.identity, Vector3.one,
                            fbLiquid, new[] { liquidMat }, Vector3.zero, Quaternion.identity, Vector3.one, logoMat, strawMesh, discMesh, id);
                    }
                    tallest = Mathf.Max(tallest, h);
                    id++;
                }
            }
            return tallest;
        }

        private static float MakeGlass(Transform parent, string name, Vector3 pos, Mesh glassMesh, Material[] glassMats, Quaternion glassRot, Vector3 glassScale,
            Mesh liquidMesh, Material[] liquidMats, Vector3 liqPos, Quaternion liqRot, Vector3 liqScale, Material logoMat, Mesh strawMesh, Mesh discMesh, int id)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;

            // the model sits so the bottom center of the glass is the pivot (that's where it locks onto a slot)
            var model = new GameObject("Model");
            model.transform.SetParent(go.transform, false);
            model.transform.localRotation = glassRot;
            model.transform.localScale = glassScale;
            model.AddComponent<MeshFilter>().sharedMesh = glassMesh;
            var bodyR = model.AddComponent<MeshRenderer>();
            bodyR.sharedMaterials = glassMats;
            Shadowless(bodyR);
            var liquid = new GameObject("Liquid");
            liquid.transform.SetParent(model.transform, false);
            liquid.transform.localPosition = liqPos;
            liquid.transform.localRotation = liqRot;
            liquid.transform.localScale = liqScale;
            liquid.AddComponent<MeshFilter>().sharedMesh = liquidMesh;
            var liquidR = liquid.AddComponent<MeshRenderer>();
            liquidR.sharedMaterials = liquidMats;
            Shadowless(liquidR);

            Bounds wb = bodyR.bounds;
            Vector3 p = go.transform.position;
            model.transform.position += new Vector3(p.x - wb.center.x, p.y - wb.min.y, p.z - wb.center.z);
            float liquidBottom = go.transform.InverseTransformPoint(liquidR.bounds.min).y;
            liquidR.enabled = false;

            // radius profile of the glass (in the glass's own space), used for the collider, straw, garnish and logo
            float[] prof = Profile(glassMesh, model.transform, go.transform, 32, out float height);
            float maxR = 0f;
            foreach (float r in prof) maxR = Mathf.Max(maxR, r);
            float rimR = Mathf.Max(prof[prof.Length - 1], prof[prof.Length - 2]);

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(maxR * 2f, height, maxR * 2f);
            box.center = new Vector3(0f, height * 0.5f, 0f);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.3f;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var pickup = go.AddComponent<VRCPickup>();
            pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;
            pickup.UseText = "Drink";
            pickup.InteractionText = "Glass";
            pickup.proximity = 0.5f;
            pickup.DisallowTheft = true;
            pickup.orientation = VRC_Pickup.PickupOrientation.Any;

            // straw: from near the bottom, leaning out over the rim
            float strawLen = Mathf.Max(0.08f, height * 1.15f);
            var straw = Part(go.transform, "Straw", strawMesh, glassMats[0], new Vector3(rimR * 0.1f, liquidBottom + 0.004f, -rimR * 0.1f), Quaternion.Euler(4f, 0f, 9f));
            straw.transform.localScale = new Vector3(1f, strawLen, 1f);
            straw.enabled = false;
            // fruit wheel on the rim, front right
            float gr = Mathf.Clamp(rimR * 0.55f, 0.012f, 0.022f);
            float a = 40f * Mathf.Deg2Rad;
            var garnish = Part(go.transform, "Garnish", discMesh, glassMats[0], new Vector3(Mathf.Cos(a) * rimR, height - gr * 0.6f, Mathf.Sin(a) * rimR), Quaternion.Euler(0f, 50f, 90f));
            garnish.transform.localScale = Vector3.one * (gr / 0.021f);
            garnish.enabled = false;
            // FBT Mayfair logo decal, wrapped on the widest part of the glass
            MeshRenderer logo = LogoDecal(go.transform, name, prof, height, logoMat);

            var cup = Add<DrinkCup>(go);
            cup.LiquidRenderer = liquidR;
            cup.BodyRenderer = bodyR;
            cup.LogoRenderer = logo;
            cup.StrawRenderer = straw;
            cup.GarnishRenderer = garnish;
            cup.OneShotSource = Audio(go, 0.9f, 6f);
            cup.LoopSource = Audio(go, 0.6f, 4f);
            made.Add(cup);
            return height;
        }

        /// <summary>Max distance from the glass axis per height band, in the glass root's space (pivot = bottom center).</summary>
        private static float[] Profile(Mesh mesh, Transform model, Transform root, int bins, out float height)
        {
            Vector3[] verts = mesh.vertices;
            var pts = new Vector3[verts.Length];
            height = 0f;
            for (int i = 0; i < verts.Length; i++)
            {
                pts[i] = root.InverseTransformPoint(model.TransformPoint(verts[i]));
                height = Mathf.Max(height, pts[i].y);
            }
            if (verts.Length == 0)
            {
                Bounds b = mesh.bounds;
                height = b.size.y * model.lossyScale.y;
                var flat = new float[bins];
                for (int i = 0; i < bins; i++) flat[i] = Mathf.Max(b.extents.x, b.extents.z) * model.lossyScale.x;
                return flat;
            }
            var prof = new float[bins];
            foreach (Vector3 v in pts)
            {
                int k = Mathf.Clamp((int)(v.y / Mathf.Max(0.0001f, height) * bins), 0, bins - 1);
                prof[k] = Mathf.Max(prof[k], new Vector2(v.x, v.z).magnitude);
            }
            // fill empty bands from their neighbours
            for (int i = 1; i < bins; i++) if (prof[i] <= 0f) prof[i] = prof[i - 1];
            for (int i = bins - 2; i >= 0; i--) if (prof[i] <= 0f) prof[i] = prof[i + 1];
            return prof;
        }

        private static float ProfileAt(float[] prof, float height, float y)
        {
            float f = Mathf.Clamp(y / Mathf.Max(0.0001f, height) * prof.Length - 0.5f, 0f, prof.Length - 1f);
            int i = Mathf.FloorToInt(f);
            int j = Mathf.Min(i + 1, prof.Length - 1);
            return Mathf.Lerp(prof[i], prof[j], f - i);
        }

        private static MeshRenderer LogoDecal(Transform glass, string name, float[] prof, float height, Material logoMat)
        {
            // the widest band between 35% and 90% of the height
            int best = Mathf.FloorToInt(prof.Length * 0.35f);
            for (int i = best; i < Mathf.CeilToInt(prof.Length * 0.9f); i++) if (prof[i] > prof[best]) best = i;
            float y = (best + 0.5f) / prof.Length * height;
            float r = prof[best];
            float aspect = logoSprite.rect.width / logoSprite.rect.height;
            float h = Mathf.Clamp(height * 0.16f, 0.008f, 0.024f);
            float w = h * aspect;
            float arc = w / Mathf.Max(0.005f, r);
            if (arc > 1.7f) { arc = 1.7f; w = arc * r; h = w / aspect; }
            y = Mathf.Clamp(y, h * 0.5f + 0.002f, height - h * 0.5f - 0.002f);

            const int cols = 10;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            for (int j = 0; j <= cols; j++)
            {
                float u = j / (float)cols;
                float ang = Mathf.PI * 0.5f + (u - 0.5f) * arc; // centered on +Z (the front), left to right as seen from the front
                var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                float y0 = y - h * 0.5f, y1 = y + h * 0.5f;
                v.Add(dir * (ProfileAt(prof, height, y0) + 0.0012f) + Vector3.up * y0); n.Add(dir); uv.Add(new Vector2(u, 0f));
                v.Add(dir * (ProfileAt(prof, height, y1) + 0.0012f) + Vector3.up * y1); n.Add(dir); uv.Add(new Vector2(u, 1f));
            }
            for (int j = 0; j < cols; j++)
            {
                int k = j * 2;
                tri.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 });
            }
            var mesh = new Mesh();
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0);
            // every copy of the same glass shares one decal mesh
            string key = "Logo_" + name.Substring(0, name.LastIndexOf(' ') > 0 ? name.LastIndexOf(' ') : name.Length).Replace(' ', '_');
            if (!logoMeshes.TryGetValue(key, out Mesh shared))
            {
                shared = SaveMesh(key, mesh);
                logoMeshes[key] = shared;
            }
            else Object.DestroyImmediate(mesh);
            return Part(glass, "Logo", shared, logoMat, Vector3.zero, Quaternion.identity);
        }

        private static void Rack(Transform st, Transform rt, Material body, Material trim, Material warm)
        {
            var r = new GameObject("Glass Rack").transform;
            r.SetParent(st, false);
            float cx = (RackX0 + RackX1) * 0.5f, w = RackX1 - RackX0;
            Box(r, "Front Tier", new Vector3(cx, 0.03f, 0.105f), new Vector3(w, 0.06f, 0.21f), body, true);
            Box(r, "Back Tier", new Vector3(cx, 0.065f, -0.105f), new Vector3(w, 0.13f, 0.21f), body, true);
            Box(r, "Front Glow", new Vector3(cx, 0.061f, 0.11f), new Vector3(w - 0.04f, 0.003f, 0.08f), warm);
            Box(r, "Back Glow", new Vector3(cx, 0.131f, -0.09f), new Vector3(w - 0.04f, 0.003f, 0.08f), warm);
            Box(r, "Trim Front", new Vector3(cx, 0.06f, Front + 0.002f), new Vector3(w + 0.004f, 0.004f, 0.006f), trim);
            Box(r, "Trim Step", new Vector3(cx, 0.13f, 0.002f), new Vector3(w + 0.004f, 0.004f, 0.006f), trim);
            Box(r, "Backboard", new Vector3(cx, (0.13f + BodyTop) * 0.5f, Back + 0.012f), new Vector3(w, BodyTop - 0.13f, 0.024f), body, true);
            Box(r, "Trim Board", new Vector3(cx, BodyTop, Back + 0.026f), new Vector3(w + 0.004f, 0.005f, 0.006f), trim);

            float top = BodyTop - 0.01f, bottom = Mathf.Max(0.40f, top - 0.26f);
            RectTransform c = Canvas(rt, "Step 1 Panel", new Vector3(cx, (top + bottom) * 0.5f, Back + 0.0255f), new Vector2((w - 0.04f) / PxScale, (top - bottom) / PxScale), false);
            Vector2 size = c.sizeDelta;
            Img(c, "Background", Vector2.zero, size, roundSprite, Panel, true);
            StepBadge(c, new Vector2(0f, size.y * 0.5f - 90f), 1, 110f);
            Label(c, "Title", "TAKE A GLASS", new Vector2(0f, size.y * 0.5f - 200f), new Vector2(size.x - 60f, 70f), 56f, Color.white);
            Label(c, "Hint", "Any glass works with any drink.\nTake it to the drink machine next.", new Vector2(0f, -size.y * 0.5f + 90f), new Vector2(size.x - 80f, 110f), 30f, Hint);
        }

        // ------------------------------------------------------------------ STEP 2 + 3: drink machine

        private static void MainUnit(Transform st, Material body, Material trim, Material metal, Material warm, Material grate, Material darkGlass,
            Material coneMat, Material poolMat, Mesh coneMesh, Mesh sphere, Mesh disc,
            Material orange, Material lime, Material lemon, Material berry, Material grape, Material bean)
        {
            var m = new GameObject("Drink Machine").transform;
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

            for (int i = 0; i < 6; i++)
            {
                float x = -SlotSpacing * 2.5f + i * SlotSpacing;
                Cyl(m, "Nozzle " + (i + 1), new Vector3(x, BodyBottom - 0.0175f, NozzleZ), 0.026f, 0.035f, metal);
                Cyl(m, "Nozzle Ring", new Vector3(x, BodyBottom - 0.002f, NozzleZ), 0.036f, 0.004f, trim);
                Cyl(m, "Nozzle Light", new Vector3(x, BodyBottom - 0.0355f, NozzleZ), 0.016f, 0.002f, warm);
                LightCone(m, new Vector3(x, BodyBottom - 0.035f, NozzleZ), coneMesh, coneMat, poolMat);
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

        private static void DrinkPanel(Transform rt, DrinkMachine mb)
        {
            float hPx = (BodyTop - BodyBottom) / PxScale;
            RectTransform bg = Canvas(rt, "Drink Panel", new Vector3(0f, (BodyBottom + BodyTop) * 0.5f, Front + 0.0015f), new Vector2(2040f, hPx), true);
            float top = hPx * 0.5f;
            Img(bg, "Background", Vector2.zero, new Vector2(2040f, hPx), roundSprite, Panel, true);
            Img(bg, "Logo", new Vector2(-880f, top - 45f), new Vector2(190f, 78f), logoSprite, Color.white, false).preserveAspect = true;
            StepBadge(bg, new Vector2(-690f, top - 45f), 2, 54f);
            Label(bg, "Step 2", "PLACE YOUR GLASS UNDER A DRINK", new Vector2(-400f, top - 45f), new Vector2(520f, 46f), 30f, Color.white, TextAlignmentOptions.Left);
            StepBadge(bg, new Vector2(170f, top - 45f), 3, 54f);
            Label(bg, "Step 3", "PICK STRENGTH + FROTH, THEN PRESS POUR", new Vector2(560f, top - 45f), new Vector2(720f, 46f), 30f, Color.white, TextAlignmentOptions.Left);
            Img(bg, "Line", new Vector2(0f, top - 80f), new Vector2(1960f, 2f), null, new Color(Gold.r, Gold.g, Gold.b, 0.5f), false);

            AudioSource click = Audio(bg.gameObject, 0.9f, 6f);
            float rowY = top - 118f;
            mb.StrengthButtons = OptionRow(bg, mb, click, "STRENGTH", "how drunk it gets", new[] { "LOW", "MED", "STRONG" }, "OptionStrength", -560f, rowY);
            mb.FrothButtons = OptionRow(bg, mb, click, "FROTH", "foam on top", new[] { "LOW", "MED", "HIGH" }, "OptionFroth", 260f, rowY);
        }

        private static Image[] OptionRow(RectTransform c, DrinkMachine mb, AudioSource click, string title, string hint, string[] names, string evt, float x, float y)
        {
            Label(c, title, title, new Vector2(x - 150f, y + 10f), new Vector2(220f, 34f), 26f, GoldText, TextAlignmentOptions.Right);
            Label(c, title + " Hint", hint, new Vector2(x - 150f, y - 20f), new Vector2(220f, 22f), 15f, Hint, TextAlignmentOptions.Right);
            var buttons = new Image[names.Length];
            for (int j = 0; j < names.Length; j++)
            {
                Image b = Img(c, title + " " + names[j], new Vector2(x + 30f + j * 130f, y), new Vector2(118f, 52f), roundSprite, OptionOff, true);
                Label(b.rectTransform, "Label", names[j], Vector2.zero, new Vector2(108f, 34f), 20f, Color.white);
                Button(b.gameObject, mb, evt, true, j, b.transform, click, mb.ClipButtonClick);
                buttons[j] = b;
            }
            return buttons;
        }

        private static void NozzleSlot(Transform parent, int i, float x, Sprite icon, DrinkMachine mb,
            Material guideMat, Material streamMat, Mesh streamMesh, Material splashMat)
        {
            var go = new GameObject("Slot " + (i + 1) + " " + Drinks[i].Name);
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

            var slot = Add<DrinkSlot>(go);
            slot.FlavorIndex = i;
            slot.SnapPoint = snap;
            slot.Highlight = guide;
            slot.Nozzle = nozzle;
            slot.Stream = streamR;
            slot.Splash = splash;
            slot.LoopSource = loop;
            slot.OneShotSource = shot;
            made.Add(slot);

            // Drink card: icon, name, what to do next, pour button, fill bar
            const float W = 310f, H = 400f;
            float hPx = (BodyTop - BodyBottom) / PxScale;
            float cardY = (BodyBottom + BodyTop) * 0.5f + (-hPx * 0.5f + 20f + H * 0.5f) * PxScale;
            RectTransform card = Canvas(go.transform.parent.parent, "Card " + (i + 1), new Vector3(x, cardY, Front + 0.0035f), new Vector2(W, H), true);
            card.SetParent(go.transform, true);
            Img(card, "Background", Vector2.zero, new Vector2(W, H), roundSprite, Card, true);
            Img(card, "Icon", new Vector2(0f, 88f), new Vector2(190f, 190f), icon, Color.white, false).preserveAspect = true;
            Label(card, "Name", Drinks[i].Name, new Vector2(0f, -36f), new Vector2(W - 30f, 56f), 26f, Color.white);
            slot.StatusText = Label(card, "Status", "PLACE A GLASS HERE", new Vector2(0f, -84f), new Vector2(W - 24f, 26f), 19f, new Color(0.85f, 0.79f, 0.66f));
            slot.StatusLight = Img(card, "Status Light", new Vector2(W * 0.5f - 22f, H * 0.5f - 22f), new Vector2(14f, 14f), knobSprite, new Color(0.25f, 0.25f, 0.3f), false);

            RectTransform hit = Img(card, "Pour Button", new Vector2(0f, -132f), new Vector2(230f, 104f), null, new Color(1f, 1f, 1f, 0f), false).rectTransform;
            Image ring = Img(hit, "Ring", new Vector2(0f, -8f), new Vector2(76f, 76f), knobSprite, Gold, false);
            Img(ring.rectTransform, "Face", Vector2.zero, new Vector2(64f, 64f), knobSprite, new Color(0.05f, 0.045f, 0.06f, 1f), false);
            Label(ring.rectTransform, "Label", "POUR", Vector2.zero, new Vector2(56f, 24f), 15f, GoldText);
            Button(hit.gameObject, slot, "Press", false, 0, ring.transform, shot, mb.ClipButtonClick);

            Img(card, "Fill Back", new Vector2(0f, -186f), new Vector2(230f, 7f), roundSprite, new Color(1f, 1f, 1f, 0.1f), false);
            Image fill = Img(card, "Fill", new Vector2(0f, -186f), new Vector2(230f, 7f), roundSprite, Color.white, false);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            slot.FillBar = fill;
        }

        // ------------------------------------------------------------------ STEP 4 + 5: finishing station

        private static float FinishTop => BodyBottom + 0.42f;

        private static void FinishUnit(Transform st, Material body, Material trim, Material warm, Material grate, Material coneMat, Material poolMat, Mesh coneMesh)
        {
            var f = new GameObject("Finishing Station").transform;
            f.SetParent(st, false);
            float cx = (FinishX0 + FinishX1) * 0.5f, w = FinishX1 - FinishX0;
            Box(f, "Base", new Vector3(cx, BaseTop * 0.5f, 0f), new Vector3(w, BaseTop, Depth), body, true);
            Box(f, "Pad", new Vector3(cx, (BaseTop + GrateTop) * 0.5f, NozzleZ + 0.01f), new Vector3(0.18f, GrateTop - BaseTop, 0.22f), grate, true);
            Box(f, "Back", new Vector3(cx, (BaseTop + BodyBottom) * 0.5f, Back + 0.06f), new Vector3(w, BodyBottom - BaseTop, 0.12f), body, true);
            Box(f, "Hood", new Vector3(cx, (BodyBottom + FinishTop) * 0.5f, 0f), new Vector3(w, FinishTop - BodyBottom, Depth), body, true);
            Box(f, "Trim Top", new Vector3(cx, FinishTop, Front + 0.002f), new Vector3(w + 0.004f, 0.005f, 0.006f), trim);
            Box(f, "Trim Hood", new Vector3(cx, BodyBottom, Front + 0.002f), new Vector3(w + 0.004f, 0.005f, 0.006f), trim);
            Box(f, "Trim Base", new Vector3(cx, BaseTop + 0.001f, Front + 0.002f), new Vector3(w + 0.004f, 0.004f, 0.006f), trim);
            Box(f, "Trim Left", new Vector3(FinishX0 - 0.002f, (BodyBottom + FinishTop) * 0.5f, Front + 0.002f), new Vector3(0.005f, FinishTop - BodyBottom, 0.006f), trim);
            Box(f, "Trim Right", new Vector3(FinishX1 + 0.002f, (BodyBottom + FinishTop) * 0.5f, Front + 0.002f), new Vector3(0.005f, FinishTop - BodyBottom, 0.006f), trim);
            Cyl(f, "Spot", new Vector3(cx, BodyBottom - 0.002f, NozzleZ), 0.03f, 0.004f, warm);
            LightCone(f, new Vector3(cx, BodyBottom - 0.004f, NozzleZ), coneMesh, coneMat, poolMat);
        }

        private static DrinkSlot FinishPad(Transform parent, Material guideMat)
        {
            var go = new GameObject("Finishing Pad");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3((FinishX0 + FinishX1) * 0.5f, GrateTop, NozzleZ);
            var snap = new GameObject("SnapPoint").transform;
            snap.SetParent(go.transform, false);
            var slot = Add<DrinkSlot>(go);
            slot.IsCustomizer = true;
            slot.SnapPoint = snap;
            slot.Highlight = Guide(go.transform, guideMat);
            slot.OneShotSource = Audio(go, 0.9f, 6f);
            made.Add(slot);
            return slot;
        }

        private static void FinishPanel(Transform rt, DrinkMachine mb, DrinkSlot pad, Sprite[] fruitSprites)
        {
            float cx = (FinishX0 + FinishX1) * 0.5f;
            float wPx = (FinishX1 - FinishX0 - 0.04f) / PxScale, hPx = (FinishTop - BodyBottom - 0.02f) / PxScale;
            RectTransform c = Canvas(rt, "Step 4 Panel", new Vector3(cx, (BodyBottom + FinishTop) * 0.5f, Front + 0.0015f), new Vector2(wPx, hPx), true);
            float top = hPx * 0.5f, left = -wPx * 0.5f;
            AudioSource click = pad.OneShotSource;
            Img(c, "Background", Vector2.zero, new Vector2(wPx, hPx), roundSprite, Panel, true);
            StepBadge(c, new Vector2(left + 60f, top - 50f), 4, 60f);
            Label(c, "Title", "FINISH YOUR DRINK", new Vector2(left + 110f + 330f, top - 50f), new Vector2(660f, 50f), 38f, Color.white, TextAlignmentOptions.Left);
            pad.StatusText = Label(c, "Status", "PUT YOUR GLASS ON THE PAD", new Vector2(0f, top - 105f), new Vector2(wPx - 60f, 30f), 22f, GoldText);
            pad.StatusLight = Img(c, "Status Light", new Vector2(-left - 30f, top - 30f), new Vector2(18f, 18f), knobSprite, new Color(0.25f, 0.25f, 0.3f), false);

            float tileX0 = left + 260f, tileStep = (wPx - 300f) / 6f;
            // STRAW
            float y = top - 220f;
            RowLabel(c, "STRAW", left, y);
            var straws = new Image[StrawNames.Length];
            for (int i = 0; i < StrawNames.Length; i++)
            {
                Image b = Img(c, "Straw " + StrawNames[i], new Vector2(tileX0 + i * tileStep, y), new Vector2(tileStep - 14f, 150f), roundSprite, OptionOff, true);
                Img(b.rectTransform, "Swatch", new Vector2(0f, 14f), new Vector2(90f, 100f), SaveSprite("Straw_" + i, StrawSwatch(i)), Color.white, false).preserveAspect = true;
                Label(b.rectTransform, "Label", StrawNames[i].ToUpperInvariant(), new Vector2(0f, -56f), new Vector2(tileStep - 24f, 24f), 15f, Color.white);
                Button(b.gameObject, mb, "FinishStraw", true, i, b.transform, click, mb.ClipButtonClick);
                straws[i] = b;
            }
            // FRUIT
            y -= 165f;
            RowLabel(c, "FRUIT", left, y);
            var fruits = new Image[Fruits.Length];
            for (int i = 0; i < Fruits.Length; i++)
            {
                Image b = Img(c, "Fruit " + Fruits[i].Name, new Vector2(tileX0 + i * tileStep, y), new Vector2(tileStep - 14f, 150f), roundSprite, OptionOff, true);
                Img(b.rectTransform, "Icon", new Vector2(0f, 14f), new Vector2(84f, 84f), fruitSprites[i], Color.white, false);
                Label(b.rectTransform, "Label", Fruits[i].Name.ToUpperInvariant(), new Vector2(0f, -56f), new Vector2(tileStep - 24f, 24f), 15f, Color.white);
                Button(b.gameObject, mb, "FinishFruit", true, i, b.transform, click, mb.ClipButtonClick);
                fruits[i] = b;
            }
            // GLASS tint
            y -= 155f;
            RowLabel(c, "GLASS", left, y);
            var tints = new Image[Tints.Length];
            for (int i = 0; i < Tints.Length; i++)
            {
                Image ring = Img(c, "Tint " + TintNames[i], new Vector2(tileX0 + i * tileStep, y + 12f), new Vector2(84f, 84f), knobSprite, OptionOff, false);
                Color sw = Tints[i];
                sw.a = 1f;
                Img(ring.rectTransform, "Swatch", Vector2.zero, new Vector2(66f, 66f), knobSprite, i == 0 ? new Color(0.85f, 0.9f, 0.95f) : sw, false);
                Label(ring.rectTransform, "Name", TintNames[i].ToUpperInvariant(), new Vector2(0f, -58f), new Vector2(130f, 22f), 14f, Hint);
                Button(ring.gameObject, mb, "CupTint", true, i, ring.transform, click, mb.ClipButtonClick);
                tints[i] = ring;
            }
            // LOGO
            y -= 135f;
            RowLabel(c, "LOGO", left, y);
            var logos = new Image[LogoNames.Length];
            float logoStep = (wPx - 300f) / 3f;
            for (int i = 0; i < LogoNames.Length; i++)
            {
                Image b = Img(c, "Logo " + LogoNames[i], new Vector2(tileX0 - 85f + logoStep * (i + 0.5f), y), new Vector2(logoStep - 20f, 62f), roundSprite, OptionOff, true);
                Label(b.rectTransform, "Label", LogoNames[i], Vector2.zero, new Vector2(logoStep - 40f, 34f), 20f, i == 0 ? GoldText : Color.white);
                Button(b.gameObject, mb, "CupLogo", true, i, b.transform, click, mb.ClipButtonClick);
                logos[i] = b;
            }
            // STEP 5
            y -= 85f;
            StepBadge(c, new Vector2(left + 60f, y), 5, 52f);
            Label(c, "Step 5", "PICK UP YOUR GLASS AND HOLD USE (TRIGGER / LEFT CLICK) TO DRINK. ENJOY!", new Vector2(left + 100f + 530f, y), new Vector2(1060f, 40f), 22f, Color.white, TextAlignmentOptions.Left);
            Image respawn = Img(c, "Respawn Glasses", new Vector2(-left - 110f, y), new Vector2(190f, 52f), roundSprite, new Color(0.2f, 0.17f, 0.1f, 1f), true);
            Label(respawn.rectTransform, "Label", "RESET GLASSES", Vector2.zero, new Vector2(176f, 30f), 16f, GoldText);
            Button(respawn.gameObject, mb, "RespawnAllCups", false, 0, respawn.transform, click, mb.ClipButtonClick);

            mb.StrawButtons = straws;
            mb.FruitButtons = fruits;
            mb.TintButtons = tints;
            mb.LogoButtons = logos;
        }

        private static void RowLabel(RectTransform c, string text, float left, float y)
        {
            Label(c, text, text, new Vector2(left + 120f, y), new Vector2(200f, 40f), 28f, GoldText, TextAlignmentOptions.Left);
        }

        // ------------------------------------------------------------------ sign: logo + the whole line of steps

        private static void Sign(Transform st, Transform rt, Material body, Material trim, Material warm)
        {
            float y0 = BodyTop + 0.06f, h = 0.46f, w = 1.15f;
            Box(st, "Sign Board", new Vector3(0f, y0 + h * 0.5f, Back + 0.012f), new Vector3(w, h, 0.024f), body, true);
            Box(st, "Sign Trim Top", new Vector3(0f, y0 + h, Back + 0.026f), new Vector3(w + 0.004f, 0.005f, 0.006f), trim);
            Box(st, "Sign Trim Bottom", new Vector3(0f, y0, Back + 0.026f), new Vector3(w + 0.004f, 0.005f, 0.006f), trim);
            Box(st, "Sign Post", new Vector3(0f, (BodyTop + y0) * 0.5f, Back + 0.012f), new Vector3(0.06f, y0 - BodyTop, 0.02f), trim);
            Box(st, "Sign Glow", new Vector3(0f, y0 + 0.004f, Back + 0.03f), new Vector3(w - 0.04f, 0.003f, 0.004f), warm);
            Vector2 size = new Vector2((w - 0.03f) / PxScale, (h - 0.03f) / PxScale);
            RectTransform c = Canvas(rt, "Sign", new Vector3(0f, y0 + h * 0.5f, Back + 0.0255f), size, false);
            Img(c, "Logo", new Vector2(0f, 120f), new Vector2(size.x * 0.62f, 400f), logoSprite, Color.white, false).preserveAspect = true;
            string[] steps = { "TAKE A GLASS", "PLACE IT", "POUR", "FINISH IT", "DRINK" };
            float stepW = (size.x - 120f) / steps.Length;
            for (int i = 0; i < steps.Length; i++)
            {
                float x = -size.x * 0.5f + 60f + stepW * (i + 0.5f);
                StepBadge(c, new Vector2(x - stepW * 0.5f + 40f, -290f), i + 1, 56f);
                Label(c, "Step " + (i + 1), steps[i], new Vector2(x + 30f, -290f), new Vector2(stepW - 90f, 40f), 26f, Color.white, TextAlignmentOptions.Left);
                if (i < steps.Length - 1) Label(c, "Next", ">", new Vector2(x + stepW * 0.5f - 6f, -290f), new Vector2(30f, 40f), 30f, GoldText);
            }
        }

        private static void StepBadge(RectTransform parent, Vector2 pos, int n, float size)
        {
            Image b = Img(parent, "Badge " + n, pos, new Vector2(size, size), knobSprite, Gold, false);
            Label(b.rectTransform, "Number", n.ToString(), Vector2.zero, new Vector2(size * 0.8f, size * 0.8f), size * 0.62f, new Color(0.06f, 0.05f, 0.04f, 1f));
        }

        // ------------------------------------------------------------------ shared pieces

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

        private static GameObject Guide(Transform parent, Material mat)
        {
            var guide = Prim(PrimitiveType.Quad, "Placement Guide", parent, new Vector3(0f, 0.002f, 0f), Vector3.one * 0.11f, mat);
            guide.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Shadowless(guide.GetComponent<Renderer>());
            guide.SetActive(false);
            return guide;
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
            var db = Add<DrinkButton>(hit);
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
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is Sprite sp) return sp;
            // the importer didn't make a sprite: make one from the texture
            Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t == null) throw new Exception("Could not create the image " + path);
            string spritePath = Gen + "/Textures/" + name + "_Sprite.asset";
            if (AssetDatabase.LoadAssetAtPath<Sprite>(spritePath) != null) AssetDatabase.DeleteAsset(spritePath);
            sprite = Sprite.Create(t, new Rect(0f, 0f, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name;
            AssetDatabase.CreateAsset(sprite, spritePath);
            return sprite;
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
