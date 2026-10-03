using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.Udon;
using Object = UnityEngine.Object;

namespace LoopLand.EditorTools
{
    /// <summary>
    /// One-click builder: LoopLand > Build Game In Scene.
    /// Generates every asset (materials, dice textures + mesh, particle sprite, sound effects) into
    /// Assets/LoopLand/Generated, builds the whole game hierarchy, wires all references and saves a prefab.
    /// </summary>
    public static class LoopLandBuilder
    {
        private const string Root = "Assets/LoopLand";
        private const string Gen = Root + "/Generated";
        private const float R = 2.15f;
        private const float TopY = 0.875f;

        private static TMP_FontAsset font;
        private static Texture2D dot;
        private static Material fxMat;
        private static readonly List<UdonSharpBehaviour> made = new List<UdonSharpBehaviour>();
        private static readonly System.Random rng = new System.Random(7);

        private const int LiveW = 2048;
        private const int LiveH = 1024;
        private const float LobeC = 1.75f;
        private const float LobeR = 1.35f;
        private const float CrossGap = 0.55f;
        private static readonly string[] DiceBodyHex = { "F2EEE4", "15151C", "0B0F1E", "B0102A", "BFE9FF", "E8B730", "2A0E5E", "FF5A00" };
        private static readonly string[] DicePipHex = { "1A1A22", "E8E8F0", "00F0FF", "FFE3E8", "1B4E8C", "3A2500", "FFFFFF", "FFF3B0" };
        private static readonly string[] DiceGlowHex = { "FFFFFF", "8A8AFF", "00F0FF", "FF4060", "9AF2FF", "FFD54A", "B07CFF", "FF7A00" };
        private static readonly string[] RibbonHex = { "FFE14D", "7CFF4F", "00E5FF", "4D8BFF", "B07CFF", "FF3DCB", "FF8A3D", "FFE14D" };
        private static List<Vector2> pathPts;
        private static List<float> pathLen;
        private static readonly string[] SlotHex = { "00E5FF", "FF3DCB", "FFD23F", "7CFF4F", "FF8A3D", "A57BFF" };

        [MenuItem("LoopLand/Build Game In Scene", priority = 0)]
        public static void Build()
        {
            font = TMP_Settings.instance != null ? TMP_Settings.defaultFontAsset : null;
            if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (font == null)
            {
                if (EditorUtility.DisplayDialog("LoopLand", "TextMesh Pro Essential Resources are missing.\nImport them, then run LoopLand > Build Game In Scene again.", "Import now", "Cancel"))
                    EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources");
                return;
            }

            GameObject old = GameObject.Find("LoopLand");
            if (old != null)
            {
                if (!EditorUtility.DisplayDialog("LoopLand", "A LoopLand object already exists in this scene. Replace it?", "Replace", "Cancel")) return;
                Undo.DestroyObjectImmediate(old);
            }

            made.Clear();
            try
            {
                EditorUtility.DisplayProgressBar("LoopLand", "Preparing UdonSharp programs...", 0.1f);
                EnsurePrograms();
                EditorUtility.DisplayProgressBar("LoopLand", "Generating materials, dice and sounds...", 0.3f);
                GameObject root = BuildAll();
                if (Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None).Length == 0)
                {
                    var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
                    Undo.RegisterCreatedObjectUndo(es, "EventSystem");
                }
                EditorUtility.DisplayProgressBar("LoopLand", "Wiring Udon behaviours...", 0.85f);
                foreach (UdonSharpBehaviour b in made) UdonSharpEditorUtility.CopyProxyToUdon(b);
                Undo.RegisterCreatedObjectUndo(root, "Build LoopLand");
                EditorSceneManager.MarkSceneDirty(root.scene);
                AssetDatabase.SaveAssets();
                PrefabUtility.SaveAsPrefabAssetAndConnect(root, Root + "/LoopLand Game.prefab", InteractionMode.AutomatedAction);
                Selection.activeGameObject = root;
                Debug.Log("[LoopLand] Built! Prefab saved to " + Root + "/LoopLand Game.prefab. Assign your UdonProducts on 'LoopLand/Store' (see README).");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("LoopLand/Add Demo World Setup (floor, light, VRCWorld)", priority = 20)]
        public static void DemoSetup()
        {
            if (Object.FindObjectsByType<VRC.SDK3.Components.VRCSceneDescriptor>(FindObjectsSortMode.None).Length == 0)
            {
                foreach (string g in AssetDatabase.FindAssets("VRCWorld t:Prefab"))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                    if (prefab == null || prefab.GetComponent<VRC.SDK3.Components.VRCSceneDescriptor>() == null) continue;
                    var w = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    w.transform.position = new Vector3(0f, 0f, -3.6f);
                    Undo.RegisterCreatedObjectUndo(w, "VRCWorld");
                    break;
                }
            }
            dot = dot != null ? dot : DotTexture();
            var floor = Prim(PrimitiveType.Cube, "LoopLand Floor", null, new Vector3(0f, -0.05f, 0f), new Vector3(40f, 0.1f, 40f), Std("Floor", Hex("0E0D18"), 0.1f, 0.8f, Color.black), true);
            Undo.RegisterCreatedObjectUndo(floor, "Floor");
            var lightGo = new GameObject("LoopLand Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.9f;
            light.color = new Color(0.9f, 0.9f, 1f);
            lightGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            Undo.RegisterCreatedObjectUndo(lightGo, "Light");
        }

        // ------------------------------------------------------------------ programs

