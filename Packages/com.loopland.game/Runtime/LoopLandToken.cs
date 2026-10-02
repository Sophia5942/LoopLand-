using TMPro;
using UdonSharp;
using UnityEngine;

namespace LoopLand
{
    /// <summary>Player counter: hops around the loop with a particle trail, landing bursts and a glowing aura.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LoopLandToken : UdonSharpBehaviour
    {
        public Transform[] spaces;
        public Vector3 slotOffset;
        public Transform body;
        public Renderer[] bodyRenderers;
        public Renderer[] glowRenderers;
        public ParticleSystem trail;
        public ParticleSystem burst;
        public ParticleSystem aura;
        public TextMeshPro nameTag;
        public AudioSource sfx;
        public AudioClip hopClip;
        public AudioClip landClip;
        public AudioClip warpClip;
        public float hopTime = 0.24f;
        public float hopHeight = 0.14f;

        private int cur;
        private int target;
        private int mode;
        private int next;
        private bool moving;
        private bool init;
        private float t;
        private float delay;
        private float stepTime;
        private Vector3 from;
        private Vector3 to;
        private bool rainbowBody;
        private bool rainbowTrail;
        private Color glowCol = Color.white;

        public void _MoveTo(int p, int m, float d)
        {
            if (spaces == null || spaces.Length == 0) return;
            if (!init) { init = true; cur = p; target = p; transform.position = _Point(p); return; }
            if (p == target) return;
            target = p;
            mode = m;
            delay = d;
            int dist = m == 2 ? (cur - p + spaces.Length) % spaces.Length : (p - cur + spaces.Length) % spaces.Length;
            stepTime = Mathf.Min(hopTime, 4f / Mathf.Max(1, dist));
            if (!moving) { moving = true; t = 1f; next = cur; from = transform.position; to = from; }
        }

        public void _Skin(Color bodyCol, Color glow, Color trailCol, bool rbBody, bool rbTrail, string label)
        {
            rainbowBody = rbBody;
            rainbowTrail = rbTrail;
            glowCol = glow;
            _Tint(bodyRenderers, bodyCol, bodyCol * 0.25f);
            _Tint(glowRenderers, glow, glow * 2.2f);
            _TintFx(trail, trailCol);
            _TintFx(burst, trailCol);
            _TintFx(aura, glow);
            if (nameTag != null) { nameTag.text = label; nameTag.color = glow; }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (rainbowBody || rainbowTrail)
            {
                Color rb = Color.HSVToRGB((Time.time * 0.25f) % 1f, 0.85f, 1f);
                if (rainbowBody) { _Tint(bodyRenderers, rb, rb * 0.4f); _Tint(glowRenderers, rb, rb * 2.2f); }
                if (rainbowTrail) _TintFx(trail, rb);
            }
            if (body != null) body.localPosition = new Vector3(0f, moving ? 0f : Mathf.Sin(Time.time * 2.4f) * 0.008f, 0f);
            if (!moving) return;
            if (delay > 0f) { delay -= dt; return; }

            if (mode == 1)
            {
                _Burst(40);
                _Play(warpClip);
                cur = target;
                transform.position = _Point(cur);
                _Burst(60);
                moving = false;
                return;
            }

            t += dt / stepTime;
            if (t >= 1f)
            {
                transform.position = to;
                cur = next;
                if (cur == target)
                {
                    moving = false;
                    if (trail != null) trail.Stop();
                    _Burst(45);
                    _Play(landClip);
                    return;
                }
                next = mode == 2 ? (cur + spaces.Length - 1) % spaces.Length : (cur + 1) % spaces.Length;
                from = _Point(cur);
                to = _Point(next);
                t = 0f;
                if (trail != null && !trail.isPlaying) trail.Play();
                _Play(hopClip);
                Vector3 dir = to - from;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
            float e = Mathf.SmoothStep(0f, 1f, t);
            transform.position = Vector3.Lerp(from, to, e) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * hopHeight);
        }

        private Vector3 _Point(int p)
        {
            Transform a = spaces[Mathf.Clamp(p, 0, spaces.Length - 1)];
            return a.TransformPoint(slotOffset);
        }

        private void _Burst(int n)
        {
            if (burst != null) burst.Emit(n);
        }

        private void _Play(AudioClip c)
        {
            if (sfx != null && c != null) sfx.PlayOneShot(c, 0.5f);
        }

        private void _Tint(Renderer[] rs, Color c, Color emission)
        {
            if (rs == null) return;
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null) continue;
                Material m = rs[i].material;
                m.SetColor("_Color", c);
                m.SetColor("_EmissionColor", emission);
            }
        }

        private void _TintFx(ParticleSystem ps, Color c)
        {
            if (ps == null) return;
            Renderer r = ps.GetComponent<Renderer>();
            if (r != null) r.material.SetColor("_TintColor", c * 0.6f);
        }
    }
}
