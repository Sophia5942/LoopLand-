using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
            Material panel = Std("Panel", Hex("0D0C1A"), 0.2f, 0.95f, Hex("05040C"));
            Material tileMat = Std("Tile", Hex("1C1A30"), 0.1f, 0.75f, Hex("06050E"));
            Material frameCyan = AddMat("Glow_Cyan", Hex("00B8D4"));
            Material frameGold = AddMat("Glow_Gold", Hex("C9A227"));
            Material white = Std("OwnerBar", Color.white, 0f, 0.6f, Color.white);
            Material loopMat = Std("Loop", Hex("3DFF8A"), 0.2f, 0.8f, Hex("1A8040"));
            Material towerMat = Std("Tower", Hex("FFD54A"), 0.9f, 0.85f, Hex("806010"));
            Material btnCyan = Std("Btn_Cyan", Hex("0B5563"), 0.2f, 0.9f, Hex("00B8D4") * 0.6f);
            Material btnPink = Std("Btn_Pink", Hex("5A1048"), 0.2f, 0.9f, Hex("FF3DCB") * 0.5f);
            Material btnGold = Std("Btn_Gold", Hex("5E4708"), 0.6f, 0.9f, Hex("FFD54A") * 0.5f);
            Material btnDark = Std("Btn_Dark", Hex("24223A"), 0.2f, 0.9f, Hex("4D4A80") * 0.4f);

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

            // table
            Prim(PrimitiveType.Cylinder, "Table Top", rt, new Vector3(0f, TopY - 0.025f, 0f), new Vector3(5.6f, 0.025f, 5.6f), dark, true);
            Prim(PrimitiveType.Cylinder, "Table Rim Glow", rt, new Vector3(0f, TopY - 0.03f, 0f), new Vector3(5.68f, 0.012f, 5.68f), Std("Rim", Hex("00B8D4"), 0f, 0.5f, Hex("00E5FF") * 1.5f));
            Prim(PrimitiveType.Cylinder, "Table Pedestal", rt, new Vector3(0f, 0.42f, 0f), new Vector3(1.3f, 0.42f, 1.3f), dark, true);
            Prim(PrimitiveType.Cylinder, "Center Disc", rt, new Vector3(0f, TopY + 0.002f, 0f), new Vector3(3.4f, 0.002f, 3.4f), Std("CenterDisc", Hex("120F26"), 0.3f, 0.95f, Hex("1A0F3A")));

            // board
            var board = new GameObject("Board").transform;
            board.SetParent(rt, false);
            float tileW = 2f * Mathf.PI * R / 40f * 0.95f;
            var anchors = new Transform[40];
            var ownerBars = new Renderer[40];
            var markers = new GameObject[200];
            Color[] groupCol = new Color[game.groupHex.Length];
            for (int g = 0; g < groupCol.Length; g++) groupCol[g] = Hex(game.groupHex[g]);
            var groupMats = new Material[groupCol.Length];
            for (int g = 0; g < groupCol.Length; g++) groupMats[g] = Std("Group_" + g, groupCol[g], 0.2f, 0.85f, groupCol[g] * 0.9f);
            Material goMat = Std("Tile_Go", Hex("3A2E08"), 0.6f, 0.9f, Hex("FFD54A") * 0.35f);
            Material jailMat = Std("Tile_Glitch", Hex("2A0F3A"), 0.3f, 0.9f, Hex("B07CFF") * 0.35f);
            Material freeMat = Std("Tile_Chill", Hex("0E2A1C"), 0.3f, 0.9f, Hex("3DFF8A") * 0.3f);
            Material goJailMat = Std("Tile_Glitched", Hex("3A0E14"), 0.3f, 0.9f, Hex("FF4D5E") * 0.35f);
            Material cardMat = Std("Tile_Card", Hex("2B1430"), 0.3f, 0.9f, Hex("FF5FBF") * 0.2f);

            for (int i = 0; i < 40; i++)
            {
                float a = (-90f - 9f * i) * Mathf.Deg2Rad;
                Vector3 p = new Vector3(Mathf.Cos(a) * R, TopY + 0.02f, Mathf.Sin(a) * R);
                var anchor = new GameObject("Space " + i.ToString("00") + " " + game.spaceName[i]).transform;
                anchor.SetParent(board, false);
                anchor.localPosition = p;
                anchor.localRotation = Quaternion.LookRotation(new Vector3(-p.x, 0f, -p.z).normalized, Vector3.up);
                anchors[i] = anchor;

                int type = game.spaceType[i];
                int grp = game.spaceGroup[i];
                Material m = type == 0 ? goMat : type == 7 ? jailMat : type == 8 ? freeMat : type == 9 ? goJailMat : (type == 5 || type == 6) ? cardMat : tileMat;
                GameObject tile = Prim(PrimitiveType.Cube, "Tile", anchor, new Vector3(0f, -0.01f, 0f), new Vector3(tileW, 0.02f, 0.52f), m, true);
                var tb = UdonSharpUndo.AddComponent<LoopLandButton>(tile);
                tb.target = game;
                tb.eventName = "_OnTile";
                tb.arg = i;
                Done(tb, game.spaceName[i], 5f);

                bool corner = type == 0 || type == 7 || type == 8 || type == 9;
                if (grp >= 0) Prim(PrimitiveType.Cube, "Color", anchor, new Vector3(0f, 0.003f, 0.2f), new Vector3(tileW * 0.94f, 0.006f, 0.1f), groupMats[grp]);
                Text(anchor, "Name", game.spaceName[i], new Vector3(0f, 0.004f, corner ? 0.05f : 0.02f), Quaternion.Euler(90f, 0f, 0f), new Vector2(tileW * 0.9f, corner ? 0.3f : 0.2f), corner ? 0.4f : 0.28f, Color.white);
                string sub = (type == 1 || type == 2 || type == 3) ? "$" + game.spacePrice[i] : type == 4 ? "PAY $" + game.spacePrice[i] : type == 0 ? "COLLECT $200" : type == 5 ? "?" : type == 6 ? "CHEST" : "";
                if (sub.Length > 0) Text(anchor, "Price", sub, new Vector3(0f, 0.004f, -0.17f), Quaternion.Euler(90f, 0f, 0f), new Vector2(tileW * 0.9f, 0.08f), 0.22f, Hex("9AF2FF"));

                if (type == 1 || type == 2 || type == 3)
                {
                    GameObject bar = Prim(PrimitiveType.Cube, "Owner", anchor, new Vector3(0f, 0.004f, -0.235f), new Vector3(tileW * 0.9f, 0.006f, 0.03f), white);
                    ownerBars[i] = bar.GetComponent<Renderer>();
                    ownerBars[i].enabled = false;
                }
                if (type == 1)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        markers[i * 5 + k] = Prim(PrimitiveType.Cube, "Loop " + (k + 1), anchor, new Vector3(-0.105f + k * 0.07f, 0.026f, 0.2f), Vector3.one * 0.04f, loopMat);
                        markers[i * 5 + k].SetActive(false);
                    }
                    markers[i * 5 + 4] = Prim(PrimitiveType.Cube, "Tower", anchor, new Vector3(0f, 0.046f, 0.2f), new Vector3(0.075f, 0.08f, 0.075f), towerMat);
                    markers[i * 5 + 4].SetActive(false);
                }
            }
            GameObject sel = Prim(PrimitiveType.Cube, "Selection", rt, Vector3.zero, Vector3.one, AddMat("Glow_Select", Hex("FFE14D")));
            var selChild = sel.transform;
            selChild.localScale = Vector3.one;
            Object.DestroyImmediate(sel.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(sel.GetComponent<MeshFilter>());
            Prim(PrimitiveType.Cube, "Frame", selChild, new Vector3(0f, 0.001f, 0f), new Vector3(tileW + 0.03f, 0.004f, 0.55f), AddMat("Glow_Select", Hex("FFE14D")));

            // center hologram
            Fx("Loop Ring", rt, new Vector3(0f, TopY + 0.03f, 0f), Quaternion.Euler(-90f, 0f, 0f), 2.6f, 0f, 0.035f, 70f, 0f, true, false, ParticleSystemShapeType.Circle, 1.6f, 0f, 400, Hex("00E5FF"), Hex("FF3DCB"), 0.35f);
            var holo = new GameObject("Hologram").transform;
            holo.SetParent(rt, false);
            holo.localPosition = new Vector3(0f, 1.95f, 0f);
            var status = new TextMeshPro[4];
            var players = new TextMeshPro[4];
            var cards = new TextMeshPro[4];
            for (int d = 0; d < 4; d++)
            {
                Vector3 dir = Quaternion.Euler(0f, d * 90f, 0f) * Vector3.back;
                var face = new GameObject("Screen " + d).transform;
                face.SetParent(holo, false);
                face.localPosition = dir * 0.66f;
                face.localRotation = Quaternion.LookRotation(-dir, Vector3.up);
                Prim(PrimitiveType.Cube, "Glass", face, new Vector3(0f, 0f, 0.012f), new Vector3(1.22f, 0.98f, 0.02f), panel);
                Prim(PrimitiveType.Cube, "Frame", face, new Vector3(0f, 0f, 0.022f), new Vector3(1.27f, 1.03f, 0.01f), frameCyan);
                status[d] = Text(face, "Status", "LOOPLAND", new Vector3(0f, 0.22f, -0.003f), Quaternion.identity, new Vector2(1.12f, 0.5f), 0.75f, Color.white);
                players[d] = Text(face, "Players", "", new Vector3(0f, -0.26f, -0.003f), Quaternion.identity, new Vector2(1.12f, 0.42f), 0.45f, Color.white, TextAlignmentOptions.Left);
                cards[d] = Text(face, "Card", "", new Vector3(0f, -0.68f, -0.01f), Quaternion.identity, new Vector2(1.2f, 0.3f), 0.5f, Hex("FFE14D"));
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
                rest.localPosition = new Vector3(d == 0 ? -0.12f : 0.12f, TopY + 0.058f, d == 0 ? 0.06f : -0.06f);
                var thr = new GameObject("Throw " + d).transform;
                thr.SetParent(diceRoot, false);
                thr.localPosition = new Vector3(d == 0 ? -0.55f : 0.45f, TopY + 0.6f, -1.0f);
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

            // consoles
            var consoles = new GameObject("Consoles").transform;
            consoles.SetParent(rt, false);
            var info = new TextMeshPro[4];
            var prim = new TextMeshPro[4];
            var sec = new TextMeshPro[4];
            var rounds = new TextMeshPro[4];
            for (int d = 0; d < 4; d++)
            {
                Vector3 dir = Quaternion.Euler(0f, d * 90f, 0f) * Vector3.back;
                var c = new GameObject("Console " + d).transform;
                c.SetParent(consoles, false);
                c.localPosition = dir * 2.62f + Vector3.up * (TopY + 0.02f);
                c.localRotation = Quaternion.LookRotation(-dir, Vector3.up);
                var face = new GameObject("Face").transform;
                face.SetParent(c, false);
                face.localPosition = new Vector3(0f, 0.17f, 0f);
                face.localRotation = Quaternion.Euler(35f, 0f, 0f);
                Prim(PrimitiveType.Cube, "Back", face, new Vector3(0f, 0f, 0.014f), new Vector3(1.12f, 0.58f, 0.02f), panel);
                Prim(PrimitiveType.Cube, "Frame", face, new Vector3(0f, 0f, 0.024f), new Vector3(1.16f, 0.62f, 0.01f), frameCyan);
                info[d] = Text(face, "Info", "", new Vector3(0f, 0.18f, -0.004f), Quaternion.identity, new Vector2(1.04f, 0.19f), 0.32f, Color.white);
                prim[d] = Button(face, "Primary", "JOIN GAME", new Vector3(-0.26f, 0.0f, 0f), new Vector2(0.5f, 0.13f), btnCyan, game, "_OnPrimary", 0, "Main action");
                sec[d] = Button(face, "Secondary", "-", new Vector3(0.26f, 0.0f, 0f), new Vector2(0.5f, 0.13f), btnPink, game, "_OnSecondary", 0, "Second action");
                Button(face, "Build", "BUILD", new Vector3(-0.42f, -0.18f, 0f), new Vector2(0.2f, 0.1f), btnDark, game, "_OnBuild", 0, "Build a Loop on the selected space");
                Button(face, "Sell", "SELL", new Vector3(-0.21f, -0.18f, 0f), new Vector2(0.2f, 0.1f), btnDark, game, "_OnSell", 0, "Sell an upgrade");
                Button(face, "Mortgage", "MORTGAGE", new Vector3(0f, -0.18f, 0f), new Vector2(0.2f, 0.1f), btnDark, game, "_OnMortgage", 0, "Mortgage / unmortgage");
                rounds[d] = Button(face, "Rounds", "ROUNDS", new Vector3(0.21f, -0.18f, 0f), new Vector2(0.2f, 0.1f), btnDark, game, "_OnRounds", 0, "Change the round limit (lobby)");
                Button(face, "Reset", "RESET", new Vector3(0.42f, -0.18f, 0f), new Vector2(0.2f, 0.1f), btnPink, game, "_OnReset", 0, "Reset the game (press twice)");
            }

            // store kiosk
            Transform st = storeGo.transform;
            AudioSource storeAudio = Audio(storeGo);
            Prim(PrimitiveType.Cube, "Stage", st, new Vector3(0f, 0.03f, -0.25f), new Vector3(4.4f, 0.06f, 1.5f), dark, true);
            Prim(PrimitiveType.Cube, "Back", st, new Vector3(0f, 1.45f, 0.05f), new Vector3(2.75f, 1.8f, 0.06f), panel, true);
            Prim(PrimitiveType.Cube, "Frame", st, new Vector3(0f, 1.45f, 0.09f), new Vector3(2.83f, 1.88f, 0.02f), frameGold);
            Text(st, "Title", "<b>LOOPLAND <color=#FFE14D>STORE</color></b>", new Vector3(0f, 2.2f, -0.01f), Quaternion.identity, new Vector2(2.5f, 0.22f), 2f, Color.white);
            store.coinsText = Text(st, "Coins", "", new Vector3(0f, 2.0f, -0.01f), Quaternion.identity, new Vector2(2.5f, 0.15f), 1.1f, Hex("FFE14D"));
            var tabs = new TextMeshPro[4];
            string[] tabNames = { "DICE", "TOKENS", "TRAILS", "PREMIUM" };
            for (int i = 0; i < 4; i++)
                tabs[i] = Button(st, "Tab " + tabNames[i], tabNames[i], new Vector3(-0.93f + i * 0.62f, 1.8f, 0f), new Vector2(0.56f, 0.12f), i == 3 ? btnGold : btnDark, store, "_OnTab", i, tabNames[i]);
            store.tabLabels = tabs;
            var itemRoots = new GameObject[8];
            var itemLabels = new TextMeshPro[8];
            var swatches = new Renderer[8];
            Material swatchMat = Std("Swatch", Color.white, 0.3f, 0.9f, Color.white);
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = new Vector3(-0.95f + (i % 4) * 0.633f, i < 4 ? 1.53f : 1.25f, 0f);
                itemLabels[i] = Button(st, "Item " + i, "", p, new Vector2(0.6f, 0.25f), btnDark, store, "_OnItem", i, "Preview item");
                itemRoots[i] = itemLabels[i].transform.parent.parent.gameObject;
                swatches[i] = Prim(PrimitiveType.Cube, "Swatch", itemLabels[i].transform.parent, new Vector3(-0.26f, 0.09f, -0.016f), new Vector3(0.045f, 0.045f, 0.01f), swatchMat).GetComponent<Renderer>();
            }
            store.itemButtons = itemRoots;
            store.itemLabels = itemLabels;
            store.itemSwatches = swatches;
            store.detailText = Text(st, "Detail", "", new Vector3(-0.33f, 0.93f, -0.01f), Quaternion.identity, new Vector2(1.9f, 0.36f), 0.5f, Color.white, TextAlignmentOptions.Left);
            store.actionLabel = Button(st, "Action", "SELECT AN ITEM", new Vector3(0.97f, 0.93f, 0f), new Vector2(0.62f, 0.18f), btnGold, store, "_OnAction", 0, "Buy / Equip");
            Button(st, "Daily", "DAILY BONUS", new Vector3(-0.9f, 0.62f, 0f), new Vector2(0.62f, 0.12f), btnCyan, store, "_OnDaily", 0, "Claim your daily Loop Coins");
            Button(st, "World Store", "WORLD STORE", new Vector3(0f, 0.62f, 0f), new Vector2(0.62f, 0.12f), btnGold, store, "_OnWorldStore", 0, "Open this world's VRChat store");
            Prim(PrimitiveType.Cylinder, "Preview Pedestal", st, new Vector3(1.85f, 0.45f, -0.45f), new Vector3(0.45f, 0.45f, 0.45f), Std("Pedestal", Hex("2A2208"), 0.9f, 0.9f, Hex("FFD54A") * 0.2f), true);
            var spin = new GameObject("Preview Spinner").transform;
            spin.SetParent(st, false);
            spin.localPosition = new Vector3(1.85f, 1.12f, -0.45f);
            var pdie = new GameObject("Preview Die");
            pdie.transform.SetParent(spin, false);
            pdie.transform.localPosition = new Vector3(-0.12f, 0.06f, 0f);
            pdie.transform.localScale = Vector3.one * 0.16f;
            pdie.transform.localRotation = Quaternion.Euler(20f, 35f, 10f);
            pdie.AddComponent<MeshFilter>().sharedMesh = dieMesh;
            store.previewDie = pdie.AddComponent<MeshRenderer>();
            store.previewDie.sharedMaterial = diceMats[0];
            var ptok = new GameObject("Preview Token").transform;
            ptok.SetParent(spin, false);
            ptok.localPosition = new Vector3(0.14f, -0.12f, 0f);
            ptok.localScale = Vector3.one * 2.4f;
            TokenBody(ptok, tokenBody, tokenGlow, 1f, out Renderer[] pbr, out Renderer[] pgr);
            var previewR = new Renderer[pbr.Length + pgr.Length];
            pbr.CopyTo(previewR, 0);
            pgr.CopyTo(previewR, pbr.Length);
            store.previewToken = previewR;
            store.previewTrail = Fx("Preview FX", spin, Vector3.zero, Quaternion.identity, 1.2f, 0.4f, 0.04f, 0f, 0f, false, true, ParticleSystemShapeType.Sphere, 0.18f, -0.1f, 200, Color.white, Color.white, 0f);
            store.previewSpinner = spin;
            store.sfx = storeAudio;
            store.clickClip = Wav("click", 0.06f, t => Sin(1800f, t) * Env(t, 0.001f, 0.015f) * 0.5f);
            store.buyClip = Wav("coin", 0.5f, t => Notes(t, new[] { 988f, 1319f }, 0.08f, 0.25f));
            store.errorClip = Wav("error", 0.25f, t => Notes(t, new[] { 220f, 196f }, 0.1f, 0.06f));

            // store catalogue
            store.game = game;
            store.diceNames = diceNames;
            store.dicePrices = new[] { 0, 150, 300, 450, 600, 0, 0, 0 };
            store.diceProduct = new[] { -1, -1, -1, -1, -1, 0, 0, 2 };
            store.diceMaterials = diceMats;
            store.diceGlow = diceGlow;
            store.tokenNames = new[] { "Classic Glow", "Chrome", "Toxic", "Lava", "Diamond", "Prism" };
            store.tokenPrices = new[] { 0, 200, 350, 500, 0, 0 };
            store.tokenProduct = new[] { -1, -1, -1, -1, 1, 1 };
            store.tokenColors = new[] { Color.white, Hex("D8DEE9"), Hex("7CFF4F"), Hex("FF5A1F"), Hex("BFF6FF"), Hex("FF3DCB") };
            store.rainbowToken = 5;
            store.trailNames = new[] { "Stardust", "Comet", "Ember", "Golden", "Rainbow" };
            store.trailPrices = new[] { 0, 250, 400, 0, 0 };
            store.trailProduct = new[] { -1, -1, -1, 2, 2 };
            store.trailColors = new[] { Color.white, Hex("4DB8FF"), Hex("FF7A2E"), Hex("FFD54A"), Hex("FF3DCB") };
            store.rainbowTrail = 4;
            store.productNames = new[] { "Premium Dice Pack", "Holo Token Pack", "LoopLand VIP", "Coin Pouch", "Coin Vault" };
            store.productDescriptions = new[]
            {
                "Unlocks Royal Gold and Galaxy Holo dice forever.",
                "Unlocks the Diamond and animated Prism tokens forever.",
                "2x Loop Coins from games and daily bonus, Inferno Plasma dice, Golden and Rainbow trails.",
                "+500 Loop Coins instantly. Buy as many as you like.",
                "+3000 Loop Coins instantly. Best value!"
            };
            store.productListingIds = new[] { "", "", "", "", "" };
            store.productPriceLabels = new[] { "Credits", "Credits", "Credits", "Credits", "Credits" };
            store.productCoins = new[] { 0, 0, 0, 500, 3000 };
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
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
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

        private static TextMeshPro Button(Transform parent, string name, string label, Vector3 lpos, Vector2 size, Material cap, UdonSharpBehaviour target, string evt, int arg, string interact)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = lpos;
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(size.x, size.y, 0.05f);
            var vis = new GameObject("Visual").transform;
            vis.SetParent(root.transform, false);
            Prim(PrimitiveType.Cube, "Cap", vis, Vector3.zero, new Vector3(size.x, size.y, 0.025f), cap);
            TextMeshPro tmp = Text(vis, "Label", label, new Vector3(0f, 0f, -0.0135f), Quaternion.identity, size * 0.9f, size.y * 3.2f, Color.white);
            var b = UdonSharpUndo.AddComponent<LoopLandButton>(root);
            b.target = target;
            b.eventName = evt;
            b.arg = arg;
            b.pressVisual = vis;
            Done(b, interact, 3f);
            return tmp;
        }

        private static void Done(UdonSharpBehaviour b, string interact, float proximity)
        {
            made.Add(b);
            UdonBehaviour ub = UdonSharpEditorUtility.GetBackingUdonBehaviour(b);
            if (ub == null) return;
            ub.interactText = interact;
            ub.proximity = proximity;
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

        private static void Dir(string path)
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
            string[] body = { "F2EEE4", "15151C", "0B0F1E", "B0102A", "BFE9FF", "E8B730", "2A0E5E", "FF5A00" };
            string[] pip = { "1A1A22", "E8E8F0", "00F0FF", "FFE3E8", "1B4E8C", "3A2500", "FFFFFF", "FFF3B0" };
            string[] glowHex = { "FFFFFF", "8A8AFF", "00F0FF", "FF4060", "9AF2FF", "FFD54A", "B07CFF", "FF7A00" };
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
