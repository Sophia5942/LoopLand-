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
            Type[] types = { typeof(LoopLandGame), typeof(LoopLandStore), typeof(LoopLandToken), typeof(LoopLandDice), typeof(LoopLandButton) };
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
            var ownerBars = new Image[40];
            var markers = new Image[200];
            Color[] groupCol = new Color[game.groupHex.Length];
            for (int g = 0; g < groupCol.Length; g++) groupCol[g] = Hex(game.groupHex[g]);
            Dir(Root + "/Space Art");
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
                int grp = game.spaceGroup[i];
                bool corner = type == 0 || type == 7 || type == 8 || type == 9;
                RectTransform card = URect(boardUi, "Card " + i.ToString("00") + " " + game.spaceName[i], p2 * 1000f, new Vector2(cardW, cardH));
                card.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-n2.x, n2.y) * Mathf.Rad2Deg);
                Image bg = card.gameObject.AddComponent<Image>();
                bg.raycastTarget = false;
                Sprite art = SpaceArt(i, game.spaceName[i]);
                if (art != null) { bg.sprite = art; bg.color = Color.white; }
                else
                {
                    bg.sprite = roundSprite;
                    bg.type = Image.Type.Sliced;
                    bg.pixelsPerUnitMultiplier = 1f;
                    bg.color = CardColor(type);
                }
                if (grp >= 0) UImage(card, "Band", new Vector2(0f, 200f), new Vector2(cardW - 16f, 84f), groupCol[grp]);
                UText(card, "Name", game.spaceName[i], new Vector2(0f, corner ? 20f : 40f), new Vector2(cardW - 30f, corner ? 260f : 170f), corner ? 46f : 36f, Color.white).fontStyle = FontStyles.Bold;
                string sub = (type == 1 || type == 2 || type == 3) ? "$" + game.spacePrice[i] : type == 4 ? "PAY $" + game.spacePrice[i] : type == 0 ? "COLLECT $200" : type == 5 ? "?" : type == 6 ? "CHEST" : "";
                if (sub.Length > 0) UText(card, "Price", sub, new Vector2(0f, -165f), new Vector2(cardW - 30f, 60f), 30f, Hex("9AF2FF"));
                if (type == 1 || type == 2 || type == 3)
                {
                    ownerBars[i] = UImage(card, "Owner", new Vector2(0f, -226f), new Vector2(cardW - 40f, 24f), Color.white);
                    ownerBars[i].enabled = false;
                }
                if (type == 1)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        markers[i * 5 + k] = UImg(card, "Loop " + (k + 1), new Vector2(-96f + k * 64f, 200f), new Vector2(50f, 50f), LoopLandArt.House, Color.white, false);
                        markers[i * 5 + k].gameObject.SetActive(false);
                    }
                    markers[i * 5 + 4] = UImg(card, "Tower", new Vector2(0f, 205f), new Vector2(70f, 70f), LoopLandArt.Tower, Color.white, false);
                    markers[i * 5 + 4].gameObject.SetActive(false);
                }
            }
            GameObject sel = Prim(PrimitiveType.Cube, "Selection", rt, Vector3.zero, Vector3.one, AddMat("Glow_Select", Hex("FFE14D")));
            var selChild = sel.transform;
            selChild.localScale = Vector3.one;
            Object.DestroyImmediate(sel.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(sel.GetComponent<MeshFilter>());
            Prim(PrimitiveType.Cube, "Frame", selChild, new Vector3(0f, 0.001f, 0f), new Vector3(tileW + 0.03f, 0.004f, 0.53f), AddMat("Glow_Select", Hex("FFE14D")));

            // center hologram
            Texture liveTex = LiveCamera(rt);
            var holo = new GameObject("Hologram").transform;
            holo.SetParent(rt, false);
            holo.localPosition = new Vector3(0f, 2.0f, 0f);
            var status = new TMP_Text[4];
            var players = new TMP_Text[4];
            var cards = new TMP_Text[4];
            for (int d = 0; d < 4; d++)
            {
                Vector3 dir = Quaternion.Euler(0f, 45f + d * 90f, 0f) * Vector3.back;
                RectTransform c = UCanvas(holo, "Screen " + d, dir * 0.8f, Quaternion.LookRotation(-dir, Vector3.up), new Vector2(1500f, 1100f));
                UImg(c, "Glow", Vector2.zero, new Vector2(1560f, 1160f), LoopLandArt.Glow, new Color(0f, 0.9f, 1f, 0.9f));
                UImg(c, "Panel", Vector2.zero, new Vector2(1500f, 1100f), LoopLandArt.Panel, Color.white);
                UImg(c, "Live Frame", new Vector2(0f, 175f), new Vector2(1462f, 742f), LoopLandArt.Round, new Color(1f, 0.24f, 0.8f, 0.9f));
                URaw(c, "Live View", new Vector2(0f, 175f), new Vector2(1440f, 720f), liveTex);
                UImg(c, "Live Tag", new Vector2(-590f, 500f), new Vector2(200f, 56f), LoopLandArt.Pill, new Color(0.9f, 0.1f, 0.3f, 0.95f));
                UText(c, "Live Tag Text", "<b>LIVE</b>", new Vector2(-590f, 500f), new Vector2(180f, 50f), 34f, Color.white);
                cards[d] = UText(c, "Card", "", new Vector2(0f, -248f), new Vector2(1420f, 86f), 40f, Hex("FFE14D"));
                UImg(c, "Status Back", new Vector2(-365f, -420f), new Vector2(710f, 230f), LoopLandArt.Round, new Color(0f, 0f, 0f, 0.3f));
                status[d] = UText(c, "Status", "LOOPLAND", new Vector2(-365f, -420f), new Vector2(680f, 215f), 40f, Color.white);
                UImg(c, "Players Back", new Vector2(365f, -420f), new Vector2(710f, 230f), LoopLandArt.Round, new Color(0f, 0f, 0f, 0.3f));
                players[d] = UText(c, "Players", "", new Vector2(365f, -420f), new Vector2(680f, 215f), 32f, Color.white, TextAlignmentOptions.Left);
            }
            var spinner = new GameObject("Logo Spinner").transform;
            spinner.SetParent(rt, false);
            spinner.localPosition = new Vector3(0f, 2.7f, 0f);
            for (int s = 0; s < 2; s++)
                Text(spinner, "Logo", "<b>LOOP<color=#FF3DCB>LAND</color></b>", Vector3.zero, Quaternion.Euler(0f, s * 180f, 0f), new Vector2(1.8f, 0.4f), 2.4f, Hex("00E5FF"));
            Fx("Logo Ring", spinner, Vector3.zero, Quaternion.Euler(-90f, 0f, 0f), 1.8f, 0f, 0.03f, 30f, 0f, true, false, ParticleSystemShapeType.Circle, 0.95f, 0f, 200, Hex("FFE14D"), Hex("00E5FF"), 0.5f);
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

            // consoles: clean world-space UI panels at the table edge
            var consoles = new GameObject("Consoles").transform;
            consoles.SetParent(rt, false);
            var info = new TMP_Text[4];
            var prim = new TMP_Text[4];
            var sec = new TMP_Text[4];
            var rounds = new TMP_Text[4];
            for (int d = 0; d < 4; d++)
            {
                int side = d < 2 ? 1 : -1;
                float ang = (d % 2 == 0 ? -50f : 50f) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(side * Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var c = new GameObject("Console " + d).transform;
                c.SetParent(consoles, false);
                c.localPosition = new Vector3(side * LobeC, 0f, 0f) + dir * 1.88f + Vector3.up * (TopY + 0.02f);
                c.localRotation = Quaternion.LookRotation(-dir, Vector3.up);
                RectTransform ui = UCanvas(c, "Panel", new Vector3(0f, 0.26f, 0f), Quaternion.Euler(35f, 0f, 0f), new Vector2(1100f, 640f));
                UImage(ui, "Glow", Vector2.zero, new Vector2(1124f, 664f), new Color(0f, 0.9f, 1f, 0.5f));
                UImage(ui, "Back", Vector2.zero, new Vector2(1100f, 640f), new Color(0.035f, 0.03f, 0.07f, 0.95f));
                UImage(ui, "Info Back", new Vector2(0f, 222f), new Vector2(1060f, 160f), new Color(1f, 1f, 1f, 0.05f));
                info[d] = UText(ui, "Info", "", new Vector2(0f, 222f), new Vector2(1030f, 150f), 30f, Color.white);
                prim[d] = UButton(ui, "Primary", "JOIN GAME", new Vector2(-265f, 62f), new Vector2(510f, 120f), cCyan, game, "_OnPrimary", 44f);
                sec[d] = UButton(ui, "Secondary", "-", new Vector2(265f, 62f), new Vector2(510f, 120f), cPink, game, "_OnSecondary", 40f);
                UButton(ui, "Prev Space", "< SPACE", new Vector2(-397f, -82f), new Vector2(245f, 95f), cDark, game, "_OnPrevSpace", 30f);
                UButton(ui, "Next Space", "SPACE >", new Vector2(-132f, -82f), new Vector2(245f, 95f), cDark, game, "_OnNextSpace", 30f);
                UButton(ui, "Build", "BUILD", new Vector2(132f, -82f), new Vector2(245f, 95f), cDark, game, "_OnBuild", 30f);
                UButton(ui, "Sell", "SELL", new Vector2(397f, -82f), new Vector2(245f, 95f), cDark, game, "_OnSell", 30f);
                UButton(ui, "Mortgage", "MORTGAGE", new Vector2(-397f, -200f), new Vector2(245f, 95f), cDark, game, "_OnMortgage", 30f);
                UButton(ui, "My Space", "MY SPACE", new Vector2(-132f, -200f), new Vector2(245f, 95f), cDark, game, "_OnMySpace", 30f);
                rounds[d] = UButton(ui, "Rounds", "ROUNDS", new Vector2(132f, -200f), new Vector2(245f, 95f), cDark, game, "_OnRounds", 28f);
                UButton(ui, "Reset", "RESET", new Vector2(397f, -200f), new Vector2(245f, 95f), cRed, game, "_OnReset", 30f);
            }

            // store kiosk: premium store UI + live board panel
            Transform st = storeGo.transform;
            AudioSource storeAudio = Audio(storeGo);
            Dir(Root + "/Store Art");
            Prim(PrimitiveType.Cube, "Stage", st, new Vector3(0f, 0.03f, -0.25f), new Vector3(6.6f, 0.06f, 1.5f), dark, true);
            RectTransform sui = UCanvas(st, "Store UI", new Vector3(0f, 1.5f, 0f), Quaternion.identity, new Vector2(2700f, 1800f));
            UImg(sui, "Glow", Vector2.zero, new Vector2(2790f, 1890f), LoopLandArt.Glow, new Color(0.35f, 0.6f, 1f, 1f));
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
            store.vipText = UText(sui, "VIP", "", new Vector2(1000f, 750f), new Vector2(500f, 100f), 70f, Hex("FFD54A"));

            string[] tabNames = { "DICE", "TOKENS", "BUILDINGS", "TRAILS", "PREMIUM" };
            Sprite[] tabIcons = { LoopLandArt.IconDice, LoopLandArt.IconPawn, LoopLandArt.IconBuilding, LoopLandArt.IconSparkle, LoopLandArt.IconCrown };
            var tabs = new TMP_Text[5];
            var tabSel = new GameObject[5];
            for (int i = 0; i < 5; i++)
            {
                var pos = new Vector2(-1040f + i * 520f, 560f);
                tabSel[i] = UImg(sui, "Tab Glow " + i, pos, new Vector2(540f, 150f), LoopLandArt.Glow, new Color(1f, 0.8f, 0.25f, 1f)).gameObject;
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
                itemSel[i] = UImg(sui, "Item Glow " + i, pos, new Vector2(400f, 440f), LoopLandArt.Glow, new Color(1f, 0.8f, 0.25f, 1f)).gameObject;
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
            UImg(lui, "Glow", Vector2.zero, new Vector2(1460f, 920f), LoopLandArt.Glow, new Color(0f, 0.9f, 1f, 0.9f));
            UImg(lui, "Back", Vector2.zero, new Vector2(1400f, 860f), LoopLandArt.Panel, Color.white);
            UText(lui, "Header", "<b>LIVE <color=#FF3DCB>BOARD</color></b>", new Vector2(0f, 360f), new Vector2(1300f, 100f), 64f, Hex("00E5FF"));
            URaw(lui, "Live View", new Vector2(0f, -50f), new Vector2(1320f, 660f), liveTex);
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
                "A clean and modern skyline to get your city started.",
                "Neon towers that light up every property you build on.",
                "Sunny beachfront resorts with ocean views.",
                "Eco towers topped with lush rooftop gardens.",
                "Crimson towers glowing with ember light.",
                "Gilded skyscrapers for the ultimate landlord.",
                "Holographic towers from a distant galaxy.",
                "Fiery plasma towers that burn bright."
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
                "2x Loop Coins from games and the daily bonus, plus Inferno Plasma dice and buildings and the Golden and Rainbow trails.",
                "+500 Loop Coins instantly. Buy as many as you like.",
                "+3000 Loop Coins instantly. Best value!",
                "Unlocks the Royal Gold and Galaxy Holo building styles forever."
            };
            store.productListingIds = new[] { "", "", "", "", "", "" };
            store.productPriceLabels = new[] { "Credits", "Credits", "Credits", "Credits", "Credits", "Credits" };
            store.productCoins = new[] { 0, 0, 0, 500, 3000, 0 };
            store.productArt = new Sprite[6];
            for (int i = 0; i < 6; i++) store.productArt[i] = StoreArt("premium", i) ?? LoopLandArt.ProductThumb("Thumb_Premium_" + i, i);
            store.vipProduct = 2;

            // game wiring
            game.store = store;
            game.tokens = tokens;
            game.dice = dice;
            game.spaceAnchors = anchors;
            game.ownerBars = ownerBars;
            game.buildMarkers = markers;
            game.selectionMarker = selChild;
            game.spinner = spinner;
            game.statusTexts = status;
            game.playerTexts = players;
            game.cardTexts = cards;
            game.infoTexts = info;
            game.primaryLabels = prim;
            game.secondaryLabels = sec;
            game.roundsLabels = rounds;
            game.celebrateFx = confetti;
            game.moneyFx = money;
            game.sfx = gameAudio;
            game.slotColors = slotCols;
            game.slotHex = SlotHex;
            game.fxClips = new[]
            {
                store.clickClip,
                store.buyClip,
                Wav("rent", 0.45f, t => Notes(t, new[] { 784f, 659f, 523f }, 0.09f, 0.15f)),
                Wav("glitch", 0.6f, t => Mathf.Sign(Sin(110f, t)) * 0.22f * (0.6f + 0.4f * Sin(18f, t)) * Env(t, 0.01f, 0.25f)),
                Wav("card", 0.5f, t => Notes(t, new[] { 1047f, 1319f, 1568f, 2093f }, 0.06f, 0.2f)),
                Wav("win", 1.4f, t => Notes(t, new[] { 523f, 659f, 784f, 1047f, 784f, 1047f }, 0.14f, 0.35f)),
                Wav("build", 0.4f, t => Notes(t, new[] { 659f, 880f, 1175f }, 0.07f, 0.15f)),
                Wav("bankrupt", 0.9f, t => Sin(400f - 250f * t, t) * Env(t, 0.01f, 0.4f) * 0.5f),
                Wav("start", 0.6f, t => Notes(t, new[] { 392f, 523f, 659f, 784f }, 0.08f, 0.2f)),
                store.buyClip
            };
            AudioClip roll = Wav("dice_roll", 0.9f, t => Noise() * Env(t % 0.07f, 0.001f, 0.012f) * 0.6f * (1f - t / 0.9f));
            AudioClip clack = Wav("dice_land", 0.25f, t => (Noise() * 0.6f + Sin(900f, t) * 0.3f) * (Env(t, 0.001f, 0.02f) + (t > 0.09f ? Env(t - 0.09f, 0.001f, 0.02f) : 0f)));
            foreach (LoopLandDice dc in dice) { dc.rollClip = roll; dc.landClip = clack; }
            return root;
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

        private static Color CardColor(int type)
        {
            switch (type)
            {
                case 0: return Hex("5A4510");
                case 4: return Hex("2A2A38");
                case 5: return Hex("4A1A44");
                case 6: return Hex("4A3A10");
                case 7: return Hex("3A1D5A");
                case 8: return Hex("134A2E");
                case 9: return Hex("5A1420");
                default: return Hex("1C1A30");
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
        private static Texture LiveCamera(Transform root)
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
            var camGo = new GameObject("Live Board Camera");
            camGo.transform.SetParent(root, false);
            camGo.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            camGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 1.75f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 1.2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex("0A0914");
            cam.cullingMask = 1;
            cam.depth = -10f;
            cam.allowHDR = false;
            cam.useOcclusionCulling = false;
            cam.targetTexture = liveTex;
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
