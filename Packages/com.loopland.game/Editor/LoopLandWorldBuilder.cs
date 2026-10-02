using System;
using System.Collections.Generic;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Image = UnityEngine.UI.Image;
using Object = UnityEngine.Object;
using static LoopLand.EditorTools.LoopLandBuilder;

namespace LoopLand.EditorTools
{
    /// <summary>
    /// LoopLand > Build LoopLand Tower World: a stepped glass tower with the LOOPLAND sign, a neon infinity logo and a halo,
    /// crowned by the infinity-shaped Loop Deck where the game is played. A glass elevator rides from the plaza to the deck.
    /// Around it: a plaza with trees, lamps, planters, billboards and signs, a water ring with four bridges, a city skyline,
    /// floating islands, clouds and a twilight sky. Everything is generated into Assets/LoopLand/Generated.
    /// </summary>
    public static class LoopLandWorldBuilder
    {
        private const string Gen = "Assets/LoopLand/Generated";
        private const string WorldName = "LoopLand World";
        private const float H = 60f;        // Loop Deck height: the game sits here
        private const float DeckC = 4.6f;   // deck lobes are centred at x = ±DeckC
        private const float DeckR = 6f;     // deck lobe radius
        private const float ShaftX = 11.9f; // elevator shaft, at the right end of the deck
        private static readonly string[] RainbowHex = { "FFE14D", "7CFF4F", "00E5FF", "4D8BFF", "B07CFF", "FF3DCB", "FF8A3D", "FFE14D" };
        private static readonly float[] FacadeGlow = { 1.1f, 0.9f, 0.9f, 0.8f };

        private static readonly List<UdonSharpBehaviour> made = new List<UdonSharpBehaviour>();
        private static readonly Dictionary<string, Material> facadeMats = new Dictionary<string, Material>();
        private static System.Random rnd;
        private static Texture2D[] facadeTex;
        private static Mesh haloMesh;
        private static Material mDark, mMetal, mStone, mPaving, mGrass, mIslandGrass, mWater, mTrunk, mLeaf, mLeaf2, mBlossom, mRock, mGlass, mCloud;
        private static Material mCyan, mPink, mGold, mPurple, mWhiteGlow, mNeon, mRibbon;

