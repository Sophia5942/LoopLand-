using UdonSharp;
using UnityEngine;

namespace LoopLand
{
    /// <summary>
    /// Live board camera director. Shows the whole board by default; when someone rolls it zooms in on the dice,
    /// then follows that player's token around the loop, then eases back to the overview. Runs locally on every
    /// client from the synced game events, so everyone sees the same camera moves.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LoopLandCamera : UdonSharpBehaviour
    {
        public Camera cam;
        public float overviewSize = 1.75f;
        public float followSize = 0.95f;
        public float moveSharpness = 3.5f;
        public float zoomSharpness = 2.5f;
        [Tooltip("How far the zoomed view may pan from the center (keeps it on the board).")]
        public float limitX = 1.6f;
        public float limitZ = 0.8f;

        private Vector3 home;
        private Transform target;
        private Transform target2;
        private Transform nextTarget;
        private float phaseEnd;
        private float nextHold;
        private bool following;

        private void Start()
        {
            if (cam == null) cam = GetComponent<Camera>();
            home = transform.localPosition;
        }

        /// <summary>Follow a (or the midpoint of a and b) for firstTime seconds, then follow next for nextTime seconds.</summary>
        public void _FollowSequence(Transform a, Transform b, float firstTime, Transform next, float nextTime)
        {
            if (firstTime > 0f && a != null)
            {
                target = a;
                target2 = b;
                phaseEnd = Time.time + firstTime;
                nextTarget = next;
                nextHold = nextTime;
            }
            else
            {
                target = next;
                target2 = null;
                phaseEnd = Time.time + nextTime;
                nextTarget = null;
            }
            following = target != null;
        }

        public void _Overview()
        {
            following = false;
        }

        private void LateUpdate()
        {
            if (cam == null) return;
            if (following && Time.time > phaseEnd)
            {
                if (nextTarget != null)
                {
                    target = nextTarget;
                    target2 = null;
                    nextTarget = null;
                    phaseEnd = Time.time + nextHold;
                }
                else following = false;
            }

            Vector3 goal = home;
            float size = overviewSize;
            if (following && target != null)
            {
                Vector3 p = target.position;
                if (target2 != null) p = (p + target2.position) * 0.5f;
                if (transform.parent != null) p = transform.parent.InverseTransformPoint(p);
                goal = new Vector3(Mathf.Clamp(p.x, -limitX, limitX), home.y, Mathf.Clamp(p.z, -limitZ, limitZ));
                size = followSize;
            }
            float dt = Time.deltaTime;
            transform.localPosition = Vector3.Lerp(transform.localPosition, goal, 1f - Mathf.Exp(-moveSharpness * dt));
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, size, 1f - Mathf.Exp(-zoomSharpness * dt));
        }
    }
}
