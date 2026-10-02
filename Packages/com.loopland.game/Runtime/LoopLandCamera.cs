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
        [Tooltip("Seconds for the eased glide from a close-up back to the overview.")]
        public float panBackTime = 3.2f;
        [Tooltip("Seconds for the eased move into a new shot (dice, chase, close-up).")]
        public float shotBlendTime = 1.1f;
        [Tooltip("How high the camera lifts (meters) in the middle of the pan back, like a crane move.")]
        public float panArc = 0.5f;
        public float posSharpness = 2.5f;
        public float rotSharpness = 3f;

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
        private int lastShot = -1;
        private Vector3 fromPos;
        private Quaternion fromRot;
        private float blendStart;
        private float blendTime;
        private float smoothY;
        private Vector3 smoothF = Vector3.forward;
        private bool chaseInit;

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

            if (shot != lastShot)
            {
                // every shot change starts a fresh eased blend from wherever the camera is right now
                lastShot = shot;
                fromPos = transform.position;
                fromRot = transform.rotation;
                blendStart = Time.time;
                blendTime = shot == SHOT_OVERVIEW ? panBackTime : shotBlendTime;
                chaseInit = false;
            }

            float dt = Time.deltaTime;
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
                // smooth out the token's hop bounce and the direction snaps between spaces
                Transform t = token.transform;
                Vector3 f = t.forward;
                f.y = 0f;
                if (f.sqrMagnitude < 0.001f) f = Vector3.forward;
                f.Normalize();
                if (!chaseInit) { chaseInit = true; smoothY = t.position.y; smoothF = f; }
                smoothY = Mathf.Lerp(smoothY, t.position.y, 1f - Mathf.Exp(-2f * dt));
                smoothF = Vector3.Slerp(smoothF, f, 1f - Mathf.Exp(-3f * dt));
                Vector3 tp = new Vector3(t.position.x, smoothY, t.position.z);
                Vector3 right = Vector3.Cross(Vector3.up, smoothF);
                gp = tp - smoothF * chaseDistance + right * chaseSide + Vector3.up * chaseHeight;
                gr = Quaternion.LookRotation(tp + smoothF * 0.35f + Vector3.up * 0.05f - gp, Vector3.up);
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

            float k = blendTime > 0f ? Mathf.Clamp01((Time.time - blendStart) / blendTime) : 1f;
            if (k < 1f)
            {
                // smootherstep: zero speed at the start and the end, so the camera eases out and eases in
                float e = k * k * k * (k * (6f * k - 15f) + 10f);
                Vector3 p = Vector3.Lerp(fromPos, gp, e);
                if (shot == SHOT_OVERVIEW) p += Vector3.up * (panArc * Mathf.Sin(e * Mathf.PI));
                transform.position = p;
                transform.rotation = Quaternion.Slerp(fromRot, gr, e);
            }
            else
            {
                transform.position = Vector3.Lerp(transform.position, gp, 1f - Mathf.Exp(-posSharpness * dt));
                transform.rotation = Quaternion.Slerp(transform.rotation, gr, 1f - Mathf.Exp(-rotSharpness * dt));
            }
        }
    }
}
