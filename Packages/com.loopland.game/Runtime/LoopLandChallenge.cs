using TMPro;
using UdonSharp;
using UnityEngine;

namespace LoopLand
{
    /// <summary>
    /// Challenge tile mini-games, played on the dashboard in front of the player (VR laser or desktop click):
    /// LASER LOOP (hit 5 glowing targets in 15 seconds) and COIN RUSH (grab as many coins as you can in 12 seconds).
    /// The game moves the panel to the player's dashboard, calls _Begin, and gets the hits back in _ChallengeFinished.
    /// Local to the player whose turn it is.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LoopLandChallenge : UdonSharpBehaviour
    {
        public LoopLandGame game;
        public GameObject root;
        public RectTransform[] targets;         // target buttons; target k calls _OnHit<k>
        public UnityEngine.UI.Image[] targetImages;
        public Sprite laserSprite;
        public Sprite coinSprite;
        public Color laserColor = new Color(1f, 0.3f, 0.75f, 1f);
        public TMP_Text titleText;
        public TMP_Text infoText;
        public TMP_Text bigText;
        public Vector2 area = new Vector2(600f, 280f);
        public AudioSource sfx;
        public AudioClip hitClip;
        public AudioClip tickClip;
        public AudioClip goClip;
        public AudioClip endClip;

        private int type;
        private bool jackpot;
        private int state;          // 0 idle, 1 countdown, 2 playing, 3 result
        private float until;
        private float goUntil;
        private int hits;
        private int lastCount = -1;
        private float[] shownAt = new float[8];

        public bool _IsRunning() { return state != 0; }

        public string _Name(int t)
        {
            return t == 0 ? "LASER LOOP" : "COIN RUSH";
        }

        public string _Rules(int t)
        {
            return t == 0 ? "Hit 5 glowing targets in 15 seconds!" : "Grab as many coins as you can in 12 seconds!";
        }

        public void _Begin(int t, bool jp)
        {
            type = t;
            jackpot = jp;
            hits = 0;
            state = 1;
            until = Time.time + 3f;
            lastCount = -1;
            if (root != null) root.SetActive(true);
            _HideTargets();
            if (targetImages != null)
                for (int k = 0; k < targetImages.Length; k++)
                {
                    if (targetImages[k] == null) continue;
                    targetImages[k].sprite = t == 0 ? laserSprite : coinSprite;
                    targetImages[k].color = t == 0 ? laserColor : Color.white;
                }
            if (titleText != null) titleText.text = (jp ? "<color=#FFE14D>JACKPOT</color> " : "") + _Name(t);
            if (infoText != null) infoText.text = _Rules(t);
        }

        public void _Stop()
        {
            state = 0;
            _HideTargets();
            if (root != null) root.SetActive(false);
        }

        public void _OnHit0() { _Hit(0); }
        public void _OnHit1() { _Hit(1); }
        public void _OnHit2() { _Hit(2); }

        private void _Hit(int k)
        {
            if (state != 2) return;
            hits++;
            _Sfx(hitClip);
            if (type == 0 && hits >= 5)
            {
                _End();
                return;
            }
            _Place(k);
            _Info();
        }

        private void Update()
        {
            if (state == 0) return;
            if (state == 1)
            {
                int left = Mathf.CeilToInt(until - Time.time);
                if (left != lastCount && left > 0)
                {
                    lastCount = left;
                    if (bigText != null) bigText.text = left.ToString();
                    _Sfx(tickClip);
                }
                if (Time.time >= until)
                {
                    state = 2;
                    until = Time.time + (type == 0 ? 15f : 12f);
                    goUntil = Time.time + 0.7f;
                    if (bigText != null) bigText.text = "GO!";
                    _Sfx(goClip);
                    int n = type == 0 ? 1 : Mathf.Min(3, targets.Length);
                    for (int k = 0; k < n; k++) _Place(k);
                    _Info();
                }
            }
            else if (state == 2)
            {
                if (goUntil > 0f && Time.time > goUntil)
                {
                    goUntil = 0f;
                    if (bigText != null) bigText.text = "";
                }
                float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 10f);
                for (int k = 0; k < targets.Length; k++)
                {
                    if (targets[k] == null || !targets[k].gameObject.activeSelf) continue;
                    targets[k].localScale = new Vector3(pulse, pulse, 1f);
                    // laser targets don't wait around: they jump somewhere else after a moment
                    if (type == 0 && Time.time - shownAt[k] > 2.2f) _Place(k);
                }
                _Info();
                if (Time.time >= until) _End();
            }
            else if (state == 3 && Time.time >= until)
            {
                state = 0;
                if (root != null) root.SetActive(false);
                if (game != null) game._ChallengeFinished(hits);
            }
        }

        private void _End()
        {
            state = 3;
            until = Time.time + 2.5f;
            _HideTargets();
            if (bigText != null) bigText.text = type == 0 ? hits + " / 5 HITS!" : hits + " COINS GRABBED!";
            if (infoText != null) infoText.text = jackpot ? "JACKPOT CHALLENGE: your coins are tripled!" : "Nice! Your coins are on the way.";
            _Sfx(endClip);
        }

        private void _Info()
        {
            if (infoText == null) return;
            int secs = Mathf.CeilToInt(Mathf.Max(0f, until - Time.time));
            infoText.text = "TIME <color=#FFE14D>" + secs + "</color>      HITS <color=#7CFF4F>" + hits + (type == 0 ? " / 5" : "") + "</color>";
        }

        /// <summary>Puts target k somewhere new inside the play area, away from the other targets.</summary>
        private void _Place(int k)
        {
            if (targets == null || k >= targets.Length || targets[k] == null) return;
            Vector2 p = Vector2.zero;
            for (int tries = 0; tries < 8; tries++)
            {
                p = new Vector2(Random.Range(-area.x * 0.5f, area.x * 0.5f), Random.Range(-area.y * 0.5f, area.y * 0.5f));
                bool ok = true;
                for (int j = 0; j < targets.Length; j++)
                {
                    if (j == k || targets[j] == null || !targets[j].gameObject.activeSelf) continue;
                    if ((targets[j].anchoredPosition - p).sqrMagnitude < 150f * 150f)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) break;
            }
            targets[k].anchoredPosition = p;
            targets[k].gameObject.SetActive(true);
            if (k < shownAt.Length) shownAt[k] = Time.time;
        }

        private void _HideTargets()
        {
            if (targets == null) return;
            for (int k = 0; k < targets.Length; k++) if (targets[k] != null) targets[k].gameObject.SetActive(false);
        }

        private void _Sfx(AudioClip c)
        {
            if (sfx != null && c != null) sfx.PlayOneShot(c, 0.8f);
        }
    }
}
