using UdonSharp;
using UnityEngine;

namespace LoopLand
{
    /// <summary>Animated die. The result comes from synced state; the tumble is local and always lands on that face.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LoopLandDice : UdonSharpBehaviour
    {
        public Transform restPoint;
        public Transform throwPoint;
        public Renderer rend;
        public ParticleSystem trail;
        public ParticleSystem burst;
        public AudioSource sfx;
        public AudioClip rollClip;
        public AudioClip landClip;
        public float duration = 1.15f;
        public float arcHeight = 0.45f;

        private bool rolling;
        private float t;
        private float delay;
        private Quaternion targetRot = Quaternion.identity;
        private Vector3 spinAxis = Vector3.right;
        private float spinTotal;
        private Material lastMat;

        public void _Roll(int value, float startDelay)
        {
            targetRot = _FaceUp(value);
            spinAxis = Random.onUnitSphere;
            spinTotal = Random.Range(900f, 1440f);
            delay = startDelay;
            t = 0f;
            rolling = true;
            if (sfx != null && rollClip != null) sfx.PlayOneShot(rollClip, 0.7f);
            if (trail != null) trail.Play();
        }

        public void _Show(int value)
        {
            rolling = false;
            targetRot = _FaceUp(value);
            if (restPoint != null) transform.position = restPoint.position;
            transform.rotation = targetRot;
        }

        public void _SetSkin(Material m, Color glow)
        {
            if (rend != null && m != null && m != lastMat) { rend.sharedMaterial = m; lastMat = m; }
            if (trail != null) { Renderer r = trail.GetComponent<Renderer>(); if (r != null) r.material.SetColor("_TintColor", glow * 0.6f); }
            if (burst != null) { Renderer r = burst.GetComponent<Renderer>(); if (r != null) r.material.SetColor("_TintColor", glow * 0.6f); }
        }

        private void Update()
        {
            if (!rolling || restPoint == null || throwPoint == null) return;
            if (delay > 0f) { delay -= Time.deltaTime; return; }
            t += Time.deltaTime / duration;
            if (t >= 1f)
            {
                rolling = false;
                transform.position = restPoint.position;
                transform.rotation = targetRot;
                if (trail != null) trail.Stop();
                if (burst != null) burst.Emit(35);
                if (sfx != null && landClip != null) sfx.PlayOneShot(landClip, 0.8f);
                return;
            }
            float e = 1f - (1f - t) * (1f - t);
            float bounce = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2.5f)) * (1f - t);
            transform.position = Vector3.Lerp(throwPoint.position, restPoint.position, e) + Vector3.up * (arcHeight * bounce);
            transform.rotation = Quaternion.AngleAxis(spinTotal * (1f - e), spinAxis) * targetRot;
        }

        private Quaternion _FaceUp(int v)
        {
            Vector3 n;
            if (v == 1) n = Vector3.up;
            else if (v == 6) n = Vector3.down;
            else if (v == 2) n = Vector3.forward;
            else if (v == 5) n = Vector3.back;
            else if (v == 3) n = Vector3.right;
            else n = Vector3.left;
            return Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up) * Quaternion.FromToRotation(n, Vector3.up);
        }
    }
}