        private static void EnsurePrograms()
        {
            Type[] types = { typeof(LoopLandGame), typeof(LoopLandStore), typeof(LoopLandToken), typeof(LoopLandDice), typeof(LoopLandButton), typeof(LoopLandCamera), typeof(LoopLandScratch), typeof(LoopLandTicket), typeof(LoopLandChallenge) };
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
                if (script == null) throw new Exception("[LoopLand] Could not find the script for " + t.Name);
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
            dot = DotTexture();
            fxMat = AddMat("FX_Particle", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            Material dark = Std("Table", Hex("15131F"), 0.35f, 0.88f, Color.black);

            LoopLandArt.Build(Gen + "/UI");
            roundSprite = LoopLandArt.Round;
            Color cCyan = new Color(0f, 0.55f, 0.68f, 1f);
            Color cPink = new Color(0.72f, 0.1f, 0.55f, 1f);
            Color cGold = new Color(0.72f, 0.53f, 0.06f, 1f);
            Color cDark = new Color(0.17f, 0.16f, 0.29f, 1f);
            Color cRed = new Color(0.58f, 0.1f, 0.17f, 1f);
            var root = new GameObject("LoopLand");
            Transform rt = root.transform;

            // brains first so buttons can target them
            var gameGo = new GameObject("Game");
            gameGo.transform.SetParent(rt, false);
            var game = UdonSharpUndo.AddComponent<LoopLandGame>(gameGo);
            made.Add(game);
            AudioSource gameAudio = Audio(gameGo);

            var storeGo = new GameObject("Store");
            storeGo.transform.SetParent(rt, false);
            storeGo.transform.localPosition = new Vector3(0f, 0f, -5.4f);
            storeGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var store = UdonSharpUndo.AddComponent<LoopLandStore>(storeGo);
            made.Add(store);

            // table: two joined round tables shaped like an infinity sign
            Material rimMat = Std("Rim", Hex("00B8D4"), 0f, 0.5f, Hex("00E5FF") * 1.5f);
            for (int side = -1; side <= 1; side += 2)
            {
                Prim(PrimitiveType.Cylinder, "Table Top", rt, new Vector3(side * LobeC, TopY - 0.025f, 0f), new Vector3(4.1f, 0.025f, 4.1f), dark, true);
                Prim(PrimitiveType.Cylinder, "Table Rim Glow", rt, new Vector3(side * LobeC, TopY - 0.03f, 0f), new Vector3(4.18f, 0.012f, 4.18f), rimMat);
                Prim(PrimitiveType.Cylinder, "Table Pedestal", rt, new Vector3(side * LobeC, 0.42f, 0f), new Vector3(1.0f, 0.42f, 1.0f), dark, true);
            }

            // board: 40 UI cards along an infinity loop, over a glowing gradient ribbon
            BuildPath();
            float pathL = pathLen[pathLen.Count - 1];
            float spacing = (pathL / 4f - CrossGap) / 10f;
            var board = new GameObject("Board").transform;
            board.SetParent(rt, false);
            Mesh ribbon = RibbonMesh(0.64f);
            var ribbonGo = new GameObject("Infinity Ribbon");
            ribbonGo.transform.SetParent(board, false);
            ribbonGo.transform.localPosition = new Vector3(0f, TopY + 0.0015f, 0f);
            ribbonGo.AddComponent<MeshFilter>().sharedMesh = ribbon;
            ribbonGo.AddComponent<MeshRenderer>().sharedMaterial = RibbonMaterial();
            ParticleSystem sparkle = Fx("Ribbon Sparkles", ribbonGo.transform, Vector3.zero, Quaternion.identity, 2.2f, 0.03f, 0.03f, 90f, 0f, true, false, ParticleSystemShapeType.Sphere, 0.1f, -0.01f, 400, Color.white, Color.white, 0f);
            var ssh = sparkle.shape;
            ssh.shapeType = ParticleSystemShapeType.Mesh;
            ssh.meshShapeType = ParticleSystemMeshShapeType.Triangle;
            ssh.mesh = ribbon;
            ssh.useMeshColors = true;
            RectTransform boardUi = UCanvas(board, "Board UI", new Vector3(0f, TopY + 0.004f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector2(7800f, 4400f), false);
            const float cardW = 300f, cardH = 500f;
            float tileW = cardW / 1000f;
            var anchors = new Transform[40];
            var badges = new GameObject[40];
            Dir(Root + "/Space Art");
            Sprite portalArt = LoopLandArt.TrailThumb("Space_Portal", Hex("00E5FF"), false);
            UImg(boardUi, "Dice Tray", new Vector2(LobeC * 1000f, 0f), new Vector2(760f, 560f), LoopLandArt.Round, new Color(0.05f, 0.05f, 0.12f, 0.85f));
            TextMeshProUGUI boardLogo = UText(boardUi, "Board Logo", "<b>LOOPLAND</b>", new Vector2(-LobeC * 1000f, 0f), new Vector2(1600f, 360f), 280f, Color.white);
            boardLogo.enableVertexGradient = true;
            boardLogo.colorGradient = new VertexGradient(Hex("FFE14D"), Hex("00E5FF"), Hex("FF3DCB"), Hex("B07CFF"));
            for (int i = 0; i < 40; i++)
            {
                int lobe = i / 20;
                PathAt(lobe * pathL * 0.5f + CrossGap + (i % 20) * spacing, out Vector2 p2, out Vector2 t2);
                Vector2 n2 = new Vector2(-t2.y, t2.x);
                if (Vector2.Dot(new Vector2(lobe == 0 ? LobeC : -LobeC, 0f) - p2, n2) < 0f) n2 = -n2;
                Vector3 p = new Vector3(p2.x, TopY + 0.006f, p2.y);
                var anchor = new GameObject("Space " + i.ToString("00") + " " + game.spaceName[i]).transform;
                anchor.SetParent(board, false);
                anchor.localPosition = p;
                anchor.localRotation = Quaternion.LookRotation(new Vector3(n2.x, 0f, n2.y), Vector3.up);
                anchors[i] = anchor;

                int type = game.spaceType[i];
                int value = game.spaceValue[i];
                bool corner = i % 10 == 0;
                Color tc = TileColor(type, value);
                RectTransform card = URect(boardUi, "Card " + i.ToString("00") + " " + game.spaceName[i], p2 * 1000f, new Vector2(cardW, cardH));
                card.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-n2.x, n2.y) * Mathf.Rad2Deg);
                Image bg = card.gameObject.AddComponent<Image>();
                bg.raycastTarget = false;
                Sprite art = SpaceArt(i, game.spaceName[i]);
                if (art != null)
                {
                    bg.sprite = art;
                    bg.color = Color.white;
                }
                else
                {
                    bg.sprite = roundSprite;
                    bg.type = Image.Type.Sliced;
                    bg.pixelsPerUnitMultiplier = 1f;
                    bg.color = CardColor(type, value);
                    UImage(card, "Band", new Vector2(0f, 200f), new Vector2(cardW - 16f, 84f), tc);
                    UText(card, "Kind", "<b>" + TileWord(type) + "</b>", new Vector2(0f, 200f), new Vector2(cardW - 30f, 66f), 34f, Hex("15131F"));
                    Sprite icon = type == 0 ? LoopLandArt.LogoInfinity : type == 1 ? LoopLandArt.Coin : type == 2 ? LoopLandArt.IconTicket : type == 3 ? LoopLandArt.IconTarget
                        : type == 4 ? LoopLandArt.IconBolt : type == 5 ? LoopLandArt.IconMystery : portalArt;
                    bool colourful = type == 0 || type == 6 || (type == 1 && value >= 0);
                    UImg(card, "Art", new Vector2(0f, 45f), new Vector2(190f, 190f), icon, colourful ? Color.white : tc, false).preserveAspect = true;
                }
                UText(card, "Name", game.spaceName[i], new Vector2(0f, -100f), new Vector2(cardW - 24f, 90f), corner ? 40f : 34f, Color.white).fontStyle = FontStyles.Bold;
                UText(card, "Effect", "<b>" + TileSub(type, value, game.lapBonus) + "</b>", new Vector2(0f, -185f), new Vector2(cardW - 30f, 60f), 30f, tc);
                if (game.spaceWake[i] != 0)
                {
                    // from Loop 3 this tile turns into a Mystery tile (the game switches the badge on)
                    Image badge = UImg(card, "Mystery Badge", new Vector2(0f, 20f), new Vector2(280f, 330f), LoopLandArt.Round, new Color(0.08f, 0.29f, 0.18f, 0.97f));
                    UImg(badge.transform, "Mark", new Vector2(0f, 45f), new Vector2(190f, 190f), LoopLandArt.IconMystery, TileColor(5, 0), false);
                    UText(badge.transform, "Word", "<b>MYSTERY</b>", new Vector2(0f, -110f), new Vector2(260f, 60f), 38f, TileColor(5, 0));
                    badge.gameObject.SetActive(false);
                    badges[i] = badge.gameObject;
                }
            }
            // a glowing frame on the current player's tile, in their tile glow (building style) colour
            var turnMarker = new GameObject("Turn Marker").transform;
            turnMarker.SetParent(rt, false);
            GameObject markerFrame = Prim(PrimitiveType.Cube, "Frame", turnMarker, new Vector3(0f, 0.001f, 0f), new Vector3(tileW + 0.03f, 0.004f, 0.53f), AddMat("Glow_Select", Hex("FFE14D")));
            turnMarker.gameObject.SetActive(false);

            // center hologram
            Texture liveTex = LiveCamera(rt, out LoopLandCamera liveCam);
            var statusL = new List<TMP_Text>();
            var playersL = new List<TMP_Text>();
            var cardsL = new List<TMP_Text>();
            var captionsL = new List<TMP_Text>();
            var holo = new GameObject("Center Screens").transform;
            holo.SetParent(rt, false);
            holo.localPosition = new Vector3(0f, 2.3f, 0f);
            for (int d = 0; d < 4; d++)
            {
                Vector3 dir = Quaternion.Euler(0f, 45f + d * 90f, 0f) * Vector3.back;
                RectTransform c = UCanvas(holo, "Screen " + d, dir * 0.8f, Quaternion.LookRotation(-dir, Vector3.up), new Vector2(1500f, 1100f), false);
                Prim(PrimitiveType.Cube, "Back Plate", c, new Vector3(0f, 0f, 14f), new Vector3(1520f, 1120f, 16f), dark);
                UImg(c, "Glow", Vector2.zero, new Vector2(1540f, 1140f), LoopLandArt.Glow, new Color(0f, 0.9f, 1f, 0.9f));
                UImg(c, "Panel", Vector2.zero, new Vector2(1500f, 1100f), LoopLandArt.Panel, Color.white);
                UImg(c, "Live Frame", new Vector2(0f, 175f), new Vector2(1462f, 742f), LoopLandArt.Round, new Color(1f, 0.24f, 0.8f, 0.9f));
                URaw(c, "Live View", new Vector2(0f, 175f), new Vector2(1440f, 720f), liveTex);
                UImg(c, "Live Tag", new Vector2(-590f, 500f), new Vector2(200f, 56f), LoopLandArt.Pill, new Color(0.9f, 0.1f, 0.3f, 0.95f));
                UText(c, "Live Tag Text", "<b>LIVE</b>", new Vector2(-590f, 500f), new Vector2(180f, 50f), 34f, Color.white);
                captionsL.Add(UText(c, "Caption", "", new Vector2(40f, 480f), new Vector2(980f, 70f), 46f, Color.white));
                cardsL.Add(UText(c, "Card", "", new Vector2(0f, -248f), new Vector2(1420f, 86f), 40f, Hex("FFE14D")));
                UImg(c, "Status Back", new Vector2(-365f, -420f), new Vector2(710f, 230f), LoopLandArt.Round, new Color(0f, 0f, 0f, 0.3f));
                statusL.Add(UText(c, "Status", "LOOPLAND", new Vector2(-365f, -420f), new Vector2(680f, 215f), 40f, Color.white));
                UImg(c, "Players Back", new Vector2(365f, -420f), new Vector2(710f, 230f), LoopLandArt.Round, new Color(0f, 0f, 0f, 0.3f));
                playersL.Add(UText(c, "Players", "", new Vector2(365f, -420f), new Vector2(680f, 215f), 32f, Color.white, TextAlignmentOptions.Left));
            }
            SetLayer(holo.gameObject, 1);
            var spinner = new GameObject("Logo Spinner").transform;
            spinner.SetParent(rt, false);
            spinner.localPosition = new Vector3(0f, 2.7f, 0f);
            for (int s = 0; s < 2; s++)
                Text(spinner, "Logo", "<b>LOOP<color=#FF3DCB>LAND</color></b>", Vector3.zero, Quaternion.Euler(0f, s * 180f, 0f), new Vector2(1.8f, 0.4f), 2.4f, Hex("00E5FF"));
            Fx("Logo Ring", spinner, Vector3.zero, Quaternion.Euler(-90f, 0f, 0f), 1.8f, 0f, 0.03f, 30f, 0f, true, false, ParticleSystemShapeType.Circle, 0.95f, 0f, 200, Hex("FFE14D"), Hex("00E5FF"), 0.5f);
            SetLayer(spinner.gameObject, 1);
            ParticleSystem confetti = Fx("Celebrate", rt, new Vector3(0f, 1.6f, 0f), Quaternion.identity, 2.8f, 3f, 0.05f, 0f, 0f, false, true, ParticleSystemShapeType.Sphere, 0.3f, 0.5f, 600, Color.white, Color.white, 0f);
            var cem = confetti.emission;
            cem.SetBursts(new[] { new ParticleSystem.Burst(0f, 250), new ParticleSystem.Burst(0.4f, 200) });
            var cmain = confetti.main;
            var rainbow = new Gradient();
            rainbow.SetKeys(new[] { new GradientColorKey(Hex("FF3DCB"), 0f), new GradientColorKey(Hex("FFE14D"), 0.33f), new GradientColorKey(Hex("00E5FF"), 0.66f), new GradientColorKey(Hex("7CFF4F"), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            cmain.startColor = new ParticleSystem.MinMaxGradient(rainbow) { mode = ParticleSystemGradientMode.RandomColor };
            ParticleSystem money = Fx("Money", rt, new Vector3(0f, TopY + 0.1f, 0f), Quaternion.identity, 1.4f, 1.2f, 0.04f, 0f, 0f, false, true, ParticleSystemShapeType.Sphere, 0.2f, -0.15f, 200, Hex("7CFF4F"), Hex("FFE14D"), 0f);

            // dice
            Mesh dieMesh = DiceMesh();
            Material[] diceMats = BuildDiceMaterials(out string[] diceNames, out Color[] diceGlow);
            var diceRoot = new GameObject("Dice").transform;
            diceRoot.SetParent(rt, false);
            var dice = new LoopLandDice[2];
            for (int d = 0; d < 2; d++)
            {
                var rest = new GameObject("Rest " + d).transform;
                rest.SetParent(diceRoot, false);
                rest.localPosition = new Vector3(LobeC + (d == 0 ? -0.12f : 0.12f), TopY + 0.062f, d == 0 ? 0.06f : -0.06f);
                var thr = new GameObject("Throw " + d).transform;
                thr.SetParent(diceRoot, false);
                thr.localPosition = new Vector3(LobeC + (d == 0 ? -0.55f : 0.45f), TopY + 0.6f, -1.0f);
                var die = new GameObject("Die " + d);
                die.transform.SetParent(diceRoot, false);
                die.transform.localPosition = rest.localPosition;
                die.transform.localScale = Vector3.one * 0.11f;
                die.AddComponent<MeshFilter>().sharedMesh = dieMesh;
                var mr = die.AddComponent<MeshRenderer>();
                mr.sharedMaterial = diceMats[0];
                var dc = UdonSharpUndo.AddComponent<LoopLandDice>(die);
                dc.restPoint = rest;
                dc.throwPoint = thr;
                dc.rend = mr;
                dc.trail = Fx("Trail", die.transform, Vector3.zero, Quaternion.identity, 0.5f, 0.05f, 0.4f, 0f, 90f, true, true, ParticleSystemShapeType.Sphere, 0.3f, 0f, 300, Color.white, Color.white, 0f);
                dc.trail.Stop();
                dc.burst = Fx("Burst", die.transform, Vector3.zero, Quaternion.identity, 0.7f, 6f, 0.45f, 0f, 0f, false, true, ParticleSystemShapeType.Sphere, 0.5f, 0.3f, 200, Color.white, Color.white, 0f);
                dc.sfx = gameAudio;
                made.Add(dc);
                dice[d] = dc;
            }

            // tokens
            Color[] slotCols = new Color[6];
            for (int s = 0; s < 6; s++) slotCols[s] = Hex(SlotHex[s]);
            var tokensRoot = new GameObject("Tokens").transform;
            tokensRoot.SetParent(rt, false);
            var tokens = new LoopLandToken[6];
            Vector3[] offsets = { new Vector3(-0.09f, 0f, -0.03f), new Vector3(0f, 0f, -0.03f), new Vector3(0.09f, 0f, -0.03f), new Vector3(-0.09f, 0f, -0.13f), new Vector3(0f, 0f, -0.13f), new Vector3(0.09f, 0f, -0.13f) };
            Material tokenBody = Std("Token_Body", Color.white, 0.7f, 0.9f, Color.gray * 0.2f);
            Material tokenGlow = Std("Token_Glow", Color.white, 0f, 0.9f, Color.white);
            for (int s = 0; s < 6; s++)
            {
                var tgo = new GameObject("Token " + (s + 1));
                tgo.transform.SetParent(tokensRoot, false);
                tgo.transform.position = anchors[0].TransformPoint(offsets[s]);
                var tk = UdonSharpUndo.AddComponent<LoopLandToken>(tgo);
                tk.spaces = anchors;
                tk.slotOffset = offsets[s];
                tk.body = TokenBody(tgo.transform, tokenBody, tokenGlow, 1f, out Renderer[] br, out Renderer[] gr);
                tk.bodyRenderers = br;
                tk.glowRenderers = gr;
                tk.trail = Fx("Trail", tgo.transform, new Vector3(0f, 0.05f, 0f), Quaternion.identity, 0.6f, 0.05f, 0.03f, 0f, 140f, true, true, ParticleSystemShapeType.Sphere, 0.02f, -0.05f, 300, Color.white, Color.white, 0f);
                tk.trail.Stop();
                tk.burst = Fx("Burst", tgo.transform, new Vector3(0f, 0.02f, 0f), Quaternion.identity, 0.7f, 0.7f, 0.03f, 0f, 0f, false, true, ParticleSystemShapeType.Circle, 0.03f, -0.1f, 200, Color.white, Color.white, 0f);
                tk.burst.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                tk.aura = Fx("Aura", tgo.transform, new Vector3(0f, 0.01f, 0f), Quaternion.Euler(-90f, 0f, 0f), 1.4f, 0.04f, 0.015f, 10f, 0f, true, false, ParticleSystemShapeType.Circle, 0.05f, -0.03f, 60, Color.white, Color.white, 0f);
                tk.sfx = gameAudio;
                tk.hopClip = Wav("hop", 0.09f, t => Sin(500f + 5000f * t, t) * Env(t, 0.003f, 0.03f) * 0.35f);
                tk.landClip = Wav("land", 0.22f, t => (Sin(140f, t) * 0.7f + Noise() * 0.25f) * Env(t, 0.002f, 0.06f));
                tk.warpClip = Wav("warp", 0.5f, t => Sin(300f + 2400f * t, t) * Env(t, 0.01f, 0.2f) * 0.4f);
                made.Add(tk);
                tokens[s] = tk;
            }

            // seats: dashboard (big context button, info panels and a menu), a top screen and a store spot
            var consoles = new GameObject("Consoles").transform;
            consoles.SetParent(rt, false);
            var info = new TMP_Text[4];
            var prim = new TMP_Text[4];
            var views = new TMP_Text[4];
            var dashSpots = new Transform[4];
            var panelRoots = new GameObject[16];
            var statusBodies = new TMP_Text[4];
            var ticketBodies = new TMP_Text[4];
            var powerBodies = new TMP_Text[4];
            var resultCards = new GameObject[4];
            var resultTitles = new TMP_Text[4];
            var resultSubs = new TMP_Text[4];
            var resultIcons = new Image[4];
            var introCards = new GameObject[4];
            var introTitles = new TMP_Text[4];
            var introBodies = new TMP_Text[4];
            var storeSpots = new Transform[4];
            var seatScreens = new GameObject[4];
            Color cBlue = Hex("1F4FD8"), cPurple = Hex("6A2BD9"), cOrange = Hex("D98A00"), cTeal = Hex("0E9F7E"), cRedBtn = Hex("C21836"), cJoin = Hex("E0218A"), cTicket = Hex("D61F8C");
            string[] panelNames = { "MY STATUS", "TICKETS", "POWER-UPS", "HOW TO PLAY" };
            string howTo = "<b>GOAL:</b> beat everyone: finish with the <color=#FFE14D>most coins</color> after the last round.\n"
                + "Press <b>ROLL</b>, move, and your tile does its thing:\n"
                + "<color=#FFE14D><b>COINS</b></color>  gain 50-200 (watch out for leaks)\n"
                + "<color=#FF3DCB><b>LUCKY LOOP</b></color>  scratch a ticket for a reward\n"
                + "<color=#00E5FF><b>CHALLENGE</b></color>  <b>DUEL</b> your closest rival: winner takes " + game.duelStake + "\n"
                + "<color=#B07CFF><b>POWER</b></color>  Shield, Boost, Swap or Bonus Roll\n"
                + "<color=#3DFF8A><b>MYSTERY</b></color>  a random event for you or everyone\n"
                + "<color=#4D8BFF><b>PORTAL</b></color>  warp to the next section\n"
                + "<color=#FFD23F><b>LOOP START</b></color>  +" + game.lapBonus + " coins every lap\n"
                + "<color=#FF8A3D><b>BUMP</b></color>  land on a player to grab " + game.bumpSteal + " of their coins\n"
                + "<color=#FF3DCB><b>LOOP BATTLE</b></color>  every " + game.battleEveryRounds + " rounds everyone plays: 1st wins big, last pays!\n"
                + "Every loop gets wilder: <b>2</b> bigger rewards, <b>3</b> more Mystery, <b>4</b> jackpots!";
            for (int d = 0; d < 4; d++)
            {
                int side = d < 2 ? 1 : -1;
                float ang = (d % 2 == 0 ? -50f : 50f) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(side * Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var c = new GameObject("Console " + d).transform;
                c.SetParent(consoles, false);
                c.localPosition = new Vector3(side * LobeC, 0f, 0f) + dir * 1.88f + Vector3.up * (TopY + 0.02f);
                c.localRotation = Quaternion.LookRotation(-dir, Vector3.up);

                RectTransform ui = UCanvas(c, "Dashboard", new Vector3(0f, 0.28f, 0f), Quaternion.Euler(35f, 0f, 0f), new Vector2(1100f, 800f));
                Prim(PrimitiveType.Cube, "Back Plate", ui, new Vector3(0f, 0f, 14f), new Vector3(1120f, 820f, 16f), dark);
                UImg(ui, "Glow", Vector2.zero, new Vector2(1140f, 840f), LoopLandArt.Glow, new Color(1f, 0.35f, 0.85f, 1f));
                UImg(ui, "Back", Vector2.zero, new Vector2(1100f, 800f), LoopLandArt.Panel, Color.white);
                UImg(ui, "Content Back", new Vector2(-140f, 75f), new Vector2(790f, 590f), LoopLandArt.Round, new Color(0.03f, 0.03f, 0.1f, 0.6f));
                for (int k = 0; k < 4; k++)
                {
                    RectTransform pr = URect(ui, "Panel " + panelNames[k], new Vector2(-140f, 75f), new Vector2(760f, 570f));
                    UText(pr, "Title", "<b>" + panelNames[k] + "</b>", new Vector2(0f, 245f), new Vector2(720f, 60f), 44f, Hex("FFE14D"));
                    TextMeshProUGUI body = UText(pr, "Body", k == 3 ? howTo : "", new Vector2(0f, -32f), new Vector2(720f, 470f), 32f, Color.white, TextAlignmentOptions.TopLeft);
                    if (k == 0) statusBodies[d] = body;
                    else if (k == 1) ticketBodies[d] = body;
                    else if (k == 2) powerBodies[d] = body;
                    panelRoots[d * 4 + k] = pr.gameObject;
                    pr.gameObject.SetActive(k == 0);
                }
                // shown over the panels: the challenge intro and the result of each turn
                RectTransform intro = URect(ui, "Challenge Intro", new Vector2(-140f, 75f), new Vector2(760f, 570f));
                UImg(intro, "Icon", new Vector2(0f, 160f), new Vector2(170f, 170f), LoopLandArt.IconTarget, Hex("00E5FF"), false);
                introTitles[d] = UText(intro, "Title", "", new Vector2(0f, 30f), new Vector2(720f, 90f), 66f, Hex("00E5FF"));
                introTitles[d].fontStyle = FontStyles.Bold;
                introBodies[d] = UText(intro, "Body", "", new Vector2(0f, -150f), new Vector2(720f, 260f), 38f, Color.white);
                introCards[d] = intro.gameObject;
                intro.gameObject.SetActive(false);
                RectTransform res = URect(ui, "Result", new Vector2(-140f, 75f), new Vector2(760f, 570f));
                UImg(res, "Glow", new Vector2(0f, 150f), new Vector2(270f, 270f), LoopLandArt.Glow, new Color(1f, 0.85f, 0.3f, 0.9f));
                resultIcons[d] = UImg(res, "Icon", new Vector2(0f, 150f), new Vector2(190f, 190f), LoopLandArt.Coin, Color.white, false);
                resultIcons[d].preserveAspect = true;
                resultTitles[d] = UText(res, "Title", "", new Vector2(0f, -10f), new Vector2(740f, 100f), 72f, Hex("FFE14D"));
                resultTitles[d].fontStyle = FontStyles.Bold;
                resultSubs[d] = UText(res, "Sub", "", new Vector2(0f, -170f), new Vector2(720f, 200f), 34f, Color.white);
                resultCards[d] = res.gameObject;
                res.gameObject.SetActive(false);
                // the Lucky Loop ticket and the challenge dock here, on the dashboard nearest to the player
                dashSpots[d] = URect(ui, "Ticket Spot", new Vector2(-140f, 75f), new Vector2(760f, 570f));

                prim[d] = NeonButton(ui, "Primary", "JOIN GAME", new Vector2(-140f, -300f), new Vector2(760f, 120f), cJoin, LoopLandArt.IconPeople, game, "_OnPrimary", 54f, true);
                info[d] = UText(ui, "Info", "", new Vector2(-140f, -378f), new Vector2(760f, 40f), 26f, Color.white);
                const float mx = 400f;
                MenuButton(ui, "My Status", "MY STATUS", new Vector2(mx, 300f), new Vector2(216f, 80f), cBlue, LoopLandArt.IconPeople, game, "_OnPanel0");
                MenuButton(ui, "Tickets", "TICKETS", new Vector2(mx, 205f), new Vector2(216f, 80f), cTicket, LoopLandArt.IconTicket, game, "_OnPanel1");
                MenuButton(ui, "Power-Ups", "POWER-UPS", new Vector2(mx, 110f), new Vector2(216f, 80f), cPurple, LoopLandArt.IconBolt, game, "_OnPanel2");
                views[d] = MenuButton(ui, "View", "VIEW: CENTER", new Vector2(mx, 15f), new Vector2(216f, 80f), Hex("0A9BE0"), LoopLandArt.IconScreen, game, "_OnView" + d);
                MenuButton(ui, "How To Play", "HOW TO PLAY", new Vector2(mx, -80f), new Vector2(216f, 80f), cTeal, LoopLandArt.IconMystery, game, "_OnPanel3");
                MenuButton(ui, "Store", "STORE", new Vector2(mx, -175f), new Vector2(216f, 80f), cOrange, LoopLandArt.IconStore, store, "_OpenStore" + d);
                MenuButton(ui, "Leave", "LEAVE", new Vector2(mx - 56f, -270f), new Vector2(104f, 80f), cPurple, null, game, "_OnLeave");
                MenuButton(ui, "Reset", "RESET", new Vector2(mx + 56f, -270f), new Vector2(104f, 80f), cRedBtn, null, game, "_OnReset");
                SetLayer(ui.gameObject, 1); // TransparentFX: still clickable, but hidden from the live board camera

                // top screen above the dashboard (VIEW: TOP), held by two posts that stay off the board
                var top = new GameObject("Top Screen").transform;
                top.SetParent(c, false);
                for (int post = -1; post <= 1; post += 2)
                    Prim(PrimitiveType.Cube, "Screen Post", top, new Vector3(post * 0.5f, 0.5f, 0.3f), new Vector3(0.04f, 1.0f, 0.04f), dark);
                var screen = new GameObject("Screen").transform;
                screen.SetParent(top, false);
                screen.localPosition = new Vector3(0f, 1.1f, 0.25f);
                screen.localRotation = Quaternion.Euler(-20f, 0f, 0f);
                Prim(PrimitiveType.Cube, "Back", screen, new Vector3(0f, 0f, 0.025f), new Vector3(1.34f, 0.99f, 0.03f), dark);
                RectTransform sc = UCanvas(screen, "Top Screen UI", Vector3.zero, Quaternion.identity, new Vector2(1300f, 950f), false);
                UImg(sc, "Glow", Vector2.zero, new Vector2(1340f, 990f), LoopLandArt.Glow, new Color(0f, 0.9f, 1f, 0.9f));
                UImg(sc, "Panel", Vector2.zero, new Vector2(1300f, 950f), LoopLandArt.Panel, Color.white);
                UImg(sc, "Live Frame", new Vector2(0f, 150f), new Vector2(1262f, 642f), LoopLandArt.Round, new Color(1f, 0.24f, 0.8f, 0.9f));
                URaw(sc, "Live View", new Vector2(0f, 150f), new Vector2(1240f, 620f), liveTex);
                UImg(sc, "Live Tag", new Vector2(-520f, 425f), new Vector2(170f, 50f), LoopLandArt.Pill, new Color(0.9f, 0.1f, 0.3f, 0.95f));
                UText(sc, "Live Tag Text", "<b>LIVE</b>", new Vector2(-520f, 425f), new Vector2(150f, 44f), 30f, Color.white);
                captionsL.Add(UText(sc, "Caption", "", new Vector2(40f, 418f), new Vector2(860f, 64f), 40f, Color.white));
                cardsL.Add(UText(sc, "Card", "", new Vector2(0f, -205f), new Vector2(1240f, 70f), 36f, Hex("FFE14D")));
                UImg(sc, "Status Back", new Vector2(-320f, -360f), new Vector2(620f, 180f), LoopLandArt.Round, new Color(0f, 0f, 0f, 0.3f));
                statusL.Add(UText(sc, "Status", "", new Vector2(-320f, -360f), new Vector2(590f, 165f), 34f, Color.white));
                UImg(sc, "Players Back", new Vector2(320f, -360f), new Vector2(620f, 180f), LoopLandArt.Round, new Color(0f, 0f, 0f, 0.3f));
                playersL.Add(UText(sc, "Players", "", new Vector2(320f, -360f), new Vector2(590f, 165f), 28f, Color.white, TextAlignmentOptions.Left));
                SetLayer(top.gameObject, 1);
                top.gameObject.SetActive(false);
                seatScreens[d] = top.gameObject;

                // where the store pops up for this seat (same spot as the top screen, independent of the VIEW setting)
                var spot = new GameObject("Store Spot").transform;
                spot.SetParent(c, false);
                spot.localPosition = new Vector3(0f, 1.1f, 0.24f);
                spot.localRotation = Quaternion.Euler(-20f, 0f, 0f);
                spot.localScale = Vector3.one * 0.48f;
                storeSpots[d] = spot;
            }
            store.storeSpots = storeSpots;

            // one Lucky Loop ticket and one challenge panel: the game moves them to the dashboard nearest the player
            var turnLogic = new GameObject("Turn Logic").transform;
            turnLogic.SetParent(rt, false);
            LoopLandTicket luckyTicket = BuildTicket(dashSpots[0], turnLogic, "Lucky Loop Ticket", new Vector2(760f, 570f), new Vector2(660f, 300f), 40f,
                Hex("FF3DCB"), "LUCKY LOOP TICKET", game, "_OnTicketScratched", gameAudio);
            SetLayer(luckyTicket.root, 1);
            LoopLandChallenge challenge = BuildChallenge(dashSpots[0], turnLogic, game, gameAudio);
            SetLayer(challenge.root, 1);

            // store kiosk: premium store UI + live board panel
            Transform st = storeGo.transform;
            AudioSource storeAudio = Audio(storeGo);
            Dir(Root + "/Store Art");
            Prim(PrimitiveType.Cube, "Stage", st, new Vector3(0f, 0.03f, -0.25f), new Vector3(6.6f, 0.06f, 1.5f), dark, true);
            RectTransform sui = UCanvas(st, "Store UI", new Vector3(0f, 1.5f, 0f), Quaternion.identity, new Vector2(2700f, 1800f));
            UImg(sui, "Glow", Vector2.zero, new Vector2(2740f, 1840f), LoopLandArt.Glow, new Color(0.35f, 0.6f, 1f, 1f));
            UImg(sui, "Back", Vector2.zero, new Vector2(2700f, 1800f), LoopLandArt.Panel, Color.white);
            Sprite bgArt = FindArt(Root + "/Store Art", "Background");
            if (bgArt != null) UImg(sui, "Background Art", Vector2.zero, new Vector2(2660f, 1760f), bgArt, new Color(1f, 1f, 1f, 0.6f), false);
            TextMeshProUGUI logo = UText(sui, "Logo", "<b>LOOPLAND</b>", new Vector2(-760f, 800f), new Vector2(1100f, 170f), 150f, Color.white, TextAlignmentOptions.Left);
            logo.enableVertexGradient = true;
            logo.colorGradient = new VertexGradient(Hex("FFE14D"), Hex("00E5FF"), Hex("FF3DCB"), Hex("B07CFF"));
            UText(sui, "Store Word", "<b>STORE</b>", new Vector2(-760f, 685f), new Vector2(1100f, 110f), 96f, Color.white, TextAlignmentOptions.Left);
            UImg(sui, "Coin Badge Glow", new Vector2(110f, 750f), new Vector2(620f, 240f), LoopLandArt.Glow, new Color(1f, 0.8f, 0.3f, 0.8f));
            UImg(sui, "Coin Badge", new Vector2(110f, 750f), new Vector2(560f, 190f), LoopLandArt.Round, new Color(0.08f, 0.07f, 0.2f, 0.95f));
            UImg(sui, "Coin", new Vector2(-75f, 750f), new Vector2(150f, 150f), LoopLandArt.Coin, Color.white, false);
            store.coinsText = UText(sui, "Coins", "0", new Vector2(180f, 778f), new Vector2(330f, 90f), 80f, Color.white, TextAlignmentOptions.Left);
            UText(sui, "Coins Label", "<b>LOOP COINS</b>", new Vector2(180f, 705f), new Vector2(330f, 50f), 36f, Hex("FFD54A"), TextAlignmentOptions.Left);
            store.vipText = UText(sui, "VIP", "", new Vector2(880f, 750f), new Vector2(460f, 100f), 70f, Hex("FFD54A"));
            store.storePanel = sui;
            NeonButton(sui, "Close", "X", new Vector2(1250f, 820f), new Vector2(110f, 110f), Hex("C21836"), null, store, "_CloseStore", 56f, false);

            string[] tabNames = { "DICE", "TOKENS", "TILE GLOW", "TRAILS", "PREMIUM" };
            Sprite[] tabIcons = { LoopLandArt.IconDice, LoopLandArt.IconPawn, LoopLandArt.IconBuilding, LoopLandArt.IconSparkle, LoopLandArt.IconCrown };
            var tabs = new TMP_Text[5];
            var tabSel = new GameObject[5];
            for (int i = 0; i < 5; i++)
            {
                var pos = new Vector2(-1040f + i * 520f, 560f);
                tabSel[i] = UImg(sui, "Tab Glow " + i, pos, new Vector2(512f, 137f), LoopLandArt.Glow, new Color(1f, 0.8f, 0.25f, 1f)).gameObject;
                TextMeshProUGUI lab = UButton(sui, "Tab " + tabNames[i], tabNames[i], pos, new Vector2(480f, 105f), new Color(0.13f, 0.14f, 0.34f, 1f), store, "_OnTab" + i, 38f);
                lab.rectTransform.anchoredPosition = new Vector2(40f, 0f);
                lab.rectTransform.sizeDelta = new Vector2(340f, 90f);
                UImg(lab.transform.parent, "Icon", new Vector2(-170f, 0f), new Vector2(66f, 66f), tabIcons[i], i == 4 ? Hex("FFD54A") : Hex("CFE3FF"), false);
                tabs[i] = lab;
            }
            store.tabLabels = tabs;
            store.tabSelected = tabSel;

            var itemRoots = new GameObject[8];
            var itemSel = new GameObject[8];
            var itemIcons = new Image[8];
            var itemNames = new TMP_Text[8];
            var itemPills = new Image[8];
            var itemStatus = new TMP_Text[8];
            for (int i = 0; i < 8; i++)
            {
                var pos = new Vector2(-1110f + (i % 4) * 380f, i < 4 ? 280f : -140f);
                itemSel[i] = UImg(sui, "Item Glow " + i, pos, new Vector2(390f, 430f), LoopLandArt.Glow, new Color(1f, 0.8f, 0.25f, 1f)).gameObject;
                TextMeshProUGUI lab = UButton(sui, "Item " + i, "", pos, new Vector2(360f, 400f), new Color(0.1f, 0.1f, 0.26f, 1f), store, "_OnItem" + i, 36f);
                Transform btn = lab.transform.parent;
                itemRoots[i] = btn.gameObject;
                itemIcons[i] = UImg(btn, "Art", new Vector2(0f, 60f), new Vector2(300f, 240f), null, Color.white, false);
                itemIcons[i].preserveAspect = true;
                lab.rectTransform.anchoredPosition = new Vector2(0f, -95f);
                lab.rectTransform.sizeDelta = new Vector2(330f, 60f);
                itemNames[i] = lab;
                itemPills[i] = UImg(btn, "Pill", new Vector2(0f, -160f), new Vector2(270f, 58f), LoopLandArt.Pill, Color.gray);
                itemStatus[i] = UText(itemPills[i].transform, "Status", "", Vector2.zero, new Vector2(250f, 50f), 30f, Color.white);
                itemStatus[i].fontStyle = FontStyles.Bold;
            }
            store.itemButtons = itemRoots;
            store.itemSelected = itemSel;
            store.itemIcons = itemIcons;
            store.itemNames = itemNames;
            store.itemPills = itemPills;
            store.itemStatus = itemStatus;

            UImg(sui, "Detail Panel", new Vector2(780f, 5f), new Vector2(1020f, 970f), LoopLandArt.Round, new Color(0.07f, 0.08f, 0.22f, 0.92f));
            store.detailName = UText(sui, "Detail Name", "", new Vector2(640f, 420f), new Vector2(700f, 90f), 58f, Color.white, TextAlignmentOptions.Left);
            store.detailRarityPill = UImg(sui, "Rarity", new Vector2(1130f, 420f), new Vector2(240f, 60f), LoopLandArt.Pill, Hex("1E6FD9"));
            store.detailRarity = UText(store.detailRarityPill.transform, "Rarity Text", "", Vector2.zero, new Vector2(220f, 50f), 28f, Color.white);
            store.detailDesc = UText(sui, "Detail Desc", "", new Vector2(780f, 300f), new Vector2(940f, 150f), 36f, Hex("D6DCFF"), TextAlignmentOptions.TopLeft);
            store.detailPreview = UImg(sui, "Detail Preview", new Vector2(780f, -40f), new Vector2(620f, 480f), null, Color.white, false);
            store.detailPreview.preserveAspect = true;
            store.actionLabel = UButton(sui, "Action", "SELECT ITEM", new Vector2(780f, -385f), new Vector2(940f, 150f), Hex("F5B800"), store, "_OnAction", 56f);
            store.actionLabel.color = new Color(0.12f, 0.08f, 0f, 1f);

            UImg(sui, "Banner", new Vector2(-530f, -430f), new Vector2(1520f, 150f), LoopLandArt.Round, new Color(0.1f, 0.12f, 0.34f, 0.95f));
            UImg(sui, "Banner Coins", new Vector2(-1205f, -430f), new Vector2(120f, 120f), LoopLandArt.Coin, Color.white, false);
            store.bannerText = UText(sui, "Banner Text", "", new Vector2(-470f, -430f), new Vector2(1300f, 130f), 36f, Color.white, TextAlignmentOptions.Left);

            TextMeshProUGUI daily = UButton(sui, "Daily", "DAILY BONUS", new Vector2(-665f, -735f), new Vector2(1250f, 190f), Hex("1E78FF"), store, "_OnDaily", 70f);
            daily.rectTransform.anchoredPosition = new Vector2(20f, 0f);
            daily.rectTransform.sizeDelta = new Vector2(800f, 150f);
            UImg(daily.transform.parent, "Icon", new Vector2(-480f, 0f), new Vector2(120f, 120f), LoopLandArt.IconGift, Color.white, false);
            UText(daily.transform.parent, "Arrow", "<b>></b>", new Vector2(560f, 0f), new Vector2(60f, 120f), 80f, Color.white);
            TextMeshProUGUI world = UButton(sui, "World Store", "WORLD STORE", new Vector2(665f, -735f), new Vector2(1250f, 190f), Hex("8A2BE2"), store, "_OnWorldStore", 64f);
            world.rectTransform.anchoredPosition = new Vector2(20f, 22f);
            world.rectTransform.sizeDelta = new Vector2(800f, 90f);
            UText(world.transform.parent, "Sub", "MORE ITEMS & EXCLUSIVE BUILDS", new Vector2(20f, -45f), new Vector2(800f, 50f), 32f, Hex("E8D5FF"));
            UImg(world.transform.parent, "Icon", new Vector2(-480f, 0f), new Vector2(120f, 120f), LoopLandArt.IconGlobe, Color.white, false);
            UText(world.transform.parent, "Arrow", "<b>></b>", new Vector2(560f, 0f), new Vector2(60f, 120f), 80f, Color.white);

            RectTransform lui = UCanvas(st, "Live Board UI", new Vector3(-2.4f, 1.5f, -0.3f), Quaternion.Euler(0f, -20f, 0f), new Vector2(1400f, 860f));
            UImg(lui, "Glow", Vector2.zero, new Vector2(1440f, 900f), LoopLandArt.Glow, new Color(0f, 0.9f, 1f, 0.9f));
            UImg(lui, "Back", Vector2.zero, new Vector2(1400f, 860f), LoopLandArt.Panel, Color.white);
            UText(lui, "Header", "<b>LIVE <color=#FF3DCB>BOARD</color></b>", new Vector2(0f, 360f), new Vector2(1300f, 100f), 64f, Hex("00E5FF"));
            URaw(lui, "Live View", new Vector2(0f, -50f), new Vector2(1320f, 660f), liveTex);
            captionsL.Add(UText(lui, "Caption", "", new Vector2(0f, 240f), new Vector2(1000f, 64f), 42f, Color.white));
            // purchase reveal: a scratch-off look at the bundle that was just bought (already granted, exactly as listed)
            RectTransform reveal = URect(sui, "Purchase Reveal", Vector2.zero, new Vector2(2700f, 1800f));
            UImg(reveal, "Shade", Vector2.zero, new Vector2(2700f, 1800f), null, new Color(0.02f, 0.02f, 0.08f, 0.94f), false, true); // blocks the store buttons behind it
            UText(reveal, "Title", "<b>THANK YOU!</b>", new Vector2(0f, 740f), new Vector2(2000f, 140f), 110f, Hex("FFE14D"));
            store.revealText = UText(reveal, "Text", "", new Vector2(0f, 630f), new Vector2(2300f, 80f), 44f, Color.white);
            store.revealTicket = BuildTicket(reveal, st, "Reveal Ticket", new Vector2(1500f, 760f), new Vector2(1240f, 460f), 50f, Hex("FFD23F"), "YOUR PURCHASE", store, "_OnRevealScratched", storeAudio);
            ((RectTransform)store.revealTicket.root.transform).anchoredPosition = new Vector2(0f, 90f);
            NeonButton(reveal, "Done", "DONE", new Vector2(0f, -560f), new Vector2(520f, 140f), Hex("12B76A"), null, store, "_CloseReveal", 60f, false);
            store.revealPanel = reveal.gameObject;
            reveal.gameObject.SetActive(false);
            SetLayer(lui.gameObject, 1);
            SetLayer(sui.gameObject, 1);
            store.sfx = storeAudio;
            store.clickClip = Wav("click", 0.06f, t => Sin(1800f, t) * Env(t, 0.001f, 0.015f) * 0.5f);
            store.buyClip = Wav("coin", 0.5f, t => Notes(t, new[] { 988f, 1319f }, 0.08f, 0.25f));
            store.errorClip = Wav("error", 0.25f, t => Notes(t, new[] { 220f, 196f }, 0.1f, 0.06f));

            // store catalogue (thumbnails are generated; drop your own into Assets/LoopLand/Store Art to replace them)
            store.game = game;
            store.diceNames = diceNames;
            store.dicePrices = new[] { 0, 150, 300, 450, 600, 0, 0, 0 };
            store.diceProduct = new[] { -1, -1, -1, -1, -1, 0, 0, 2 };
            store.diceMaterials = diceMats;
            store.diceGlow = diceGlow;
            store.diceDesc = new[]
            {
                "Clean ivory dice with crisp black pips. A timeless classic.",
                "Glossy black dice with silver pips for a stealthy roll.",
                "Electric neon pips that glow with every throw.",
                "Deep red dice with a warm ember glow.",
                "Icy dice that shine like fresh frost.",
                "Solid gold dice for true LoopLand royalty.",
                "Cosmic purple dice sprinkled with stars.",
                "Blazing plasma dice that leave a fiery glow."
            };
            store.diceArt = new Sprite[8];
            for (int i = 0; i < 8; i++) store.diceArt[i] = StoreArt("dice", i) ?? LoopLandArt.DiceThumb("Thumb_Dice_" + i, Hex(DiceBodyHex[i]), Hex(DicePipHex[i]), i == 6);

            store.tokenNames = new[] { "Classic Glow", "Chrome", "Toxic", "Lava", "Diamond", "Prism" };
            store.tokenPrices = new[] { 0, 200, 350, 500, 0, 0 };
            store.tokenProduct = new[] { -1, -1, -1, -1, 1, 1 };
            store.tokenColors = new[] { Hex("00E5FF"), Hex("D8DEE9"), Hex("7CFF4F"), Hex("FF5A1F"), Hex("BFF6FF"), Hex("FF3DCB") };
            store.rainbowToken = 5;
            store.tokenDesc = new[]
            {
                "Your seat color with a soft glow ring.",
                "Polished chrome that reflects the whole board.",
                "Radioactive green with a toxic aura.",
                "A molten lava core that smolders as you move.",
                "Crystal-clear diamond with a brilliant shine.",
                "An animated rainbow prism that shifts colors."
            };
            store.tokenArt = new Sprite[6];
            for (int i = 0; i < 6; i++) store.tokenArt[i] = StoreArt("token", i) ?? LoopLandArt.TokenThumb("Thumb_Token_" + i, store.tokenColors[i], i == 5);

            store.buildingNames = new[] { "Classic Skyline", "Neon Pulse", "Coastal Resort", "Rooftop Gardens", "Ruby Ember", "Royal Gold", "Galaxy Holo", "Inferno Plasma" };
            store.buildingPrices = new[] { 0, 200, 300, 400, 450, 0, 0, 0 };
            store.buildingProduct = new[] { -1, -1, -1, -1, -1, 5, 5, 2 };
            store.buildingColors = new[] { Hex("6FA8FF"), Hex("00F0FF"), Hex("3DE0C0"), Hex("5BE36B"), Hex("FF4060"), Hex("FFD54A"), Hex("B07CFF"), Hex("FF7A00") };
            store.buildingDesc = new[]
            {
                "Your tile glow: the frame that lights up the tile you land on. A clean classic.",
                "Electric cyan light on every tile you land on.",
                "Sunny aqua light, like a beachfront at night.",
                "Fresh green light, like a rooftop garden.",
                "Crimson ember light for a fiery landing.",
                "A gilded glow for the ultimate looper.",
                "Holographic violet light from a distant galaxy.",
                "Blazing plasma light that burns bright."
            };
            store.buildingArt = new Sprite[8];
            for (int i = 0; i < 8; i++) store.buildingArt[i] = StoreArt("building", i) ?? LoopLandArt.BuildingThumb("Thumb_Building_" + i, store.buildingColors[i], i);

            store.trailNames = new[] { "Stardust", "Comet", "Ember", "Golden", "Rainbow" };
            store.trailPrices = new[] { 0, 250, 400, 0, 0 };
            store.trailProduct = new[] { -1, -1, -1, 2, 2 };
            store.trailColors = new[] { Color.white, Hex("4DB8FF"), Hex("FF7A2E"), Hex("FFD54A"), Hex("FF3DCB") };
            store.rainbowTrail = 4;
            store.trailDesc = new[]
            {
                "A gentle sparkle that follows every hop.",
                "A blue comet tail streaking across the board.",
                "Glowing embers drifting behind you.",
                "Golden sparks for VIP players.",
                "A full rainbow trail that cycles colors."
            };
            store.trailArt = new Sprite[5];
            for (int i = 0; i < 5; i++) store.trailArt[i] = StoreArt("trail", i) ?? LoopLandArt.TrailThumb("Thumb_Trail_" + i, store.trailColors[i], i == 4);

            store.productNames = new[] { "Premium Dice Pack", "Holo Token Pack", "LoopLand VIP", "Coin Pouch", "Coin Vault", "Skyline Pack" };
            store.productDescriptions = new[]
            {
                "Unlocks Royal Gold and Galaxy Holo dice forever.",
                "Unlocks the Diamond and animated Prism tokens forever.",
                "Contents: 2x Loop Coins from games and the daily bonus, Inferno Plasma dice, Inferno Plasma tile glow, Golden trail and Rainbow trail.",
                "+500 Loop Coins instantly. Buy as many as you like.",
                "+3000 Loop Coins instantly. Best value!",
                "Contents: the Royal Gold and Galaxy Holo tile glows, yours forever."
            };
            store.productListingIds = new[] { "", "", "", "", "", "" };
            store.productPriceLabels = new[] { "Credits", "Credits", "Credits", "Credits", "Credits", "Credits" };
            store.productCoins = new[] { 0, 0, 0, 500, 3000, 0 };
            store.productArt = new Sprite[6];
            for (int i = 0; i < 6; i++) store.productArt[i] = StoreArt("premium", i) ?? LoopLandArt.ProductThumb("Thumb_Premium_" + i, i);
            store.vipProduct = 2;
            LoopLandScratch freeScratch = ScratchMachine(rt, store, dark);
            for (int i = 0; i < 8; i++)
            {
                itemIcons[i].sprite = store.diceArt[i];
                itemNames[i].text = store.diceNames[i];
            }
            store.detailPreview.sprite = store.diceArt[0];
            store.detailName.text = store.diceNames[0];
            store.detailDesc.text = store.diceDesc[0];

            // game wiring
            game.store = store;
            game.freeScratch = freeScratch;
            game.ticket = luckyTicket;
            game.challenge = challenge;
            game.tokens = tokens;
            game.dice = dice;
            game.spaceAnchors = anchors;
            game.turnMarker = turnMarker;
            game.turnMarkerRenderer = markerFrame.GetComponent<Renderer>();
            game.mysteryBadges = badges;
            game.spinner = spinner;
            game.statusTexts = statusL.ToArray();
            game.playerTexts = playersL.ToArray();
            game.cardTexts = cardsL.ToArray();
            game.captionTexts = captionsL.ToArray();
            game.viewLabels = views;
            game.seatScreens = seatScreens;
            game.centerScreens = holo.gameObject;
            game.liveCam = liveCam;
            liveCam.game = game;
            game.celebrateFx = confetti;
            game.moneyFx = money;
            game.sfx = gameAudio;
            game.slotColors = slotCols;
            game.slotHex = SlotHex;
            game.dashSpots = dashSpots;
            game.primaryLabels = prim;
            game.infoTexts = info;
            game.panelRoots = panelRoots;
            game.statusBodies = statusBodies;
            game.ticketBodies = ticketBodies;
            game.powerBodies = powerBodies;
            game.resultCards = resultCards;
            game.resultTitles = resultTitles;
            game.resultSubs = resultSubs;
            game.resultIcons = resultIcons;
            game.introCards = introCards;
            game.introTitles = introTitles;
            game.introBodies = introBodies;
            // result pictures, in LoopLandGame.I_* order
            game.resultArt = new[]
            {
                LoopLandArt.Coin, LoopLandArt.NoPrize, LoopLandArt.IconTicket, LoopLandArt.IconTarget, LoopLandArt.IconShield, LoopLandArt.IconBolt,
                LoopLandArt.IconRefresh, LoopLandArt.IconDice, LoopLandArt.IconMystery, LoopLandArt.LogoInfinity, portalArt, LoopLandArt.NoPrize, LoopLandArt.IconCrown
            };
            // sound cues, in LoopLandGame.FX_* order
            game.fxClips = new[]
            {
                store.clickClip,
                store.buyClip,
                Wav("glitch", 0.6f, t => Mathf.Sign(Sin(110f, t)) * 0.22f * (0.6f + 0.4f * Sin(18f, t)) * Env(t, 0.01f, 0.25f)),
                Wav("card", 0.5f, t => Notes(t, new[] { 1047f, 1319f, 1568f, 2093f }, 0.06f, 0.2f)),
                Wav("win", 1.4f, t => Notes(t, new[] { 523f, 659f, 784f, 1047f, 784f, 1047f }, 0.14f, 0.35f)),
                Wav("build", 0.4f, t => Notes(t, new[] { 659f, 880f, 1175f }, 0.07f, 0.15f)),
                Wav("mystery", 0.8f, t => Notes(t, new[] { 880f, 698f, 1175f, 932f, 1397f }, 0.09f, 0.25f) * 0.8f),
                Wav("level_up", 1.2f, t => Notes(t, new[] { 392f, 523f, 659f, 784f, 1047f, 1319f }, 0.1f, 0.4f)),
                Wav("start", 0.6f, t => Notes(t, new[] { 392f, 523f, 659f, 784f }, 0.08f, 0.2f)),
                Wav("lap", 0.5f, t => Notes(t, new[] { 784f, 1175f, 1568f }, 0.07f, 0.2f)),
                Wav("challenge", 0.6f, t => Notes(t, new[] { 659f, 659f, 988f }, 0.12f, 0.08f)),
                Wav("warp", 0.5f, t => Sin(300f + 2400f * t, t) * Env(t, 0.01f, 0.2f) * 0.4f)
            };
            AudioClip roll = Wav("dice_roll", 0.9f, t => Noise() * Env(t % 0.07f, 0.001f, 0.012f) * 0.6f * (1f - t / 0.9f));
            AudioClip clack = Wav("dice_land", 0.25f, t => (Noise() * 0.6f + Sin(900f, t) * 0.3f) * (Env(t, 0.001f, 0.02f) + (t > 0.09f ? Env(t - 0.09f, 0.001f, 0.02f) : 0f)));
            foreach (LoopLandDice dc in dice) { dc.rollClip = roll; dc.landClip = clack; }
            return root;
        }

        // ------------------------------------------------------------------ free loop scratch

        /// <summary>
        /// Free Loop Scratch: an arcade machine beside the store. Free tickets only (one every half hour and one per finished
        /// game); press SCRATCH on the screen and rub the ticket that lands on the counter for a just-for-fun reward.
        /// </summary>
        private static LoopLandScratch ScratchMachine(Transform rt, LoopLandStore store, Material dark)
        {
            var go = new GameObject("Free Loop Scratch");
            Transform m = go.transform;
            m.SetParent(rt, false);
            m.localPosition = new Vector3(-5.2f, 0f, -4.6f);
            m.localRotation = Quaternion.Euler(0f, -132f, 0f); // the front (local -Z) faces the table
            var sc = UdonSharpUndo.AddComponent<LoopLandScratch>(go);
            made.Add(sc);
            sc.sfx = Audio(go);

            // cabinet: base, sloped desk, screen box and a marquee, trimmed with neon
            Material body = Std("Scratch_Body", Hex("1B1838"), 0.45f, 0.85f, Color.black);
            Material pink = Std("Scratch_Neon_Pink", Hex("FF3DCB"), 0f, 0.5f, Hex("FF3DCB") * 1.6f);
            Material cyan = Std("Scratch_Neon_Cyan", Hex("00E5FF"), 0f, 0.5f, Hex("00E5FF") * 1.5f);
            Material gold = Std("Scratch_Gold", Hex("FFD23F"), 0.85f, 0.8f, Hex("FFB000") * 0.5f);
            Prim(PrimitiveType.Cube, "Base", m, new Vector3(0f, 0.475f, 0.1f), new Vector3(2.3f, 0.95f, 0.9f), body, true);
            Prim(PrimitiveType.Cube, "Desk", m, new Vector3(0f, 1f, -0.25f), new Vector3(2.3f, 0.1f, 0.62f), dark, true).transform.localRotation = Quaternion.Euler(-18f, 0f, 0f);
            Prim(PrimitiveType.Cube, "Desk Back", m, new Vector3(0f, 1.05f, 0.1f), new Vector3(2.3f, 0.2f, 0.12f), body, true);
            Prim(PrimitiveType.Cube, "Screen Box", m, new Vector3(0f, 1.95f, 0.3f), new Vector3(2.3f, 1.6f, 0.3f), body, true);
            Prim(PrimitiveType.Cube, "Marquee", m, new Vector3(0f, 3.02f, 0.3f), new Vector3(2.5f, 0.55f, 0.4f), body, true);
            for (int s = -1; s <= 1; s += 2)
            {
                Prim(PrimitiveType.Cube, "Side Neon", m, new Vector3(s * 1.16f, 1.95f, 0.14f), new Vector3(0.05f, 1.6f, 0.04f), pink);
                Prim(PrimitiveType.Cube, "Marquee Neon", m, new Vector3(0f, 3.02f + s * 0.26f, 0.095f), new Vector3(2.5f, 0.03f, 0.03f), cyan);
            }
            Prim(PrimitiveType.Cube, "Base Neon", m, new Vector3(0f, 0.6f, -0.36f), new Vector3(2.3f, 0.04f, 0.03f), cyan);
            Prim(PrimitiveType.Cube, "Prize Tray", m, new Vector3(0f, 0.3f, -0.36f), new Vector3(0.8f, 0.2f, 0.04f), dark);
            Prim(PrimitiveType.Cube, "Prize Tray Trim", m, new Vector3(0f, 0.3f, -0.355f), new Vector3(0.86f, 0.26f, 0.03f), gold);
            Prim(PrimitiveType.Cube, "Coin Slot", m, new Vector3(0.75f, 0.78f, -0.36f), new Vector3(0.2f, 0.1f, 0.03f), gold);
            Prim(PrimitiveType.Cube, "Coin Slot Gap", m, new Vector3(0.75f, 0.78f, -0.37f), new Vector3(0.12f, 0.02f, 0.02f), dark);

            // "FEEL LUCKY?" sign beside the machine
            Prim(PrimitiveType.Cube, "Sign Post", m, new Vector3(-1.75f, 0.4f, 0.1f), new Vector3(0.12f, 0.8f, 0.12f), body, true);
            Prim(PrimitiveType.Cube, "Sign Neon", m, new Vector3(-1.75f, 1.6f, 0.12f), new Vector3(0.84f, 1.74f, 0.06f), pink);
            Prim(PrimitiveType.Cube, "Sign", m, new Vector3(-1.75f, 1.6f, 0.1f), new Vector3(0.8f, 1.7f, 0.08f), body, true);
            RectTransform sign = UCanvas(m, "Sign UI", new Vector3(-1.75f, 1.6f, 0.055f), Quaternion.identity, new Vector2(760f, 1660f), false);
            UImg(sign, "Back", Vector2.zero, new Vector2(760f, 1660f), LoopLandArt.Panel, Color.white);
            TextMeshProUGUI feel = UText(sign, "Feel Lucky", "<b>FEEL\nLUCKY?</b>", new Vector2(0f, 560f), new Vector2(700f, 400f), 150f, Color.white);
            feel.enableVertexGradient = true;
            feel.colorGradient = new VertexGradient(Hex("FFE14D"), Hex("FFE14D"), Hex("FF3DCB"), Hex("FF3DCB"));
            UImg(sign, "Dice Glow", new Vector2(0f, 120f), new Vector2(380f, 380f), LoopLandArt.Glow, new Color(0f, 0.9f, 1f, 0.6f));
            UImg(sign, "Dice", new Vector2(0f, 120f), new Vector2(300f, 300f), LoopLandArt.IconDice, Hex("CFF8FF"), false);
            UText(sign, "Words", "<b><color=#00E5FF>SCRATCH</color>\n<color=#FFE14D>COLLECT</color>\n<color=#FF3DCB>PLAY</color></b>", new Vector2(0f, -470f), new Vector2(700f, 560f), 130f, Color.white);

            // marquee
            RectTransform mq = UCanvas(m, "Marquee UI", new Vector3(0f, 3.02f, 0.095f), Quaternion.identity, new Vector2(2400f, 460f), false);
            UImg(mq, "Logo Left", new Vector2(-920f, 0f), new Vector2(440f, 440f), LoopLandArt.LogoInfinity, Color.white, false);
            UImg(mq, "Logo Right", new Vector2(920f, 0f), new Vector2(440f, 440f), LoopLandArt.LogoInfinity, Color.white, false);
            TextMeshProUGUI brand = UText(mq, "Title", "<b>LOOPLAND</b>", new Vector2(0f, 70f), new Vector2(1300f, 230f), 200f, Color.white);
            brand.enableVertexGradient = true;
            brand.colorGradient = new VertexGradient(Hex("FFE14D"), Hex("00E5FF"), Hex("FF3DCB"), Hex("B07CFF"));
            UText(mq, "Subtitle", "<b>FREE LOOP SCRATCH</b>", new Vector2(0f, -125f), new Vector2(1300f, 130f), 110f, Hex("FFD23F"));

            // screen: free tickets, XP, next-game perks and the stamp book
            RectTransform scr = UCanvas(m, "Screen UI", new Vector3(0f, 1.95f, 0.145f), Quaternion.identity, new Vector2(2100f, 1450f));
            UImg(scr, "Glow", Vector2.zero, new Vector2(2140f, 1490f), LoopLandArt.Glow, new Color(0.75f, 0.35f, 1f, 1f));
            UImg(scr, "Back", Vector2.zero, new Vector2(2100f, 1450f), LoopLandArt.Panel, Color.white);
            TextMeshProUGUI title = UText(scr, "Title", "<b>FREE LOOP SCRATCH</b>", new Vector2(0f, 615f), new Vector2(1900f, 140f), 110f, Color.white);
            title.enableVertexGradient = true;
            title.colorGradient = brand.colorGradient;
            UText(scr, "Sub", "Free tickets, just for fun: scratch for XP, perks for your next game, fireworks and stamps!", new Vector2(0f, 510f), new Vector2(1900f, 70f), 40f, Hex("D6DCFF"));

            UImg(scr, "Left Back", new Vector2(-520f, -40f), new Vector2(940f, 1000f), LoopLandArt.Round, new Color(0.06f, 0.07f, 0.2f, 0.92f));
            UImg(scr, "Ticket Icon", new Vector2(-520f, 300f), new Vector2(240f, 240f), LoopLandArt.IconTicket, Hex("FF3DCB"), false);
            sc.ticketsText = UText(scr, "Tickets", "...", new Vector2(-520f, 130f), new Vector2(880f, 120f), 90f, Color.white);
            sc.timerText = UText(scr, "Timer", "", new Vector2(-520f, 40f), new Vector2(880f, 60f), 40f, Hex("9AF2FF"));
            sc.buttonLabel = NeonButton(scr, "Scratch", "SCRATCH A FREE TICKET", new Vector2(-520f, -120f), new Vector2(820f, 170f), Hex("12B76A"), LoopLandArt.IconTicket, sc, "_OnScratch", 60f, false);
            sc.messageText = UText(scr, "Message", "", new Vector2(-520f, -270f), new Vector2(880f, 90f), 40f, Color.white);
            sc.perksText = UText(scr, "Perks", "", new Vector2(-520f, -380f), new Vector2(880f, 70f), 36f, Color.white);
            UText(scr, "How", "<color=#FF3DCB>+1 free ticket every " + sc.refillMinutes + " min</color>  and  <color=#7CFF4F>+1 for every finished game</color>", new Vector2(-520f, -480f), new Vector2(880f, 60f), 32f, Color.white);

            UImg(scr, "Right Back", new Vector2(520f, -40f), new Vector2(940f, 1000f), LoopLandArt.Round, new Color(0.06f, 0.07f, 0.2f, 0.92f));
            sc.xpText = UText(scr, "XP", "", new Vector2(520f, 380f), new Vector2(880f, 90f), 60f, Color.white);
            sc.stampText = UText(scr, "Stamp Title", "STAMP BOOK", new Vector2(520f, 260f), new Vector2(880f, 70f), 46f, Hex("FFE14D"));
            Sprite[] stampArt = { LoopLandArt.IconSparkle, LoopLandArt.LogoInfinity, LoopLandArt.IconBuilding, LoopLandArt.IconDice, LoopLandArt.IconTree, LoopLandArt.IconTarget, LoopLandArt.IconRocket, LoopLandArt.IconCrown };
            Color[] stampCols = { Hex("FFE14D"), Color.white, Hex("00E5FF"), Hex("FFD23F"), Hex("7CFFD4"), Hex("4D8BFF"), Hex("FF8A3D"), Hex("FFD23F") };
            var stampIcons = new Image[8];
            for (int i = 0; i < 8; i++)
            {
                var at = new Vector2(520f + (i % 4 - 1.5f) * 210f, i < 4 ? 80f : -190f);
                UImg(scr, "Stamp Slot " + i, at + new Vector2(0f, 10f), new Vector2(190f, 230f), LoopLandArt.Round, new Color(0.12f, 0.13f, 0.32f, 1f));
                stampIcons[i] = UImg(scr, "Stamp " + i, at + new Vector2(0f, 35f), new Vector2(140f, 140f), stampArt[i], new Color(0.25f, 0.25f, 0.35f, 0.5f), false);
                stampIcons[i].preserveAspect = true;
                UText(scr, "Stamp Name " + i, sc.stampNames[i], at + new Vector2(0f, -70f), new Vector2(180f, 50f), 24f, Hex("D6DCFF"));
            }
            sc.stampIcons = stampIcons;
            sc.stampArt = stampArt;
            sc.stampColors = stampCols;
            UText(scr, "Stamp Hint", "Collect all 8 stamps from free tickets!", new Vector2(520f, -400f), new Vector2(880f, 60f), 34f, Hex("9AF2FF"));
            UText(scr, "Fine Print", "Just for fun: tickets are free and can't be bought. Rewards can't be traded or cashed out, and only work in LoopLand.",
                new Vector2(0f, -610f), new Vector2(2000f, 60f), 30f, Hex("8C93C8"));

            // counter: how it works, the free ticket you scratch, and tips
            RectTransform desk = UCanvas(m, "Desk UI", new Vector3(0f, 1.052f, -0.267f), Quaternion.Euler(72f, 0f, 0f), new Vector2(2100f, 560f), false);
            UImg(desk, "Back", Vector2.zero, new Vector2(2100f, 560f), LoopLandArt.Panel, Color.white);
            UText(desk, "How Title", "<b>HOW IT WORKS</b>", new Vector2(-865f, 225f), new Vector2(360f, 60f), 40f, Hex("FFE14D"));
            string[] steps = { "<b>GET</b> free tickets\nby playing", "<b>PRESS</b> SCRATCH\non the screen", "<b>RUB</b> the silver\non this counter", "<b>COLLECT</b> your\nreward!" };
            for (int i = 0; i < 4; i++)
            {
                float y = 125f - i * 105f;
                UImg(desk, "Step " + (i + 1), new Vector2(-990f, y), new Vector2(76f, 76f), LoopLandArt.Round, Hex("8A2BE2"));
                UText(desk, "Step Number " + (i + 1), "<b>" + (i + 1) + "</b>", new Vector2(-990f, y), new Vector2(70f, 70f), 46f, Color.white);
                UText(desk, "Step Text " + (i + 1), steps[i], new Vector2(-825f, y), new Vector2(240f, 90f), 30f, Color.white, TextAlignmentOptions.Left);
            }
            UText(desk, "Tips", "<b><color=#00E5FF>VR</color></b>\nrub the silver with\nyour finger or hand\n\n<b><color=#FF3DCB>DESKTOP</color></b>\nlook across the silver\nto scratch it off",
                new Vector2(865f, 0f), new Vector2(340f, 380f), 30f, Hex("D6DCFF"));
            RectTransform idle = URect(desk, "Idle", Vector2.zero, new Vector2(1300f, 540f));
            UImg(idle, "Back", Vector2.zero, new Vector2(1300f, 540f), LoopLandArt.Round, new Color(0.1f, 0.06f, 0.24f, 0.8f));
            UImg(idle, "Logo", new Vector2(0f, 120f), new Vector2(300f, 300f), LoopLandArt.LogoInfinity, Color.white, false);
            UText(idle, "Text", "<b>PRESS SCRATCH A FREE TICKET</b>\n<size=60%>on the screen, and your ticket lands right here!</size>", new Vector2(0f, -90f), new Vector2(1200f, 220f), 72f, Color.white);
            sc.ticket = BuildTicket(desk, m, "Free Ticket", new Vector2(1300f, 540f), new Vector2(980f, 330f), 45f, Hex("7CFF4F"), "FREE LOOP SCRATCH", sc, "_OnTicketScratched", sc.sfx);

            SetLayer(scr.gameObject, 1);
            SetLayer(desk.gameObject, 1);
            SetLayer(mq.gameObject, 1);
            SetLayer(sign.gameObject, 1);

            // fireworks over the table, for everyone, when someone scratches FIREWORKS!
            ParticleSystem fireworks = Fx("Fireworks", rt, new Vector3(0f, 3.6f, 0f), Quaternion.identity, 2.4f, 4.5f, 0.07f, 0f, 0f, false, true,
                ParticleSystemShapeType.Sphere, 0.3f, 0.6f, 1200, Color.white, Color.white, 0f);
            var fem = fireworks.emission;
            fem.SetBursts(new[] { new ParticleSystem.Burst(0f, 300), new ParticleSystem.Burst(0.5f, 300), new ParticleSystem.Burst(1.0f, 400) });
            var fmain = fireworks.main;
            fmain.duration = 1.5f;
            var rainbow = new Gradient();
            rainbow.SetKeys(new[] { new GradientColorKey(Hex("FF3DCB"), 0f), new GradientColorKey(Hex("FFE14D"), 0.33f), new GradientColorKey(Hex("00E5FF"), 0.66f), new GradientColorKey(Hex("7CFF4F"), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            fmain.startColor = new ParticleSystem.MinMaxGradient(rainbow) { mode = ParticleSystemGradientMode.RandomColor };
            sc.fireworks = fireworks;

            sc.xpArt = LoopLandArt.IconChart;
            sc.doubleXpArt = LoopLandArt.IconChart;
            sc.boostArt = LoopLandArt.IconBolt;
            sc.shieldArt = LoopLandArt.IconShield;
            sc.fireworksArt = LoopLandArt.IconSparkle;
            sc.clickClip = store.clickClip;
            sc.errorClip = store.errorClip;
            sc.ticketClip = Wav("card", 0.5f, t => Notes(t, new[] { 1047f, 1319f, 1568f, 2093f }, 0.06f, 0.2f));
            sc.fireworksClip = Wav("fireworks", 2.2f, t => (Noise() * Env(t % 0.55f, 0.002f, 0.12f) * 0.5f + Sin(220f + 600f * (t % 0.55f), t) * Env(t % 0.55f, 0.01f, 0.08f) * 0.2f) * (1f - t / 2.2f));
            return sc;
        }

        /// <summary>
        /// A scratchable ticket: frame, header, a prize window covered by overlapping foil flakes (clipped to the window), and
        /// a hint line. The LoopLandTicket component sits on an always-active object under logicParent, so it is initialised
        /// at start; its visual root (under uiParent) starts hidden and is shown by _Show.
        /// </summary>
        private static LoopLandTicket BuildTicket(Transform uiParent, Transform logicParent, string name, Vector2 size, Vector2 window, float spacing, Color accent,
            string header, UdonSharpBehaviour target, string doneEvent, AudioSource audio)
        {
            var logic = new GameObject(name + " Logic");
            logic.transform.SetParent(logicParent, false);
            var tk = UdonSharpUndo.AddComponent<LoopLandTicket>(logic);
            made.Add(tk);
            RectTransform root = URect(uiParent, name, Vector2.zero, size);
            UImg(root, "Frame", Vector2.zero, size, LoopLandArt.Round, accent);
            UImg(root, "Back", Vector2.zero, size - new Vector2(16f, 16f), LoopLandArt.Round, new Color(0.09f, 0.05f, 0.22f, 1f));
            float top = size.y * 0.5f - 50f;
            tk.headerText = UText(root, "Header", "<b>" + header + "</b>", new Vector2(0f, top), new Vector2(size.x - 260f, 70f), 48f, Color.white);
            for (int s = -1; s <= 1; s += 2) UImg(root, "Logo", new Vector2(s * (size.x * 0.5f - 85f), top), new Vector2(120f, 120f), LoopLandArt.LogoInfinity, Color.white, false);
            UImg(root, "Window Frame", new Vector2(0f, -8f), window + new Vector2(16f, 16f), LoopLandArt.Round, Hex("FFD23F"));
            UImg(root, "Window", new Vector2(0f, -8f), window, LoopLandArt.Round, new Color(0.04f, 0.04f, 0.13f, 1f));
            float iconSize = Mathf.Min(window.y - 60f, 240f);
            tk.prizeIcon = UImg(root, "Prize Icon", new Vector2(-window.x * 0.5f + iconSize * 0.5f + 40f, -8f), new Vector2(iconSize, iconSize), LoopLandArt.Coin, Color.white, false);
            tk.prizeIcon.preserveAspect = true;
            float textW = window.x - iconSize - 100f;
            float textX = -window.x * 0.5f + iconSize + 70f + textW * 0.5f;
            tk.prizeTitle = UText(root, "Prize Title", "", new Vector2(textX, window.y * 0.12f), new Vector2(textW, window.y * 0.45f), 64f, Hex("FFE14D"));
            tk.prizeTitle.fontStyle = FontStyles.Bold;
            tk.prizeSub = UText(root, "Prize Sub", "", new Vector2(textX, -window.y * 0.27f), new Vector2(textW, window.y * 0.35f), 32f, Hex("D6DCFF"));
            // the foil: overlapping flakes clipped to the window, so scratched holes get torn, scalloped edges
            RectTransform foil = URect(root, "Foil", new Vector2(0f, -8f), window);
            foil.gameObject.AddComponent<RectMask2D>();
            int cols = Mathf.CeilToInt(window.x / spacing);
            int rows = Mathf.CeilToInt(window.y / spacing);
            var cells = new GameObject[cols * rows];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var at = new Vector2((c - (cols - 1) * 0.5f) * spacing, ((rows - 1) * 0.5f - r) * spacing);
                    Image flake = UImg(foil, "Flake " + (r * cols + c), at, Vector2.one * (spacing * 1.64f), LoopLandArt.ScratchCell, Color.white, false);
                    flake.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f * rng.Next(4));
                    cells[r * cols + c] = flake.gameObject;
                }
            tk.hintText = UText(root, "Hint", "", new Vector2(0f, -size.y * 0.5f + 34f), new Vector2(size.x - 60f, 44f), 26f, Hex("9AF2FF"));
            tk.root = root.gameObject;
            tk.cardSpace = foil;
            tk.cells = cells;
            tk.target = target;
            tk.doneEvent = doneEvent;
            tk.sfx = audio;
            tk.scratchClip = Wav("scratch", 0.16f, t => Noise() * (0.55f + 0.45f * Sin(70f, t)) * Env(t, 0.004f, 0.05f) * 0.45f);
            tk.doneClip = Wav("ticket_reveal", 0.6f, t => Notes(t, new[] { 784f, 1047f, 1319f, 1568f }, 0.07f, 0.2f));
            tk.dust = Fx("Scratch Dust", logic.transform, Vector3.zero, Quaternion.identity, 0.9f, 0.4f, 0.014f, 0f, 0f, false, true,
                ParticleSystemShapeType.Sphere, 0.02f, 1.2f, 300, Hex("F2F5FF"), Hex("9AA3B5"), 0f);
            root.gameObject.SetActive(false);
            return tk;
        }

        /// <summary>The challenge panel: title, timer line, a play area with three target buttons, and a big countdown/result text.</summary>
        private static LoopLandChallenge BuildChallenge(Transform uiParent, Transform logicParent, LoopLandGame game, AudioSource audio)
        {
            var logic = new GameObject("Challenge Logic");
            logic.transform.SetParent(logicParent, false);
            var ch = UdonSharpUndo.AddComponent<LoopLandChallenge>(logic);
            made.Add(ch);
            RectTransform root = URect(uiParent, "Challenge", Vector2.zero, new Vector2(760f, 570f));
            UImg(root, "Frame", Vector2.zero, new Vector2(760f, 570f), LoopLandArt.Round, Hex("00E5FF"));
            UImg(root, "Back", Vector2.zero, new Vector2(744f, 554f), LoopLandArt.Round, new Color(0.03f, 0.06f, 0.16f, 1f));
            ch.titleText = UText(root, "Title", "", new Vector2(0f, 235f), new Vector2(700f, 70f), 54f, Hex("00E5FF"));
            ch.titleText.fontStyle = FontStyles.Bold;
            ch.infoText = UText(root, "Info", "", new Vector2(0f, 178f), new Vector2(700f, 50f), 34f, Color.white);
            RectTransform playArea = URect(root, "Play Area", new Vector2(0f, -55f), new Vector2(700f, 400f));
            UImg(playArea, "Back", Vector2.zero, new Vector2(700f, 400f), LoopLandArt.Round, new Color(1f, 1f, 1f, 0.05f));
            var targets = new RectTransform[3];
            var images = new Image[3];
            for (int k = 0; k < 3; k++)
            {
                Image img = UImg(playArea, "Target " + k, Vector2.zero, new Vector2(130f, 130f), LoopLandArt.IconTarget, Hex("FF3DCB"), false, true);
                var btn = img.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                var nav = btn.navigation;
                nav.mode = Navigation.Mode.None;
                btn.navigation = nav;
                UnityEventTools.AddStringPersistentListener(btn.onClick, UdonSharpEditorUtility.GetBackingUdonBehaviour(ch).SendCustomEvent, "_OnHit" + k);
                img.gameObject.SetActive(false);
                targets[k] = img.rectTransform;
                images[k] = img;
            }
            ch.bigText = UText(root, "Big", "", new Vector2(0f, -55f), new Vector2(700f, 220f), 110f, Color.white);
            ch.bigText.fontStyle = FontStyles.Bold;
            ch.game = game;
            ch.root = root.gameObject;
            ch.targets = targets;
            ch.targetImages = images;
            ch.laserSprite = LoopLandArt.IconTarget;
            ch.coinSprite = LoopLandArt.Coin;
            ch.area = new Vector2(540f, 250f);
            ch.sfx = audio;
            ch.hitClip = Wav("challenge_hit", 0.18f, t => Notes(t, new[] { 1319f, 1976f }, 0.05f, 0.06f));
            ch.tickClip = Wav("challenge_tick", 0.12f, t => Sin(880f, t) * Env(t, 0.002f, 0.04f) * 0.5f);
            ch.goClip = Wav("challenge_go", 0.4f, t => Notes(t, new[] { 1047f, 1568f }, 0.08f, 0.15f));
            ch.endClip = Wav("challenge_end", 0.7f, t => Notes(t, new[] { 784f, 988f, 1175f, 1568f }, 0.08f, 0.2f));
            root.gameObject.SetActive(false);
            return ch;
        }

        // ------------------------------------------------------------------ helpers: objects

        private static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 lpos, Vector3 lscale, Material m, bool keepCollider = false)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = lpos;
            go.transform.localScale = lscale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            Collider col = go.GetComponent<Collider>();
            if (!keepCollider) Object.DestroyImmediate(col);
            else if (col is CapsuleCollider)
            {
                // A flattened cylinder's capsule collider turns into a huge sphere that blocks every Interact.
                Object.DestroyImmediate(col);
                go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            }
            return go;
        }

        private static TextMeshPro Text(Transform parent, string name, string text, Vector3 lpos, Quaternion lrot, Vector2 size, float maxSize, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = lpos;
            go.transform.localRotation = lrot;
            var t = go.AddComponent<TextMeshPro>();
            t.font = font;
            t.text = text;
            t.color = color;
            t.alignment = align;
            t.richText = true;
            t.enableAutoSizing = true;
            t.fontSizeMax = maxSize;
            t.fontSizeMin = maxSize * 0.2f;
            t.fontSize = maxSize;
            t.rectTransform.sizeDelta = size;
            return t;
        }

        private static Sprite roundSprite;

        /// <summary>World-space canvas set up the way VRChat needs it (VRC Ui Shape, Default layer, collider). 1 canvas unit = 1 mm.</summary>
        private static RectTransform UCanvas(Transform parent, string name, Vector3 lpos, Quaternion lrot, Vector2 px, bool interactive = true)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = lpos;
            go.transform.localRotation = lrot;
            go.transform.localScale = Vector3.one * 0.001f;
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = px;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 2f;
            if (!interactive) return rect;
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<VRC.SDK3.Components.VRCUiShape>();
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(px.x, px.y, 4f);
            return rect;
        }

        /// <summary>Dark card background for a tile type (0 start, 1 coins, 2 lucky loop, 3 challenge, 4 power, 5 mystery, 6 portal).</summary>
        private static Color CardColor(int type, int value)
        {
            switch (type)
            {
                case 0: return Hex("5A4510");
                case 1: return value < 0 ? Hex("5A1420") : Hex("4A3A10");
                case 2: return Hex("4A1A44");
                case 3: return Hex("0E3A4A");
                case 4: return Hex("3A1D5A");
                case 5: return Hex("134A2E");
                default: return Hex("1C2A5A");
            }
        }

        /// <summary>Bright accent colour for a tile type.</summary>
        private static Color TileColor(int type, int value)
        {
            switch (type)
            {
                case 0: return Hex("FFD23F");
                case 1: return value < 0 ? Hex("FF4D5E") : Hex("FFE14D");
                case 2: return Hex("FF3DCB");
                case 3: return Hex("00E5FF");
                case 4: return Hex("B07CFF");
                case 5: return Hex("3DFF8A");
                default: return Hex("4D8BFF");
            }
        }

        private static string TileWord(int type)
        {
            switch (type)
            {
                case 0: return "LOOP START";
                case 1: return "COINS";
                case 2: return "LUCKY LOOP";
                case 3: return "CHALLENGE";
                case 4: return "POWER";
                case 5: return "MYSTERY";
                default: return "PORTAL";
            }
        }

        private static string TileSub(int type, int value, int lapBonus)
        {
            switch (type)
            {
                case 0: return "+" + lapBonus + " EVERY LAP";
                case 1: return (value > 0 ? "+" : "") + value + " COINS";
                case 2: return "SCRATCH A TICKET";
                case 3: return "MINI-GAME";
                case 4: return "POWER-UP";
                case 5: return "RANDOM EVENT";
                default: return "WARP AHEAD";
            }
        }

        /// <summary>Custom card art: Assets/LoopLand/Space Art/NN.png (space number 00-39) or "Space Name.png".</summary>
        private static Sprite SpaceArt(int index, string spaceName)
        {
            string folder = Root + "/Space Art";
            foreach (string g in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                string file = Path.GetFileNameWithoutExtension(path);
                if (!file.StartsWith(index.ToString("00")) && !string.Equals(file, spaceName, StringComparison.OrdinalIgnoreCase)) continue;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null && importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
            return null;
        }

        private static Image UImg(Transform parent, string name, Vector2 pos, Vector2 size, Sprite sprite, Color color, bool sliced = true, bool raycast = false)
        {
            var img = URect(parent, name, pos, size).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sliced && sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            img.pixelsPerUnitMultiplier = 1f;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        private static Sprite StoreArt(string prefix, int index) => FindArt(Root + "/Store Art", prefix + "_" + index);

        /// <summary>Finds an image by exact file name (no extension) in a folder and makes sure it imports as a Sprite.</summary>
        private static Sprite FindArt(string folderPath, string fileName)
        {
            if (!AssetDatabase.IsValidFolder(folderPath)) return null;
            foreach (string g in AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!string.Equals(Path.GetFileNameWithoutExtension(path), fileName, StringComparison.OrdinalIgnoreCase)) continue;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null && importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
            return null;
        }

        // ------------------------------------------------------------------ infinity path

        /// <summary>Figure-8: two round lobes (centers at +-LobeC) joined by straight lanes that cross at the origin.</summary>
        private static void BuildPath()
        {
            pathPts = new List<Vector2>();
            float a = Mathf.Acos(LobeR / LobeC);
            var lowR = new Vector2(LobeC + LobeR * Mathf.Cos(Mathf.PI + a), LobeR * Mathf.Sin(Mathf.PI + a));
            var upR = new Vector2(LobeC + LobeR * Mathf.Cos(Mathf.PI - a), LobeR * Mathf.Sin(Mathf.PI - a));
            var lowL = new Vector2(-LobeC + LobeR * Mathf.Cos(-a), LobeR * Mathf.Sin(-a));
            var upL = new Vector2(-LobeC + LobeR * Mathf.Cos(a), LobeR * Mathf.Sin(a));
            AddLine(Vector2.zero, lowR, 60);
            AddArc(LobeC, Mathf.PI + a, 3f * Mathf.PI - a, 480);
            AddLine(upR, lowL, 120);
            AddArc(-LobeC, -a, -(2f * Mathf.PI - a), 480);
            AddLine(upL, Vector2.zero, 60);
            pathPts.Add(Vector2.zero);
            pathLen = new List<float> { 0f };
            for (int i = 1; i < pathPts.Count; i++) pathLen.Add(pathLen[i - 1] + Vector2.Distance(pathPts[i], pathPts[i - 1]));
        }

        private static void AddLine(Vector2 a, Vector2 b, int steps)
        {
            for (int i = 0; i < steps; i++) pathPts.Add(Vector2.Lerp(a, b, i / (float)steps));
        }

        private static void AddArc(float cx, float a0, float a1, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                float t = Mathf.Lerp(a0, a1, i / (float)steps);
                pathPts.Add(new Vector2(cx + LobeR * Mathf.Cos(t), LobeR * Mathf.Sin(t)));
            }
        }

        private static void PathAt(float s, out Vector2 p, out Vector2 tangent)
        {
            float total = pathLen[pathLen.Count - 1];
            s = Mathf.Repeat(s, total);
            int lo = 0, hi = pathLen.Count - 1;
            while (hi - lo > 1)
            {
                int m = (lo + hi) / 2;
                if (pathLen[m] <= s) lo = m; else hi = m;
            }
            float seg = Mathf.Max(1e-6f, pathLen[lo + 1] - pathLen[lo]);
            p = Vector2.Lerp(pathPts[lo], pathPts[lo + 1], (s - pathLen[lo]) / seg);
            tangent = (pathPts[lo + 1] - pathPts[lo]).normalized;
        }

        private static Color RibbonColor(float v)
        {
            float f = Mathf.Clamp01(v) * (RibbonHex.Length - 1);
            int i = Mathf.Min((int)f, RibbonHex.Length - 2);
            return Color.Lerp(Hex(RibbonHex[i]), Hex(RibbonHex[i + 1]), f - i);
        }

        private static Mesh RibbonMesh(float width)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var cols = new List<Color>();
            var tris = new List<int>();
            float total = pathLen[pathLen.Count - 1];
            for (int i = 0; i < pathPts.Count; i++)
            {
                Vector2 t = (pathPts[Mathf.Min(i + 1, pathPts.Count - 1)] - pathPts[Mathf.Max(i - 1, 0)]).normalized;
                Vector2 n = new Vector2(-t.y, t.x) * (width * 0.5f);
                Vector2 p = pathPts[i];
                verts.Add(new Vector3(p.x + n.x, 0f, p.y + n.y));
                verts.Add(new Vector3(p.x - n.x, 0f, p.y - n.y));
                float v = pathLen[i] / total;
                uvs.Add(new Vector2(0f, v * 40f));
                uvs.Add(new Vector2(1f, v * 40f));
                Color c = RibbonColor(v);
                cols.Add(c);
                cols.Add(c);
                if (i == 0) continue;
                int b = verts.Count - 4;
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }
            var mesh = new Mesh { name = "LoopLand Infinity Ribbon" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Dir(Gen + "/Meshes");
            string path = Gen + "/Meshes/LoopLand_Ribbon.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Material RibbonMaterial()
        {
            const int w = 64;
            var tex = new Texture2D(w, 4, TextureFormat.RGBA32, false);
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float edge = Mathf.Exp(-Mathf.Pow((u - 0.07f) / 0.045f, 2f)) + Mathf.Exp(-Mathf.Pow((u - 0.93f) / 0.045f, 2f));
                float a = Mathf.Clamp01(0.22f + edge);
                for (int y = 0; y < 4; y++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            Texture2D ribbonTex = SavePng("Ribbon", tex);
            string path = Gen + "/Materials/Ribbon.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                Dir(Gen + "/Materials");
                m = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.mainTexture = ribbonTex;
            m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Glowing button with optional icon on the left and an optional ">" arrow on the right. Returns its label.</summary>
        private static TextMeshProUGUI NeonButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, Color color, Sprite icon,
            UdonSharpBehaviour target, string evt, float fontSize, bool arrow)
        {
            UImg(parent, name + " Glow", pos, size + new Vector2(30f, 30f), LoopLandArt.Glow, new Color(Mathf.Lerp(color.r, 1f, 0.3f), Mathf.Lerp(color.g, 1f, 0.3f), Mathf.Lerp(color.b, 1f, 0.3f), 0.95f));
            TextMeshProUGUI t = UButton(parent, name, label, pos, size, color, target, evt, fontSize);
            Image gloss = UImg(t.transform.parent, "Gloss", new Vector2(0f, size.y * 0.2f), new Vector2(size.x - 18f, size.y * 0.45f), LoopLandArt.Round, new Color(1f, 1f, 1f, 0.1f));
            gloss.transform.SetSiblingIndex(0);
            if (icon != null)
            {
                float isz = Mathf.Min(size.y * 0.62f, 90f);
                Color tint = icon == LoopLandArt.Coin ? Color.white : Color.Lerp(color, Color.white, 0.75f);
                UImg(t.transform.parent, "Icon", new Vector2(-size.x * 0.5f + isz * 0.5f + 24f, 0f), new Vector2(isz, isz), icon, tint, false);
                t.rectTransform.anchoredPosition = new Vector2(isz * 0.5f + 8f, 0f);
                t.rectTransform.sizeDelta = new Vector2(size.x - isz - 70f, size.y - 12f);
            }
            if (arrow)
            {
                UText(t.transform.parent, "Arrow", "<b>></b>", new Vector2(size.x * 0.5f - 40f, 0f), new Vector2(50f, size.y), fontSize, Color.white);
                t.rectTransform.sizeDelta -= new Vector2(60f, 0f);
            }
            return t;
        }

        private static TextMeshProUGUI Fixed(TextMeshProUGUI t)
        {
            t.enableAutoSizing = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        private static Color Lighter(Color c) => new Color(Mathf.Lerp(c.r, 1f, 0.3f), Mathf.Lerp(c.g, 1f, 0.3f), Mathf.Lerp(c.b, 1f, 0.3f), 0.95f);

        /// <summary>Square menu button: icon on top, small label underneath (or a big glyph when there's no icon).</summary>
        private static TextMeshProUGUI MenuButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, Color color, Sprite icon, UdonSharpBehaviour target, string evt)
        {
            UImg(parent, name + " Glow", pos, size + new Vector2(14f, 14f), LoopLandArt.Glow, Lighter(color));
            TextMeshProUGUI t = UButton(parent, name, label, pos, size, color, target, evt, icon == null ? 56f : 24f);
            Image gloss = UImg(t.transform.parent, "Gloss", new Vector2(0f, size.y * 0.2f), new Vector2(size.x - 14f, size.y * 0.45f), LoopLandArt.Round, new Color(1f, 1f, 1f, 0.1f));
            gloss.transform.SetSiblingIndex(0);
            if (icon == null) return t;
            float isz = size.y * 0.44f;
            UImg(t.transform.parent, "Icon", new Vector2(0f, size.y * 0.17f), new Vector2(isz, isz), icon, icon == LoopLandArt.Coin ? Color.white : Color.Lerp(color, Color.white, 0.75f), false);
            t.rectTransform.anchoredPosition = new Vector2(0f, -size.y * 0.27f);
            t.rectTransform.sizeDelta = new Vector2(size.x - 16f, size.y * 0.36f);
            return t;
        }

        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayer(child.gameObject, layer);
        }

        private static RectTransform URect(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            return rect;
        }

        private static Image UImage(Transform parent, string name, Vector2 pos, Vector2 size, Color color, bool raycast = false)
        {
            var img = URect(parent, name, pos, size).gameObject.AddComponent<Image>();
            img.sprite = roundSprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        private static RawImage URaw(Transform parent, string name, Vector2 pos, Vector2 size, Texture tex)
        {
            var raw = URect(parent, name, pos, size).gameObject.AddComponent<RawImage>();
            raw.texture = tex;
            raw.raycastTarget = false;
            return raw;
        }

        private static TextMeshProUGUI UText(Transform parent, string name, string text, Vector2 pos, Vector2 size, float fontSize, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var t = URect(parent, name, pos, size).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.text = text;
            t.color = color;
            t.alignment = align;
            t.richText = true;
            t.raycastTarget = false;
            t.enableAutoSizing = true;
            t.fontSizeMax = fontSize;
            t.fontSizeMin = fontSize * 0.35f;
            t.fontSize = fontSize;
            return t;
        }

        /// <summary>Rounded UI button that calls SendCustomEvent(evt) on the target's UdonBehaviour. Returns its label.</summary>
        private static TextMeshProUGUI UButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, Color color, UdonSharpBehaviour target, string evt, float fontSize)
        {
            Image img = UImage(parent, name, pos, size, color, true);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
            ColorBlock cb = btn.colors;
            cb.normalColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            cb.highlightedColor = Color.white;
            cb.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            cb.selectedColor = cb.normalColor;
            cb.fadeDuration = 0.08f;
            btn.colors = cb;
            UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(target);
            UnityEventTools.AddStringPersistentListener(btn.onClick, ub.SendCustomEvent, evt);
            TextMeshProUGUI t = UText(img.transform, "Label", label, Vector2.zero, size - new Vector2(24f, 12f), fontSize, Color.white);
            t.fontStyle = FontStyles.Bold;
            return t;
        }

        /// <summary>Orthographic camera above the board rendering into a texture for the Live Board views.</summary>
        private static Texture LiveCamera(Transform root, out LoopLandCamera director)
        {
            Dir(Gen + "/Textures");
            string rtPath = Gen + "/Textures/LiveBoardWide.renderTexture";
            var liveTex = AssetDatabase.LoadAssetAtPath<RenderTexture>(rtPath);
            if (liveTex == null)
            {
                liveTex = new RenderTexture(LiveW, LiveH, 24, RenderTextureFormat.ARGB32) { name = "LiveBoardWide", antiAliasing = 2 };
                AssetDatabase.CreateAsset(liveTex, rtPath);
            }
            else if (liveTex.width != LiveW || liveTex.height != LiveH)
            {
                liveTex.Release();
                liveTex.width = LiveW;
                liveTex.height = LiveH;
                liveTex.antiAliasing = 2;
                EditorUtility.SetDirty(liveTex);
            }
            var pose = new GameObject("Live Camera Overview Pose").transform;
            pose.SetParent(root, false);
            pose.localPosition = new Vector3(0f, 6.0f, -3.2f);
            pose.localRotation = Quaternion.LookRotation(new Vector3(0f, TopY, 0f) - pose.localPosition, Vector3.up);
            var camGo = new GameObject("Live Board Camera");
            camGo.transform.SetParent(root, false);
            camGo.transform.localPosition = pose.localPosition;
            camGo.transform.localRotation = pose.localRotation;
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = false;
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 25f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex("0A0914");
            cam.cullingMask = 1; // Default layer only: the board, tokens, dice and effects (all UI panels live on TransparentFX)
            cam.depth = -10f;
            cam.allowHDR = false;
            cam.useOcclusionCulling = false;
            cam.targetTexture = liveTex;
            director = UdonSharpUndo.AddComponent<LoopLandCamera>(camGo);
            director.cam = cam;
            director.overviewPose = pose;
            made.Add(director);
            return liveTex;
        }

        private static AudioSource Audio(GameObject go)
        {
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.spatialBlend = 0.7f;
            a.minDistance = 2f;
            a.maxDistance = 25f;
            a.rolloffMode = AudioRolloffMode.Linear;
            return a;
        }

        private static Transform TokenBody(Transform parent, Material bodyMat, Material glowMat, float scale, out Renderer[] body, out Renderer[] glow)
        {
            var b = new GameObject("Body").transform;
            b.SetParent(parent, false);
            b.localScale = Vector3.one * scale;
            var basePart = Prim(PrimitiveType.Cylinder, "Base", b, new Vector3(0f, 0.008f, 0f), new Vector3(0.07f, 0.008f, 0.07f), bodyMat);
            var stem = Prim(PrimitiveType.Capsule, "Stem", b, new Vector3(0f, 0.05f, 0f), new Vector3(0.042f, 0.036f, 0.042f), bodyMat);
            var head = Prim(PrimitiveType.Sphere, "Head", b, new Vector3(0f, 0.102f, 0f), Vector3.one * 0.05f, glowMat);
            var ring = Prim(PrimitiveType.Cylinder, "Ring", b, new Vector3(0f, 0.018f, 0f), new Vector3(0.082f, 0.002f, 0.082f), glowMat);
            body = new[] { basePart.GetComponent<Renderer>(), stem.GetComponent<Renderer>() };
            glow = new[] { head.GetComponent<Renderer>(), ring.GetComponent<Renderer>() };
            return b;
        }

        private static ParticleSystem Fx(string name, Transform parent, Vector3 lpos, Quaternion lrot, float life, float speed, float size, float rateTime, float rateDist,
            bool loop, bool world, ParticleSystemShapeType shape, float radius, float gravity, int max, Color c1, Color c2, float orbit)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = lpos;
            go.transform.localRotation = lrot;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = loop;
            main.playOnAwake = loop;
            main.duration = loop ? 5f : 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startColor = new ParticleSystem.MinMaxGradient(c1, c2);
            main.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.gravityModifier = gravity;
            main.maxParticles = max;
            var em = ps.emission;
            em.rateOverTime = rateTime;
            em.rateOverDistance = rateDist;
            var sh = ps.shape;
            sh.shapeType = shape;
            sh.radius = radius;
            if (shape == ParticleSystemShapeType.Circle) sh.radiusThickness = 0.1f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0f)));
            if (orbit != 0f)
            {
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.Local;
                vel.orbitalX = new ParticleSystem.MinMaxCurve(0f);
                vel.orbitalY = new ParticleSystem.MinMaxCurve(0f);
                vel.orbitalZ = new ParticleSystem.MinMaxCurve(orbit);
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = fxMat;
            if (loop) ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ helpers: assets

        internal static void Dir(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Dir(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static Material Std(string name, Color c, float metal, float smooth, Color emission, Texture tex = null)
        {
            Dir(Gen + "/Materials");
            string path = Gen + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(Shader.Find("Standard"));
            m.color = c;
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_Glossiness", smooth);
            if (tex != null) m.mainTexture = tex;
            if (emission.maxColorComponent > 0.001f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission);
                if (tex != null) m.SetTexture("_EmissionMap", tex);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else m.DisableKeyword("_EMISSION");
            if (isNew) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
            return m;
        }

        private static Material AddMat(string name, Color tint)
        {
            Dir(Gen + "/Materials");
            string path = Gen + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
            m.mainTexture = dot;
            m.SetColor("_TintColor", tint);
            if (isNew) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
            return m;
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

        private static Texture2D DotTexture()
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
            return SavePng("Particle_Dot", tex);
        }

        private static Material[] BuildDiceMaterials(out string[] names, out Color[] glow)
        {
            names = new[] { "Classic Ivory", "Midnight Onyx", "Neon Pulse", "Ruby Ember", "Frost Crystal", "Royal Gold", "Galaxy Holo", "Inferno Plasma" };
            string[] body = DiceBodyHex, pip = DicePipHex, glowHex = DiceGlowHex;
            float[] metal = { 0f, 0.3f, 0.1f, 0.2f, 0.1f, 1f, 0.3f, 0.1f };
            float[] emit = { 0f, 0f, 1.2f, 0.15f, 0.2f, 0.1f, 0.8f, 1.3f };
            var mats = new Material[names.Length];
            glow = new Color[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                glow[i] = Hex(glowHex[i]);
                Texture2D atlas = DiceAtlas("Dice_" + i, Hex(body[i]), Hex(pip[i]), i == 6);
                mats[i] = Std("Dice_" + i, Color.white, metal[i], 0.92f, Color.white * emit[i], atlas);
            }
            return mats;
        }

        private static Texture2D DiceAtlas(string name, Color body, Color pip, bool speckle)
        {
            const int f = 128;
            var tex = new Texture2D(f * 3, f * 2, TextureFormat.RGBA32, true);
            Vector2[][] pips =
            {
                new[] { new Vector2(0.5f, 0.5f) },
                new[] { new Vector2(0.27f, 0.27f), new Vector2(0.73f, 0.73f) },
                new[] { new Vector2(0.27f, 0.27f), new Vector2(0.5f, 0.5f), new Vector2(0.73f, 0.73f) },
                new[] { new Vector2(0.27f, 0.27f), new Vector2(0.73f, 0.27f), new Vector2(0.27f, 0.73f), new Vector2(0.73f, 0.73f) },
                new[] { new Vector2(0.27f, 0.27f), new Vector2(0.73f, 0.27f), new Vector2(0.5f, 0.5f), new Vector2(0.27f, 0.73f), new Vector2(0.73f, 0.73f) },
                new[] { new Vector2(0.27f, 0.25f), new Vector2(0.73f, 0.25f), new Vector2(0.27f, 0.5f), new Vector2(0.73f, 0.5f), new Vector2(0.27f, 0.75f), new Vector2(0.73f, 0.75f) }
            };
            Color edge = Color.Lerp(body, Color.black, 0.35f);
            for (int v = 0; v < 6; v++)
            {
                int ox = (v % 3) * f, oy = (v / 3) * f;
                for (int y = 0; y < f; y++)
                    for (int x = 0; x < f; x++)
                    {
                        var uv = new Vector2((x + 0.5f) / f, (y + 0.5f) / f);
                        float e = Mathf.Max(Mathf.Abs(uv.x - 0.5f), Mathf.Abs(uv.y - 0.5f)) * 2f;
                        Color c = Color.Lerp(body, edge, Mathf.Clamp01((e - 0.8f) / 0.2f));
                        c = Color.Lerp(c, Color.white, Mathf.Clamp01(0.12f - uv.y * 0.12f + uv.x * 0.05f));
                        if (speckle && rng.NextDouble() < 0.012) c = Color.Lerp(c, Color.white, 0.8f);
                        foreach (Vector2 pc in pips[v])
                        {
                            float d = Vector2.Distance(uv, pc);
                            c = Color.Lerp(c, pip, Mathf.Clamp01((0.095f - d) / 0.012f));
                        }
                        c.a = 1f;
                        tex.SetPixel(ox + x, oy + y, c);
                    }
            }
            tex.Apply();
            return SavePng(name, tex);
        }

        private static Mesh DiceMesh()
        {
            Dir(Gen + "/Meshes");
            string path = Gen + "/Meshes/LoopLand_Die.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            // value -> outward normal (must match LoopLandDice._FaceUp)
            Vector3[] n = { Vector3.up, Vector3.forward, Vector3.right, Vector3.left, Vector3.back, Vector3.down };
            Vector3[] up = { Vector3.forward, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.forward };
            for (int v = 0; v < 6; v++)
            {
                Vector3 c = n[v] * 0.5f, u = up[v] * 0.5f, r = Vector3.Cross(n[v], up[v]) * 0.5f;
                int b = verts.Count;
                verts.Add(c - r - u); verts.Add(c - r + u); verts.Add(c + r + u); verts.Add(c + r - u);
                float u0 = (v % 3) / 3f, v0 = (v / 3) / 2f;
                uvs.Add(new Vector2(u0, v0)); uvs.Add(new Vector2(u0, v0 + 0.5f)); uvs.Add(new Vector2(u0 + 1f / 3f, v0 + 0.5f)); uvs.Add(new Vector2(u0 + 1f / 3f, v0));
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            var mesh = new Mesh { name = "LoopLand Die" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static AudioClip Wav(string name, float dur, Func<float, float> f)
        {
            Dir(Gen + "/Audio");
            string path = Gen + "/Audio/" + name + ".wav";
            var existing = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (existing != null) return existing;
            const int sr = 44100;
            int count = (int)(sr * dur);
            using (var bw = new BinaryWriter(File.Create(path)))
            {
                bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(36 + count * 2);
                bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                bw.Write(16);
                bw.Write((short)1);
                bw.Write((short)1);
                bw.Write(sr);
                bw.Write(sr * 2);
                bw.Write((short)2);
                bw.Write((short)16);
                bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                bw.Write(count * 2);
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)sr;
                    float fade = Mathf.Clamp01((dur - t) / 0.015f);
                    bw.Write((short)(Mathf.Clamp(f(t) * fade, -1f, 1f) * 30000f));
                }
            }
            AssetDatabase.ImportAsset(path);
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
