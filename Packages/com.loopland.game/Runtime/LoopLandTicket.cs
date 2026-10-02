using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace LoopLand
{
    /// <summary>
    /// A scratchable ticket: silver foil flakes over a prize window. Used for Lucky Loop tickets on the board, free
    /// tickets at the Free Loop Scratch machine and the reveal of a Creator Economy purchase.
    /// Scratching is tracked here in Udon, so it never depends on UI hover events:
    /// - VR: the index fingertip (or the hand, for avatars without finger bones) wipes the foil where it touches;
    /// - desktop: the middle of the view wipes it as you look across it.
    /// Once enough foil is gone, the target behaviour gets doneEvent. Everything is local to the player.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LoopLandTicket : UdonSharpBehaviour
    {
        public UdonSharpBehaviour target;
        public string doneEvent = "_OnTicketScratched";
        public GameObject root;
        public Transform cardSpace;     // the foil's RectTransform: 1 unit = 1 mm, +Z points into the ticket
        public GameObject[] cells;      // foil flakes
        public Image prizeIcon;
        public TMP_Text prizeTitle;
        public TMP_Text prizeSub;
        public TMP_Text headerText;
        public TMP_Text hintText;
        [Range(0.2f, 1f)] public float revealAt = 0.65f;
        public float brush = 32f;       // scratch radius in mm
        public float reach = 70f;       // how far above the ticket a fingertip still scratches, in mm
        public ParticleSystem dust;
        public AudioSource sfx;
        public AudioClip scratchClip;
        public AudioClip doneClip;

        private float[] flakeX;
        private float[] flakeY;
        private bool[] gone;
        private float minX;
        private float maxX;
        private float minY;
        private float maxY;
        private int removed;
        private bool active;
        private bool done;

        // 0 left hand, 1 right hand, 2 desktop view
        private float[] lastX = new float[3];
        private float[] lastY = new float[3];
        private float[] lastT = new float[3];
        private int removedNow;
        private int scratchHand;
        private float dustX;
        private float dustY;
        private float nextSound;

        public bool _IsActive() { return active; }
        public bool _IsDone() { return done; }

        /// <summary>Shows a fresh ticket with this prize under the foil.</summary>
        public void _Show(Sprite icon, string title, string sub, string header)
        {
            if (prizeIcon != null)
            {
                prizeIcon.sprite = icon;
                prizeIcon.gameObject.SetActive(icon != null);
            }
            if (prizeTitle != null) prizeTitle.text = title;
            if (prizeSub != null) prizeSub.text = sub;
            if (headerText != null && header.Length > 0) headerText.text = header;
            if (hintText != null) hintText.text = "<b>VR:</b> rub the silver with your finger   <b>DESKTOP:</b> look across the silver";
            if (root != null) root.SetActive(true);
            _Cache();
            if (cells != null)
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i] != null) cells[i].SetActive(true);
                    if (gone != null && i < gone.Length) gone[i] = false;
                }
            removed = 0;
            done = false;
            active = true;
            for (int h = 0; h < 3; h++) lastT[h] = 0f;
        }

        public void _Hide()
        {
            active = false;
            if (root != null) root.SetActive(false);
        }

        /// <summary>Wipes all the foil at once (used when a turn times out).</summary>
        public void _RevealAll()
        {
            if (cells != null) for (int i = 0; i < cells.Length; i++) if (cells[i] != null) cells[i].SetActive(false);
            if (gone != null) for (int i = 0; i < gone.Length; i++) gone[i] = true;
            removed = cells == null ? 0 : cells.Length;
            _Finish();
        }

        /// <summary>Remembers where every foil flake sits (in mm), so scratching is plain distance maths.</summary>
        private void _Cache()
        {
            if (flakeX != null || cells == null || cardSpace == null) return;
            int n = cells.Length;
            flakeX = new float[n];
            flakeY = new float[n];
            gone = new bool[n];
            minX = 100000f;
            maxX = -100000f;
            minY = 100000f;
            maxY = -100000f;
            for (int i = 0; i < n; i++)
            {
                if (cells[i] == null) continue;
                Vector3 p = cardSpace.InverseTransformPoint(cells[i].transform.position);
                flakeX[i] = p.x;
                flakeY[i] = p.y;
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
        }

        private void Update()
        {
            if (!active || cardSpace == null || flakeX == null) return;
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me)) return;
            removedNow = 0;
            if (me.IsUserInVR())
            {
                _Hand(me, 0);
                _Hand(me, 1);
            }
            else _View(me);
            if (removedNow > 0) _AfterScratch();
        }

        private void _Hand(VRCPlayerApi me, int h)
        {
            bool left = h == 0;
            Vector3 p = me.GetBonePosition(left ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal);
            float r = reach;
            if (p == Vector3.zero)
            {
                p = me.GetTrackingData(left ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand).position;
                r = reach + 60f;
            }
            Vector3 lp = cardSpace.InverseTransformPoint(p);
            if (lp.z < -r || lp.z > 80f)
            {
                lastT[h] = 0f;
                return;
            }
            _StrokeTo(h, lp.x, lp.y);
        }

        private void _View(VRCPlayerApi me)
        {
            VRCPlayerApi.TrackingData head = me.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 o = cardSpace.InverseTransformPoint(head.position);
            Vector3 d = cardSpace.InverseTransformDirection(head.rotation * Vector3.forward);
            if (d.z < 0.05f)
            {
                lastT[2] = 0f;
                return;
            }
            float t = -o.z / d.z;
            if (t < 0f || t > 2500f)
            {
                lastT[2] = 0f;
                return;
            }
            _StrokeTo(2, o.x + d.x * t, o.y + d.y * t);
        }

        /// <summary>Scratches along the path since the last frame, so fast strokes leave no gaps.</summary>
        private void _StrokeTo(int h, float x, float y)
        {
            if (lastT[h] > 0f && Time.time - lastT[h] < 0.2f)
            {
                float dx = x - lastX[h];
                float dy = y - lastY[h];
                int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(dx * dx + dy * dy) / (brush * 0.5f)), 1, 24);
                for (int k = 1; k <= steps; k++) _ScratchAt(lastX[h] + dx * k / steps, lastY[h] + dy * k / steps, h);
            }
            else _ScratchAt(x, y, h);
            lastX[h] = x;
            lastY[h] = y;
            lastT[h] = Time.time;
        }

        private void _ScratchAt(float x, float y, int h)
        {
            if (x < minX - brush || x > maxX + brush || y < minY - brush || y > maxY + brush) return;
            float r2 = brush * brush;
            for (int i = 0; i < flakeX.Length; i++)
            {
                if (gone[i]) continue;
                float ex = flakeX[i] - x;
                float ey = flakeY[i] - y;
                if (ex * ex + ey * ey > r2) continue;
                gone[i] = true;
                if (cells[i] != null) cells[i].SetActive(false);
                removed++;
                removedNow++;
                scratchHand = h;
                dustX = flakeX[i];
                dustY = flakeY[i];
            }
        }

        private void _AfterScratch()
        {
            if (dust != null)
            {
                dust.transform.position = cardSpace.TransformPoint(new Vector3(dustX, dustY, -8f));
                dust.Emit(Mathf.Min(8, removedNow * 2));
            }
            if (Time.time >= nextSound)
            {
                nextSound = Time.time + 0.09f;
                _Sfx(scratchClip, 0.7f);
                if (scratchHand < 2)
                {
                    VRCPlayerApi me = Networking.LocalPlayer;
                    if (Utilities.IsValid(me)) me.PlayHapticEventInHand(scratchHand == 0 ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right, 0.05f, 0.25f, 160f);
                }
            }
            if (!done && cells != null && removed >= Mathf.CeilToInt(cells.Length * revealAt)) _Finish();
        }

        private void _Finish()
        {
            if (done) return;
            done = true;
            if (hintText != null) hintText.text = "<color=#FFE14D><b>REVEALED!</b></color>";
            _Sfx(doneClip, 0.9f);
            if (target != null) target.SendCustomEvent(doneEvent);
        }

        private void _Sfx(AudioClip c, float v)
        {
            if (sfx != null && c != null) sfx.PlayOneShot(c, v);
        }
    }
}
