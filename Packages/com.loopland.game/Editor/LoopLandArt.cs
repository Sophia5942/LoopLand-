using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LoopLand.EditorTools
{
    /// <summary>
    /// Procedural UI art for LoopLand (no external files needed): rounded panels, glows, pills,
    /// icons and store thumbnails. Everything is drawn with signed-distance shapes and saved as sprites.
    /// </summary>
    internal static class LoopLandArt
    {
        public static Sprite Round, Glow, Pill, Panel, Coin, House, Tower;
        public static Sprite IconDice, IconPawn, IconBuilding, IconSparkle, IconCrown, IconGift, IconGlobe;
        public static Sprite IconBank, IconHammer, IconChart, IconRefresh, IconPeople, IconStore, IconScreen, LogoInfinity, ScratchCell;

        private static string folder;
        private static float aa;

        private static readonly float[] PipX = { 0.34f, 0.66f, 0.5f, 0.34f, 0.66f };
        private static readonly float[] PipY = { 0.36f, 0.36f, 0.52f, 0.68f, 0.68f };
        private static readonly Vector2[] PawnBody = { new Vector2(0.34f, 0.2f), new Vector2(0.66f, 0.2f), new Vector2(0.56f, 0.6f), new Vector2(0.44f, 0.6f) };
        private static readonly Vector2[] SparkV = { new Vector2(0.5f, 0.1f), new Vector2(0.58f, 0.5f), new Vector2(0.5f, 0.9f), new Vector2(0.42f, 0.5f) };
        private static readonly Vector2[] SparkH = { new Vector2(0.1f, 0.5f), new Vector2(0.5f, 0.42f), new Vector2(0.9f, 0.5f), new Vector2(0.5f, 0.58f) };
        private static readonly Vector2[] CrownL = { new Vector2(0.16f, 0.34f), new Vector2(0.4f, 0.34f), new Vector2(0.2f, 0.72f) };
        private static readonly Vector2[] CrownM = { new Vector2(0.36f, 0.34f), new Vector2(0.64f, 0.34f), new Vector2(0.5f, 0.82f) };
        private static readonly Vector2[] CrownR = { new Vector2(0.6f, 0.34f), new Vector2(0.84f, 0.34f), new Vector2(0.8f, 0.72f) };
        private static readonly Vector2[] BankRoof = { new Vector2(0.1f, 0.64f), new Vector2(0.9f, 0.64f), new Vector2(0.5f, 0.9f) };
        private static readonly Vector2[] HammerHandle = { new Vector2(0.2801f, 0.1237f), new Vector2(0.6301f, 0.5237f), new Vector2(0.5699f, 0.5763f), new Vector2(0.2199f, 0.1763f) };
        private static readonly Vector2[] HammerHead = { new Vector2(0.7244f, 0.3956f), new Vector2(0.8166f, 0.501f), new Vector2(0.5156f, 0.7644f), new Vector2(0.4234f, 0.659f) };
        private static readonly Vector2[] RefreshHead = { new Vector2(0.56f, 0.64f), new Vector2(0.8f, 0.78f), new Vector2(0.56f, 0.94f) };
        private static readonly Vector2[] PlayTri = { new Vector2(0.44f, 0.48f), new Vector2(0.6f, 0.58f), new Vector2(0.44f, 0.68f) };
        private static readonly Vector2[] HouseShape = { new Vector2(0.18f, 0.12f), new Vector2(0.82f, 0.12f), new Vector2(0.82f, 0.55f), new Vector2(0.5f, 0.88f), new Vector2(0.18f, 0.55f) };

        public static void Build(string outFolder)
        {
            folder = outFolder;
            LoopLandBuilder.Dir(folder);
            Round = Paint("Round", 64, 26, (x, y) => A(Color.Lerp(new Color(0.8f, 0.8f, 0.86f), Color.white, y), Cov(Box(x, y, 0.5f, 0.5f, 0.5f, 0.5f, 0.4f))));
            Pill = Paint("Pill", 64, 31, (x, y) => A(Color.Lerp(new Color(0.85f, 0.85f, 0.9f), Color.white, y), Cov(Circle(x, y, 0.5f, 0.5f, 0.5f))));
            Glow = Paint("Glow", 64, 30, (x, y) =>
            {
                float t = Mathf.Clamp01(1f - Mathf.Abs(Box(x, y, 0.5f, 0.5f, 0.34f, 0.34f, 0.22f)) / 0.13f);
                return A(Color.white, t * t);
            });
            Panel = Paint("Panel", 256, 60, (x, y) =>
            {
                Color c = Color.Lerp(Hex(0x16206A), Hex(0x2C0F57), (x + (1f - y)) * 0.5f);
                c = Color.Lerp(c, Hex(0x3A57D8), Mathf.Clamp01(y - 0.75f) * 0.9f);
                return A(c, 0.97f * Cov(Box(x, y, 0.5f, 0.5f, 0.5f, 0.5f, 0.22f)));
            });
            Coin = Paint("Coin", 128, 0, (x, y) => DrawCoin(Color.clear, x, y, 0.5f, 0.5f, 0.44f));
            House = Paint("House", 64, 0, (x, y) => A(Color.white, Cov(Poly(x, y, HouseShape)) * (1f - Cov(Box(x, y, 0.5f, 0.24f, 0.08f, 0.12f, 0.02f)))));
            Tower = Paint("Tower", 64, 0, (x, y) =>
            {
                float body = Mathf.Min(Box(x, y, 0.5f, 0.42f, 0.2f, 0.36f, 0.03f), Box(x, y, 0.5f, 0.86f, 0.025f, 0.08f, 0.01f));
                float win = Mathf.Min(Box(x, y, 0.42f, 0.55f, 0.04f, 0.05f, 0.01f), Box(x, y, 0.58f, 0.55f, 0.04f, 0.05f, 0.01f));
                win = Mathf.Min(win, Mathf.Min(Box(x, y, 0.42f, 0.32f, 0.04f, 0.05f, 0.01f), Box(x, y, 0.58f, 0.32f, 0.04f, 0.05f, 0.01f)));
                return A(Color.white, Cov(body) * (1f - Cov(win)));
            });
            IconDice = Paint("Icon_Dice", 128, 0, (x, y) =>
            {
                float holes = 1f;
                for (int k = 0; k < 5; k++) holes = Mathf.Min(holes, Circle(x, y, PipX[k], PipY[k], 0.06f));
                return A(Color.white, Cov(Box(x, y, 0.5f, 0.52f, 0.36f, 0.36f, 0.09f)) * (1f - Cov(holes)));
            });
            IconPawn = Paint("Icon_Pawn", 128, 0, (x, y) => A(Color.white, Cov(PawnDist(x, y))));
            IconBuilding = Paint("Icon_Building", 128, 0, (x, y) =>
            {
                float d = Mathf.Min(Box(x, y, 0.3f, 0.4f, 0.1f, 0.26f, 0.02f), Box(x, y, 0.52f, 0.5f, 0.1f, 0.36f, 0.02f));
                d = Mathf.Min(d, Mathf.Min(Box(x, y, 0.74f, 0.33f, 0.09f, 0.19f, 0.02f), Box(x, y, 0.5f, 0.1f, 0.4f, 0.03f, 0.02f)));
                return A(Color.white, Cov(d));
            });
            IconSparkle = Paint("Icon_Sparkle", 128, 0, (x, y) => A(Color.white, Cov(Mathf.Min(Poly(x, y, SparkV), Poly(x, y, SparkH)))));
            IconCrown = Paint("Icon_Crown", 128, 0, (x, y) => A(Color.white, Cov(CrownDist(x, y))));
            IconGift = Paint("Icon_Gift", 128, 0, (x, y) =>
            {
                float d = Mathf.Min(Box(x, y, 0.5f, 0.36f, 0.3f, 0.22f, 0.03f), Box(x, y, 0.5f, 0.64f, 0.36f, 0.07f, 0.03f));
                d = Mathf.Min(d, Mathf.Min(Circle(x, y, 0.4f, 0.79f, 0.08f), Circle(x, y, 0.6f, 0.79f, 0.08f)));
                float gap = Mathf.Min(Box(x, y, 0.5f, 0.45f, 0.035f, 0.4f, 0f), Box(x, y, 0.5f, 0.57f, 0.4f, 0.012f, 0f));
                return A(Color.white, Cov(d) * (1f - Cov(gap)));
            });
            IconBank = Paint("Icon_Bank", 128, 0, (x, y) =>
            {
                float d = Mathf.Min(Poly(x, y, BankRoof), Mathf.Min(Box(x, y, 0.5f, 0.14f, 0.4f, 0.045f, 0.01f), Box(x, y, 0.5f, 0.59f, 0.37f, 0.03f, 0.01f)));
                for (int k = 0; k < 4; k++) d = Mathf.Min(d, Box(x, y, 0.23f + k * 0.18f, 0.37f, 0.045f, 0.19f, 0.01f));
                return A(Color.white, Cov(d));
            });
            IconHammer = Paint("Icon_Hammer", 128, 0, (x, y) => A(Color.white, Cov(Mathf.Min(Poly(x, y, HammerHandle), Poly(x, y, HammerHead)))));
            IconChart = Paint("Icon_Chart", 128, 0, (x, y) =>
            {
                float d = Mathf.Min(Box(x, y, 0.28f, 0.3f, 0.075f, 0.14f, 0.015f), Box(x, y, 0.5f, 0.4f, 0.075f, 0.24f, 0.015f));
                d = Mathf.Min(d, Mathf.Min(Box(x, y, 0.72f, 0.5f, 0.075f, 0.34f, 0.015f), Box(x, y, 0.5f, 0.11f, 0.4f, 0.025f, 0.01f)));
                return A(Color.white, Cov(d));
            });
            IconRefresh = Paint("Icon_Refresh", 128, 0, (x, y) =>
            {
                float ang = Mathf.Atan2(y - 0.5f, x - 0.5f) * Mathf.Rad2Deg;
                float ring = Mathf.Abs(Circle(x, y, 0.5f, 0.5f, 0.3f)) - 0.055f;
                if (ang > 15f && ang < 75f) ring = 1f;
                return A(Color.white, Cov(Mathf.Min(ring, Poly(x, y, RefreshHead))));
            });
            IconPeople = Paint("Icon_People", 128, 0, (x, y) =>
            {
                float d = Mathf.Min(Circle(x, y, 0.33f, 0.64f, 0.11f), Circle(x, y, 0.66f, 0.68f, 0.12f));
                d = Mathf.Min(d, Mathf.Max(Ellipse(x, y, 0.33f, 0.3f, 0.2f, 0.17f), 0.2f - y));
                d = Mathf.Min(d, Mathf.Max(Ellipse(x, y, 0.66f, 0.34f, 0.22f, 0.19f), 0.24f - y));
                return A(Color.white, Cov(d));
            });
            IconStore = Paint("Icon_Store", 128, 0, (x, y) =>
            {
                float handle = Mathf.Max(Mathf.Abs(Circle(x, y, 0.5f, 0.64f, 0.15f)) - 0.035f, 0.6f - y);
                float body = Box(x, y, 0.5f, 0.38f, 0.32f, 0.26f, 0.06f);
                return A(Color.white, Cov(Mathf.Min(handle, body)));
            });
            LogoInfinity = Paint("Logo_Infinity", 256, 0, (x, y) =>
            {
                float d = Mathf.Min(Mathf.Abs(Ellipse(x, y, 0.31f, 0.5f, 0.2f, 0.17f)), Mathf.Abs(Ellipse(x, y, 0.69f, 0.5f, 0.2f, 0.17f))) - 0.045f;
                Color c = Color.HSVToRGB(Mathf.Repeat(0.14f + x * 0.75f, 1f), 0.7f, 1f);
                return Mix(A(c, 0.45f * Soft(d - 0.02f, 0.08f)), Color.Lerp(c, Color.white, 0.25f), Cov(d));
            });
            ScratchCell = Paint("Scratch_Cell", 96, 0, (x, y) =>
            {
                // one round flake of silver foil; the flakes overlap, so a scratched hole gets a torn, scalloped edge
                float dx = x - 0.5f, dy = y - 0.5f;
                float ang = Mathf.Atan2(dy, dx);
                float rim = 0.448f + 0.03f * (Mathf.Sin(ang * 5f + 1.3f) * 0.5f + 0.5f) + 0.022f * Hash(Mathf.Floor((ang + 3.1416f) * 9f), 7f);
                float v = 0.64f + Hash(Mathf.Floor(x * 96f), Mathf.Floor(y * 96f)) * 0.07f + Hash(Mathf.Floor(x * 12f), Mathf.Floor(y * 12f)) * 0.05f;
                Color c = new Color(v * 0.95f, v * 0.97f, v, 1f);
                if (Hash(Mathf.Floor(x * 10f) + 3f, Mathf.Floor(y * 10f)) > 0.95f && Circle(Mathf.Repeat(x * 10f, 1f), Mathf.Repeat(y * 10f, 1f), 0.5f, 0.5f, 0.13f) < 0f) c = Color.white;
                return A(c, Mathf.Clamp01((rim - Mathf.Sqrt(dx * dx + dy * dy)) / 0.012f + 0.5f));
            });
            IconScreen = Paint("Icon_Screen", 128, 0, (x, y) =>
            {
                float frame = Mathf.Abs(Box(x, y, 0.5f, 0.58f, 0.38f, 0.27f, 0.05f)) - 0.04f;
                float stand = Mathf.Min(Box(x, y, 0.5f, 0.24f, 0.04f, 0.08f, 0.01f), Box(x, y, 0.5f, 0.15f, 0.18f, 0.03f, 0.02f));
                return A(Color.white, Cov(Mathf.Min(frame, Mathf.Min(stand, Poly(x, y, PlayTri)))));
            });
            IconGlobe = Paint("Icon_Globe", 128, 0, (x, y) =>
            {
                float ring = Mathf.Abs(Circle(x, y, 0.5f, 0.5f, 0.36f)) - 0.035f;
                float mer = Mathf.Abs(Ellipse(x, y, 0.5f, 0.5f, 0.15f, 0.36f)) - 0.03f;
                float eq = Mathf.Min(Box(x, y, 0.5f, 0.5f, 0.36f, 0.025f, 0f), Box(x, y, 0.5f, 0.5f, 0.025f, 0.36f, 0f));
                return A(Color.white, Cov(Mathf.Min(ring, Mathf.Min(mer, eq))));
            });
        }

        // ------------------------------------------------------------------ thumbnails

        public static Sprite DiceThumb(string name, Color body, Color pip, bool speckle)
        {
            return Paint(name, 256, 0, (x, y) =>
            {
                Color c = A(Color.Lerp(body, Color.white, 0.3f), 0.25f * Soft(Circle(x, y, 0.5f, 0.52f, 0.3f), 0.25f));
                return DrawDie(c, x, y, 0.5f, 0.5f, 0.86f, body, pip, speckle);
            });
        }

        public static Sprite TokenThumb(string name, Color col, bool rainbow)
        {
            return Paint(name, 256, 0, (x, y) => DrawPawn(Color.clear, x, y, 0.5f, 0.5f, 0.92f, col, rainbow));
        }

        public static Sprite TrailThumb(string name, Color col, bool rainbow)
        {
            return Paint(name, 256, 0, (x, y) => DrawTrail(Color.clear, x, y, col, rainbow));
        }

        public static Sprite BuildingThumb(string name, Color col, int variant)
        {
            var rnd = new System.Random(variant * 7919 + 13);
            float[] bx = new float[5], bw = new float[5], bh = new float[5];
            for (int k = 0; k < 5; k++)
            {
                bx[k] = 0.22f + k * 0.14f;
                bw[k] = 0.05f + (float)rnd.NextDouble() * 0.02f;
                bh[k] = 0.18f + (float)rnd.NextDouble() * 0.3f + (k == 2 ? 0.15f : 0f);
            }
            return Paint(name, 256, 0, (x, y) => DrawSkyline(Color.clear, x, y, col, variant, bx, bw, bh));
        }

        /// <summary>Scratch card pack: crimped foil pouch in the rarity colour with a skyline and the infinity logo.</summary>
        public static Sprite PackArt(string name, Color col)
        {
            return Paint(name, 256, 0, (x, y) =>
            {
                float zig = Mathf.Abs(Mathf.Repeat(x * 16f, 1f) - 0.5f) * 0.04f;
                float shape = Mathf.Max(Mathf.Abs(x - 0.5f) - 0.31f, Mathf.Max(y - (0.95f - zig), 0.05f + zig - y));
                Color dark = new Color(col.r * 0.3f, col.g * 0.3f, col.b * 0.35f, 1f);
                Color c = Color.Lerp(dark, col, y);
                if (y > 0.88f || y < 0.12f) c = Color.Lerp(c, new Color(0.85f, 0.88f, 0.95f, 1f), 0.35f);
                c = Color.Lerp(c, Color.white, Mathf.Exp(-Mathf.Pow((x + y * 0.6f - 0.75f) / 0.06f, 2f)) * 0.4f);
                float sky = Box(x, y, 0.36f, 0.42f, 0.05f, 0.16f, 0.005f);
                sky = Mathf.Min(sky, Box(x, y, 0.47f, 0.48f, 0.045f, 0.22f, 0.005f));
                sky = Mathf.Min(sky, Box(x, y, 0.57f, 0.4f, 0.05f, 0.14f, 0.005f));
                sky = Mathf.Min(sky, Box(x, y, 0.66f, 0.36f, 0.04f, 0.1f, 0.005f));
                sky = Mathf.Min(sky, Box(x, y, 0.28f, 0.34f, 0.035f, 0.08f, 0.005f));
                c = Mix(c, A(Color.Lerp(col, Color.white, 0.55f), 0.85f), Cov(sky));
                if (sky < -0.006f && Hash(Mathf.Floor(x * 60f), Mathf.Floor(y * 60f)) > 0.72f) c = Color.Lerp(c, Color.white, 0.6f);
                float inf = Mathf.Min(Mathf.Abs(Ellipse(x, y, 0.42f, 0.18f, 0.085f, 0.055f)), Mathf.Abs(Ellipse(x, y, 0.58f, 0.18f, 0.085f, 0.055f))) - 0.016f;
                c = Mix(c, A(Color.white, 0.4f), Soft(inf - 0.01f, 0.04f) * 0.6f);
                c = Mix(c, A(Color.white, 0.95f), Cov(inf));
                c.a = 1f;
                return A(c, Cov(shape));
            });
        }

        /// <summary>0 dice pack, 1 token pack, 2 VIP crown, 3 coin pouch, 4 coin vault, 5 skyline pack.</summary>
        public static Sprite ProductThumb(string name, int kind)
        {
            float[] bx = { 0.22f, 0.36f, 0.5f, 0.64f, 0.78f }, bw = { 0.05f, 0.06f, 0.06f, 0.055f, 0.05f }, bh = { 0.25f, 0.38f, 0.5f, 0.32f, 0.22f };
            return Paint(name, 256, 0, (x, y) =>
            {
                Color c = A(Hex(0xFFD54A), 0.22f * Soft(Circle(x, y, 0.5f, 0.5f, 0.32f), 0.25f));
                switch (kind)
                {
                    case 0:
                        c = DrawDie(c, x, y, 0.36f, 0.42f, 0.5f, Hex(0xE8B730), Hex(0x3A2500), false);
                        return DrawDie(c, x, y, 0.64f, 0.56f, 0.5f, Hex(0x2A0E5E), Color.white, true);
                    case 1:
                        c = DrawPawn(c, x, y, 0.36f, 0.5f, 0.62f, Hex(0xBFF6FF), false);
                        return DrawPawn(c, x, y, 0.64f, 0.5f, 0.62f, Color.white, true);
                    case 2:
                        return Mix(c, Color.Lerp(Hex(0xE0A010), Hex(0xFFE27A), y), Cov(CrownDist(x, y)));
                    case 3:
                        for (int k = 0; k < 3; k++) c = DrawCoin(c, x, y, 0.5f, 0.3f + k * 0.13f, 0.2f);
                        return c;
                    case 4:
                        for (int k = 0; k < 3; k++) c = DrawCoin(c, x, y, 0.3f, 0.25f + k * 0.12f, 0.17f);
                        for (int k = 0; k < 4; k++) c = DrawCoin(c, x, y, 0.62f, 0.22f + k * 0.12f, 0.19f);
                        return DrawCoin(c, x, y, 0.46f, 0.72f, 0.16f);
                    default:
                        return DrawSkyline(c, x, y, Hex(0xFFD54A), 5, bx, bw, bh);
                }
            });
        }

        // ------------------------------------------------------------------ shapes

        private static Color DrawDie(Color c, float x, float y, float cx, float cy, float s, Color body, Color pip, bool speckle)
        {
            float lx = (x - cx) / s + 0.5f, ly = (y - cy) / s + 0.5f;
            if (lx < -0.1f || lx > 1.1f || ly < -0.1f || ly > 1.1f) return c;
            c = Mix(c, new Color(0f, 0f, 0f, 0.4f), Soft(Box(lx, ly, 0.5f, 0.1f, 0.34f, 0.05f, 0.05f) * s, 0.04f));
            float face = Box(lx, ly, 0.5f, 0.52f, 0.38f, 0.38f, 0.09f);
            Color b = body * Mathf.Lerp(0.8f, 1.12f, ly);
            b.a = 1f;
            b = Color.Lerp(b, Color.white, Mathf.Clamp01(1f - Mathf.Abs(face + 0.025f) / 0.02f) * 0.3f);
            if (speckle && Hash(x, y) > 0.985f) b = Color.Lerp(b, Color.white, 0.8f);
            c = Mix(c, b, Cov(face * s));
            for (int k = 0; k < 5; k++) c = Mix(c, pip, Cov(Circle(lx, ly, PipX[k], PipY[k], 0.065f) * s));
            return c;
        }

        private static float PawnDist(float x, float y)
        {
            float d = Mathf.Min(Box(x, y, 0.5f, 0.16f, 0.26f, 0.05f, 0.03f), Poly(x, y, PawnBody));
            return Mathf.Min(d, Mathf.Min(Box(x, y, 0.5f, 0.6f, 0.12f, 0.028f, 0.02f), Circle(x, y, 0.5f, 0.75f, 0.15f)));
        }

        private static Color DrawPawn(Color c, float x, float y, float cx, float cy, float s, Color col, bool rainbow)
        {
            float lx = (x - cx) / s + 0.5f, ly = (y - cy) / s + 0.5f;
            if (lx < -0.1f || lx > 1.1f || ly < -0.1f || ly > 1.1f) return c;
            Color bc = rainbow ? Color.HSVToRGB(Mathf.Repeat(lx * 0.8f + ly * 0.4f, 1f), 0.75f, 1f) : col;
            c = Mix(c, A(bc, 0.5f), Soft(Ellipse(lx, ly, 0.5f, 0.12f, 0.36f, 0.08f) * s, 0.05f));
            Color b = bc * Mathf.Lerp(0.55f, 1.15f, ly);
            b.a = 1f;
            b = Color.Lerp(b, Color.white, Mathf.Clamp01(1f - Circle(lx, ly, 0.44f, 0.8f, 0.035f) / 0.03f) * 0.7f);
            return Mix(c, b, Cov(PawnDist(lx, ly) * s));
        }

        private static float CrownDist(float x, float y)
        {
            float d = Mathf.Min(Box(x, y, 0.5f, 0.3f, 0.34f, 0.08f, 0.03f), Poly(x, y, CrownL));
            d = Mathf.Min(d, Mathf.Min(Poly(x, y, CrownM), Poly(x, y, CrownR)));
            return Mathf.Min(d, Mathf.Min(Circle(x, y, 0.2f, 0.74f, 0.045f), Mathf.Min(Circle(x, y, 0.5f, 0.84f, 0.05f), Circle(x, y, 0.8f, 0.74f, 0.045f))));
        }

        private static Color DrawCoin(Color c, float x, float y, float cx, float cy, float r)
        {
            float d = Circle(x, y, cx, cy, r);
            if (d > aa * 2f) return c;
            float t = Mathf.Clamp01(((y - cy) / r + 1f) * 0.5f);
            c = Mix(c, new Color(0f, 0f, 0f, 0.35f), Soft(Circle(x, y, cx + r * 0.05f, cy - r * 0.08f, r), r * 0.15f));
            c = Mix(c, Color.Lerp(Hex(0xE09A12), Hex(0xFFE27A), t), Cov(d));
            c = Mix(c, Color.Lerp(Hex(0xD68A0C), Hex(0xF9C935), t), Cov(Circle(x, y, cx, cy, r * 0.76f)));
            float ring = Mathf.Min(Mathf.Abs(Circle(x, y, cx - r * 0.2f, cy, r * 0.2f)), Mathf.Abs(Circle(x, y, cx + r * 0.2f, cy, r * 0.2f))) - r * 0.06f;
            c = Mix(c, Hex(0xB86A00), Cov(ring));
            return Mix(c, new Color(1f, 1f, 1f, 0.35f), Cov(Ellipse(x, y, cx - r * 0.3f, cy + r * 0.38f, r * 0.25f, r * 0.12f)));
        }

        private static Color DrawTrail(Color c, float x, float y, Color col, bool rainbow)
        {
            for (int k = 0; k < 9; k++)
            {
                float t = k / 8f;
                float cx = 0.18f + t * 0.55f, cy = 0.22f + t * 0.45f + Mathf.Sin(t * Mathf.PI) * 0.06f;
                Color dc = rainbow ? Color.HSVToRGB(t, 0.8f, 1f) : col;
                c = Mix(c, A(dc, 0.25f + 0.6f * t), Soft(Circle(x, y, cx, cy, 0.015f + 0.03f * t), 0.03f));
            }
            Color hc = rainbow ? Color.HSVToRGB(0.85f, 0.6f, 1f) : col;
            c = Mix(c, A(hc, 0.6f), Soft(Circle(x, y, 0.75f, 0.72f, 0.06f), 0.08f));
            float lx = (x - 0.75f) / 0.36f + 0.5f, ly = (y - 0.72f) / 0.36f + 0.5f;
            float star = Mathf.Min(Poly(lx, ly, SparkV), Poly(lx, ly, SparkH)) * 0.36f;
            return Mix(c, Color.Lerp(hc, Color.white, 0.6f), Cov(star));
        }

        private static Color DrawSkyline(Color c, float x, float y, Color col, int variant, float[] bx, float[] bw, float[] bh)
        {
            c = Mix(c, A(col, 0.3f), Soft(Circle(x, y, 0.5f, 0.5f, 0.3f), 0.25f));
            if (variant == 6 && y > 0.45f && Hash(x, y) > 0.992f) c = Mix(c, Color.white, 0.9f);
            if (variant == 2) c = Mix(c, new Color(0.2f, 0.75f, 0.95f, 0.85f), Cov(Ellipse(x, y, 0.5f, 0.13f, 0.47f, 0.07f)));
            if (variant == 7) c = Mix(c, A(Hex(0xFF5A00), 0.35f), Soft(Circle(x, y, 0.5f, 0.35f, 0.25f), 0.2f));
            c = Mix(c, Color.Lerp(Hex(0x1B2A6B), Hex(0x2E4BB8), Mathf.Clamp01(y * 3f)), Cov(Box(x, y, 0.5f, 0.2f, 0.42f, 0.05f, 0.03f)));
            c = Mix(c, A(col, 0.9f), Cov(Box(x, y, 0.5f, 0.245f, 0.42f, 0.006f, 0.005f)));
            for (int k = 0; k < bx.Length; k++)
            {
                float top = 0.25f + bh[k];
                float d = Box(x, y, bx[k], 0.25f + bh[k] * 0.5f, bw[k], bh[k] * 0.5f, 0.008f);
                if (k == 2) d = Mathf.Min(d, Box(x, y, bx[k], top + 0.05f, 0.004f, 0.05f, 0.002f));
                if (d > 0.02f) continue;
                Color b = Color.Lerp(col * 0.32f, col * 0.75f, Mathf.Clamp01((x - bx[k] + bw[k]) / (2f * bw[k])));
                b.a = 1f;
                float wx = Mathf.Repeat((x - bx[k]) * 60f, 1f), wy = Mathf.Repeat(y * 50f, 1f);
                if (wx > 0.35f && wx < 0.75f && wy > 0.3f && wy < 0.7f && Mathf.Abs(x - bx[k]) < bw[k] - 0.012f && y < top - 0.02f)
                    b = Color.Lerp(b, Color.Lerp(col, Color.white, 0.6f), 0.85f);
                c = Mix(c, b, Cov(d));
                if (variant == 3) c = Mix(c, Hex(0x5BE36B), Cov(Circle(x, y, bx[k], top + 0.02f, 0.035f)));
            }
            return c;
        }

        // ------------------------------------------------------------------ raster helpers

        private static Sprite Paint(string name, int size, int border, Func<float, float, Color> f)
        {
            string path = folder + "/" + name + ".png";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;
            aa = 1f / size;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = f((x + 0.5f) / size, (y + 0.5f) / size);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = true;
            imp.spriteBorder = new Vector4(border, border, border, border);
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static float Cov(float d) => Mathf.Clamp01(0.5f - d / aa);
        private static float Soft(float d, float w) => Mathf.Clamp01(0.5f - d / w);
        private static Color A(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        private static Color Mix(Color dst, Color src, float a)
        {
            a *= src.a;
            if (a <= 0f) return dst;
            float outA = a + dst.a * (1f - a);
            Color o = (src * a + dst * (dst.a * (1f - a))) / outA;
            o.a = outA;
            return o;
        }

        private static float Circle(float x, float y, float cx, float cy, float r) => new Vector2(x - cx, y - cy).magnitude - r;

        private static float Ellipse(float x, float y, float cx, float cy, float rx, float ry) =>
            (new Vector2((x - cx) / rx, (y - cy) / ry).magnitude - 1f) * Mathf.Min(rx, ry);

        private static float Box(float x, float y, float cx, float cy, float hw, float hh, float r)
        {
            float qx = Mathf.Abs(x - cx) - hw + r, qy = Mathf.Abs(y - cy) - hh + r;
            return new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        /// <summary>Signed distance (approximate outside corners) to a convex counter-clockwise polygon.</summary>
        private static float Poly(float x, float y, Vector2[] pts)
        {
            float d = -1e9f;
            var p = new Vector2(x, y);
            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 a = pts[i], e = (pts[(i + 1) % pts.Length] - a).normalized;
                d = Mathf.Max(d, Vector2.Dot(p - a, new Vector2(e.y, -e.x)));
            }
            return d;
        }

        private static float Hash(float x, float y)
        {
            float h = Mathf.Sin(x * 3313.17f + y * 20012.6f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        private static Color Hex(int h) => new Color(((h >> 16) & 255) / 255f, ((h >> 8) & 255) / 255f, (h & 255) / 255f, 1f);
    }
}