        [MenuItem("LoopLand/Build LoopLand Tower World", priority = 10)]
        public static void BuildWorld()
        {
            if (!LoopLandBuilder.Prepare())
            {
                if (EditorUtility.DisplayDialog("LoopLand", "TextMesh Pro Essential Resources are missing.\nImport them, then run LoopLand > Build LoopLand Tower World again.", "Import now", "Cancel"))
                    EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources");
                return;
            }
            GameObject old = GameObject.Find(WorldName);
            if (old != null)
            {
                if (!EditorUtility.DisplayDialog("LoopLand", "The LoopLand Tower World is already in this scene. Rebuild it? (The game itself is kept.)", "Rebuild", "Cancel")) return;
                Undo.DestroyObjectImmediate(old);
            }
            if (GameObject.Find("LoopLand") == null) LoopLandBuilder.Build();
            GameObject game = GameObject.Find("LoopLand");
            if (game == null) return;

            Undo.SetCurrentGroupName("Build LoopLand Tower World");
            int undoGroup = Undo.GetCurrentGroup();
            made.Clear();
            facadeMats.Clear();
            rnd = new System.Random(2024);
            try
            {
                EditorUtility.DisplayProgressBar("LoopLand", "Raising the tower...", 0.2f);
                var world = new GameObject(WorldName).transform;
                Materials();
                Sky(world);
                EditorUtility.DisplayProgressBar("LoopLand", "Pouring the plaza and the water ring...", 0.35f);
                Ground(world);
                Tower(world);
                Deck(world);
                EditorUtility.DisplayProgressBar("LoopLand", "Installing the elevator...", 0.55f);
                Elevator(world);
                Plaza(world);
                EditorUtility.DisplayProgressBar("LoopLand", "Building the skyline...", 0.7f);
                City(world);
                Islands(world);
                Clouds(world);
                EditorUtility.DisplayProgressBar("LoopLand", "Wiring Udon behaviours...", 0.9f);
                foreach (UdonSharpBehaviour b in made) UdonSharpEditorUtility.CopyProxyToUdon(b);
                MarkStatic(world);
                Undo.RegisterCreatedObjectUndo(world.gameObject, "Build LoopLand Tower World");

                Undo.RecordObject(game.transform, "Move LoopLand to the Loop Deck");
                Transform store = game.transform.Find("Store");
                if (store != null) Undo.RecordObject(store, "Move LoopLand to the Loop Deck");
                PlaceGame(game);
                Spawn();
                foreach (string n in new[] { "LoopLand Floor", "LoopLand Light" })
                {
                    GameObject demo = GameObject.Find(n);
                    if (demo == null) continue;
                    Undo.RecordObject(demo, "Hide demo setup");
                    demo.SetActive(false);
                }
                EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
                AssetDatabase.SaveAssets();
                Selection.activeGameObject = world.gameObject;
                Debug.Log("[LoopLand] Tower world built! Spawn on the plaza, take the elevator up to the Loop Deck (" + H + " m) where the game is.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            Undo.CollapseUndoOperations(undoGroup);
        }

        /// <summary>Puts the game on the Loop Deck when the tower world is in the scene (also called after a game rebuild).</summary>
        internal static void PlaceGame(GameObject game)
        {
            if (game == null || GameObject.Find(WorldName) == null) return;
            game.transform.SetPositionAndRotation(new Vector3(0f, H, 0f), Quaternion.identity);
            Transform store = game.transform.Find("Store");
            if (store == null) return;
            // the store kiosk moves to the left end of the deck, facing the table
            store.localPosition = new Vector3(-9f, 0f, 0f);
            store.localRotation = Quaternion.Euler(0f, -90f, 0f);
        }

        // ------------------------------------------------------------------ materials and sky

        private static void Materials()
        {
            mDark = Std("World_Dark", Hex("14121E"), 0.6f, 0.85f, Color.black);
            mMetal = Std("World_Metal", Hex("3A3F52"), 0.85f, 0.7f, Color.black);
            mStone = Std("World_Stone", Hex("C9C2D6"), 0f, 0.35f, Color.black);
            mPaving = Std("World_Paving", Color.white, 0f, 0.3f, Color.black, PavingTex());
            mGrass = Std("World_Grass", Color.white, 0f, 0.15f, Color.black, GrassTex());
            mIslandGrass = Std("World_Island_Grass", Hex("5CC46A"), 0f, 0.2f, Color.black);
            mWater = Std("World_Water", Hex("1C6E9A"), 0.35f, 0.97f, Hex("0A3A55"));
            mTrunk = Std("World_Trunk", Hex("5A3B2A"), 0f, 0.2f, Color.black);
            mLeaf = Std("World_Leaf", Hex("3FA34D"), 0f, 0.25f, Color.black);
            mLeaf2 = Std("World_Leaf_2", Hex("2E8B57"), 0f, 0.25f, Color.black);
            mBlossom = Std("World_Blossom", Hex("FF9FD2"), 0f, 0.25f, Hex("3A1030"));
            mRock = Std("World_Rock", Hex("6B5F7A"), 0.05f, 0.2f, Color.black);
            mCloud = Std("World_Cloud", Hex("FFF4FA"), 0f, 0.05f, Hex("6A4A70"));
            mGlass = Glass("World_Glass", new Color(0.65f, 0.88f, 1f, 0.16f));
            mCyan = Std("Neon_Cyan", Hex("00E5FF"), 0f, 0.6f, Hex("00E5FF") * 2f);
            mPink = Std("Neon_Pink", Hex("FF3DCB"), 0f, 0.6f, Hex("FF3DCB") * 2f);
            mGold = Std("Neon_Gold", Hex("FFE14D"), 0f, 0.6f, Hex("FFE14D") * 2f);
            mPurple = Std("Neon_Purple", Hex("B07CFF"), 0f, 0.6f, Hex("B07CFF") * 2f);
            mWhiteGlow = Std("Neon_White", Color.white, 0f, 0.6f, new Color(1f, 0.92f, 0.8f) * 1.6f);
            mNeon = AddMat("Neon_Additive", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            mNeon.mainTexture = SolidTex();
            EditorUtility.SetDirty(mNeon);
            mRibbon = AssetDatabase.LoadAssetAtPath<Material>(Gen + "/Materials/Ribbon.mat");
            if (mRibbon == null) mRibbon = mNeon;
            facadeTex = new[]
            {
                WindowTex("Facade_LoopLand", Hex("1B2C5C"), Hex("BFF6FF"), Hex("FFE6B0"), 0.5f, 3),
                WindowTex("Facade_Teal", Hex("123F4A"), Hex("FFD27A"), Hex("FFF1D0"), 0.35f, 4),
                WindowTex("Facade_Violet", Hex("2A1A4A"), Hex("FF8AD8"), Hex("FFD0F0"), 0.35f, 5),
                WindowTex("Facade_Steel", Hex("22334A"), Hex("E8F0FF"), Hex("A8D8FF"), 0.3f, 6)
            };
            haloMesh = Torus("World_Halo", 1f, 0.035f, 120, 12);
        }

        private static void Sky(Transform root)
        {
            string path = Gen + "/Materials/World_Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(sky, path);
            }
            sky.DisableKeyword("_SUNDISK_NONE");
            sky.DisableKeyword("_SUNDISK_SIMPLE");
            sky.EnableKeyword("_SUNDISK_HIGH_QUALITY");
            sky.SetFloat("_SunDisk", 2f);
            sky.SetFloat("_SunSize", 0.05f);
            sky.SetFloat("_SunSizeConvergence", 4f);
            sky.SetFloat("_AtmosphereThickness", 1.35f);
            sky.SetColor("_SkyTint", new Color(0.78f, 0.5f, 0.9f));
            sky.SetColor("_GroundColor", new Color(0.3f, 0.22f, 0.36f));
            sky.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;

            // low twilight sun from behind the spawn, so the tower front is lit
            var sunGo = new GameObject("LoopLand Sun");
            sunGo.transform.SetParent(root, false);
            sunGo.transform.rotation = Quaternion.Euler(20f, 30f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.82f, 0.72f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.5f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.46f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.17f, 0.26f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.66f, 0.52f, 0.74f);
            RenderSettings.fogStartDistance = 90f;
            RenderSettings.fogEndDistance = 360f;
            DynamicGI.UpdateEnvironment();

            Fx("Sky Motes", root, new Vector3(0f, 30f, 0f), Quaternion.identity, 9f, 0.25f, 0.35f, 35f, 0f, true, true, ParticleSystemShapeType.Sphere, 45f, -0.005f, 400, Hex("00E5FF"), Hex("FF3DCB"), 0f);
        }

        // ------------------------------------------------------------------ ground: plaza island, water ring, bridges

        private static void Ground(Transform root)
        {
            var g = Group(root, "Ground");
            MeshObj(g, "Outer Ground", Annulus("World_Ground", 31.4f, 115f, 96, 4f), mGrass, Vector3.zero, Quaternion.identity, true);
            MeshObj(g, "Plaza", Annulus("World_Plaza", 0f, 26f, 96, 2f), mPaving, Vector3.zero, Quaternion.identity, true);
            MeshObj(g, "Plaza Edge", Band("World_Plaza_Edge", Circle(26f, 96), true, -0.8f, 0f, null), mStone, Vector3.zero, Quaternion.identity, false);
            MeshObj(g, "Pool Edge", Band("World_Pool_Edge", Circle(31.4f, 96), true, -0.8f, 0f, null), mStone, Vector3.zero, Quaternion.identity, false);
            MeshObj(g, "Water", Annulus("World_Water", 0f, 31.6f, 96, 4f), mWater, new Vector3(0f, -0.2f, 0f), Quaternion.identity, true);

            for (int i = 0; i < 4; i++)
            {
                // each bridge runs along its local +Z, from the plaza (r 24.5) over the water to the park (r 33)
                var b = Group(g, "Bridge " + (i + 1));
                b.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
                Prim(PrimitiveType.Cube, "Bridge Deck", b, new Vector3(0f, -0.07f, 28.75f), new Vector3(4.6f, 0.26f, 8.5f), mStone, true);
                for (int s = -1; s <= 1; s += 2)
                {
                    Prim(PrimitiveType.Cube, "Bridge Glass", b, new Vector3(s * 2.25f, 0.53f, 28.75f), new Vector3(0.06f, 0.9f, 8.5f), mGlass, true);
                    Prim(PrimitiveType.Cube, "Bridge Rail", b, new Vector3(s * 2.25f, 1f, 28.75f), new Vector3(0.1f, 0.06f, 8.5f), i % 2 == 0 ? mCyan : mPink);
                    Prim(PrimitiveType.Cube, "Bridge Post", b, new Vector3(s * 2.25f, 0.5f, 24.55f), new Vector3(0.16f, 1.05f, 0.16f), mMetal, true);
                    Prim(PrimitiveType.Cube, "Bridge Post", b, new Vector3(s * 2.25f, 0.5f, 32.95f), new Vector3(0.16f, 1.05f, 0.16f), mMetal, true);
                    Lamp(b, new Vector3(s * 2.8f, 0f, 33.6f));
                }
            }
        }

        // ------------------------------------------------------------------ tower

        private static void Tower(Transform root)
        {
            var t = Group(root, "Tower");
            Prim(PrimitiveType.Cube, "Podium", t, new Vector3(0f, 3f, 0f), new Vector3(18f, 6f, 18f), Facade(0, 18f, 6f), true);
            Prim(PrimitiveType.Cube, "Podium Roof", t, new Vector3(0f, 6.1f, 0f), new Vector3(18.4f, 0.3f, 18.4f), mDark, true);
            Prim(PrimitiveType.Cube, "Podium Glow", t, new Vector3(0f, 5.88f, 0f), new Vector3(18.25f, 0.12f, 18.25f), mCyan);

            // three stepped glass blocks, each with a glowing crown band and neon corners
            float[] w = { 12f, 10f, 8f };
            float[] y0 = { 6f, 26f, 46f };
            float[] y1 = { 26f, 46f, 58f };
            Material[] band = { mCyan, mPink, mGold };
            for (int i = 0; i < 3; i++)
            {
                float hgt = y1[i] - y0[i], cy = (y0[i] + y1[i]) * 0.5f;
                Prim(PrimitiveType.Cube, "Tower Block " + (i + 1), t, new Vector3(0f, cy, 0f), new Vector3(w[i], hgt, w[i]), Facade(0, w[i], hgt), true);
                Prim(PrimitiveType.Cube, "Tower Band " + (i + 1), t, new Vector3(0f, y1[i] - 0.2f, 0f), new Vector3(w[i] + 0.3f, 0.4f, w[i] + 0.3f), band[i]);
                for (int c = 0; c < 4; c++)
                {
                    float sx = (c & 1) == 0 ? -1f : 1f, sz = (c & 2) == 0 ? -1f : 1f;
                    Prim(PrimitiveType.Cube, "Corner Light", t, new Vector3(sx * w[i] * 0.5f, cy, sz * w[i] * 0.5f), new Vector3(0.22f, hgt - 0.6f, 0.22f), band[i]);
                }
            }
            Prim(PrimitiveType.Cylinder, "Tower Neck", t, new Vector3(0f, 59f, 0f), new Vector3(6f, 1f, 6f), mDark, true);

            // LOOPLAND sign (front and back) and the glowing infinity logo above it
            Mesh logo = Ribbon("World_Infinity_Logo", Lemniscate(3.2f, 160), true, 0.55f);
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;
                Quaternion face = Quaternion.Euler(0f, s * 180f, 0f);
                Prim(PrimitiveType.Cube, "Sign Frame", t, new Vector3(0f, 40f, side * 5.12f), new Vector3(9.5f, 3.6f, 0.3f), mPink);
                Prim(PrimitiveType.Cube, "Sign Panel", t, new Vector3(0f, 40f, side * 5.15f), new Vector3(9.2f, 3.3f, 0.3f), mDark);
                TextMeshPro sign = Text(t, "LOOPLAND Sign", "<b>LOOPLAND</b>", new Vector3(0f, 40f, side * 5.32f), face, new Vector2(8.6f, 2.7f), 30f, Color.white);
                sign.enableVertexGradient = true;
                sign.colorGradient = new VertexGradient(Hex("FFE14D"), Hex("00E5FF"), Hex("FF3DCB"), Hex("B07CFF"));
                sign.fontSharedMaterial = SignMaterial(sign);
                Prim(PrimitiveType.Cube, "Logo Panel", t, new Vector3(0f, 52f, side * 4.1f), new Vector3(7.6f, 3.6f, 0.2f), mDark);
                MeshObj(t, "Infinity Logo", logo, mRibbon, new Vector3(0f, 52f, side * 4.25f), Quaternion.Euler(-90f, 0f, 0f), false);
            }

            // halo ring around the top block (slowly cycling colors) with four struts
            GameObject halo = MeshObj(t, "Halo", haloMesh, mNeon, new Vector3(0f, 48f, 0f), Quaternion.identity, false);
            halo.transform.localScale = Vector3.one * 9.5f;
            Mover(halo, Vector3.zero, 0f, 0f, 0f, new Vector3(0f, 6f, 0f));
            for (int k = 0; k < 4; k++)
            {
                float a = 45f + k * 90f;
                Rod(t, "Halo Strut", Polar(5.6f, a) + Vector3.up * 48f, Polar(9.3f, a) + Vector3.up * 48f, 0.08f, mMetal);
            }

            // struts carrying the deck lobes out from the top block
            for (int s = -1; s <= 1; s += 2)
                for (int k = -1; k <= 1; k++)
                    Rod(t, "Deck Strut", new Vector3(s * 4f, 52f, k * 2.6f), new Vector3(s * (DeckC + 3.6f), H - 0.35f, k * 3.4f), 0.14f, mMetal);

            // billboards wrapped around the podium
            Billboard(t, "Podium Billboard", new Vector3(0f, 3.3f, -9.08f), Quaternion.identity, new Vector2(14000f, 4600f),
                "GOOD PEOPLE.\nBETTER PLACES.", LoopLandArt.LogoInfinity, Hex("00E5FF"));
            Billboard(t, "Podium Screen West", new Vector3(-9.08f, 3.3f, 0f), Quaternion.Euler(0f, 90f, 0f), new Vector2(12000f, 4600f),
                "ROLL. BUILD.\nLOOP AGAIN.", LoopLandArt.IconDice, Hex("FF3DCB"));
            Billboard(t, "Podium Screen North", new Vector3(0f, 3.3f, 9.08f), Quaternion.Euler(0f, 180f, 0f), new Vector2(12000f, 4600f),
                "SAME PEOPLE.\nNEW PLACES.", LoopLandArt.IconHeart, Hex("FFE14D"));
        }

        // ------------------------------------------------------------------ Loop Deck (the infinity on top, where the game sits)

        private static void Deck(Transform root)
        {
            var d = Group(root, "Loop Deck");
            Material deckMat = Std("World_Deck", Hex("221E33"), 0.45f, 0.85f, Color.black);
            Mesh disc = Annulus("World_Deck_Disc", 0f, DeckR, 96, 2f);
            Mesh rim = Band("World_Deck_Rim", Circle(DeckR, 96), true, -0.3f, 0f, null);
            for (int s = -1; s <= 1; s += 2)
            {
                // smooth lobes with a neon rim, so from the plaza the deck reads as a glowing infinity on top of the tower
                float drop = s < 0 ? 0f : 0.001f;
                MeshObj(d, "Deck Lobe", disc, deckMat, new Vector3(s * DeckC, H - drop, 0f), Quaternion.identity, true);
                MeshObj(d, "Deck Underside", disc, deckMat, new Vector3(s * DeckC, H - 0.3f - drop, 0f), Quaternion.Euler(180f, 0f, 0f), false);
                MeshObj(d, "Deck Rim", rim, s < 0 ? mPink : mCyan, new Vector3(s * DeckC, H, 0f), Quaternion.identity, false);
            }
            MeshObj(d, "Deck Infinity Inlay", Ribbon("World_Deck_Inlay", DeckOutline(DeckR - 0.7f, 0f), true, 0.3f), mRibbon, new Vector3(0f, H + 0.004f, 0f), Quaternion.identity, false);
            // glass railing with neon rails all the way round, open only where the elevator docks
            MeshObj(d, "Deck Railing", Band("World_Deck_Railing", DeckOutline(DeckR - 0.05f, 16f), false, 0f, 1.1f, Rainbow), mRibbon, new Vector3(0f, H, 0f), Quaternion.identity, true);
            Prim(PrimitiveType.Cube, "Elevator Landing", d, new Vector3(10.25f, H - 0.09f, 0f), new Vector3(1.3f, 0.2f, 3.2f), mMetal, true);
            for (int s = -1; s <= 1; s += 2)
                Prim(PrimitiveType.Cube, "Landing Glow", d, new Vector3(10.25f, H + 0.012f, s * 1.55f), new Vector3(1.3f, 0.01f, 0.06f), mGold);
        }

        // ------------------------------------------------------------------ elevator

        private static void Elevator(Transform root)
        {
            var e = Group(root, "Elevator");
            var lift = UdonSharpUndo.AddComponent<LoopLandElevator>(e.gameObject);
            made.Add(lift);
            var spots = new Transform[2];
            var doorsL = new Transform[2];
            var doorsR = new Transform[2];
            var displays = new TMP_Text[2];
            var audio = new AudioSource[2];
            var lights = new List<Renderer>();
            Color lightCol = new Color(0.75f, 0.95f, 1f) * 1.6f;
            Material wall = Std("Elevator_Wall", Hex("2B2748"), 0.6f, 0.8f, Color.black);
            Material door = Std("Elevator_Door", Hex("C8CCE0"), 0.95f, 0.8f, Color.black);
            Material light = Std("Elevator_Light", Color.white, 0f, 0.5f, lightCol);
            for (int i = 0; i < 2; i++)
            {
                var cab = Group(e, i == 0 ? "Cabin Plaza" : "Cabin Loop Deck");
                cab.localPosition = new Vector3(ShaftX, i == 0 ? 0f : H, 0f);
                cab.localRotation = Quaternion.Euler(0f, i == 0 ? 0f : 90f, 0f); // doors face the path below, the deck above
                Cabin(cab, i, lift, wall, door, light, out spots[i], out doorsL[i], out doorsR[i], out displays[i], lights);
                var a = cab.gameObject.AddComponent<AudioSource>();
                a.playOnAwake = false;
                a.spatialBlend = 1f;
                a.minDistance = 2f;
                a.maxDistance = 18f;
                a.rolloffMode = AudioRolloffMode.Linear;
                audio[i] = a;
            }
            lift.riderSpots = spots;
            lift.doorsLeft = doorsL;
            lift.doorsRight = doorsR;
            lift.displays = displays;
            lift.rideLights = lights.ToArray();
            lift.cabinAudio = audio;
            lift.lightColor = lightCol;
            lift.doorSlide = 0.72f;
            lift.topFloor = Mathf.RoundToInt(H);
            lift.floorNames = new[] { "PLAZA", "LOOP DECK" };
            lift.dingClip = Wav("elevator_ding", 1.4f, t => Notes(t, new[] { 1319f, 1047f }, 0.4f, 0.45f));
            lift.rideClip = Wav("elevator_ride", 3.2f, t => (Sin(55f + 25f * t, t) * 0.3f + Noise() * 0.07f) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 3.2f)));

