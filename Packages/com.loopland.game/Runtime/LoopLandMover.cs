using UdonSharp;
using UnityEngine;

namespace LoopLand
{
    /// <summary>Ambient motion for decor: eased back-and-forth travel, a gentle bob, a slow spin and texture scrolling (water, light).</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LoopLandMover : UdonSharpBehaviour
    {
        public Vector3 moveOffset;
        public float movePeriod = 16f;
        public float bobHeight;
        public float bobPeriod = 7f;
        public Vector3 spin;
        public Renderer scrollRenderer;
        public Vector2 scrollSpeed;

        private Vector3 start;
        private float phase;
        private bool moves;
        private Material scrollMat;
        private Vector2 scrollOffset;

        private void Start()
        {
            start = transform.localPosition;
            phase = Random.Range(0f, 20f);
            moves = moveOffset != Vector3.zero || bobHeight > 0f;
            if (scrollRenderer != null) scrollMat = scrollRenderer.material;
        }

        private void Update()
        {
            if (moves)
            {
                float t = Time.time + phase;
                Vector3 p = start;
                if (moveOffset != Vector3.zero) p += moveOffset * (0.5f - 0.5f * Mathf.Cos(t / movePeriod * 2f * Mathf.PI));
                if (bobHeight > 0f) p.y += Mathf.Sin(t / bobPeriod * 2f * Mathf.PI) * bobHeight;
                transform.localPosition = p;
            }
            if (spin != Vector3.zero) transform.Rotate(spin * Time.deltaTime, Space.Self);
            if (scrollMat != null)
            {
                scrollOffset += scrollSpeed * Time.deltaTime;
                scrollOffset.x = Mathf.Repeat(scrollOffset.x, 1f);
                scrollOffset.y = Mathf.Repeat(scrollOffset.y, 1f);
                scrollMat.SetTextureOffset("_MainTex", scrollOffset);
            }
        }
    }
}
