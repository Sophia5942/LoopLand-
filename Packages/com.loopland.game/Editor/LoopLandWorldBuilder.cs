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
    /// LoopLand > Build LoopLand Tower World. A rounded glass tower with a curved LOOPLAND screen, a mall podium, a crown,
    /// a spire and a light spine. Around it at 26 m runs The Loop: two huge ring skyways that form an infinity with the
    /// sky terrace around the tower, where the game is played. Waterfalls pour from the rings into the pool.
    /// A real glass elevator rides from the plaza to The Loop. Also builds the plaza (fountain, trees, signs), the water
    /// ring with arched bridges, a rounded skyline, floating islands, clouds, a daytime sky, music and ambience.
    /// Everything is generated into Assets/LoopLand/Generated; the sounds ship with the package (Audio folder).
    /// </summary>
    public static class LoopLandWorldBuilder
    {
        private const string Gen = "Assets/LoopLand/Generated";
        private const string Pkg = "Packages/com.loopland.game";
        private const string WorldName = "LoopLand World";
        private const float H = 26f;            // The Loop: skyway level
        private const float HubR = 19f;         // sky terrace around the tower (the crossing of the infinity)
        private const float RingC = 34.5f;      // ring centres at x = ±RingC
        private const float RingIn = 12.5f;     // ring walkway inner radius
        private const float RingOut = 17.5f;    // ring walkway outer radius
        private const float PlazaR = 30f;       // plaza island
        private const float PoolR = 36f;        // outer edge of the water ring
        private const float GameZ = -13f;       // the game sits on the terrace in front of the tower
        private const float BridgeIn = PlazaR - 1.5f;
        private const float BridgeOut = PoolR + 1.5f;
        private const float BridgeHalf = 2.3f;
        private static readonly Vector2 LiftXZ = new Vector2(9f, -21.5f);
        private static readonly Vector2 ScreenC = new Vector2(0f, 5.49f); // centre of the curved screen's arc (radius 15)
        private static readonly string[] RainbowHex = { "FFE14D", "7CFF4F", "00E5FF", "4D8BFF", "B07CFF", "FF3DCB", "FF8A3D", "FFE14D" };

        private static readonly List<UdonSharpBehaviour> made = new List<UdonSharpBehaviour>();
        private static readonly List<TMP_Text> musicLabels = new List<TMP_Text>();
        private static System.Random rnd;
        private static int meshCount;
        private static Mesh haloMesh;
        private static Type spatialType;
        private static LoopLandAmbience ambience;
        private static Material[] facade;
        private static Material mWhite, mSteel, mDark, mStone, mRoof, mPaving, mGrass, mIslandGrass, mWater, mTrunk, mLeaf, mLeaf2, mBlossom, mRock;
        private static Material mGlass, mDoorGlass, mCyan, mPink, mGold, mPurple, mWhiteGlow, mLobby, mShop, mScreen, mSpine, mNeon, mRibbon, mFalls, mCloud, mSoil;

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
            musicLabels.Clear();
            rnd = new System.Random(2024);
            meshCount = 0;
            try
            {
                EditorUtility.DisplayProgressBar("LoopLand", "Mixing materials and painting the screen...", 0.1f);
                var world = new GameObject(WorldName).transform;
                Materials();
                Sky(world);
                Music(world);
                EditorUtility.DisplayProgressBar("LoopLand", "Pouring the plaza and the water ring...", 0.25f);
                Ground(world);
                EditorUtility.DisplayProgressBar("LoopLand", "Raising the tower...", 0.35f);
                Tower(world);
                EditorUtility.DisplayProgressBar("LoopLand", "Building The Loop...", 0.5f);
                Skyway(world);
                EditorUtility.DisplayProgressBar("LoopLand", "Installing the glass elevator...", 0.62f);
                Lift(world);
                Plaza(world);
                EditorUtility.DisplayProgressBar("LoopLand", "Building the skyline...", 0.75f);
                City(world);
                Islands(world);
                FrontWalk(world);
                ambience.musicLabels = musicLabels.ToArray();
                EditorUtility.DisplayProgressBar("LoopLand", "Wiring Udon behaviours...", 0.92f);
                foreach (UdonSharpBehaviour b in made) UdonSharpEditorUtility.CopyProxyToUdon(b);
                MarkStatic(world);
                Undo.RegisterCreatedObjectUndo(world.gameObject, "Build LoopLand Tower World");

                Undo.RecordObject(game.transform, "Move LoopLand onto The Loop");
                Transform store = game.transform.Find("Store");
                if (store != null) Undo.RecordObject(store, "Move LoopLand onto The Loop");
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
                Debug.Log("[LoopLand] Tower world built! Spawn on the plaza and take the glass elevator up to The Loop (" + H + " m), where the game is.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            Undo.CollapseUndoOperations(undoGroup);
        }

        /// <summary>Puts the game on the sky terrace when the tower world is in the scene (also called after a game rebuild).</summary>
        internal static void PlaceGame(GameObject game)
        {
            if (game == null || GameObject.Find(WorldName) == null) return;
            game.transform.SetPositionAndRotation(new Vector3(0f, H, GameZ), Quaternion.identity);
            Transform store = game.transform.Find("Store");
            if (store == null) return;
            // store kiosk on the left of the terrace, facing the table
            store.localPosition = new Vector3(-13f, 0f, 6f);
            store.localRotation = Quaternion.Euler(0f, -90f, 0f);
        }

        // ------------------------------------------------------------------ materials, sky, music

        private static void Materials()
        {
            mWhite = Std("World_White", Hex("ECE9F5"), 0.1f, 0.6f, Color.black);
            mSteel = Std("World_Steel", Hex("9097AE"), 0.85f, 0.7f, Color.black);
            mDark = Std("World_Dark", Hex("1E1B2E"), 0.6f, 0.8f, Color.black);
            mStone = Std("World_Stone", Hex("D9D3E3"), 0f, 0.35f, Color.black);
            mRoof = Std("World_Roof", Hex("A9A4BC"), 0.1f, 0.3f, Color.black);
            mSoil = Std("World_Soil", Hex("3B2F2A"), 0f, 0.1f, Color.black);
            mPaving = Std("World_Paving", Color.white, 0f, 0.35f, Color.black, PavingTex());
            mGrass = Std("World_Grass", Color.white, 0f, 0.15f, Color.black, GrassTex());
            mIslandGrass = Std("World_Island_Grass", Hex("5CC46A"), 0f, 0.2f, Color.black);
            mWater = Std("World_Water", Hex("2A8FC4"), 0.4f, 0.97f, Hex("06324A"));
            mTrunk = Std("World_Trunk", Hex("5A3B2A"), 0f, 0.2f, Color.black);
            mLeaf = Std("World_Leaf", Hex("3FA34D"), 0f, 0.25f, Color.black);
            mLeaf2 = Std("World_Leaf_2", Hex("2E8B57"), 0f, 0.25f, Color.black);
            mBlossom = Std("World_Blossom", Hex("FF9FD2"), 0f, 0.25f, Hex("3A1030"));
            mRock = Std("World_Rock", Hex("8A7F9A"), 0.05f, 0.2f, Color.black);
            mGlass = Glass("World_Glass", new Color(0.7f, 0.9f, 1f, 0.18f));
            mDoorGlass = Glass("World_Door_Glass", new Color(0.6f, 0.8f, 0.95f, 0.35f));
            mCyan = Std("Neon_Cyan", Hex("00E5FF"), 0f, 0.6f, Hex("00E5FF") * 2f);
            mPink = Std("Neon_Pink", Hex("FF3DCB"), 0f, 0.6f, Hex("FF3DCB") * 2f);
            mGold = Std("Neon_Gold", Hex("FFE14D"), 0f, 0.6f, Hex("FFE14D") * 2f);
            mPurple = Std("Neon_Purple", Hex("B07CFF"), 0f, 0.6f, Hex("B07CFF") * 2f);
            mWhiteGlow = Std("Neon_White", Color.white, 0f, 0.6f, new Color(1f, 0.95f, 0.85f) * 1.6f);
            mLobby = Std("World_Lobby", Hex("FFE9C7"), 0.2f, 0.85f, Hex("FFC98A") * 0.8f);
            mShop = Std("World_Shop", Color.white, 0.2f, 0.85f, Color.white * 0.9f, ShopTex());
            mScreen = Std("World_Screen", Color.white, 0f, 0.85f, Color.white * 1.3f, LoopLandArt.SignScreen("Sign_Screen", 1536, 1080));
            mSpine = Std("World_Spine", Color.white, 0f, 0.5f, Color.white * 1.7f, LoopLandArt.LightColumn("Light_Column"));
            mNeon = AddMat("Neon_Additive", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            mNeon.mainTexture = SolidTex();
            EditorUtility.SetDirty(mNeon);
            mRibbon = AssetDatabase.LoadAssetAtPath<Material>(Gen + "/Materials/Ribbon.mat");
            if (mRibbon == null) mRibbon = mNeon;
            mFalls = ParticleMat("World_Waterfall", LoopLandArt.WaterStreaks("Water_Streaks"), new Color(0.48f, 0.52f, 0.56f, 0.5f));
            mCloud = ParticleMat("World_Cloud", LoopLandArt.CloudPuff("Cloud_Puff"), new Color(0.5f, 0.5f, 0.5f, 0.5f));
            facade = new[]
            {
                FacadeMat(0, WindowTex("Facade_LoopLand", Hex("3A6FB5"), Hex("FFF1D6"), Hex("BFF6FF"), 0.2f, 3), 0.45f),
                FacadeMat(1, WindowTex("Facade_Teal", Hex("2E7C8F"), Hex("FFD27A"), Hex("FFF1D0"), 0.15f, 4), 0.35f),
                FacadeMat(2, WindowTex("Facade_Violet", Hex("6A5FB0"), Hex("FF8AD8"), Hex("FFD0F0"), 0.15f, 5), 0.35f),
                FacadeMat(3, WindowTex("Facade_Steel", Hex("4A6A8F"), Hex("E8F0FF"), Hex("A8D8FF"), 0.12f, 6), 0.3f)
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
            sky.SetFloat("_SunSize", 0.04f);
            sky.SetFloat("_SunSizeConvergence", 5f);
            sky.SetFloat("_AtmosphereThickness", 0.85f);
            sky.SetColor("_SkyTint", new Color(0.42f, 0.58f, 0.92f));
            sky.SetColor("_GroundColor", new Color(0.55f, 0.6f, 0.68f));
            sky.SetFloat("_Exposure", 1.35f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;

            // bright daytime sun from behind the spawn, so the tower front and the screen are lit
            var sunGo = new GameObject("LoopLand Sun");
            sunGo.transform.SetParent(root, false);
            sunGo.transform.rotation = Quaternion.Euler(42f, 35f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.7f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.72f, 0.74f, 0.84f);
            RenderSettings.ambientGroundColor = new Color(0.38f, 0.36f, 0.42f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.7f, 0.8f, 0.94f);
            RenderSettings.fogStartDistance = 160f;
            RenderSettings.fogEndDistance = 560f;
            DynamicGI.UpdateEnvironment();

            // soft billboard clouds drifting in a wide ring around the city
            var go = new GameObject("Clouds");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, 130f, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 60f;
            main.startLifetime = 600f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(45f, 95f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.95f), new Color(0.9f, 0.94f, 1f, 0.8f));
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 0.1f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = 230f;
            sh.radiusThickness = 0.45f;
            sh.randomPositionAmount = 18f;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.6f);
            vel.y = new ParticleSystem.MinMaxCurve(0f);
            vel.z = new ParticleSystem.MinMaxCurve(0.2f);
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f), new GradientAlphaKey(1f, 0.94f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(g);
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = mCloud;
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.sortMode = ParticleSystemSortMode.Distance;
            pr.maxParticleSize = 3f;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            ps.Play();
        }

        /// <summary>Theme tune (2D, with a per-player MUSIC toggle) and the city/park ambience bed.</summary>
        private static void Music(Transform root)
        {
            var a = Group(root, "Audio");
            AudioSource music = Sound(a, "Theme Music", Vector3.zero, Clip("LoopLand_Theme", AudioClipLoadType.Streaming), 0.3f, false, 0f, 0f);
            Sound(a, "City Ambience", Vector3.zero, Clip("Ambience_City", AudioClipLoadType.CompressedInMemory), 0.45f, false, 0f, 0f);
            ambience = UdonSharpUndo.AddComponent<LoopLandAmbience>(a.gameObject);
            ambience.music = music;
            ambience.musicVolume = 0.3f;
            made.Add(ambience);
        }

        // ------------------------------------------------------------------ ground: plaza island, water ring, arched bridges

        private static void Ground(Transform root)
        {
            var g = Group(root, "Ground");
            MeshObj(g, "Outer Ground", Annulus("World_Ground", PoolR, 180f, 128, 4f), mGrass, Vector3.zero, Quaternion.identity, true);
            MeshObj(g, "Plaza", Annulus("World_Plaza", 0f, PlazaR, 128, 2f), mPaving, Vector3.zero, Quaternion.identity, true);
            MeshObj(g, "Plaza Edge", WallMesh("World_Plaza_Edge", Circle(PlazaR, 128), true, -0.9f, 0f), mStone, Vector3.zero, Quaternion.identity, false);
            MeshObj(g, "Pool Edge", WallMesh("World_Pool_Edge", Circle(PoolR, 128), true, -0.9f, 0f), mStone, Vector3.zero, Quaternion.identity, false);
            MeshObj(g, "Water", Annulus("World_Water", PlazaR - 0.5f, PoolR + 0.5f, 128, 4f), mWater, new Vector3(0f, -0.25f, 0f), Quaternion.identity, true);

            // glass railings along both shores, open where the bridges land
            float gapIn = Mathf.Asin((BridgeHalf - 0.1f) / (PlazaR - 0.15f)) * Mathf.Rad2Deg;
            float gapOut = Mathf.Asin((BridgeHalf - 0.1f) / (PoolR + 0.15f)) * Mathf.Rad2Deg;
            for (int q = 0; q < 4; q++)
            {
                Railing(g, "Shore Railing", MeshKit.At(MeshKit.Arc(Vector2.zero, PlazaR - 0.15f, q * 90f + gapIn, (q + 1) * 90f - gapIn, 40), 0f), false, 1f, true);
                Railing(g, "Shore Railing", MeshKit.At(MeshKit.Arc(Vector2.zero, PoolR + 0.15f, q * 90f + gapOut, (q + 1) * 90f - gapOut, 48), 0f), false, 1f, true);
            }

            Mesh deck = ArchDeck();
            float zIn = Mathf.Sqrt((PlazaR - 0.15f) * (PlazaR - 0.15f) - (BridgeHalf - 0.1f) * (BridgeHalf - 0.1f));
            float zOut = Mathf.Sqrt((PoolR + 0.15f) * (PoolR + 0.15f) - (BridgeHalf - 0.1f) * (BridgeHalf - 0.1f));
            for (int i = 0; i < 4; i++)
            {
                // each bridge runs along its local +Z, arching over the water
                var b = Group(g, "Bridge " + (i + 1));
                b.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
                GameObject d = MeshObj(b, "Bridge Deck", deck, mStone, Vector3.zero, Quaternion.identity, true);
                d.GetComponent<MeshRenderer>().sharedMaterials = new[] { mStone, mWhite };
                for (int s = -1; s <= 1; s += 2)
                {
                    var path = new List<Vector3>();
                    for (int k = 0; k <= 16; k++)
                    {
                        float z = Mathf.Lerp(zIn, zOut, k / 16f);
                        path.Add(new Vector3(s * (BridgeHalf - 0.1f), ArchY(z), z));
                    }
                    Railing(b, "Bridge Railing", path, false, 1.05f, true);
                    Lamp(b, new Vector3(s * (BridgeHalf + 0.7f), 0f, BridgeOut + 0.6f));
                }
            }
        }

        private static float ArchY(float z) => 0.02f + 0.55f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((z - BridgeIn) / (BridgeOut - BridgeIn)));

        /// <summary>Arched bridge deck (stone walking surface in submesh 0, white sides and underside in submesh 1).</summary>
        private static Mesh ArchDeck()
        {
            var k = new MeshKit(2);
            const int n = 24;
            const float thick = 0.35f;
            float w = BridgeHalf;
            for (int i = 0; i < n; i++)
            {
                float za = Mathf.Lerp(BridgeIn, BridgeOut, i / (float)n), zb = Mathf.Lerp(BridgeIn, BridgeOut, (i + 1) / (float)n);
                float ya = ArchY(za), yb = ArchY(zb);
                Vector3 up = new Vector3(0f, zb - za, -(yb - ya)).normalized;
                k.Sub = 0;
                Face4(k, new Vector3(-w, ya, za), new Vector3(w, ya, za), new Vector3(w, yb, zb), new Vector3(-w, yb, zb), up, 2f);
                k.Sub = 1;
                Face4(k, new Vector3(-w, ya - thick, za), new Vector3(w, ya - thick, za), new Vector3(w, yb - thick, zb), new Vector3(-w, yb - thick, zb), -up, 2f);
                for (int s = -1; s <= 1; s += 2)
                    Face4(k, new Vector3(s * w, ya - thick, za), new Vector3(s * w, yb - thick, zb), new Vector3(s * w, yb, zb), new Vector3(s * w, ya, za), new Vector3(s, 0f, 0f), 2f);
            }
            for (int e = 0; e < 2; e++)
            {
                float z = e == 0 ? BridgeIn : BridgeOut, y = ArchY(z);
                Face4(k, new Vector3(-w, y - thick, z), new Vector3(w, y - thick, z), new Vector3(w, y, z), new Vector3(-w, y, z), new Vector3(0f, 0f, e == 0 ? -1f : 1f), 2f);
            }
            return SaveMesh("World_Bridge", k.ToMesh("Bridge"));
        }

        // ------------------------------------------------------------------ tower

        private static void Tower(Transform root)
        {
            var t = Group(root, "Tower");

            // mall podium: shop fronts, a glass floor, a wrap-around canopy, a cornice and a roof garden
            var podium = MeshKit.RoundRect(28f, 22f, 6f, 6);
            var roof = MeshKit.RoundRect(27.8f, 21.8f, 5.9f, 6);
            var k = new MeshKit(4);
            k.Sub = 0;
            k.Extrude(podium, 0f, 4.6f, 16f, 4.6f, false, false);
            k.Sub = 1;
            k.Extrude(podium, 4.6f, 9f, 16f, 24f, false, false);
            k.Sub = 2;
            k.RingSlab(podium, MeshKit.RoundRect(33.2f, 27.2f, 8.6f, 6), 4.55f, 4.85f, 4f, false);
            k.RingSlab(roof, MeshKit.RoundRect(28.8f, 22.8f, 6.4f, 6), 9f, 9.45f, 4f, true);
            k.Sub = 3;
            k.Cap(roof, 9f, true, 4f);
            Solid(t, "Podium", k, new[] { mShop, facade[0], mWhite, mRoof }, true);
            foreach (Vector3 p in new[] { new Vector3(-11.4f, 9f, -3f), new Vector3(-11.4f, 9f, 3f), new Vector3(11.4f, 9f, -3f), new Vector3(11.4f, 9f, 3f), new Vector3(-4f, 9f, 9.2f), new Vector3(4f, 9f, 9.2f) })
            {
                Planter(t, p, 1f);
                Tree(t, p + Vector3.up * 0.6f, 0.75f, p.z > 5f, false);
            }
            // glowing entrance portal
            Prim(PrimitiveType.Cube, "Entrance Frame", t, new Vector3(0f, 2.3f, -11.04f), new Vector3(6.6f, 4.5f, 0.04f), mCyan);
            Prim(PrimitiveType.Cube, "Entrance Glow", t, new Vector3(0f, 2.25f, -11.1f), new Vector3(6.2f, 4.2f, 0.04f), mWhiteGlow);
            Prim(PrimitiveType.Cube, "Entrance Doors", t, new Vector3(0f, 2.1f, -11.16f), new Vector3(5.8f, 4f, 0.04f), mDoorGlass);

            // body, sky lobby, belts, crown and spire
            var body = MeshKit.RoundRect(18f, 15f, 5f, 8);
            var crown = MeshKit.RoundRect(14f, 11f, 4f, 8);
            k = new MeshKit(4);
            k.Sub = 0;
            k.Extrude(body, 9f, 78f, 16f, 24f, false, false);
            k.Extrude(crown, 78f, 88f, 16f, 24f, false, false);
            k.Sub = 1;
            k.RingFace(crown, body, 78f, true, 4f);
            k.Cap(crown, 88f, true, 4f);
            foreach (float y in new[] { 39f, 52f, 65f }) k.RingSlab(body, MeshKit.RoundRect(18.3f, 15.3f, 5.15f, 8), y, y + 0.3f, 4f, false);
            k.Lathe(new List<Vector2> { new Vector2(0f, 88f), new Vector2(1.3f, 88f), new Vector2(1.1f, 91f), new Vector2(0.45f, 99f), new Vector2(0.1f, 106f), new Vector2(0f, 106.3f) }, 24, 4f, 4f);
            k.Sub = 2;
            k.RingSlab(body, MeshKit.RoundRect(18.5f, 15.5f, 5.25f, 8), 77.3f, 77.7f, 4f, false);
            k.RingSlab(crown, MeshKit.RoundRect(14.5f, 11.5f, 4.25f, 8), 87.3f, 87.7f, 4f, false);
            k.Sub = 3;
            k.RingSlab(body, MeshKit.RoundRect(18.4f, 15.4f, 5.2f, 8), H, H + 4.2f, 4f, false);
            Solid(t, "Tower Body", k, new[] { facade[0], mWhite, mCyan, mLobby }, true);
            Prim(PrimitiveType.Sphere, "Beacon", t, new Vector3(0f, 106.5f, 0f), Vector3.one * 0.7f, mPink);
            GameObject halo = MeshObj(t, "Crown Halo", haloMesh, mNeon, new Vector3(0f, 82f, 0f), Quaternion.identity, false);
            halo.transform.localScale = Vector3.one * 10.5f;
            Mover(halo, Vector3.zero, 0f, 0f, 0f, new Vector3(0f, 8f, 0f));

            // curved LOOPLAND screen above the podium (the arc meets the facade at its ends)
            var housing = MeshKit.Arc(ScreenC, 15f, -120f, -60f, 48);
            housing.Add(new Vector2(7.5f, -5.5f));
            housing.Add(new Vector2(-7.5f, -5.5f));
            k = new MeshKit(3);
            k.Sub = 0;
            k.Extrude(housing, 10f, 22f, 4f, 4f, true, true);
            k.Sub = 1;
            k.CurvedPanel(ScreenC, 15.03f, -118f, -62f, 10.55f, 21.45f, 48);
            k.Sub = 2;
            k.Slab(ScreenC, a => 14.7f, a => 15.12f, -120.5f, -59.5f, 48, 22.12f, 0.12f, 4f);
            k.Slab(ScreenC, a => 14.7f, a => 15.12f, -120.5f, -59.5f, 48, 10f, 0.12f, 4f);
            Solid(t, "LOOPLAND Screen", k, new[] { mDark, mScreen, mCyan }, false);

            // light spine running up the front, above the sky lobby
            float s0 = H + 4.6f, s1 = 76f;
            GameObject spine = Prim(PrimitiveType.Cube, "Light Spine", t, new Vector3(0f, (s0 + s1) * 0.5f, -7.62f), new Vector3(2.4f, s1 - s0, 0.3f), mSpine);
            Mover(spine, Vector3.zero, 0f, 0f, 0f, Vector3.zero, spine.GetComponent<Renderer>(), new Vector2(0f, -0.08f));

            // LOOPLAND on the back, with the neon infinity above it
            TextMeshPro sign = Text(t, "LOOPLAND Sign", "<b>LOOPLAND</b>", new Vector3(0f, 50f, 7.56f), Quaternion.Euler(0f, 180f, 0f), new Vector2(7.6f, 2.4f), 26f, Color.white);
            sign.enableVertexGradient = true;
            sign.colorGradient = new VertexGradient(Hex("FFE14D"), Hex("00E5FF"), Hex("FF3DCB"), Hex("B07CFF"));
            sign.fontSharedMaterial = SignMaterial(sign);
            MeshObj(t, "Infinity Logo", Ribbon("World_Infinity_Logo", Lemniscate(3.1f, 160), true, 0.5f), mRibbon, new Vector3(0f, 56.5f, 7.62f), Quaternion.Euler(-90f, 0f, 0f), false);

            // billboards on the podium
            Billboard(t, "Podium Billboard", new Vector3(0f, 6.8f, -11.08f), Quaternion.identity, new Vector2(15000f, 3700f), "GOOD PEOPLE.\nBETTER PLACES.", LoopLandArt.LogoInfinity, Hex("00E5FF"));
            Billboard(t, "Podium Screen West", new Vector3(-14.08f, 6.8f, 0f), Quaternion.Euler(0f, 90f, 0f), new Vector2(9400f, 3600f), "ROLL. BUILD.\nLOOP AGAIN.", LoopLandArt.IconDice, Hex("FF3DCB"));
            Billboard(t, "Podium Screen East", new Vector3(14.08f, 6.8f, 0f), Quaternion.Euler(0f, -90f, 0f), new Vector2(9400f, 3600f), "PEOPLE. PLACES.\nPLAY.", LoopLandArt.IconPeople, Hex("B07CFF"));
            Billboard(t, "Podium Screen North", new Vector3(0f, 6.8f, 11.08f), Quaternion.Euler(0f, 180f, 0f), new Vector2(15000f, 3700f), "SAME PEOPLE.\nNEW PLACES.", LoopLandArt.IconHeart, Hex("FFE14D"));
        }

        // ------------------------------------------------------------------ The Loop: sky terrace + two ring skyways

        private static void Skyway(Transform root)
        {
            var s = Group(root, "The Loop");
            var k = new MeshKit(3); // 0 walking surface, 1 white structure, 2 LED
            k.Sub = 1;
            k.Slab(Vector2.zero, a => 0f, a => HubR, 0f, 360f, 160, H, 1f, 4f, false, true, 0);
            for (int side = -1; side <= 1; side += 2)
            {
                var c = new Vector2(side * RingC, 0f);
                k.Slab(c, a => RingIn, a => RingOuter(c, a), 0f, 360f, 180, H - 0.002f, 0.9f, 4f, true, true, 0);
            }
            // LED strips on the fascias (skipping where the rings join the terrace, the elevator bridge and the waterfall spouts)
            k.Sub = 2;
            float liftA0 = LiftGapAngle(BridgeRailX(true)), liftA1 = LiftGapAngle(BridgeRailX(false));
            foreach (Vector2 arc in new[] { new Vector2(19f, 161f), new Vector2(199f, liftA0 - 1f), new Vector2(liftA1 + 1f, 341f) })
                k.Slab(Vector2.zero, a => HubR - 0.04f, a => HubR + 0.06f, arc.x, arc.y, 64, H - 0.38f, 0.12f, 4f);
            for (int side = -1; side <= 1; side += 2)
            {
                var c = new Vector2(side * RingC, 0f);
                float far = side > 0 ? 0f : 180f;
                k.Slab(c, a => RingIn - 0.06f, a => RingIn + 0.04f, 0f, 360f, 160, H - 0.38f, 0.12f, 4f);
                foreach (Vector2 arc in new[] { new Vector2(far - 155f, far - 117f), new Vector2(far - 103f, far + 103f), new Vector2(far + 117f, far + 155f) })
                    k.Slab(c, a => RingOut - 0.04f, a => RingOut + 0.06f, arc.x, arc.y, 64, H - 0.38f, 0.12f, 4f);
            }
            // downlights under the terrace
            k.Sub = 2;
            for (int i = 0; i < 24; i++)
            {
                Vector2 p = new Vector2(Mathf.Cos(i * 15f * Mathf.Deg2Rad), Mathf.Sin(i * 15f * Mathf.Deg2Rad)) * 15.5f;
                var disc = MeshKit.Circle(0.22f, 12);
                for (int j = 0; j < disc.Count; j++) disc[j] += p;
                k.Cap(disc, H - 1.005f, false, 1f);
            }
            Solid(s, "Skyway", k, new[] { mPaving, mWhite, mCyan }, true);
            MeshObj(s, "Terrace Glow Ring", Ribbon("World_Terrace_Ring", Circle(HubR - 1.1f, 160), true, 0.22f), mRibbon, new Vector3(0f, H + 0.004f, 0f), Quaternion.identity, false);

            // railings: one continuous barrier around the whole infinity, open only to the elevator bridge
            Vector2 j0 = Junction(1f, true);
            float aj = Mathf.Atan2(j0.y, j0.x) * Mathf.Rad2Deg;
            float rr = HubR - 0.2f;
            Railing(s, "Terrace Railing", MeshKit.At(MeshKit.Arc(Vector2.zero, rr, aj, 180f - aj, 60), H), false, 1.1f, true);
            Railing(s, "Terrace Railing", MeshKit.At(MeshKit.Arc(Vector2.zero, rr, 180f + aj, liftA0, 40), H), false, 1.1f, true);
            Railing(s, "Terrace Railing", MeshKit.At(MeshKit.Arc(Vector2.zero, rr, liftA1, 360f - aj, 20), H), false, 1.1f, true);
            for (int side = -1; side <= 1; side += 2)
            {
                var c = new Vector2(side * RingC, 0f);
                Vector2 j = Junction(side, true);
                float ja = Mathf.Atan2(j.y, j.x - c.x) * Mathf.Rad2Deg; // junction angle seen from the ring centre
                float a0 = side > 0 ? -ja : ja, a1 = side > 0 ? ja : 360f - ja;
                Railing(s, "Ring Railing", MeshKit.At(MeshKit.Arc(c, RingOut - 0.2f, a0, a1, 110), H), false, 1.1f, true);
                Railing(s, "Ring Railing", MeshKit.At(MeshKit.Arc(c, RingIn + 0.2f, 0f, 360f, 120), H), true, 1.1f, true);
                RingDressing(s, c, side);
            }

            // terrace garden behind the tower, and a music kiosk by the game
            foreach (Vector3 p in new[] { new Vector3(-7f, H, 13f), new Vector3(7f, H, 13f), new Vector3(0f, H, 15.6f) })
            {
                Planter(s, p, 1.1f);
                Tree(s, p + Vector3.up * 0.6f, 0.85f, p.x == 0f, false);
            }
            Bench(s, new Vector3(-3.6f, H, 14.6f), Vector3.forward);
            Bench(s, new Vector3(3.6f, H, 14.6f), Vector3.forward);
            Kiosk(s, "Loop Kiosk", new Vector3(11.8f, H, -8.5f), Quaternion.Euler(0f, 90f, 0f), "<b>THE LOOP</b>\n<size=70%>Walk the rings, then play!</size>");
        }

        /// <summary>Lamps, planters with trees, benches, support columns and two waterfalls for one ring.</summary>
        private static void RingDressing(Transform s, Vector2 c, int side)
        {
            float far = side > 0 ? 0f : 180f;
            for (int i = -4; i <= 4; i++)
            {
                float a = (far + i * 30f) * Mathf.Deg2Rad;
                Lamp(s, new Vector3(c.x + Mathf.Cos(a) * 13.15f, H, Mathf.Sin(a) * 13.15f));
            }
            for (int i = -2; i <= 2; i++)
            {
                float a = (far + i * 36f) * Mathf.Deg2Rad;
                var p = new Vector3(c.x + Mathf.Cos(a) * 16.1f, H, Mathf.Sin(a) * 16.1f);
                Planter(s, p, 0.9f);
                Tree(s, p + Vector3.up * 0.6f, 0.8f, i % 2 != 0, false);
                if (i == 2) continue;
                float b = (far + i * 36f + 18f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b));
                Bench(s, new Vector3(c.x, H, 0f) + dir * 15.6f, dir);
            }
            // support columns: three in the pool, three on the lawn
            foreach (float deg in new[] { 0f, 55f, -55f, 105f, -105f })
            {
                float a = (far + (side > 0 ? deg : -deg)) * Mathf.Deg2Rad;
                Vector2 p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 14f;
                Column(s, p, p.magnitude < PoolR ? -0.9f : 0f, H - 0.9f);
            }
            // waterfalls pour from spouts on the outer fascia into the pool
            foreach (float deg in new[] { 110f, -110f })
            {
                float a = (far + (side > 0 ? deg : -deg)) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Waterfall(s, new Vector3(c.x, 0f, c.y) + dir * RingOut, dir);
            }
        }

        private static float RingOuter(Vector2 c, float deg)
        {
            var d = new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
            const float r = HubR - 0.03f;
            if ((c + d * RingOut).magnitude >= r) return RingOut;
            float b = Vector2.Dot(c, d), disc = b * b - (c.sqrMagnitude - r * r);
            if (disc < 0f) return RingOut;
            return Mathf.Max(RingIn + 0.5f, -b - Mathf.Sqrt(disc));
        }

        /// <summary>Where the terrace railing meets a ring's outer railing.</summary>
        private static Vector2 Junction(float side, bool top)
        {
            const float rh = HubR - 0.2f, rr = RingOut - 0.2f;
            float x = (RingC * RingC + rh * rh - rr * rr) / (2f * RingC);
            float z = Mathf.Sqrt(Mathf.Max(0f, rh * rh - x * x));
            return new Vector2(side * x, top ? z : -z);
        }

        private static float BridgeRailX(bool left) => LiftXZ.x + (left ? -1.65f : 1.65f);

        private static float LiftGapAngle(float x)
        {
            float r = HubR - 0.2f;
            return 360f + Mathf.Atan2(-Mathf.Sqrt(r * r - x * x), x) * Mathf.Rad2Deg;
        }

        private static void Column(Transform parent, Vector2 p, float y0, float y1)
        {
            var k = new MeshKit(2);
            float h = y1 - y0;
            k.Lathe(new List<Vector2>
            {
                new Vector2(0f, y0), new Vector2(0.85f, y0), new Vector2(0.8f, y0 + 0.5f), new Vector2(0.62f, y0 + h * 0.72f),
                new Vector2(0.7f, y1 - 1.6f), new Vector2(1.5f, y1 - 0.12f), new Vector2(1.5f, y1), new Vector2(0f, y1)
            }, 28, 3f, 3f);
            k.Sub = 1;
            k.Lathe(new List<Vector2> { new Vector2(0.6f, y1 - 2.6f), new Vector2(0.72f, y1 - 2.6f), new Vector2(0.72f, y1 - 2.45f), new Vector2(0.6f, y1 - 2.45f) }, 28, 3f, 3f);
            GameObject go = Solid(parent, "Column", k, new[] { mWhite, mCyan }, true);
            go.transform.localPosition = new Vector3(p.x, 0f, p.y);
        }

        private static void Waterfall(Transform parent, Vector3 edge, Vector3 outward)
        {
            var w = Group(parent, "Waterfall");
            w.localPosition = new Vector3(edge.x, 0f, edge.z);
            w.localRotation = Quaternion.LookRotation(outward);
            Prim(PrimitiveType.Cube, "Spout", w, new Vector3(0f, H - 0.45f, 0.25f), new Vector3(3.4f, 0.3f, 0.7f), mWhite);
            Prim(PrimitiveType.Cube, "Spout Glow", w, new Vector3(0f, H - 0.62f, 0.25f), new Vector3(3.3f, 0.04f, 0.6f), mCyan);
            // the sheet follows the ring's curve and arcs outward as it falls
            var k = new MeshKit();
            const int nx = 10, ny = 24;
            float top = H - 0.6f, bottom = -0.25f, fall = top - bottom, half = 1.5f / RingOut;
            var id = new int[nx + 1, ny + 1];
            for (int j = 0; j <= ny; j++)
            {
                float f = j / (float)ny, r = RingOut + 0.55f + 1.7f * Mathf.Sqrt(f);
                for (int i = 0; i <= nx; i++)
                {
                    float a = Mathf.Lerp(-half, half, i / (float)nx);
                    id[i, j] = k.Vert(new Vector3(r * Mathf.Sin(a), top - fall * f, r * Mathf.Cos(a) - RingOut), new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)), new Vector2(i / (float)nx * 2f, f * fall / 5f));
                }
            }
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++) k.Quad(id[i, j], id[i + 1, j], id[i + 1, j + 1], id[i, j + 1], Vector3.forward);
            GameObject sheet = MeshObj(w, "Falling Water", SaveMesh("World_Waterfall", k.ToMesh("Waterfall")), mFalls, Vector3.zero, Quaternion.identity, false);
            sheet.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Mover(sheet, Vector3.zero, 0f, 0f, 0f, Vector3.zero, sheet.GetComponent<Renderer>(), new Vector2(0f, -0.55f));
            Fx("Spray", w, new Vector3(0f, top, 0.8f), Quaternion.identity, 1.2f, 0.6f, 0.35f, 25f, 0f, true, true, ParticleSystemShapeType.Sphere, 1.2f, 0.4f, 120, Color.white, Hex("BFE9FF"), 0f);
            Fx("Splash", w, new Vector3(0f, 0.1f, 2.25f), Quaternion.identity, 1.8f, 2.2f, 1.1f, 32f, 0f, true, true, ParticleSystemShapeType.Sphere, 1.4f, 0.25f, 160, Color.white, Hex("BFE9FF"), 0f);
            Sound(w, "Waterfall Sound", new Vector3(0f, 1.5f, 2.2f), Clip("Water_Fall", AudioClipLoadType.CompressedInMemory), 0.75f, true, 4f, 55f);
        }

        // ------------------------------------------------------------------ the glass elevator

        private static void Lift(Transform root)
        {
            var shaft = Group(root, "Glass Elevator");
            shaft.localPosition = new Vector3(LiftXZ.x, 0f, LiftXZ.y);
            var lift = UdonSharpUndo.AddComponent<LoopLandLift>(shaft.gameObject);
            made.Add(lift);
            float top = H + 3.6f;

            // shaft: corner columns, glass walls, door portals (front at the plaza, back at The Loop), frame rings, roof
            var k = new MeshKit(3);
            k.Sub = 0;
            for (int c = 0; c < 4; c++)
                k.Box(new Vector3(((c & 1) == 0 ? -1f : 1f) * 1.84f, top * 0.5f, ((c & 2) == 0 ? -1f : 1f) * 1.69f), new Vector3(0.16f, top, 0.16f), Quaternion.identity, 1f);
            for (float y = 3.25f; y < top - 0.5f; y += 3.25f)
            {
                k.Box(new Vector3(-1.88f, y, 0f), new Vector3(0.05f, 0.08f, 3.3f), Quaternion.identity, 1f);
                k.Box(new Vector3(1.88f, y, 0f), new Vector3(0.05f, 0.08f, 3.3f), Quaternion.identity, 1f);
                k.Box(new Vector3(0f, y, -1.74f), new Vector3(3.5f, 0.08f, 0.05f), Quaternion.identity, 1f);
                if (y < H - 0.2f || y > H + 3.1f) k.Box(new Vector3(0f, y, 1.74f), new Vector3(3.5f, 0.08f, 0.05f), Quaternion.identity, 1f);
            }
            foreach (float y in new[] { 0f, H })
            {
                float z = y == 0f ? -1.72f : 1.72f;
                k.Box(new Vector3(-1.25f, y + 1.45f, z), new Vector3(1.06f, 2.9f, 0.06f), Quaternion.identity, 1f);
                k.Box(new Vector3(1.25f, y + 1.45f, z), new Vector3(1.06f, 2.9f, 0.06f), Quaternion.identity, 1f);
                k.Box(new Vector3(0f, y + 2.65f, z), new Vector3(1.44f, 0.5f, 0.06f), Quaternion.identity, 1f);
            }
            k.Box(new Vector3(0f, top + 0.12f, 0f), new Vector3(4f, 0.24f, 3.7f), Quaternion.identity, 1f);
            k.Sub = 1;
            k.Box(new Vector3(-1.86f, top * 0.5f, 0f), new Vector3(0.03f, top, 3.3f), Quaternion.identity, 1f);
            k.Box(new Vector3(1.86f, top * 0.5f, 0f), new Vector3(0.03f, top, 3.3f), Quaternion.identity, 1f);
            k.Box(new Vector3(0f, (2.9f + top) * 0.5f, -1.71f), new Vector3(3.55f, top - 2.9f, 0.03f), Quaternion.identity, 1f);
            k.Box(new Vector3(0f, H * 0.5f, 1.71f), new Vector3(3.55f, H - 0.05f, 0.03f), Quaternion.identity, 1f);
            k.Box(new Vector3(0f, (H + 2.9f + top) * 0.5f, 1.71f), new Vector3(3.55f, top - H - 2.9f, 0.03f), Quaternion.identity, 1f);
            k.Sub = 2;
            k.Box(new Vector3(0f, top - 0.03f, -1.86f), new Vector3(4f, 0.06f, 0.04f), Quaternion.identity, 1f);
            k.Box(new Vector3(0f, top - 0.03f, 1.86f), new Vector3(4f, 0.06f, 0.04f), Quaternion.identity, 1f);
            k.Box(new Vector3(-2.01f, top - 0.03f, 0f), new Vector3(0.04f, 0.06f, 3.7f), Quaternion.identity, 1f);
            k.Box(new Vector3(2.01f, top - 0.03f, 0f), new Vector3(0.04f, 0.06f, 3.7f), Quaternion.identity, 1f);
            Solid(shaft, "Shaft", k, new[] { mSteel, mGlass, mCyan }, false);
            var cols = new GameObject("Shaft Walls");
            cols.transform.SetParent(shaft, false);
            AddBox(cols, new Vector3(-1.86f, top * 0.5f, 0f), new Vector3(0.1f, top, 3.5f));
            AddBox(cols, new Vector3(1.86f, top * 0.5f, 0f), new Vector3(0.1f, top, 3.5f));
            AddBox(cols, new Vector3(0f, (2.9f + top) * 0.5f, -1.71f), new Vector3(3.7f, top - 2.9f, 0.1f));
            AddBox(cols, new Vector3(0f, H * 0.5f, 1.71f), new Vector3(3.7f, H, 0.1f));
            AddBox(cols, new Vector3(0f, (H + 2.9f + top) * 0.5f, 1.71f), new Vector3(3.7f, top - H - 2.9f, 0.1f));
            foreach (float y in new[] { 0f, H })
            {
                float z = y == 0f ? -1.72f : 1.72f;
                AddBox(cols, new Vector3(-1.25f, y + 1.45f, z), new Vector3(1.06f, 2.9f, 0.1f));
                AddBox(cols, new Vector3(1.25f, y + 1.45f, z), new Vector3(1.06f, 2.9f, 0.1f));
                AddBox(cols, new Vector3(0f, y + 2.65f, z), new Vector3(1.44f, 0.5f, 0.1f));
            }

            // landing doors (plaza: front, The Loop: back)
            Transform gL = Door(shaft, "Landing Door Plaza L", new Vector3(-0.355f, 1.2f, -1.66f), true);
            Transform gR = Door(shaft, "Landing Door Plaza R", new Vector3(0.355f, 1.2f, -1.66f), false);
            Transform tL = Door(shaft, "Landing Door Loop L", new Vector3(-0.355f, H + 1.2f, 1.66f), true);
            Transform tR = Door(shaft, "Landing Door Loop R", new Vector3(0.355f, H + 1.2f, 1.66f), false);

            // the car: glass sides, steel frame, doors front and back, lit ceiling, handrails, standing spots
            var car = Group(shaft, "Elevator Car");
            car.localPosition = new Vector3(0f, 0.02f, 0f);
            k = new MeshKit(4);
            k.Sub = 2;
            k.Box(new Vector3(0f, -0.06f, 0f), new Vector3(3.3f, 0.12f, 2.98f), Quaternion.identity, 1f);
            k.Sub = 0;
            k.Box(new Vector3(0f, 2.86f, 0f), new Vector3(3.3f, 0.12f, 2.98f), Quaternion.identity, 1f);
            for (int c = 0; c < 4; c++)
                k.Box(new Vector3(((c & 1) == 0 ? -1f : 1f) * 1.6f, 1.4f, ((c & 2) == 0 ? -1f : 1f) * 1.44f), new Vector3(0.1f, 2.8f, 0.1f), Quaternion.identity, 1f);
            for (int z = -1; z <= 1; z += 2)
            {
                k.Box(new Vector3(-1.16f, 1.4f, z * 1.45f), new Vector3(0.88f, 2.8f, 0.05f), Quaternion.identity, 1f);
                k.Box(new Vector3(1.16f, 1.4f, z * 1.45f), new Vector3(0.88f, 2.8f, 0.05f), Quaternion.identity, 1f);
                k.Box(new Vector3(0f, 2.6f, z * 1.45f), new Vector3(1.44f, 0.4f, 0.05f), Quaternion.identity, 1f);
            }
            k.Tube(new List<Vector3> { new Vector3(-1.52f, 0.95f, -1.2f), new Vector3(-1.52f, 0.95f, 1.2f) }, false, 0.025f, 8);
            k.Tube(new List<Vector3> { new Vector3(1.52f, 0.95f, -1.2f), new Vector3(1.52f, 0.95f, 1.2f) }, false, 0.025f, 8);
            k.Sub = 1;
            k.Box(new Vector3(-1.62f, 1.4f, 0f), new Vector3(0.03f, 2.8f, 2.8f), Quaternion.identity, 1f);
            k.Box(new Vector3(1.62f, 1.4f, 0f), new Vector3(0.03f, 2.8f, 2.8f), Quaternion.identity, 1f);
            k.Sub = 3;
            k.Box(new Vector3(0f, 2.79f, 0f), new Vector3(2.4f, 0.02f, 2f), Quaternion.identity, 1f);
            Solid(car, "Car Body", k, new[] { mSteel, mGlass, mDark, mWhiteGlow }, false);
            var carCols = new GameObject("Car Walls");
            carCols.transform.SetParent(car, false);
            AddBox(carCols, new Vector3(0f, -0.06f, 0f), new Vector3(3.3f, 0.12f, 2.98f));
            AddBox(carCols, new Vector3(-1.62f, 1.4f, 0f), new Vector3(0.08f, 2.8f, 2.98f));
            AddBox(carCols, new Vector3(1.62f, 1.4f, 0f), new Vector3(0.08f, 2.8f, 2.98f));
            for (int z = -1; z <= 1; z += 2)
            {
                AddBox(carCols, new Vector3(-1.16f, 1.4f, z * 1.45f), new Vector3(0.88f, 2.8f, 0.08f));
                AddBox(carCols, new Vector3(1.16f, 1.4f, z * 1.45f), new Vector3(0.88f, 2.8f, 0.08f));
            }
            Transform fL = Door(car, "Car Door Front L", new Vector3(-0.355f, 1.2f, -1.4f), true);
            Transform fR = Door(car, "Car Door Front R", new Vector3(0.355f, 1.2f, -1.4f), false);
            Transform rL = Door(car, "Car Door Back L", new Vector3(-0.355f, 1.2f, 1.4f), true);
            Transform rR = Door(car, "Car Door Back R", new Vector3(0.355f, 1.2f, 1.4f), false);

            var seats = new VRC.SDK3.Components.VRCStation[6];
            for (int i = 0; i < 6; i++)
            {
                var spot = new GameObject("Rider Spot " + (i + 1));
                spot.transform.SetParent(car, false);
                spot.transform.localPosition = new Vector3(-0.8f + 0.8f * (i % 3), 0.02f, i < 3 ? 0.05f : 0.8f);
                spot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // face the front glass and watch the plaza drop away
                var st = spot.AddComponent<VRC.SDK3.Components.VRCStation>();
                st.PlayerMobility = VRC.SDKBase.VRCStation.Mobility.ImmobilizeForVehicle; // the station moves
                st.seated = false;                   // ride standing
                st.disableStationExit = true;        // no jumping out mid-shaft
                st.canUseStationFromStation = false;
                foreach (Collider col in spot.GetComponents<Collider>())
                {
                    col.isTrigger = true;
                    if (col is BoxCollider bc) bc.size = Vector3.one * 0.3f;
                }
                var seat = UdonSharpUndo.AddComponent<LoopLandSeat>(spot);
                seat.lift = lift;
                seat.index = i;
                made.Add(seat);
                seats[i] = st;
            }

            // displays and buttons: inside the car, and at both landings
            var displays = new List<TMP_Text>();
            displays.Add(Display(car, new Vector3(0f, 2.6f, -1.42f), Quaternion.Euler(0f, 180f, 0f)));
            displays.Add(Display(shaft, new Vector3(0f, 2.65f, -1.76f), Quaternion.identity));
            displays.Add(Display(shaft, new Vector3(0f, H + 2.65f, 1.76f), Quaternion.Euler(0f, 180f, 0f)));
            RectTransform panel = UCanvas(car, "Ride Panel", new Vector3(-1.16f, 1.35f, -1.42f), Quaternion.Euler(0f, 180f, 0f), new Vector2(760f, 1000f));
            PanelBack(panel, new Vector2(760f, 1000f), Hex("00E5FF"));
            UText(panel, "Title", "<b>ELEVATOR</b>", new Vector2(0f, 400f), new Vector2(700f, 110f), 64f, Color.white);
            GoButton(panel, "Up", "THE LOOP", new Vector2(0f, 130f), new Vector2(640f, 330f), Hex("E0218A"), lift, "_GoLoop", true);
            GoButton(panel, "Down", "PLAZA", new Vector2(0f, -280f), new Vector2(640f, 330f), Hex("1F4FD8"), lift, "_GoPlaza", false);
            CallPanel(shaft, new Vector3(1.25f, 1.35f, -1.76f), Quaternion.identity, lift, "_GoPlaza", "CALL", true);
            CallPanel(shaft, new Vector3(-1.25f, H + 1.35f, 1.76f), Quaternion.Euler(0f, 180f, 0f), lift, "_GoLoop", "CALL", false);
            RectTransform sign = UCanvas(shaft, "Elevator Sign", new Vector3(0f, 4.3f, -1.75f), Quaternion.identity, new Vector2(3400f, 1300f), false);
            UImg(sign, "Logo", new Vector2(-1180f, 0f), new Vector2(1000f, 1000f), LoopLandArt.LogoInfinity, Color.white, false).preserveAspect = true;
            UText(sign, "Text", "<b>GLASS ELEVATOR</b>\n<size=62%>UP TO THE LOOP</size>", new Vector2(420f, 0f), new Vector2(2400f, 1150f), 330f, Color.white);

            // sound
            AudioSource hum = Sound(car, "Car Hum", new Vector3(0f, 1.4f, 0f), Clip("Lift_Hum", AudioClipLoadType.DecompressOnLoad), 0.5f, true, 1.5f, 16f, true, false);
            AudioSource fx = Sound(car, "Car Sounds", new Vector3(0f, 2.2f, 0f), null, 1f, true, 1.5f, 16f, false, false);
            AudioSource fx0 = Sound(shaft, "Plaza Landing Sounds", new Vector3(0f, 2.5f, -2f), null, 1f, true, 2f, 22f, false, false);
            AudioSource fx1 = Sound(shaft, "Loop Landing Sounds", new Vector3(0f, H + 2.5f, 2f), null, 1f, true, 2f, 22f, false, false);

            lift.car = car;
            lift.floorHeights = new[] { 0.02f, H + 0.02f };
            lift.floorNames = new[] { "PLAZA", "THE LOOP" };
            lift.floorDoorSide = new[] { 0, 1 };
            lift.carDoors = new[] { fL, fR, rL, rR };
            lift.landingDoors = new[] { gL, gR, tL, tR };
            lift.seats = seats;
            lift.displays = displays.ToArray();
            lift.carHum = hum;
            lift.carFx = fx;
            lift.landingFx = new[] { fx0, fx1 };
            lift.dingClip = Clip("Lift_Ding", AudioClipLoadType.DecompressOnLoad);
            lift.doorClip = Clip("Lift_Doors", AudioClipLoadType.DecompressOnLoad);

            // bridge from the shaft's back door to the terrace
            Prim(PrimitiveType.Cube, "Elevator Bridge", root, new Vector3(LiftXZ.x, H - 0.21f, -17.85f), new Vector3(3.4f, 0.4f, 3.8f), mStone, true);
            float rr = HubR - 0.2f;
            foreach (bool left in new[] { true, false })
            {
                float x = BridgeRailX(left);
                float zEnd = -Mathf.Sqrt(rr * rr - x * x);
                Railing(root, "Elevator Bridge Railing", new List<Vector3> { new Vector3(x, H, LiftXZ.y + 1.8f), new Vector3(x, H, zEnd) }, false, 1.1f, true);
            }
        }

        private static Transform Door(Transform parent, string name, Vector3 pos, bool left)
        {
            var d = Group(parent, name);
            d.localPosition = pos;
            Prim(PrimitiveType.Cube, "Glass", d, Vector3.zero, new Vector3(0.72f, 2.4f, 0.03f), mDoorGlass, true);
            Prim(PrimitiveType.Cube, "Frame Top", d, new Vector3(0f, 1.17f, 0f), new Vector3(0.72f, 0.06f, 0.04f), mSteel);
            Prim(PrimitiveType.Cube, "Frame Bottom", d, new Vector3(0f, -1.16f, 0f), new Vector3(0.72f, 0.08f, 0.04f), mSteel);
            Prim(PrimitiveType.Cube, "Frame Edge", d, new Vector3(left ? 0.33f : -0.33f, 0f, 0f), new Vector3(0.06f, 2.4f, 0.04f), mSteel);
            return d;
        }

        private static TMP_Text Display(Transform parent, Vector3 pos, Quaternion rot)
        {
            RectTransform c = UCanvas(parent, "Floor Display", pos, rot, new Vector2(1300f, 380f), false);
            UImg(c, "Back", Vector2.zero, new Vector2(1300f, 380f), LoopLandArt.Round, new Color(0.02f, 0.02f, 0.06f, 0.95f));
            TextMeshProUGUI t = UText(c, "Floor", "PLAZA", Vector2.zero, new Vector2(1220f, 340f), 150f, Hex("00E5FF"));
            t.fontStyle = FontStyles.Bold;
            return t;
        }

        private static void PanelBack(RectTransform c, Vector2 px, Color glow)
        {
            UImg(c, "Glow", Vector2.zero, px + new Vector2(30f, 30f), LoopLandArt.Glow, glow);
            UImg(c, "Back", Vector2.zero, px, LoopLandArt.Panel, Color.white);
        }

        private static void GoButton(RectTransform c, string name, string label, Vector2 pos, Vector2 size, Color color, LoopLandLift lift, string evt, bool up)
        {
            TextMeshProUGUI lbl = UButton(c, name, label, pos, size, color, lift, evt, 72f);
            lbl.rectTransform.anchoredPosition = new Vector2(0f, -size.y * 0.24f);
            lbl.rectTransform.sizeDelta = new Vector2(size.x - 40f, size.y * 0.4f);
            Image arrow = UImg(lbl.transform.parent, "Arrow", new Vector2(0f, size.y * 0.18f), new Vector2(size.y * 0.42f, size.y * 0.42f), LoopLandArt.IconArrow, Color.white, false);
            arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, up ? 0f : 180f);
        }

        private static void CallPanel(Transform parent, Vector3 pos, Quaternion rot, LoopLandLift lift, string evt, string label, bool up)
        {
            RectTransform c = UCanvas(parent, "Call Button", pos, rot, new Vector2(640f, 760f));
            PanelBack(c, new Vector2(640f, 760f), up ? Hex("00E5FF") : Hex("FF3DCB"));
            UText(c, "Title", "<b>ELEVATOR</b>", new Vector2(0f, 300f), new Vector2(600f, 100f), 60f, Color.white);
            GoButton(c, "Call", label, new Vector2(0f, -60f), new Vector2(520f, 520f), up ? Hex("E0218A") : Hex("1F4FD8"), lift, evt, up);
        }

        // ------------------------------------------------------------------ plaza

        private static void Plaza(Transform root)
        {
            var p = Group(root, "Plaza");
            Fountain(p, new Vector3(0f, 0f, -20.5f));

            // glowing guide line from the front bridge to the elevator door
            var guide = new List<Vector2>();
            for (int i = 0; i <= 20; i++)
            {
                float f = i / 20f;
                guide.Add(new Vector2(Mathf.Lerp(0.8f, LiftXZ.x, f), Mathf.Lerp(-28.2f, -23.6f, f)));
            }
            MeshObj(p, "Elevator Guide Line", Ribbon("World_Guide_Line", guide, false, 0.3f), mRibbon, new Vector3(0f, 0.006f, 0f), Quaternion.identity, false);

            for (int i = 0; i < 30; i++)
            {
                float a = i * 12f + 6f;
                float m = Mathf.Repeat(a, 90f);
                float w = a > 180f ? a - 360f : a;
                if (m < 12f || m > 78f || (w > -82f && w < -50f) || (w > -120f && w < -96f)) continue;
                float r = 25.6f + (float)(rnd.NextDouble() - 0.5) * 1.6f;
                Tree(p, Polar(r, a), 0.95f + (float)rnd.NextDouble() * 0.45f, i % 3 == 0, true);
            }
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f + 15f, w = a > 180f ? a - 360f : a;
                if (w > -82f && w < -50f) continue;
                Lamp(p, Polar(22f, a));
            }
            foreach (float a in new[] { 30f, 60f, 120f, 150f, 210f, 240f, 330f })
            {
                Vector3 pos = Polar(22.6f, a);
                Bench(p, pos, -pos.normalized);
            }
            Hedge(p, new Vector3(-14.7f, 0f, 0f), 9f, false);
            Hedge(p, new Vector3(14.7f, 0f, 0f), 9f, false);
            Hedge(p, new Vector3(0f, 0f, 11.7f), 14f, true);

            // the cream sign from the concept
            var sg = Group(p, "Loop Sign");
            sg.localPosition = new Vector3(-7.5f, 0f, -24.5f);
            sg.localRotation = Quaternion.Euler(0f, -45f, 0f);
            Prim(PrimitiveType.Cube, "Sign Base", sg, new Vector3(0f, 0.15f, 0f), new Vector3(3.4f, 0.3f, 1f), mStone, true);
            Prim(PrimitiveType.Cube, "Sign Frame", sg, new Vector3(0f, 2.75f, 0f), new Vector3(3f, 5f, 0.3f), Std("World_Navy", Hex("1D2340"), 0.3f, 0.6f, Color.black), true);
            RectTransform sc = UCanvas(sg, "Sign", new Vector3(0f, 2.75f, -0.16f), Quaternion.identity, new Vector2(2600f, 4500f), false);
            UImg(sc, "Paper", Vector2.zero, new Vector2(2600f, 4500f), null, Hex("F3E9D7"), false);
            Color ink = Hex("3B5BA9");
            UText(sc, "Line 1", "<i><b>SAME\nPEOPLE.</b></i>", new Vector2(-80f, 1450f), new Vector2(2300f, 1000f), 400f, ink, TextAlignmentOptions.Left);
            UText(sc, "Line 2", "<i><b>NEW\nPLACES.</b></i>", new Vector2(-80f, 400f), new Vector2(2300f, 1000f), 400f, ink, TextAlignmentOptions.Left);
            UText(sc, "Line 3", "<i><b>ALWAYS\nA NEXT\nLOOP.</b></i>", new Vector2(-80f, -900f), new Vector2(2300f, 1400f), 400f, ink, TextAlignmentOptions.Left);
            UImg(sc, "Heart", new Vector2(780f, -1850f), new Vector2(560f, 560f), LoopLandArt.IconHeartOutline, ink, false);

            // direction sign to the elevator
            var ds = Group(p, "Elevator Sign");
            ds.localPosition = new Vector3(3.9f, 0f, -27.4f);
            Prim(PrimitiveType.Cube, "Post", ds, new Vector3(0f, 1.2f, 0f), new Vector3(0.12f, 2.4f, 0.12f), mSteel, true);
            RectTransform dc = UCanvas(ds, "Sign", new Vector3(0f, 2.3f, -0.08f), Quaternion.identity, new Vector2(2400f, 900f), false);
            PanelBack(dc, new Vector2(2400f, 900f), Hex("FF3DCB"));
            UImg(dc, "Arrow", new Vector2(-860f, 0f), new Vector2(560f, 560f), LoopLandArt.IconArrow, Hex("FFE14D"), false).rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
            UText(dc, "Text", "<b>ELEVATOR</b>\n<size=62%>UP TO THE LOOP</size>", new Vector2(250f, 0f), new Vector2(1700f, 780f), 300f, Color.white);
        }

        private static void Fountain(Transform parent, Vector3 pos)
        {
            var f = Group(parent, "Fountain");
            f.localPosition = pos;
            var k = new MeshKit(2);
            k.Lathe(new List<Vector2> { new Vector2(0f, 0f), new Vector2(4.2f, 0f), new Vector2(4.25f, 0.5f), new Vector2(4f, 0.62f), new Vector2(3.8f, 0.6f), new Vector2(3.8f, 0.3f), new Vector2(0f, 0.3f) }, 48, 3f, 3f);
            k.Lathe(new List<Vector2> { new Vector2(0f, 0.3f), new Vector2(0.55f, 0.3f), new Vector2(0.34f, 1f), new Vector2(0.28f, 2.05f), new Vector2(1.5f, 2.25f), new Vector2(1.55f, 2.4f), new Vector2(1.4f, 2.4f), new Vector2(1.4f, 2.3f), new Vector2(0.3f, 2.3f), new Vector2(0.22f, 3f), new Vector2(0f, 3.1f) }, 32, 3f, 3f);
            k.Sub = 1;
            k.Cap(MeshKit.Circle(3.81f, 48), 0.45f, true, 3f);
            k.Cap(MeshKit.Circle(1.41f, 32), 2.36f, true, 3f);
            Solid(f, "Basin", k, new[] { mWhite, mWater }, true);
            ParticleSystem jet = Fx("Jet", f, new Vector3(0f, 3.1f, 0f), Quaternion.Euler(-90f, 0f, 0f), 1.1f, 4.2f, 0.16f, 140f, 0f, true, false, ParticleSystemShapeType.Cone, 0.05f, 1.1f, 400, Color.white, Hex("BFE9FF"), 0f);
            var jetShape = jet.shape;
            jetShape.angle = 9f;
            ParticleSystem ring = Fx("Ring Jets", f, new Vector3(0f, 0.6f, 0f), Quaternion.Euler(-90f, 0f, 0f), 1f, 2.2f, 0.12f, 160f, 0f, true, false, ParticleSystemShapeType.Cone, 3f, 0.9f, 400, Color.white, Hex("BFE9FF"), 0f);
            var ringShape = ring.shape;
            ringShape.angle = 5f;
            ringShape.radiusThickness = 0.12f;
            Fx("Mist", f, new Vector3(0f, 0.8f, 0f), Quaternion.identity, 2.5f, 0.3f, 0.9f, 8f, 0f, true, true, ParticleSystemShapeType.Sphere, 3f, -0.02f, 60, new Color(1f, 1f, 1f, 0.5f), new Color(0.8f, 0.9f, 1f, 0.4f), 0f);
            Sound(f, "Fountain Sound", new Vector3(0f, 1.5f, 0f), Clip("Water_Fountain", AudioClipLoadType.CompressedInMemory), 0.6f, true, 3f, 35f);
        }

        // ------------------------------------------------------------------ skyline, islands

        private static void City(Transform root)
        {
            var c = Group(root, "City");
            Material[] neon = { mCyan, mPink, mGold, mPurple };
            Color[] accents = { Hex("00E5FF"), Hex("FF3DCB"), Hex("FFE14D"), Hex("B07CFF") };
            int boards = 0;
            for (int i = 0; i < 22; i++)
            {
                float a = i * (360f / 22f) + (float)(rnd.NextDouble() - 0.5) * 3f;
                float w = 12f + 2f * rnd.Next(0, 4), d = w * (0.7f + 0.3f * (float)rnd.NextDouble()), h = 36f + 4f * rnd.Next(0, 12);
                bool round = rnd.NextDouble() < 0.3;
                int style = 1 + rnd.Next(3);
                var b = Group(c, "Tower " + (i + 1));
                b.localPosition = Polar(90f + (float)rnd.NextDouble() * 28f, a);
                b.localRotation = Quaternion.LookRotation(-b.localPosition.normalized); // +Z faces the plaza
                if (round) d = w;
                List<Vector2> prof = round ? MeshKit.Circle(w * 0.5f, 40) : MeshKit.RoundRect(w, d, w * 0.22f, 6);
                List<Vector2> belt = round ? MeshKit.Circle(w * 0.5f + 0.25f, 40) : MeshKit.RoundRect(w + 0.5f, d + 0.5f, w * 0.22f + 0.25f, 6);
                var k = new MeshKit(3);
                k.Extrude(prof, 0f, h, 16f, 24f, false, false);
                k.Sub = 2;
                k.RingSlab(prof, belt, h - 0.9f, h - 0.5f, 4f, false);
                k.Sub = 1;
                float hTop = h;
                if (rnd.NextDouble() < 0.55)
                {
                    float w2 = w * 0.62f, d2 = d * 0.62f, h2 = 6f + 3f * rnd.Next(0, 4);
                    List<Vector2> top = round ? MeshKit.Circle(w2 * 0.5f, 40) : MeshKit.RoundRect(w2, d2, w2 * 0.22f, 6);
                    k.RingFace(top, prof, h, true, 4f);
                    k.Sub = 0;
                    k.Extrude(top, h, h + h2, 16f, 24f, false, false);
                    k.Sub = 1;
                    k.Cap(top, h + h2, true, 4f);
                    k.Sub = 2;
                    List<Vector2> topBelt = round ? MeshKit.Circle(w2 * 0.5f + 0.2f, 40) : MeshKit.RoundRect(w2 + 0.4f, d2 + 0.4f, w2 * 0.22f + 0.2f, 6);
                    k.RingSlab(top, topBelt, h + h2 - 0.6f, h + h2 - 0.3f, 4f, false);
                    hTop = h + h2;
                }
                else k.Cap(prof, h, true, 4f);
                k.Sub = 1;
                if (rnd.NextDouble() < 0.4)
                    k.Lathe(new List<Vector2> { new Vector2(0f, hTop), new Vector2(0.7f, hTop), new Vector2(0.15f, hTop + 10f), new Vector2(0f, hTop + 10.2f) }, 12, 2f, 2f);
                if (!round)
                {
                    k.Sub = 2;
                    k.Box(new Vector3(-w * 0.3f, h * 0.5f, d * 0.5f), new Vector3(0.25f, h - 4f, 0.3f), Quaternion.identity, 1f);
                    k.Box(new Vector3(w * 0.3f, h * 0.5f, d * 0.5f), new Vector3(0.25f, h - 4f, 0.3f), Quaternion.identity, 1f);
                }
                Material glow = neon[rnd.Next(neon.Length)];
                Solid(b, "Building", k, new[] { facade[style], mWhite, glow }, true);
                if (!round && boards < 4 && i % 5 == 1)
                {
                    string[] texts = { "GOOD PEOPLE.\nBETTER PLACES.", "LOOP MALL\nOPEN NOW", "SKYLINE\nARCADE", "LOOP CAFE\nVIEWS & VIBES" };
                    Sprite[] art = { LoopLandArt.LogoInfinity, LoopLandArt.IconStore, LoopLandArt.IconDice, LoopLandArt.IconHeart };
                    float bw = Mathf.Min(w * 0.3f * 2f - 1f, 9f);
                    Billboard(b, "Billboard", new Vector3(0f, h * 0.62f, d * 0.5f + 0.08f), Quaternion.Euler(0f, 180f, 0f), new Vector2(bw * 1000f, bw * 420f), texts[boards], art[boards], accents[boards]);
                    boards++;
                }
                if (rnd.NextDouble() < 0.3)
                {
                    GameObject ring = MeshObj(b, "Halo", haloMesh, mNeon, new Vector3(0f, h - 3f, 0f), Quaternion.identity, false);
                    ring.transform.localScale = Vector3.one * (Mathf.Max(w, d) * 0.85f);
                }
            }
            for (int i = 0; i < 18; i++)
            {
                float a = i * 20f + 10f + (float)(rnd.NextDouble() - 0.5) * 6f;
                float w = 14f + 2f * rnd.Next(0, 4), h = 24f + 4f * rnd.Next(0, 8);
                var b = Group(c, "Block " + (i + 1));
                b.localPosition = Polar(135f + (float)rnd.NextDouble() * 16f, a);
                b.localRotation = Quaternion.LookRotation(-b.localPosition.normalized);
                var prof = MeshKit.RoundRect(w, w * 0.8f, w * 0.2f, 4);
                var k = new MeshKit(2);
                k.Extrude(prof, 0f, h, 16f, 24f, false, false);
                k.Sub = 1;
                k.Cap(prof, h, true, 4f);
                Solid(b, "Building", k, new[] { facade[3], mWhite }, false);
            }
        }

        private static void Islands(Transform root)
        {
            var g = Group(root, "Floating Islands");
            float[] angles = { 30f, 100f, 160f, 215f, 320f };
            for (int i = 0; i < angles.Length; i++)
            {
                float size = 5.5f + (float)rnd.NextDouble() * 3f;
                var isl = Group(g, "Island " + (i + 1));
                isl.localPosition = Polar(56f + (float)rnd.NextDouble() * 14f, angles[i]) + Vector3.up * (40f + (float)rnd.NextDouble() * 16f);
                GameObject rock = MeshObj(isl, "Rock", RockMesh("World_Rock_" + i, 31 + i), mRock, Vector3.zero, Quaternion.identity, false);
                rock.transform.localScale = new Vector3(size, size * 1.4f, size);
                var k = new MeshKit();
                k.Lathe(new List<Vector2> { new Vector2(0f, 0f), new Vector2(size * 1.03f, 0f), new Vector2(size * 1.02f, 0.25f), new Vector2(size * 0.6f, 0.55f), new Vector2(0f, 0.65f) }, 32, 3f, 3f);
                Solid(isl, "Grass", k, new[] { mIslandGrass }, false);
                if (i % 2 == 0)
                {
                    // little round tower with a dome and a glowing band
                    k = new MeshKit(3);
                    k.Extrude(MeshKit.Circle(1.6f, 24), 0.4f, 6.5f, 8f, 24f, false, false);
                    k.Sub = 1;
                    k.Lathe(new List<Vector2> { new Vector2(1.75f, 6.5f), new Vector2(1.75f, 6.8f), new Vector2(1.3f, 7.8f), new Vector2(0.5f, 8.5f), new Vector2(0f, 8.7f) }, 24, 3f, 3f);
                    k.Sub = 2;
                    k.RingSlab(MeshKit.Circle(1.6f, 24), MeshKit.Circle(1.75f, 24), 5.2f, 5.45f, 2f, false);
                    GameObject tw = Solid(isl, "Little Tower", k, new[] { facade[2], mWhite, mPink }, false);
                    tw.transform.localPosition = new Vector3(size * 0.25f, 0f, size * 0.15f);
                    Tree(isl, new Vector3(-size * 0.4f, 0.4f, -size * 0.2f), 0.8f, true, false);
                }
                else
                {
                    int trees = 2 + rnd.Next(2);
                    for (int t = 0; t < trees; t++)
                        Tree(isl, Polar(size * 0.45f * (0.3f + 0.7f * (float)rnd.NextDouble()), t * (360f / trees) + 20f) + Vector3.up * 0.4f, 0.8f + (float)rnd.NextDouble() * 0.3f, rnd.NextDouble() < 0.4, false);
                }
                Fx("Falling Sparkles", isl, new Vector3(0f, -size * 0.8f, 0f), Quaternion.identity, 4f, 0.1f, 0.18f, 12f, 0f, true, true, ParticleSystemShapeType.Sphere, size * 0.5f, 0.06f, 80, Hex("00E5FF"), Color.white, 0f);
                Mover(isl.gameObject, Vector3.zero, 0f, 1.2f, 8f + i, new Vector3(0f, 2f, 0f));
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
            var k = new MeshKit(2);
            k.Lathe(new List<Vector2> { new Vector2(0f, 0f), new Vector2(0.18f, 0f), new Vector2(0.16f, 0.25f), new Vector2(0.06f, 0.4f), new Vector2(0.05f, 3.7f), new Vector2(0.3f, 3.75f), new Vector2(0.3f, 3.82f), new Vector2(0f, 3.86f) }, 12, 1f, 1f);
            k.Sub = 1;
            k.Lathe(new List<Vector2> { new Vector2(0f, 3.48f), new Vector2(0.26f, 3.5f), new Vector2(0.24f, 3.75f), new Vector2(0f, 3.75f) }, 12, 1f, 1f);
            Solid(l, "Lamp", k, new[] { mSteel, mWhiteGlow }, false);
            var col = l.gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 1.9f, 0f);
            col.height = 3.8f;
            col.radius = 0.12f;
        }

        private static void Bench(Transform parent, Vector3 pos, Vector3 facing)
        {
            var b = Group(parent, "Bench");
            b.localPosition = pos;
            b.localRotation = Quaternion.LookRotation(-facing); // the sitter looks along `facing`
            Prim(PrimitiveType.Cube, "Seat", b, new Vector3(0f, 0.45f, 0f), new Vector3(1.9f, 0.08f, 0.5f), mTrunk, true);
            Prim(PrimitiveType.Cube, "Backrest", b, new Vector3(0f, 0.78f, 0.24f), new Vector3(1.9f, 0.45f, 0.06f), mTrunk, true);
            for (int s = -1; s <= 1; s += 2)
                Prim(PrimitiveType.Cube, "Leg", b, new Vector3(s * 0.8f, 0.22f, 0.02f), new Vector3(0.08f, 0.44f, 0.46f), mSteel);
        }

        /// <summary>Round planter (white rim, soil top) standing on a floor at pos.</summary>
        private static void Planter(Transform parent, Vector3 pos, float r)
        {
            var k = new MeshKit(2);
            k.Lathe(new List<Vector2> { new Vector2(0f, 0f), new Vector2(r, 0f), new Vector2(r * 1.08f, 0.6f), new Vector2(r * 0.97f, 0.62f), new Vector2(r * 0.97f, 0.55f) }, 24, 2f, 2f);
            k.Sub = 1;
            k.Cap(MeshKit.Circle(r * 0.97f, 24), 0.55f, true, 2f);
            GameObject go = Solid(parent, "Planter", k, new[] { mWhite, mSoil }, true);
            go.transform.localPosition = pos;
        }

        private static void Hedge(Transform parent, Vector3 c, float len, bool alongX)
        {
            Prim(PrimitiveType.Cube, "Hedge Box", parent, c + Vector3.up * 0.35f, alongX ? new Vector3(len, 0.7f, 1f) : new Vector3(1f, 0.7f, len), mStone, true);
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 1.5f));
            for (int i = 0; i < n; i++)
            {
                float u = ((i + 0.5f) / n - 0.5f) * (len - 0.6f);
                Prim(PrimitiveType.Sphere, "Bush", parent, c + (alongX ? new Vector3(u, 0.75f, 0f) : new Vector3(0f, 0.75f, u)), new Vector3(1f, 0.7f, 0.95f), i % 3 == 1 ? mBlossom : mLeaf);
            }
        }

        /// <summary>Small pedestal with a message and the MUSIC ON/OFF toggle.</summary>
        private static void Kiosk(Transform parent, string name, Vector3 pos, Quaternion rot, string text)
        {
            var g = Group(parent, name);
            g.localPosition = pos;
            g.localRotation = rot;
            Prim(PrimitiveType.Cube, "Pedestal", g, new Vector3(0f, 0.55f, 0.1f), new Vector3(1.3f, 1.1f, 0.4f), mWhite, true);
            RectTransform c = UCanvas(g, "Panel", new Vector3(0f, 1.55f, -0.02f), Quaternion.Euler(15f, 0f, 0f), new Vector2(1200f, 900f));
            PanelBack(c, new Vector2(1200f, 900f), Hex("00E5FF"));
            UText(c, "Text", text, new Vector2(0f, 210f), new Vector2(1100f, 380f), 110f, Color.white);
            TextMeshProUGUI lbl = UButton(c, "Music", "MUSIC: ON", new Vector2(0f, -240f), new Vector2(900f, 260f), Hex("6A2BD9"), ambience, "_ToggleMusic", 90f);
            musicLabels.Add(lbl);
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

        // ------------------------------------------------------------------ sound

        private static AudioClip Clip(string file, AudioClipLoadType load)
        {
            string path = Pkg + "/Audio/" + file + ".ogg";
            try
            {
                if (AssetImporter.GetAtPath(path) is AudioImporter imp)
                {
                    AudioImporterSampleSettings s = imp.defaultSampleSettings;
                    if (s.loadType != load || s.compressionFormat != AudioCompressionFormat.Vorbis)
                    {
                        s.loadType = load;
                        s.compressionFormat = AudioCompressionFormat.Vorbis;
                        s.quality = 0.7f;
                        imp.defaultSampleSettings = s;
                        imp.SaveAndReimport();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LoopLand] Couldn't change the import settings of " + path + " (read-only package?): " + e.Message);
            }
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) Debug.LogWarning("[LoopLand] Missing sound " + path);
            return clip;
        }

        private static AudioSource Sound(Transform parent, string name, Vector3 pos, AudioClip clip, float volume, bool spatial, float near, float far, bool loop = true, bool play = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var a = go.AddComponent<AudioSource>();
            a.clip = clip;
            a.volume = volume;
            a.loop = loop;
            a.playOnAwake = play && clip != null;
            a.spatialBlend = spatial ? 1f : 0f;
            a.dopplerLevel = 0f;
            a.priority = spatial ? 128 : 32;
            if (spatial)
            {
                a.rolloffMode = AudioRolloffMode.Logarithmic;
                a.minDistance = near;
                a.maxDistance = far;
            }
            // VRChat's spatializer: unity-style falloff and no extra gain (VRChat would otherwise add +10 dB)
            if (spatialType == null)
                foreach (Type t in TypeCache.GetTypesDerivedFrom<Component>())
                    if (t.Name == "VRCSpatialAudioSource") { spatialType = t; break; }
            if (spatialType != null)
            {
                var so = new SerializedObject(go.AddComponent(spatialType));
                SetProp(so, "Gain", 0f);
                SetProp(so, "Near", spatial ? near : 0f);
                SetProp(so, "Far", spatial ? far : 40f);
                SetProp(so, "EnableSpatialization", spatial);
                SetProp(so, "UseAudioSourceVolumeCurve", true);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return a;
        }

        private static void SetProp(SerializedObject so, string prop, float value)
        {
            SerializedProperty p = so.FindProperty(prop);
            if (p != null) p.floatValue = value;
        }

        private static void SetProp(SerializedObject so, string prop, bool value)
        {
            SerializedProperty p = so.FindProperty(prop);
            if (p != null) p.boolValue = value;
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
            // spawn on the front walkway, looking at the tower and The Loop
            w.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, -58f), Quaternion.identity);
        }

        private static void FrontWalk(Transform root)
        {
            var walk = Group(root, "Front Walk");
            Prim(PrimitiveType.Cube, "Walkway", walk, new Vector3(0f, 0.02f, -50f), new Vector3(5f, 0.04f, 25f), mStone, true);
            for (int s = -1; s <= 1; s += 2)
                Prim(PrimitiveType.Cube, "Walkway Glow", walk, new Vector3(s * 2.54f, 0.025f, -50f), new Vector3(0.08f, 0.05f, 25f), mCyan);
            Kiosk(walk, "Welcome Kiosk", new Vector3(4.2f, 0f, -54.5f), Quaternion.Euler(0f, 45f, 0f), "<b>WELCOME TO LOOPLAND</b>\n<size=70%>Take the glass elevator up to The Loop</size>");
        }

        private static void MarkStatic(Transform t)
        {
            if (t.GetComponent<LoopLandMover>() != null || t.GetComponent<TMP_Text>() != null || t.name == "Elevator Car" || t.name.StartsWith("Landing Door")) return;
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

        private static void Mover(GameObject go, Vector3 offset, float period, float bob, float bobPeriod, Vector3 spin, Renderer scroll = null, Vector2 scrollSpeed = default)
        {
            var mv = UdonSharpUndo.AddComponent<LoopLandMover>(go);
            mv.moveOffset = offset;
            mv.movePeriod = period > 0f ? period : 16f;
            mv.bobHeight = bob;
            mv.bobPeriod = bobPeriod > 0f ? bobPeriod : 7f;
            mv.spin = spin;
            mv.scrollRenderer = scroll;
            mv.scrollSpeed = scrollSpeed;
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

        /// <summary>Saves a modelling kit as a mesh asset and puts it in the scene with one material per submesh.</summary>
        private static GameObject Solid(Transform parent, string name, MeshKit k, Material[] mats, bool collider)
        {
            Mesh mesh = SaveMesh("World_" + (++meshCount) + "_" + name.Replace(' ', '_'), k.ToMesh(name));
            GameObject go = MeshObj(parent, name, mesh, mats[0], Vector3.zero, Quaternion.identity, collider);
            go.GetComponent<MeshRenderer>().sharedMaterials = mats;
            return go;
        }

        private static GameObject Railing(Transform parent, string name, List<Vector3> path, bool closed, float height, bool collide)
        {
            GameObject go = Solid(parent, name, MeshKit.Railing(path, closed, height, 1.6f), new[] { mSteel, mGlass, mCyan }, false);
            if (collide)
            {
                var ck = new MeshKit();
                ck.Band(path, closed, 0f, height, true, 1f);
                go.AddComponent<MeshCollider>().sharedMesh = SaveMesh("World_" + (++meshCount) + "_Railing_Collider", ck.ToMesh(name + " Collider"));
            }
            return go;
        }

        private static void AddBox(GameObject go, Vector3 center, Vector3 size)
        {
            var b = go.AddComponent<BoxCollider>();
            b.center = center;
            b.size = size;
        }

        /// <summary>Planar quad with one normal; UVs from the plan (or the side) in metres.</summary>
        private static void Face4(MeshKit k, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrm, float uSize)
        {
            k.Quad(k.Vert(a, nrm, FaceUv(a, nrm, uSize)), k.Vert(b, nrm, FaceUv(b, nrm, uSize)), k.Vert(c, nrm, FaceUv(c, nrm, uSize)), k.Vert(d, nrm, FaceUv(d, nrm, uSize)), nrm);
        }

        private static Vector2 FaceUv(Vector3 p, Vector3 nrm, float uSize) =>
            Mathf.Abs(nrm.y) > 0.5f ? new Vector2(p.x, p.z) / uSize : new Vector2(p.x + p.z, p.y) / uSize;

        private static Color Rainbow(float v)
        {
            float f = Mathf.Repeat(v, 1f) * (RainbowHex.Length - 1);
            int i = Mathf.Min((int)f, RainbowHex.Length - 2);
            return Color.Lerp(Hex(RainbowHex[i]), Hex(RainbowHex[i + 1]), f - i);
        }

        // ------------------------------------------------------------------ materials and textures

        private static Material FacadeMat(int style, Texture2D tex, float glow)
        {
            Material m = Std("Facade_Style_" + style, Color.white, 0.55f, 0.92f, Color.white * glow, tex);
            m.mainTextureScale = Vector2.one;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material ParticleMat(string name, Texture tex, Color tint)
        {
            string path = Gen + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.mainTexture = tex;
            m.SetColor("_TintColor", tint);
            EditorUtility.SetDirty(m);
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
            Color stone = Hex("E9E4EF"), grout = Hex("A69DB2");
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

        /// <summary>Shop fronts: four warm-lit windows with shelves per 16 m, one storey tall.</summary>
        private static Texture2D ShopTex()
        {
            const int w = 256, h = 128;
            var r = new System.Random(12);
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true);
            for (int cx = 0; cx < 4; cx++)
            {
                Color warm = Color.Lerp(Hex("FFE2B0"), Hex("FFC6E6"), (float)r.NextDouble() * 0.6f);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        Color c;
                        if (y < 10) c = Hex("2A2838");
                        else if (x < 3 || x > 60 || y > h - 6) c = Hex("3A3A4A");
                        else
                        {
                            c = Color.Lerp(warm * 0.85f, warm, (y - 10) / (float)(h - 16));
                            if ((y - 10) % 26 < 3 && x > 8 && x < 56) c *= 0.55f;
                        }
                        c.a = 1f;
                        t.SetPixel(cx * 64 + x, y, c);
                    }
            }
            t.Apply();
            return SavePng("World_Shop", t);
        }

        private static Texture2D WindowTex(string name, Color glassCol, Color litA, Color litB, float litChance, int seed)
        {
            const int n = 256, cell = 32;
            var r = new System.Random(seed);
            var frame = new Color(0.08f, 0.09f, 0.14f, 1f);
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true);
            for (int cy = 0; cy < n / cell; cy++)
                for (int cx = 0; cx < n / cell; cx++)
                {
                    bool lit = r.NextDouble() < litChance;
                    Color pane = lit ? Color.Lerp(litA, litB, (float)r.NextDouble()) : glassCol * (0.8f + 0.4f * (float)r.NextDouble());
                    for (int y = 0; y < cell; y++)
                        for (int x = 0; x < cell; x++)
                        {
                            float g = (y - 6) / (float)(cell - 8);
                            Color c;
                            if (y < 6) c = Color.Lerp(frame, Color.white, 0.55f);               // white floor slab
                            else if (x < 2 || x >= cell - 2 || y >= cell - 2) c = frame;        // mullions
                            else c = lit ? pane * (0.85f + 0.15f * g) : Color.Lerp(pane, Color.Lerp(pane, Color.white, 0.5f), g); // sky reflection
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
            existing.indexFormat = mesh.indexFormat;
            existing.vertices = mesh.vertices;
            if (mesh.normals.Length > 0) existing.normals = mesh.normals;
            if (mesh.tangents.Length > 0) existing.tangents = mesh.tangents;
            if (mesh.uv.Length > 0) existing.uv = mesh.uv;
            if (mesh.colors.Length > 0) existing.colors = mesh.colors;
            existing.subMeshCount = mesh.subMeshCount;
            for (int i = 0; i < mesh.subMeshCount; i++) existing.SetTriangles(mesh.GetTriangles(i), i);
            existing.RecalculateBounds();
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>Flat ring (or disc when r0 = 0) facing up, UVs in world metres / uvSize.</summary>
        private static Mesh Annulus(string name, float r0, float r1, int seg, float uvSize)
        {
            var k = new MeshKit();
            k.Slab(Vector2.zero, a => r0, a => r1, 0f, 360f, seg, 0f, 0f, uvSize, false, false);
            return SaveMesh(name, k.ToMesh(name));
        }

        /// <summary>Vertical wall (both sides) along a closed or open path in the XZ plane, from y0 to y1.</summary>
        private static Mesh WallMesh(string name, List<Vector2> pts, bool closed, float y0, float y1)
        {
            var k = new MeshKit();
            k.Band(MeshKit.At(pts, y0), closed, 0f, y1 - y0, true, 2f);
            return SaveMesh(name, k.ToMesh(name));
        }

        /// <summary>Flat neon ribbon along a path in the XZ plane, rainbow along its length (for the additive ribbon material).</summary>
        private static Mesh Ribbon(string name, List<Vector2> pts, bool closed, float width)
        {
            int n = pts.Count;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var cols = new List<Color>();
            var tris = new List<int>();
            float len = 0f;
            for (int i = 0; i < (closed ? n + 1 : n); i++)
            {
                int k = i % n;
                Vector2 prev = closed ? pts[(k - 1 + n) % n] : pts[Mathf.Max(k - 1, 0)];
                Vector2 next = closed ? pts[(k + 1) % n] : pts[Mathf.Min(k + 1, n - 1)];
                Vector2 dir = (next - prev).normalized;
                Vector2 off = new Vector2(-dir.y, dir.x) * (width * 0.5f);
                if (i > 0) len += Vector2.Distance(pts[k], pts[(i - 1) % n]);
                verts.Add(new Vector3(pts[k].x + off.x, 0f, pts[k].y + off.y));
                verts.Add(new Vector3(pts[k].x - off.x, 0f, pts[k].y - off.y));
                uvs.Add(new Vector2(0f, len * 0.5f));
                uvs.Add(new Vector2(1f, len * 0.5f));
                Color c = Rainbow(k / (float)n);
                cols.Add(c);
                cols.Add(c);
                if (i == 0) continue;
                int q = verts.Count - 4;
                tris.Add(q); tris.Add(q + 2); tris.Add(q + 1);
                tris.Add(q + 1); tris.Add(q + 2); tris.Add(q + 3);
            }
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return SaveMesh(name, mesh);
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
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - centre) < 0f)
            {
                Vector3 tmp = b;
                b = c;
                c = tmp;
            }
            int q = verts.Count;
            verts.Add(a);
            verts.Add(b);
            verts.Add(c);
            tris.Add(q);
            tris.Add(q + 1);
            tris.Add(q + 2);
        }

        private static List<Vector2> Circle(float r, int n) => MeshKit.Circle(r, n);

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
    }
}