            // glass shaft between the two cabins, neon corner rails and rings, and a glowing car gliding up and down
            float y0 = 4.2f, y1 = H - 0.12f, len = y1 - y0;
            Prim(PrimitiveType.Cube, "Shaft Glass", e, new Vector3(ShaftX, (y0 + y1) * 0.5f, 0f), new Vector3(3.4f, len, 3.4f), mGlass);
            for (int c = 0; c < 4; c++)
            {
                float sx = (c & 1) == 0 ? -1f : 1f, sz = (c & 2) == 0 ? -1f : 1f;
                Prim(PrimitiveType.Cube, "Shaft Rail", e, new Vector3(ShaftX + sx * 1.7f, (y0 + y1) * 0.5f, sz * 1.7f), new Vector3(0.1f, len, 0.1f), c % 3 == 0 ? mCyan : mPink);
            }
            for (float y = y0 + 6f; y < y1 - 2f; y += 7f)
                for (int s = -1; s <= 1; s += 2)
                {
                    Prim(PrimitiveType.Cube, "Shaft Ring", e, new Vector3(ShaftX + s * 1.7f, y, 0f), new Vector3(0.08f, 0.08f, 3.4f), mCyan);
                    Prim(PrimitiveType.Cube, "Shaft Ring", e, new Vector3(ShaftX, y, s * 1.7f), new Vector3(3.4f, 0.08f, 0.08f), mCyan);
                }
            Material carMat = Std("Elevator_Car", Hex("2A2550"), 0.6f, 0.9f, Hex("00E5FF") * 0.6f);
            GameObject car = Prim(PrimitiveType.Cube, "Elevator Car", e, new Vector3(ShaftX, y0 + 1.5f, 0f), new Vector3(2.9f, 2.6f, 2.9f), carMat);
            Prim(PrimitiveType.Cube, "Car Light", car.transform, new Vector3(0f, -0.48f, 0f), new Vector3(1.02f, 0.03f, 1.02f), mPink);
            Mover(car, new Vector3(0f, len - 3.2f, 0f), 22f, 0f, 0f, Vector3.zero);
        }

