using UdonSharp;
using UnityEngine;

namespace LoopLand
{
    /// <summary>Ambient motion for decor: eased back-and-forth travel, a gentle bob and a slow spin.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LoopLandMover : UdonSharpBehaviour
    {
        public Vector3 moveOffset;
        public float movePeriod = 16f;
        public float bobHeight;
        public float bobPeriod = 7f;
        public Vector3 spin;

        private Vector3 start;
        private float phase;

        private void Start()
        {
            start = transform.localPosition;
            phase = Random.Range(0f, 20f);
        }

        private void Update()
        {
            float t = Time.time + phase;
            Vector3 p = start;
            if (moveOffset != Vector3.zero) p += moveOffset * (0.5f - 0.5f * Mathf.Cos(t / movePeriod * 2f * Mathf.PI));
            if (bobHeight > 0f) p.y += Mathf.Sin(t / bobPeriod * 2f * Mathf.PI) * bobHeight;
            transform.localPosition = p;
            if (spin != Vector3.zero) transform.Rotate(spin * Time.deltaTime, Space.Self);
        }
    }
}
