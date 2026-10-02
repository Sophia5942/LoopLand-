using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LoopLand.EditorTools
{
    /// <summary>
    /// Small procedural modelling kit for the world builder: extrusions, lathes, ring slabs, curved panels, bands, tubes,
    /// boxes and glass railings, with metre-based UVs and explicit normals, combined into one mesh (optionally with
    /// submeshes). Every face is oriented from an "outside" hint, so a winding mistake can never turn a surface inside out.
    /// 2D profiles are (x, z) points, counter-clockwise when seen from above.
    /// </summary>
    internal sealed class MeshKit
    {
        private readonly List<Vector3> v = new List<Vector3>();
        private readonly List<Vector3> n = new List<Vector3>();
        private readonly List<Vector2> uv = new List<Vector2>();
        private readonly List<Color> col = new List<Color>();
        private readonly List<int>[] sub;

        /// <summary>Submesh that new faces go to.</summary>
        public int Sub;

        public MeshKit(int submeshes = 1)
        {
            sub = new List<int>[submeshes];
            for (int i = 0; i < submeshes; i++) sub[i] = new List<int>();
        }

        public int Vert(Vector3 p, Vector3 normal, Vector2 tex)
        {
            v.Add(p);
            n.Add(normal);
            uv.Add(tex);
            col.Add(Color.white);
            return v.Count - 1;
        }

        public void Tri(int a, int b, int c, Vector3 outside)
        {
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), outside) < 0f)
            {
                int t = b;
                b = c;
                c = t;
            }
            sub[Sub].Add(a);
            sub[Sub].Add(b);
            sub[Sub].Add(c);
        }

        /// <summary>Quad from four corners in order around its edge.</summary>
        public void Quad(int a, int b, int c, int d, Vector3 outside)
        {
            Tri(a, b, c, outside);
            Tri(a, c, d, outside);
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetColors(col);
            m.subMeshCount = sub.Length;
            for (int i = 0; i < sub.Length; i++) m.SetTriangles(sub[i], i);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        // ------------------------------------------------------------------ 2D profiles

        public static List<Vector2> RoundRect(float w, float d, float r, int seg)
        {
            r = Mathf.Clamp(r, 0.01f, Mathf.Min(w, d) * 0.5f);
            float hx = w * 0.5f - r, hz = d * 0.5f - r;
            Vector2[] centres = { new Vector2(hx, hz), new Vector2(-hx, hz), new Vector2(-hx, -hz), new Vector2(hx, -hz) };
            var pts = new List<Vector2>();
            for (int k = 0; k < 4; k++)
                for (int i = 0; i <= seg; i++)
                {
                    float a = (k * 90f + i * 90f / seg) * Mathf.Deg2Rad;
                    Vector2 p = centres[k] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (pts.Count == 0 || (pts[pts.Count - 1] - p).sqrMagnitude > 1e-8f) pts.Add(p);
                }
            return pts;
        }

        public static List<Vector2> Circle(float r, int seg)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * 2f * Mathf.PI;
                pts.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
            return pts;
        }

        /// <summary>Arc points around c from a0 to a1 degrees (both ends included).</summary>
        public static List<Vector2> Arc(Vector2 c, float r, float a0, float a1, int seg)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)seg) * Mathf.Deg2Rad;
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
            return pts;
        }

        public static List<Vector3> At(List<Vector2> pts, float y)
        {
            var o = new List<Vector3>();
            foreach (Vector2 p in pts) o.Add(new Vector3(p.x, y, p.y));
            return o;
        }

        // ------------------------------------------------------------------ solids

        /// <summary>Vertical prism of a closed profile from y0 to y1. Sides are smooth unless the outline turns more than hardDeg.</summary>
        public void Extrude(List<Vector2> p, float y0, float y1, float uSize, float vSize, bool top, bool bottom, bool inward = false, float hardDeg = 40f)
        {
            int cnt = p.Count;
            var en = new Vector2[cnt];
            for (int i = 0; i < cnt; i++)
            {
                Vector2 e = p[(i + 1) % cnt] - p[i];
                en[i] = new Vector2(e.y, -e.x).normalized * (inward ? -1f : 1f);
            }
            float len = 0f;
            for (int i = 0; i < cnt; i++)
            {
                int j = (i + 1) % cnt;
                int prev = (i - 1 + cnt) % cnt;
                Vector2 n0 = Vector2.Angle(en[prev], en[i]) < hardDeg ? (en[prev] + en[i]).normalized : en[i];
                Vector2 n1 = Vector2.Angle(en[i], en[j]) < hardDeg ? (en[i] + en[j]).normalized : en[i];
                float l = (p[j] - p[i]).magnitude;
                var a3 = new Vector3(n0.x, 0f, n0.y);
                var b3 = new Vector3(n1.x, 0f, n1.y);
                int a = Vert(new Vector3(p[i].x, y0, p[i].y), a3, new Vector2(len / uSize, y0 / vSize));
                int b = Vert(new Vector3(p[j].x, y0, p[j].y), b3, new Vector2((len + l) / uSize, y0 / vSize));
                int c = Vert(new Vector3(p[j].x, y1, p[j].y), b3, new Vector2((len + l) / uSize, y1 / vSize));
                int d = Vert(new Vector3(p[i].x, y1, p[i].y), a3, new Vector2(len / uSize, y1 / vSize));
                Quad(a, b, c, d, new Vector3(en[i].x, 0f, en[i].y));
                len += l;
            }
            if (top) Cap(p, y1, true, uSize);
            if (bottom) Cap(p, y0, false, uSize);
        }

        /// <summary>Flat convex cap (fan from the centroid).</summary>
        public void Cap(List<Vector2> p, float y, bool up, float uSize)
        {
            Vector2 cen = Vector2.zero;
            foreach (Vector2 q in p) cen += q;
            cen /= p.Count;
            Vector3 nn = up ? Vector3.up : Vector3.down;
            int c0 = Vert(new Vector3(cen.x, y, cen.y), nn, cen / uSize);
            int first = v.Count;
            foreach (Vector2 q in p) Vert(new Vector3(q.x, y, q.y), nn, q / uSize);
            for (int i = 0; i < p.Count; i++) Tri(c0, first + i, first + (i + 1) % p.Count, nn);
        }

        /// <summary>Flat ring between two loops with matching points (e.g. two round-rects with the same corner segments).</summary>
        public void RingFace(List<Vector2> inner, List<Vector2> outer, float y, bool up, float uSize)
        {
            Vector3 nn = up ? Vector3.up : Vector3.down;
            int cnt = Mathf.Min(inner.Count, outer.Count), first = v.Count;
            for (int i = 0; i < cnt; i++)
            {
                Vert(new Vector3(inner[i].x, y, inner[i].y), nn, inner[i] / uSize);
                Vert(new Vector3(outer[i].x, y, outer[i].y), nn, outer[i] / uSize);
            }
            for (int i = 0; i < cnt; i++)
            {
                int a = first + 2 * i, b = first + 2 * ((i + 1) % cnt);
                Quad(a, a + 1, b + 1, b, nn);
            }
        }

        /// <summary>Hollow band between two matching loops: top and bottom rings, outer wall and (optionally) inner wall.</summary>
        public void RingSlab(List<Vector2> inner, List<Vector2> outer, float y0, float y1, float uSize, bool innerWall)
        {
            RingFace(inner, outer, y1, true, uSize);
            RingFace(inner, outer, y0, false, uSize);
            Extrude(outer, y0, y1, uSize, uSize, false, false);
            if (innerWall) Extrude(inner, y0, y1, uSize, uSize, false, false, true);
        }

        /// <summary>Surface of revolution of (radius, height) points around Y, listed bottom -> outside -> top so faces point outward.</summary>
        public void Lathe(List<Vector2> prof, int seg, float uSize, float vSize, float hardDeg = 35f)
        {
            int cnt = prof.Count;
            var sn = new Vector2[cnt - 1];
            float rRef = 0.01f;
            for (int s = 0; s < cnt - 1; s++)
            {
                Vector2 t = prof[s + 1] - prof[s];
                sn[s] = new Vector2(t.y, -t.x).normalized;
                rRef = Mathf.Max(rRef, prof[s].x, prof[s + 1].x);
            }
            float vlen = 0f;
            for (int s = 0; s < cnt - 1; s++)
            {
                Vector2 n0 = s > 0 && Vector2.Angle(sn[s - 1], sn[s]) < hardDeg ? (sn[s - 1] + sn[s]).normalized : sn[s];
                Vector2 n1 = s < cnt - 2 && Vector2.Angle(sn[s], sn[s + 1]) < hardDeg ? (sn[s] + sn[s + 1]).normalized : sn[s];
                float l = (prof[s + 1] - prof[s]).magnitude;
                int first = v.Count;
                for (int k = 0; k <= seg; k++)
                {
                    float a = k / (float)seg * 2f * Mathf.PI;
                    float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    float u = a * rRef / uSize;
                    Vert(new Vector3(prof[s].x * ca, prof[s].y, prof[s].x * sa), new Vector3(n0.x * ca, n0.y, n0.x * sa), new Vector2(u, vlen / vSize));
                    Vert(new Vector3(prof[s + 1].x * ca, prof[s + 1].y, prof[s + 1].x * sa), new Vector3(n1.x * ca, n1.y, n1.x * sa), new Vector2(u, (vlen + l) / vSize));
                }
                for (int k = 0; k < seg; k++)
                {
                    int a = first + 2 * k, b = a + 2;
                    float am = (k + 0.5f) / seg * 2f * Mathf.PI;
                    Quad(a, a + 1, b + 1, b, new Vector3(sn[s].x * Mathf.Cos(am), sn[s].y, sn[s].x * Mathf.Sin(am)));
                }
                vlen += l;
            }
        }

        /// <summary>
        /// Horizontal slab around centre c between radius functions of the angle (degrees), from a0 to a1, top at y.
        /// Top/bottom UVs are world metres / uSize. Partial sweeps get end faces.
        /// </summary>
        public void Slab(Vector2 c, Func<float, float> rIn, Func<float, float> rOut, float a0, float a1, int seg, float y, float thick, float uSize, bool innerWall = true, bool outerWall = true, int topSub = -1)
        {
            var pin = new Vector2[seg + 1];
            var pout = new Vector2[seg + 1];
            for (int k = 0; k <= seg; k++)
            {
                float deg = Mathf.Lerp(a0, a1, k / (float)seg);
                var d = new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
                pin[k] = c + d * rIn(deg);
                pout[k] = c + d * rOut(deg);
            }
            float y0 = y - thick;
            int keep = Sub;
            for (int face = 0; face < 2; face++)
            {
                float yy = face == 0 ? y : y0;
                Vector3 nn = face == 0 ? Vector3.up : Vector3.down;
                Sub = face == 0 && topSub >= 0 ? topSub : keep;
                int first = v.Count;
                for (int k = 0; k <= seg; k++)
                {
                    Vert(new Vector3(pin[k].x, yy, pin[k].y), nn, pin[k] / uSize);
                    Vert(new Vector3(pout[k].x, yy, pout[k].y), nn, pout[k] / uSize);
                }
                for (int k = 0; k < seg; k++)
                {
                    int a = first + 2 * k, b = a + 2;
                    Quad(a, a + 1, b + 1, b, nn);
                }
            }
            Sub = keep;
            if (outerWall) Wall(pout, y0, y, uSize, c, true);
            if (innerWall && rIn(a0) > 0.01f) Wall(pin, y0, y, uSize, c, false);
            if (a1 - a0 < 359.99f)
                for (int end = 0; end < 2; end++)
                {
                    int k = end == 0 ? 0 : seg;
                    Vector2 dir = pout[k] - c;
                    var tangent = new Vector3(-dir.y, 0f, dir.x).normalized * (end == 0 ? -1f : 1f);
                    int a = Vert(new Vector3(pin[k].x, y0, pin[k].y), tangent, new Vector2(0f, 0f));
                    int b = Vert(new Vector3(pout[k].x, y0, pout[k].y), tangent, new Vector2((pout[k] - pin[k]).magnitude / uSize, 0f));
                    int cc = Vert(new Vector3(pout[k].x, y, pout[k].y), tangent, new Vector2((pout[k] - pin[k]).magnitude / uSize, thick / uSize));
                    int d = Vert(new Vector3(pin[k].x, y, pin[k].y), tangent, new Vector2(0f, thick / uSize));
                    Quad(a, b, cc, d, tangent);
                }
        }

        /// <summary>Vertical faces along a polyline, facing away from (or toward) c.</summary>
        private void Wall(Vector2[] p, float y0, float y1, float uSize, Vector2 c, bool away)
        {
            int cnt = p.Length;
            var sn = new Vector2[cnt - 1];
            for (int k = 0; k < cnt - 1; k++)
            {
                Vector2 e = p[k + 1] - p[k];
                Vector2 nrm = new Vector2(e.y, -e.x).normalized;
                if (Vector2.Dot(nrm, (p[k] + p[k + 1]) * 0.5f - c) > 0f != away) nrm = -nrm;
                sn[k] = nrm;
            }
            float len = 0f;
            for (int k = 0; k < cnt - 1; k++)
            {
                Vector2 na = k > 0 ? (sn[k - 1] + sn[k]).normalized : sn[k];
                Vector2 nb = k < cnt - 2 ? (sn[k] + sn[k + 1]).normalized : sn[k];
                float l = (p[k + 1] - p[k]).magnitude;
                var a3 = new Vector3(na.x, 0f, na.y);
                var b3 = new Vector3(nb.x, 0f, nb.y);
                int a = Vert(new Vector3(p[k].x, y0, p[k].y), a3, new Vector2(len / uSize, y0 / uSize));
                int b = Vert(new Vector3(p[k + 1].x, y0, p[k + 1].y), b3, new Vector2((len + l) / uSize, y0 / uSize));
                int cc = Vert(new Vector3(p[k + 1].x, y1, p[k + 1].y), b3, new Vector2((len + l) / uSize, y1 / uSize));
                int d = Vert(new Vector3(p[k].x, y1, p[k].y), a3, new Vector2(len / uSize, y1 / uSize));
                Quad(a, b, cc, d, new Vector3(sn[k].x, 0f, sn[k].y));
                len += l;
            }
        }

        /// <summary>Cylindrical panel of radius r around c from a0 to a1 degrees, y0..y1, facing outward. UV 0..1 left to right, bottom to top.</summary>
        public void CurvedPanel(Vector2 c, float r, float a0, float a1, float y0, float y1, int seg)
        {
            int first = v.Count;
            for (int k = 0; k <= seg; k++)
            {
                float f = k / (float)seg;
                float a = Mathf.Lerp(a0, a1, f) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var p = new Vector3(c.x, 0f, c.y) + d * r;
                Vert(new Vector3(p.x, y0, p.z), d, new Vector2(f, 0f));
                Vert(new Vector3(p.x, y1, p.z), d, new Vector2(f, 1f));
            }
            for (int k = 0; k < seg; k++)
            {
                int a = first + 2 * k, b = a + 2;
                Quad(a, b, b + 1, a + 1, n[a] + n[b]);
            }
        }

        /// <summary>Vertical band along a path from h0 to h1 above it; two-sided bands get a back face too. U = metres / uSize, V = 0..1.</summary>
        public void Band(List<Vector3> path, bool closed, float h0, float h1, bool twoSided, float uSize)
        {
            int cnt = path.Count, rings = closed ? cnt + 1 : cnt;
            for (int side = 0; side < (twoSided ? 2 : 1); side++)
            {
                int first = v.Count;
                float len = 0f;
                for (int i = 0; i < rings; i++)
                {
                    int k = i % cnt;
                    Vector3 prev = closed ? path[(k - 1 + cnt) % cnt] : path[Mathf.Max(k - 1, 0)];
                    Vector3 next = closed ? path[(k + 1) % cnt] : path[Mathf.Min(k + 1, cnt - 1)];
                    Vector3 t = next - prev;
                    t.y = 0f;
                    Vector3 nrm = Vector3.Cross(Vector3.up, t.normalized) * (side == 0 ? 1f : -1f);
                    if (i > 0) len += Vector3.Distance(path[k], path[(i - 1) % cnt]);
                    Vert(path[k] + Vector3.up * h0, nrm, new Vector2(len / uSize, 0f));
                    Vert(path[k] + Vector3.up * h1, nrm, new Vector2(len / uSize, 1f));
                }
                for (int i = 0; i < rings - 1; i++)
                {
                    int a = first + 2 * i, b = a + 2;
                    Quad(a, b, b + 1, a + 1, n[a] + n[b]);
                }
            }
        }

        /// <summary>Round tube swept along a path.</summary>
        public void Tube(List<Vector3> path, bool closed, float radius, int sides)
        {
            int cnt = path.Count, rings = closed ? cnt + 1 : cnt, first = v.Count;
            float len = 0f;
            for (int i = 0; i < rings; i++)
            {
                int k = i % cnt;
                Vector3 prev = closed ? path[(k - 1 + cnt) % cnt] : path[Mathf.Max(k - 1, 0)];
                Vector3 next = closed ? path[(k + 1) % cnt] : path[Mathf.Min(k + 1, cnt - 1)];
                Vector3 t = (next - prev).normalized;
                Vector3 up = Mathf.Abs(t.y) > 0.95f ? Vector3.forward : Vector3.up;
                Vector3 nx = Vector3.Cross(up, t).normalized, ny = Vector3.Cross(t, nx);
                if (i > 0) len += Vector3.Distance(path[k], path[(i - 1) % cnt]);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * 2f * Mathf.PI;
                    Vector3 d = nx * Mathf.Cos(a) + ny * Mathf.Sin(a);
                    Vert(path[k] + d * radius, d, new Vector2(s / (float)sides, len));
                }
            }
            for (int i = 0; i < rings - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = first + i * (sides + 1) + s, b = a + sides + 1;
                    Quad(a, a + 1, b + 1, b, n[a] + n[a + 1]);
                }
        }

        /// <summary>Box with centre, size and rotation; UVs in metres / uSize.</summary>
        public void Box(Vector3 c, Vector3 size, Quaternion rot, float uSize)
        {
            Vector3 h = size * 0.5f;
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            for (int ax = 0; ax < 3; ax++)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    Vector3 nn = axes[ax] * sgn, t1 = axes[(ax + 1) % 3], t2 = axes[(ax + 2) % 3];
                    float h1 = h[(ax + 1) % 3], h2 = h[(ax + 2) % 3];
                    Vector3 fc = nn * h[ax], wn = rot * nn;
                    int a = Vert(c + rot * (fc - t1 * h1 - t2 * h2), wn, new Vector2(-h1, -h2) / uSize);
                    int b = Vert(c + rot * (fc + t1 * h1 - t2 * h2), wn, new Vector2(h1, -h2) / uSize);
                    int d = Vert(c + rot * (fc + t1 * h1 + t2 * h2), wn, new Vector2(h1, h2) / uSize);
                    int e = Vert(c + rot * (fc - t1 * h1 + t2 * h2), wn, new Vector2(-h1, h2) / uSize);
                    Quad(a, b, d, e, wn);
                }
        }

        // ------------------------------------------------------------------ composites

        /// <summary>
        /// Glass balustrade along a floor-level path (points can climb, e.g. on an arched bridge): handrail, bottom rail and
        /// posts in submesh 0, glass in submesh 1, a glowing strip under the handrail in submesh 2.
        /// </summary>
        public static MeshKit Railing(List<Vector3> path, bool closed, float height, float postGap)
        {
            var k = new MeshKit(3);
            var top = new List<Vector3>();
            var low = new List<Vector3>();
            var led = new List<Vector3>();
            foreach (Vector3 p in path)
            {
                top.Add(p + Vector3.up * height);
                low.Add(p + Vector3.up * 0.09f);
                led.Add(p + Vector3.up * (height - 0.055f));
            }
            k.Tube(top, closed, 0.035f, 8);
            k.Tube(low, closed, 0.02f, 6);
            int cnt = path.Count, segs = closed ? cnt : cnt - 1;
            float acc = postGap;
            for (int i = 0; i < segs; i++)
            {
                Vector3 a = path[i], b = path[(i + 1) % cnt];
                float l = Vector3.Distance(a, b);
                Vector3 dir = b - a;
                dir.y = 0f;
                Quaternion rot = dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir) : Quaternion.identity;
                float s = 0f;
                while (acc + (l - s) >= postGap)
                {
                    s += postGap - acc;
                    acc = 0f;
                    Vector3 p = Vector3.Lerp(a, b, l > 0f ? s / l : 0f);
                    k.Box(p + Vector3.up * (height * 0.5f), new Vector3(0.05f, height, 0.05f), rot, 1f);
                }
                acc += l - s;
            }
            if (!closed) k.Box(path[cnt - 1] + Vector3.up * (height * 0.5f), new Vector3(0.06f, height, 0.06f), Quaternion.identity, 1f);
            k.Sub = 1;
            k.Band(path, closed, 0.11f, height - 0.04f, true, 2f);
            k.Sub = 2;
            k.Tube(led, closed, 0.012f, 4);
            return k;
        }
    }
}
