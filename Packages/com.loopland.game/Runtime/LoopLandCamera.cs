using UdonSharp;
using UnityEngine;

namespace LoopLand
{
    /// <summary>
    /// Live board camera director (perspective). Shots:
    /// overview (wide angled view of the whole loop) -> dice close-up when someone rolls -> chase cam from the side
    /// while their token hops -> close-up of the card it landed on (rotated so the card reads upright) -> pan back.
    /// Runs locally on every client from the synced game events, so everyone sees the same show.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LoopLandCamera : UdonSharpBehaviour
    {
        public Camera cam;
        public LoopLandGame game;
        public Transform overviewPose;
        [Header("Shots")]
        public float diceTime = 1.5f;
        public float closeUpTime = 2.6f;
        public float chaseDistance = 0.75f;
        public float chaseSide = 0.45f;
        public float chaseHeight = 0.5f;
        public float closeUpHeight = 0.75f;
        public float closeUpBack = 0.35f;
        [Header("Smoothing")]
        public float posSharpness = 3f;
        public float rotSharpness = 3.5f;

        private const int SHOT_OVERVIEW = 0;
        private const int SHOT_DICE = 1;
        private const int SHOT_CHASE = 2;
        private const int SHOT_CLOSE = 3;

        private int shot;
        private float shotEnd;
        private Transform diceA;
        private Transform diceB;
        private LoopLandToken token;
        private Transform landing;

        /// <summary>Dice close-up, then chase the token, then close-up on the landing space.</summary>
        public void _ShowRoll(Transform a, Transform b, LoopLandToken tk, Transform land)
        {
            diceA = a;
            diceB = b;
            token = tk;
            landing = land;
            shot = SHOT_DICE;
            shotEnd = Time.time + diceTime;
        }

        /// <summary>Chase the token (for card moves / teleports), then close-up on the landing space.</summary>
        public void _ShowMove(LoopLandToken tk, Transform land)
        {
            token = tk;
            landing = land;
            shot = SHOT_CHASE;
            shotEnd = Time.time + 1.0f;
        }

        public void _Overview()
        {
            shot = SHOT_OVERVIEW;
        }

        private void LateUpdate()
        {
            if (shot == SHOT_DICE && Time.time > shotEnd)
            {
                shot = token != null ? SHOT_CHASE : SHOT_OVERVIEW;
                shotEnd = Time.time + 1.0f;
            }
            else if (shot == SHOT_CHASE && Time.time > shotEnd && (token == null || !token._IsMoving()))
            {
                shot = landing != null ? SHOT_CLOSE : SHOT_OVERVIEW;
                shotEnd = Time.time + closeUpTime;
                if (shot == SHOT_CLOSE && game != null) game._OnCameraLanded();
            }
            else if (shot == SHOT_CLOSE && Time.time > shotEnd) shot = SHOT_OVERVIEW;

            Vector3 gp;
            Quaternion gr;
            if (shot == SHOT_DICE && diceA != null)
            {
                Vector3 m = diceB != null ? (diceA.position + diceB.position) * 0.5f : diceA.position;
                gp = m + new Vector3(-0.55f, 0.75f, -0.85f);
                gr = Quaternion.LookRotation(m - gp, Vector3.up);
            }
            else if (shot == SHOT_CHASE && token != null)
            {
                Transform t = token.transform;
                Vector3 f = t.forward;
                f.y = 0f;
                if (f.sqrMagnitude < 0.001f) f = Vector3.forward;
                f.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, f);
                gp = t.position - f * chaseDistance + right * chaseSide + Vector3.up * chaseHeight;
                gr = Quaternion.LookRotation(t.position + f * 0.35f + Vector3.up * 0.05f - gp, Vector3.up);
            }
            else if (shot == SHOT_CLOSE && landing != null)
            {
                // the card's text runs "up" toward the inside of the loop (the anchor's forward), so use that as screen-up
                Vector3 inward = landing.forward;
                Vector3 c = landing.position;
                gp = c - inward * closeUpBack + Vector3.up * closeUpHeight;
                gr = Quaternion.LookRotation(c + inward * 0.03f - gp, inward);
            }
            else if (overviewPose != null)
            {
                gp = overviewPose.position;
                gr = overviewPose.rotation;
            }
            else return;

            float dt = Time.deltaTime;
            transform.position = Vector3.Lerp(transform.position, gp, 1f - Mathf.Exp(-posSharpness * dt));
            transform.rotation = Quaternion.Slerp(transform.rotation, gr, 1f - Mathf.Exp(-rotSharpness * dt));
        }
    }
}