        /// <summary>One cabin in cabin space: doors on the -Z side, 3.2 m wide, 2.9 m tall, two sliding doors into side pockets.</summary>
        private static void Cabin(Transform cab, int i, LoopLandElevator lift, Material wall, Material door, Material light,
            out Transform spot, out Transform doorL, out Transform doorR, out TMP_Text display, List<Renderer> lights)
        {
            bool up = i == 0;
            Material accent = up ? mCyan : mPink;
            string evt = up ? "_GoUp" : "_GoDown";
            Prim(PrimitiveType.Cube, "Floor", cab, new Vector3(0f, -0.04f, 0f), new Vector3(3.2f, 0.12f, 2.76f), mMetal, true);
            Prim(PrimitiveType.Cube, "Ceiling", cab, new Vector3(0f, 2.95f, 0f), new Vector3(3.2f, 0.1f, 2.76f), wall, true);
            Prim(PrimitiveType.Cube, "Back Wall", cab, new Vector3(0f, 1.45f, 1.35f), new Vector3(3.2f, 2.9f, 0.06f), wall, true);
            for (int s = -1; s <= 1; s += 2)
            {
                Prim(PrimitiveType.Cube, "Side Wall", cab, new Vector3(s * 1.57f, 1.45f, 0f), new Vector3(0.06f, 2.9f, 2.76f), wall, true);
                Prim(PrimitiveType.Cube, "Door Pillar", cab, new Vector3(s * 1.12f, 1.45f, -1.35f), new Vector3(0.84f, 2.9f, 0.06f), wall, true);
                Prim(PrimitiveType.Cube, "Pillar Light", cab, new Vector3(s * 0.72f, 1.2f, -1.385f), new Vector3(0.04f, 2.4f, 0.02f), accent);
                GameObject strip = Prim(PrimitiveType.Cube, "Ride Light", cab, new Vector3(s * 0.9f, 2.89f, 0f), new Vector3(0.12f, 0.02f, 2.3f), light);
                lights.Add(strip.GetComponent<Renderer>());
            }
            Prim(PrimitiveType.Cube, "Door Header", cab, new Vector3(0f, 2.65f, -1.35f), new Vector3(1.4f, 0.5f, 0.06f), wall, true);
            doorL = Prim(PrimitiveType.Cube, "Sliding Door L", cab, new Vector3(-0.355f, 1.2f, -1.28f), new Vector3(0.72f, 2.4f, 0.04f), door, true).transform;
            doorR = Prim(PrimitiveType.Cube, "Sliding Door R", cab, new Vector3(0.355f, 1.2f, -1.28f), new Vector3(0.72f, 2.4f, 0.04f), door, true).transform;
            spot = new GameObject("Rider Spot").transform;
            spot.SetParent(cab, false);
            spot.localPosition = new Vector3(0f, 0.1f, 0.5f);
            spot.localRotation = Quaternion.Euler(0f, 180f, 0f); // face the doors

            // crown with a big sign above the cabin
            Prim(PrimitiveType.Cube, "Crown", cab, new Vector3(0f, 3.6f, 0f), new Vector3(3.4f, 1.2f, 3.1f), wall, true);
            Prim(PrimitiveType.Cube, "Crown Glow", cab, new Vector3(0f, 3.02f, 0f), new Vector3(3.44f, 0.06f, 3.14f), accent);
            RectTransform cs = UCanvas(cab, "Crown Sign", new Vector3(0f, 3.6f, -1.56f), Quaternion.identity, new Vector2(3200f, 1000f), false);
            UImg(cs, "Logo", new Vector2(-1130f, 0f), new Vector2(860f, 860f), LoopLandArt.LogoInfinity, Color.white, false).preserveAspect = true;
            UText(cs, "Text", up ? "<b>LOOPLAND</b>\n<size=55%>ELEVATOR TO THE LOOP DECK</size>" : "<b>PLAZA</b>\n<size=55%>ELEVATOR DOWN</size>",
                new Vector2(430f, 0f), new Vector2(2240f, 900f), 330f, Color.white);

            // outside: header sign and call button on the right pillar
            RectTransform head = UCanvas(cab, "Header Sign", new Vector3(0f, 2.65f, -1.39f), Quaternion.identity, new Vector2(1380f, 460f), false);
            UText(head, "Text", up ? "<b>ELEVATOR</b>\n<size=65%>UP TO LOOPLAND</size>" : "<b>ELEVATOR</b>\n<size=65%>DOWN TO PLAZA</size>",
                Vector2.zero, new Vector2(1340f, 430f), 170f, Color.white);
            RectTransform call = UCanvas(cab, "Call Button", new Vector3(1.12f, 1.35f, -1.39f), Quaternion.identity, new Vector2(640f, 760f));
            CallPanel(call, new Vector2(640f, 760f), up, lift, evt);

            // inside: floor display over the doors, ride panel on the right wall, logo on the back wall
            RectTransform disp = UCanvas(cab, "Floor Display", new Vector3(0f, 2.64f, -1.31f), Quaternion.Euler(0f, 180f, 0f), new Vector2(1300f, 420f), false);
            UImg(disp, "Back", Vector2.zero, new Vector2(1300f, 420f), LoopLandArt.Round, new Color(0.02f, 0.02f, 0.06f, 0.95f));
            TextMeshProUGUI floor = UText(disp, "Floor", up ? "PLAZA" : "LOOP DECK", Vector2.zero, new Vector2(1220f, 360f), 220f, Hex("00E5FF"));
            floor.fontStyle = FontStyles.Bold;
            display = floor;
            RectTransform panel = UCanvas(cab, "Ride Panel", new Vector3(1.53f, 1.35f, -0.55f), Quaternion.Euler(0f, 90f, 0f), new Vector2(700f, 900f));
            CallPanel(panel, new Vector2(700f, 900f), up, lift, evt);
            RectTransform back = UCanvas(cab, "Back Logo", new Vector3(0f, 1.75f, 1.31f), Quaternion.identity, new Vector2(1800f, 1100f), false);
            UImg(back, "Logo", new Vector2(0f, 120f), new Vector2(900f, 900f), LoopLandArt.LogoInfinity, Color.white, false).preserveAspect = true;
            UText(back, "Text", "<b>LOOPLAND</b>", new Vector2(0f, -400f), new Vector2(1700f, 220f), 180f, Color.white);
        }

        private static void CallPanel(RectTransform c, Vector2 px, bool up, LoopLandElevator lift, string evt)
        {
            UImg(c, "Glow", Vector2.zero, px + new Vector2(30f, 30f), LoopLandArt.Glow, up ? Hex("00E5FF") : Hex("FF3DCB"));
            UImg(c, "Back", Vector2.zero, px, LoopLandArt.Panel, Color.white);
            UText(c, "Title", "<b>ELEVATOR</b>", new Vector2(0f, px.y * 0.5f - 85f), new Vector2(px.x - 60f, 110f), 64f, Color.white);
            Vector2 size = new Vector2(px.x - 120f, px.y - 260f);
            TextMeshProUGUI lbl = UButton(c, "Go", up ? "UP TO\nLOOPLAND" : "DOWN TO\nPLAZA", new Vector2(0f, -60f), size, up ? Hex("E0218A") : Hex("1F4FD8"), lift, evt, 60f);
            lbl.rectTransform.anchoredPosition = new Vector2(0f, -size.y * 0.22f);
            lbl.rectTransform.sizeDelta = new Vector2(size.x - 30f, size.y * 0.45f);
            Image arrow = UImg(lbl.transform.parent, "Arrow", new Vector2(0f, size.y * 0.2f), new Vector2(size.y * 0.42f, size.y * 0.42f), LoopLandArt.IconArrow, Color.white, false);
            arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, up ? 0f : 180f);
        }

        // ------------------------------------------------------------------ plaza

        private static void Plaza(Transform root)
        {
            var p = Group(root, "Plaza");
            // walkways: spawn -> front bridge, then a glowing route to the elevator
            Walk(p, new Vector3(0f, 0.02f, -47f), new Vector3(0f, 0.02f, -33f), 4.6f, mCyan);
            Walk(p, new Vector3(0f, 0.02f, -24.5f), new Vector3(ShaftX, 0.02f, -11f), 3f, mPink);
            Walk(p, new Vector3(ShaftX, 0.02f, -11f), new Vector3(ShaftX, 0.02f, -1.6f), 3f, mPink);

            for (int k = 0; k < 24; k++)
            {
                float a = k * 15f + 7.5f;
                if (NearAxis(a, 11f)) continue; // keep the four bridges clear
                float r = 21.5f + (float)(rnd.NextDouble() - 0.5) * 2f;
                Tree(p, Polar(r, a), 0.95f + (float)rnd.NextDouble() * 0.45f, k % 3 == 0, true);
            }
            for (int k = 0; k < 8; k++) Lamp(p, Polar(16f, k * 45f + 22.5f));
            foreach (float a in new[] { 45f, 135f, 225f }) Bench(p, Polar(17.5f, a));
            Planter(p, new Vector3(-9.7f, 0.35f, 0f), 16f, false);
            Planter(p, new Vector3(0f, 0.35f, 9.7f), 16f, true);
            Planter(p, new Vector3(9.7f, 0.35f, -5.75f), 5.5f, false);
            Planter(p, new Vector3(9.7f, 0.35f, 5.75f), 5.5f, false);

            // standing sign by the front bridge
            var sg = Group(p, "Loop Sign");
            sg.localPosition = new Vector3(-4.8f, 0f, -20.5f);
            sg.localRotation = Quaternion.Euler(0f, -30f, 0f);
            Prim(PrimitiveType.Cube, "Sign Base", sg, new Vector3(0f, 0.15f, 0f), new Vector3(4f, 0.3f, 1f), mStone, true);
            Prim(PrimitiveType.Cube, "Sign Glow", sg, new Vector3(0f, 2.3f, 0f), new Vector3(3.5f, 4.3f, 0.3f), mPink);
            Prim(PrimitiveType.Cube, "Sign Stone", sg, new Vector3(0f, 2.3f, 0f), new Vector3(3.4f, 4.2f, 0.35f), mDark, true);
            RectTransform sc = UCanvas(sg, "Sign", new Vector3(0f, 2.3f, -0.185f), Quaternion.identity, new Vector2(3100f, 3900f), false);
            UImg(sc, "Logo", new Vector2(0f, 1380f), new Vector2(1300f, 1300f), LoopLandArt.LogoInfinity, Color.white, false).preserveAspect = true;
            UText(sc, "Line 1", "<b>SAME PEOPLE.</b>", new Vector2(0f, 620f), new Vector2(3000f, 420f), 330f, Color.white);
            UText(sc, "Line 2", "<b>NEW PLACES.</b>", new Vector2(0f, 180f), new Vector2(3000f, 420f), 330f, Hex("00E5FF"));
            UText(sc, "Line 3", "<b>ALWAYS A NEXT LOOP.</b>", new Vector2(0f, -300f), new Vector2(3000f, 380f), 270f, Hex("FFE14D"));
            UImg(sc, "Heart", new Vector2(0f, -1080f), new Vector2(760f, 760f), LoopLandArt.IconHeart, Hex("FF3DCB"), false);

            // direction sign to the elevator
            var ds = Group(p, "Elevator Sign");
            ds.localPosition = new Vector3(5f, 0f, -23.8f);
            Prim(PrimitiveType.Cube, "Post", ds, new Vector3(0f, 1.2f, 0f), new Vector3(0.12f, 2.4f, 0.12f), mMetal, true);
            RectTransform dc = UCanvas(ds, "Sign", new Vector3(0f, 2.3f, -0.08f), Quaternion.identity, new Vector2(2400f, 900f), false);
            UImg(dc, "Glow", Vector2.zero, new Vector2(2440f, 940f), LoopLandArt.Glow, Hex("FF3DCB"));
            UImg(dc, "Back", Vector2.zero, new Vector2(2400f, 900f), LoopLandArt.Panel, Color.white);
            UImg(dc, "Arrow", new Vector2(-860f, 0f), new Vector2(560f, 560f), LoopLandArt.IconArrow, Hex("FFE14D"), false).rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
            UText(dc, "Text", "<b>ELEVATOR</b>\n<size=62%>UP TO LOOPLAND</size>", new Vector2(250f, 0f), new Vector2(1700f, 780f), 300f, Color.white);
        }

        // ------------------------------------------------------------------ skyline, islands, clouds

        private static void City(Transform root)
        {
            var c = Group(root, "City");
            int[] widths = { 8, 10, 12, 14 };
            Material[] crowns = { mCyan, mPink, mGold, mPurple };
            int boards = 0;
            for (int i = 0; i < 18; i++)
            {
                float a = i * 20f + (float)(rnd.NextDouble() - 0.5) * 8f;
                float w = widths[rnd.Next(widths.Length)];
                float h = 28f + 4f * rnd.Next(0, 14);
                int style = 1 + rnd.Next(3);
                var b = Group(c, "Tower " + (i + 1));
                b.localPosition = Polar(52f + (float)rnd.NextDouble() * 16f, a);
                b.localRotation = Quaternion.LookRotation(-b.localPosition.normalized); // local +Z faces the plaza
                Prim(PrimitiveType.Cube, "Body", b, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, w), Facade(style, w, h), true);
                Material crown = crowns[rnd.Next(crowns.Length)];
                Prim(PrimitiveType.Cube, "Crown", b, new Vector3(0f, h - 0.25f, 0f), new Vector3(w + 0.3f, 0.5f, w + 0.3f), crown);
                if (a > 50f && a < 130f && boards < 2)
                {
                    // billboards on the towers behind LoopLand, facing the spawn
                    Billboard(b, "Billboard", new Vector3(0f, h * 0.62f, w * 0.5f + 0.06f), Quaternion.Euler(0f, 180f, 0f), new Vector2(w * 900f, w * 420f),
                        boards == 0 ? "GOOD PEOPLE.\nBETTER PLACES." : "SAME PEOPLE.\nNEW PLACES.", boards == 0 ? LoopLandArt.LogoInfinity : LoopLandArt.IconHeart, boards == 0 ? Hex("00E5FF") : Hex("FF3DCB"));
                    boards++;
                }
                if (rnd.NextDouble() < 0.5)
                {
                    float w2 = Mathf.Round(w * 0.6f), h2 = 6f + 3f * rnd.Next(0, 4);
                    Prim(PrimitiveType.Cube, "Top", b, new Vector3(0f, h + h2 * 0.5f, 0f), new Vector3(w2, h2, w2), Facade(style, w2, h2), true);
                    Prim(PrimitiveType.Cube, "Top Crown", b, new Vector3(0f, h + h2 - 0.2f, 0f), new Vector3(w2 + 0.25f, 0.4f, w2 + 0.25f), crown);
                    h += h2;
                }
                double extra = rnd.NextDouble();
                if (extra < 0.45)
                {
                    Rod(b, "Antenna", new Vector3(0f, h, 0f), new Vector3(0f, h + 9f, 0f), 0.12f, mMetal);
                    Prim(PrimitiveType.Sphere, "Beacon", b, new Vector3(0f, h + 9.2f, 0f), Vector3.one * 0.6f, mPink);
                }
                else if (extra < 0.75)
                {
                    GameObject ring = MeshObj(b, "Halo", haloMesh, mNeon, new Vector3(0f, h - 3f, 0f), Quaternion.identity, false);
                    ring.transform.localScale = Vector3.one * w * 0.95f;
                }
            }
            // a lower, hazier ring further out for depth
            for (int i = 0; i < 20; i++)
            {
                float a = i * 18f + 9f + (float)(rnd.NextDouble() - 0.5) * 6f;
                float w = 10f + 2f * rnd.Next(0, 4), h = 18f + 3f * rnd.Next(0, 8);
                var b = Group(c, "Block " + (i + 1));
                b.localPosition = Polar(88f + (float)rnd.NextDouble() * 14f, a);
                b.localRotation = Quaternion.LookRotation(-b.localPosition.normalized);
                Prim(PrimitiveType.Cube, "Body", b, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, w), Facade(3, w, h), true);
                Prim(PrimitiveType.Cube, "Crown", b, new Vector3(0f, h - 0.2f, 0f), new Vector3(w + 0.2f, 0.4f, w + 0.2f), crowns[i % crowns.Length]);
            }
        }

        private static void Islands(Transform root)
        {
            var g = Group(root, "Floating Islands");
            for (int i = 0; i < 4; i++)
            {
                float size = 5f + (float)rnd.NextDouble() * 3f;
                var isl = Group(g, "Island " + (i + 1));
                isl.localPosition = Polar(38f + (float)rnd.NextDouble() * 10f, 35f + i * 90f + (float)(rnd.NextDouble() - 0.5) * 20f) + Vector3.up * (24f + (float)rnd.NextDouble() * 18f);
                GameObject rock = MeshObj(isl, "Rock", RockMesh("World_Rock_" + i, 31 + i), mRock, Vector3.zero, Quaternion.identity, false);
                rock.transform.localScale = new Vector3(size, size * 1.3f, size);
                Prim(PrimitiveType.Cylinder, "Grass", isl, new Vector3(0f, 0.12f, 0f), new Vector3(size * 2.05f, 0.12f, size * 2.05f), mIslandGrass);
                int trees = 1 + rnd.Next(3);
                for (int k = 0; k < trees; k++)
                    Tree(isl, Polar(size * 0.45f * (float)rnd.NextDouble(), (float)rnd.NextDouble() * 360f) + Vector3.up * 0.24f, 0.7f + (float)rnd.NextDouble() * 0.4f, rnd.NextDouble() < 0.4, false);
                Fx("Falling Sparkles", isl, new Vector3(0f, -size * 0.7f, 0f), Quaternion.identity, 4f, 0.1f, 0.18f, 12f, 0f, true, true, ParticleSystemShapeType.Sphere, size * 0.5f, 0.06f, 80, Hex("00E5FF"), Color.white, 0f);
                Mover(isl.gameObject, Vector3.zero, 0f, 1.2f, 8f + i, new Vector3(0f, 2f, 0f));
            }
        }

        private static void Clouds(Transform root)
        {
            var g = Group(root, "Clouds");
            for (int i = 0; i < 9; i++)
            {
                float a = i * 40f + (float)rnd.NextDouble() * 20f;
                var cl = Group(g, "Cloud " + (i + 1));
                cl.localPosition = Polar(75f + (float)rnd.NextDouble() * 55f, a) + Vector3.up * (70f + (float)rnd.NextDouble() * 40f);
                cl.localRotation = Quaternion.Euler(0f, -a, 0f);
                int puffs = 3 + rnd.Next(3);
                for (int k = 0; k < puffs; k++)
                {
                    float s = 8f + (float)rnd.NextDouble() * 8f;
                    GameObject puff = Prim(PrimitiveType.Sphere, "Puff", cl, new Vector3((k - puffs * 0.5f) * 7f, (float)rnd.NextDouble() * 2f, (float)(rnd.NextDouble() - 0.5) * 6f), new Vector3(s * 1.4f, s * 0.7f, s), mCloud);
                    puff.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                Mover(cl.gameObject, new Vector3(18f, 0f, 0f), 70f + i * 7f, 0f, 0f, Vector3.zero);
            }
        }

        // ------------------------------------------------------------------ props

        private static void Tree(Transform parent, Vector3 pos, float s, bool blossom, bool collide)
        {
            var t = Group(parent, "Tree");
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
            Prim(PrimitiveType.Cylinder, "Trunk", t, new Vector3(0f, 1.3f * s, 0f), new Vector3(0.32f, 1.3f, 0.32f) * s, mTrunk, collide);
            Material leaf = blossom ? mBlossom : (rnd.NextDouble() < 0.5 ? mLeaf : mLeaf2);
            Prim(PrimitiveType.Sphere, "Crown", t, new Vector3(0f, 3.3f, 0f) * s, new Vector3(2.8f, 2.3f, 2.8f) * s, leaf);
            Prim(PrimitiveType.Sphere, "Crown", t, new Vector3(0.75f, 2.8f, 0.3f) * s, new Vector3(1.9f, 1.6f, 1.9f) * s, leaf);
            Prim(PrimitiveType.Sphere, "Crown", t, new Vector3(-0.6f, 2.9f, -0.45f) * s, new Vector3(1.7f, 1.5f, 1.7f) * s, leaf);
        }

        private static void Lamp(Transform parent, Vector3 pos)
        {
            var l = Group(parent, "Lamp");
            l.localPosition = pos;
            Prim(PrimitiveType.Cylinder, "Post", l, new Vector3(0f, 1.9f, 0f), new Vector3(0.14f, 1.9f, 0.14f), mMetal, true);
            Prim(PrimitiveType.Sphere, "Light", l, new Vector3(0f, 3.95f, 0f), Vector3.one * 0.5f, mWhiteGlow);
            Prim(PrimitiveType.Cylinder, "Cap", l, new Vector3(0f, 4.22f, 0f), new Vector3(0.6f, 0.03f, 0.6f), mMetal);
        }

        private static void Bench(Transform parent, Vector3 pos)
        {
            var b = Group(parent, "Bench");
            b.localPosition = pos;
            b.localRotation = Quaternion.LookRotation(new Vector3(pos.x, 0f, pos.z).normalized); // sit facing the tower
            Prim(PrimitiveType.Cube, "Seat", b, new Vector3(0f, 0.45f, 0f), new Vector3(1.9f, 0.08f, 0.5f), mTrunk, true);
            Prim(PrimitiveType.Cube, "Backrest", b, new Vector3(0f, 0.78f, 0.24f), new Vector3(1.9f, 0.45f, 0.06f), mTrunk, true);
            for (int s = -1; s <= 1; s += 2)
                Prim(PrimitiveType.Cube, "Leg", b, new Vector3(s * 0.8f, 0.22f, 0.02f), new Vector3(0.08f, 0.44f, 0.46f), mMetal);
        }

        private static void Planter(Transform parent, Vector3 c, float len, bool alongX)
        {
            Prim(PrimitiveType.Cube, "Planter", parent, c, alongX ? new Vector3(len, 0.7f, 1f) : new Vector3(1f, 0.7f, len), mStone, true);
            Prim(PrimitiveType.Cube, "Planter Green", parent, c + Vector3.up * 0.33f, alongX ? new Vector3(len - 0.2f, 0.1f, 0.8f) : new Vector3(0.8f, 0.1f, len - 0.2f), mLeaf2);
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 1.6f));
            for (int i = 0; i < n; i++)
            {
                float u = ((i + 0.5f) / n - 0.5f) * (len - 0.6f);
                Prim(PrimitiveType.Sphere, "Bush", parent, c + (alongX ? new Vector3(u, 0.2f, 0f) : new Vector3(0f, 0.2f, u)), new Vector3(0.95f, 0.7f, 0.95f), i % 3 == 1 ? mBlossom : mLeaf);
            }
        }

        private static void Walk(Transform parent, Vector3 a, Vector3 b, float width, Material edge)
        {
            Beam(parent, "Walkway", a, b, width, 0.04f, mStone, false);
            Vector3 side = Vector3.Cross(Vector3.up, (b - a).normalized) * (width * 0.5f);
            Vector3 lift = Vector3.up * 0.005f;
            Beam(parent, "Walkway Glow", a + side + lift, b + side + lift, 0.08f, 0.045f, edge, false);
            Beam(parent, "Walkway Glow", a - side + lift, b - side + lift, 0.08f, 0.045f, edge, false);
        }

        /// <summary>Glowing billboard: neon frame, dark panel, picture on the left, two lines of text on the right.</summary>
        private static void Billboard(Transform parent, string name, Vector3 pos, Quaternion rot, Vector2 px, string text, Sprite icon, Color accent)
        {
            RectTransform c = UCanvas(parent, name, pos, rot, px, false);
            float hgt = px.y, m = hgt * 0.1f, s = hgt * 0.8f, left = -px.x * 0.5f;
            Image glow = UImg(c, "Glow", Vector2.zero, px + Vector2.one * hgt * 0.1f, LoopLandArt.Glow, accent);
            glow.pixelsPerUnitMultiplier = 30f / (hgt * 0.07f);
            Image back = UImg(c, "Back", Vector2.zero, px, LoopLandArt.Panel, Color.white);
            back.pixelsPerUnitMultiplier = 60f / (hgt * 0.06f);
            UImg(c, "Art", new Vector2(left + m + s * 0.5f, 0f), new Vector2(s, s), icon, icon == LoopLandArt.LogoInfinity ? Color.white : accent, false).preserveAspect = true;
            float tw = px.x - s - m * 3f;
            TextMeshProUGUI t = UText(c, "Text", "<b>" + text + "</b>", new Vector2(left + m * 2f + s + tw * 0.5f, 0f), new Vector2(tw, hgt - m * 2f), hgt * 0.36f, Color.white);
            t.enableVertexGradient = true;
            t.colorGradient = new VertexGradient(Color.white, Color.white, accent, accent);
        }

        // ------------------------------------------------------------------ spawn and helpers

        private static void Spawn()
        {
            var descs = Object.FindObjectsByType<VRC.SDK3.Components.VRCSceneDescriptor>(FindObjectsSortMode.None);
            GameObject w = descs.Length > 0 ? descs[0].gameObject : null;
            if (w == null)
            {
                foreach (string g in AssetDatabase.FindAssets("VRCWorld t:Prefab"))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                    if (prefab == null || prefab.GetComponent<VRC.SDK3.Components.VRCSceneDescriptor>() == null) continue;
                    w = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    Undo.RegisterCreatedObjectUndo(w, "VRCWorld");
                    break;
                }
                if (w == null) return;
            }
            else Undo.RecordObject(w.transform, "Move spawn");
            // spawn on the front walkway, looking up at the tower
            w.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, -44f), Quaternion.identity);
        }

        private static void MarkStatic(Transform t)
        {
            if (t.GetComponent<LoopLandMover>() != null || t.GetComponent<TMP_Text>() != null || t.name.StartsWith("Sliding Door") || t.name == "Ride Light") return;
            if (t.GetComponent<MeshRenderer>() != null)
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            foreach (Transform child in t) MarkStatic(child);
        }

        private static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        private static Vector3 Polar(float r, float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }

        private static bool NearAxis(float deg, float tol)
        {
            float m = Mathf.Repeat(deg, 90f);
            return m < tol || m > 90f - tol;
        }

        private static void Mover(GameObject go, Vector3 offset, float period, float bob, float bobPeriod, Vector3 spin)
        {
            var mv = UdonSharpUndo.AddComponent<LoopLandMover>(go);
            mv.moveOffset = offset;
            mv.movePeriod = period > 0f ? period : 16f;
            mv.bobHeight = bob;
            mv.bobPeriod = bobPeriod > 0f ? bobPeriod : 7f;
            mv.spin = spin;
            made.Add(mv);
        }

        private static GameObject MeshObj(Transform parent, string name, Mesh mesh, Material m, Vector3 lpos, Quaternion lrot, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = lpos;
            go.transform.localRotation = lrot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        /// <summary>A box stretched from a to b (local space).</summary>
        private static GameObject Beam(Transform parent, string name, Vector3 a, Vector3 b, float w, float h, Material m, bool collider)
        {
            Vector3 d = b - a;
            GameObject go = Prim(PrimitiveType.Cube, name, parent, (a + b) * 0.5f, new Vector3(w, h, d.magnitude), m, collider);
            go.transform.localRotation = Quaternion.LookRotation(d, Mathf.Abs(d.normalized.y) > 0.99f ? Vector3.forward : Vector3.up);
            return go;
        }

        /// <summary>A cylinder from a to b (local space).</summary>
        private static GameObject Rod(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material m)
        {
            Vector3 d = b - a;
            GameObject go = Prim(PrimitiveType.Cylinder, name, parent, (a + b) * 0.5f, new Vector3(radius * 2f, d.magnitude * 0.5f, radius * 2f), m);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d);
            return go;
        }

        private static Color Rainbow(float v)
        {
            float f = Mathf.Repeat(v, 1f) * (RainbowHex.Length - 1);
            int i = Mathf.Min((int)f, RainbowHex.Length - 2);
            return Color.Lerp(Hex(RainbowHex[i]), Hex(RainbowHex[i + 1]), f - i);
        }

        // ------------------------------------------------------------------ materials and textures

        /// <summary>Curtain-wall glass for a w x h block: whole windows (about 2 m x 3 m) on every side.</summary>
        private static Material Facade(int style, float w, float h)
        {
            int cx = Mathf.Max(1, Mathf.RoundToInt(w / 2f)), cy = Mathf.Max(1, Mathf.RoundToInt(h / 3f));
            string key = "Facade_" + style + "_" + cx + "x" + cy;
            if (facadeMats.TryGetValue(key, out Material m)) return m;
            m = Std(key, Color.white, 0.55f, 0.9f, Color.white * FacadeGlow[style], facadeTex[style]);
            m.mainTextureScale = new Vector2(cx / 8f, cy / 8f);
            EditorUtility.SetDirty(m);
            facadeMats[key] = m;
            return m;
        }

        private static Material Glass(string name, Color c)
        {
            Material m = Std(name, c, 0.1f, 0.95f, Color.black);
            m.SetFloat("_Mode", 3f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Copy of the font material with a soft pink glow, for the big LOOPLAND letters.</summary>
        private static Material SignMaterial(TMP_Text t)
        {
            string path = Gen + "/Materials/Sign_Glow.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(t.fontSharedMaterial);
                AssetDatabase.CreateAsset(m, path);
            }
            m.EnableKeyword("GLOW_ON");
            m.SetColor("_GlowColor", new Color(1f, 0.35f, 0.9f, 0.7f));
            m.SetFloat("_GlowOffset", 0f);
            m.SetFloat("_GlowInner", 0.1f);
            m.SetFloat("_GlowOuter", 0.35f);
            m.SetFloat("_GlowPower", 0.6f);
            m.SetFloat("_FaceDilate", 0.15f);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Texture2D SolidTex()
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color[16];
            for (int i = 0; i < px.Length; i++) px[i] = Color.white;
            t.SetPixels(px);
            t.Apply();
            return SavePng("Neon_Solid", t);
        }

        private static Texture2D PavingTex()
        {
            const int n = 128;
            var r = new System.Random(5);
            var shade = new float[16];
            for (int i = 0; i < shade.Length; i++) shade[i] = 0.9f + 0.1f * (float)r.NextDouble();
            Color stone = Hex("E4DDEA"), grout = Hex("9C93A8");
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    Color c = x % 32 < 2 || y % 32 < 2 ? grout : stone * shade[(y / 32) * 4 + x / 32];
                    c.a = 1f;
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return SavePng("World_Paving", t);
        }

        private static Texture2D GrassTex()
        {
            const int n = 128;
            var r = new System.Random(9);
            Color a = Hex("3E8E4A"), b = Hex("6CBF5A");
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // sums of whole sine periods so the texture tiles seamlessly
                    float u = x / (float)n * 2f * Mathf.PI, v = y / (float)n * 2f * Mathf.PI;
                    float k = 0.5f + 0.22f * Mathf.Sin(2f * u + 1.3f) * Mathf.Sin(3f * v) + 0.14f * Mathf.Sin(5f * u + 7f * v) + 0.12f * (float)(r.NextDouble() - 0.5);
                    Color c = Color.Lerp(a, b, k);
                    c.a = 1f;
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return SavePng("World_Grass", t);
        }

        private static Texture2D WindowTex(string name, Color glassCol, Color litA, Color litB, float litChance, int seed)
        {
            const int n = 256, cell = 32;
            var r = new System.Random(seed);
            var frame = new Color(0.04f, 0.04f, 0.07f, 1f);
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true);
            for (int cy = 0; cy < n / cell; cy++)
                for (int cx = 0; cx < n / cell; cx++)
                {
                    bool lit = r.NextDouble() < litChance;
                    Color pane = lit ? Color.Lerp(litA, litB, (float)r.NextDouble()) : glassCol * (0.75f + 0.5f * (float)r.NextDouble());
                    for (int y = 0; y < cell; y++)
                        for (int x = 0; x < cell; x++)
                        {
                            float g = (y - 6) / (float)(cell - 8);
                            Color c;
                            if (y < 6) c = frame * 1.6f;                                        // floor slab
                            else if (x < 2 || x >= cell - 2 || y >= cell - 2) c = frame;        // mullions
                            else c = lit ? pane * (0.85f + 0.15f * g) : Color.Lerp(pane, pane * 1.8f, g); // pane (sky reflection on dark glass)
                            c.a = 1f;
                            t.SetPixel(cx * cell + x, cy * cell + y, c);
                        }
                }
            t.Apply();
            return SavePng(name, t);
        }

        // ------------------------------------------------------------------ meshes

        private static Mesh SaveMesh(string name, Mesh mesh)
        {
            Dir(Gen + "/Meshes");
            string path = Gen + "/Meshes/" + name + ".asset";
            mesh.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            // keep the asset (and anything referencing it), replace its data
            existing.Clear();
            existing.vertices = mesh.vertices;
            if (mesh.normals.Length > 0) existing.normals = mesh.normals;
            if (mesh.tangents.Length > 0) existing.tangents = mesh.tangents;
            if (mesh.uv.Length > 0) existing.uv = mesh.uv;
            if (mesh.colors.Length > 0) existing.colors = mesh.colors;
            existing.triangles = mesh.triangles;
            existing.RecalculateBounds();
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>Flat ring (or disc when r0 = 0) facing up, UVs in world metres / uvSize.</summary>
        private static Mesh Annulus(string name, float r0, float r1, int seg, float uvSize)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * 2f * Mathf.PI;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts.Add(d * r0);
                verts.Add(d * r1);
                uvs.Add(new Vector2(d.x, d.z) * (r0 / uvSize));
                uvs.Add(new Vector2(d.x, d.z) * (r1 / uvSize));
                if (i == 0) continue;
                int q = verts.Count - 4; // inner(i-1), outer(i-1), inner(i), outer(i)
                UpTri(tris, verts, q, q + 1, q + 3);
                if (r0 > 0f) UpTri(tris, verts, q, q + 3, q + 2);
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return SaveMesh(name, mesh);
        }

        private static void UpTri(List<int> tris, List<Vector3> v, int a, int b, int c)
        {
            if (Vector3.Cross(v[b] - v[a], v[c] - v[a]).y < 0f) { int tmp = b; b = c; c = tmp; }
            tris.Add(a);
            tris.Add(b);
            tris.Add(c);
        }

        /// <summary>Strip between two rails; u runs across (rail a = 0, rail b = 1), v along the length (metres * vScale).</summary>
        private static Mesh Strip(string name, List<Vector3> a, List<Vector3> b, List<Color> cols, bool closed, bool twoSided, float vScale)
        {
            int n = a.Count;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            var tris = new List<int>();
            float len = 0f;
            for (int i = 0; i < (closed ? n + 1 : n); i++)
            {
                int k = i % n;
                if (i > 0) len += Vector3.Distance(a[k], a[(i - 1) % n]);
                verts.Add(a[k]);
                verts.Add(b[k]);
                uvs.Add(new Vector2(0f, len * vScale));
                uvs.Add(new Vector2(1f, len * vScale));
                Color c = cols != null ? cols[k] : Color.white;
                colors.Add(c);
                colors.Add(c);
                if (i == 0) continue;
                int q = verts.Count - 4;
                tris.Add(q); tris.Add(q + 2); tris.Add(q + 1);
                tris.Add(q + 1); tris.Add(q + 2); tris.Add(q + 3);
            }
            if (twoSided)
            {
                int off = verts.Count, tc = tris.Count;
                verts.AddRange(verts.ToArray());
                uvs.AddRange(uvs.ToArray());
                colors.AddRange(colors.ToArray());
                for (int i = 0; i < tc; i += 3)
                {
                    tris.Add(tris[i] + off);
                    tris.Add(tris[i + 2] + off);
                    tris.Add(tris[i + 1] + off);
                }
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return SaveMesh(name, mesh);
        }

        /// <summary>Vertical wall (both sides) along a path in the XZ plane, from y0 to y1.</summary>
        private static Mesh Band(string name, List<Vector2> pts, bool closed, float y0, float y1, Func<float, Color> color)
        {
            var a = new List<Vector3>();
            var b = new List<Vector3>();
            List<Color> cols = color != null ? new List<Color>() : null;
            for (int i = 0; i < pts.Count; i++)
            {
                a.Add(new Vector3(pts[i].x, y0, pts[i].y));
                b.Add(new Vector3(pts[i].x, y1, pts[i].y));
                if (cols != null) cols.Add(color(i / (float)pts.Count));
            }
            return Strip(name, a, b, cols, closed, true, 0.5f);
        }

        /// <summary>Flat neon ribbon along a path in the XZ plane, rainbow along its length (for the additive ribbon material).</summary>
        private static Mesh Ribbon(string name, List<Vector2> pts, bool closed, float width)
        {
            int n = pts.Count;
            var a = new List<Vector3>();
            var b = new List<Vector3>();
            var cols = new List<Color>();
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = closed ? pts[(i - 1 + n) % n] : pts[Mathf.Max(i - 1, 0)];
                Vector2 next = closed ? pts[(i + 1) % n] : pts[Mathf.Min(i + 1, n - 1)];
                Vector2 dir = (next - prev).normalized;
                Vector2 off = new Vector2(-dir.y, dir.x) * (width * 0.5f);
                a.Add(new Vector3(pts[i].x + off.x, 0f, pts[i].y + off.y));
                b.Add(new Vector3(pts[i].x - off.x, 0f, pts[i].y - off.y));
                cols.Add(Rainbow(i / (float)n));
            }
            return Strip(name, a, b, cols, closed, false, 0.5f);
        }

        private static Mesh Torus(string name, float R, float r, int segU, int segV)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var cols = new List<Color>();
            var tris = new List<int>();
            for (int i = 0; i <= segU; i++)
            {
                float u = i / (float)segU, au = u * 2f * Mathf.PI;
                var dir = new Vector3(Mathf.Cos(au), 0f, Mathf.Sin(au));
                Color col = Rainbow(u * 2f);
                for (int j = 0; j <= segV; j++)
                {
                    float av = j / (float)segV * 2f * Mathf.PI;
                    Vector3 n = dir * Mathf.Cos(av) + Vector3.up * Mathf.Sin(av);
                    verts.Add(dir * R + n * r);
                    norms.Add(n);
                    uvs.Add(new Vector2(j / (float)segV, u * 8f));
                    cols.Add(col);
                    if (i == 0 || j == 0) continue;
                    int q = i * (segV + 1) + j, p = q - (segV + 1);
                    tris.Add(p - 1); tris.Add(q - 1); tris.Add(q);
                    tris.Add(p - 1); tris.Add(q); tris.Add(p);
                }
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return SaveMesh(name, mesh);
        }

        /// <summary>Low-poly upside-down rock for a floating island: flat top ring at y = 0, tip at y = -1.</summary>
        private static Mesh RockMesh(string name, int seed)
        {
            const int n = 10;
            var r = new System.Random(seed);
            float[] ys = { 0f, -0.3f, -0.62f, -1f };
            float[] rs = { 1f, 0.78f, 0.42f, 0f };
            var ring = new Vector3[4, n];
            for (int k = 0; k < 4; k++)
                for (int i = 0; i < n; i++)
                {
                    float a = (i + (k > 0 ? (float)r.NextDouble() * 0.4f - 0.2f : 0f)) / n * 2f * Mathf.PI;
                    float rad = rs[k] * (k == 0 ? 1f : 0.85f + 0.3f * (float)r.NextDouble());
                    float y = ys[k] + (k == 1 || k == 2 ? ((float)r.NextDouble() - 0.5f) * 0.12f : 0f);
                    ring[k, i] = new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad);
                }
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var centre = new Vector3(0f, -0.45f, 0f);
            for (int k = 0; k < 3; k++)
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    Face(verts, tris, ring[k, i], ring[k, j], ring[k + 1, j], centre);
                    if (k < 2) Face(verts, tris, ring[k, i], ring[k + 1, j], ring[k + 1, i], centre);
                }
            for (int i = 0; i < n; i++) Face(verts, tris, Vector3.zero, ring[0, i], ring[0, (i + 1) % n], centre);
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return SaveMesh(name, mesh);
        }

        /// <summary>Adds a flat-shaded triangle wound to face away from <paramref name="centre"/>.</summary>
        private static void Face(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 centre)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - centre) < 0f) { Vector3 tmp = b; b = c; c = tmp; }
            int q = verts.Count;
            verts.Add(a);
            verts.Add(b);
            verts.Add(c);
            tris.Add(q);
            tris.Add(q + 1);
            tris.Add(q + 2);
        }

        // ------------------------------------------------------------------ paths

        private static List<Vector2> Circle(float r, int n)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * 2f * Mathf.PI;
                pts.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r));
            }
            return pts;
        }

        /// <summary>Infinity (lemniscate of Bernoulli, stretched a little taller) of half-width a.</summary>
        private static List<Vector2> Lemniscate(float a, int n)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n * 2f * Mathf.PI, s = Mathf.Sin(t), c = Mathf.Cos(t), d = 1f + s * s;
                pts.Add(new Vector2(a * c / d, 1.3f * a * s * c / d));
            }
            return pts;
        }

        /// <summary>
        /// Outline of the two deck lobes (circles of radius r around x = ±DeckC). With gapDeg > 0 the outline is left open
        /// at the right end (±gapDeg around +X) where the elevator docks.
        /// </summary>
        private static List<Vector2> DeckOutline(float r, float gapDeg)
        {
            var pts = new List<Vector2>();
            float beta = Mathf.Atan2(Mathf.Sqrt(r * r - DeckC * DeckC), DeckC) * Mathf.Rad2Deg; // waist points, seen from each centre
            Arc(pts, DeckC, r, gapDeg > 0f ? gapDeg : beta - 180f, 180f - beta, 40, false);
            Arc(pts, -DeckC, r, beta, 360f - beta, 90, true);
            if (gapDeg > 0f) Arc(pts, DeckC, r, 180f + beta, 360f - gapDeg, 40, true);
            else pts.RemoveAt(pts.Count - 1); // closed: the last point repeats the first
            return pts;
        }

        private static void Arc(List<Vector2> pts, float cx, float r, float a0, float a1, int steps, bool skipFirst)
        {
            for (int i = skipFirst ? 1 : 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)steps) * Mathf.Deg2Rad;
                pts.Add(new Vector2(cx + Mathf.Cos(a) * r, Mathf.Sin(a) * r));
            }
        }
    }
}
