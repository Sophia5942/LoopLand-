using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace LoopLand
{
    /// <summary>
    /// Two-stop elevator (0 = plaza, 1 = Loop Deck). The rider presses a button: the doors close, the floor counter
    /// rolls and the cabin lights pulse, then the rider is teleported into the other cabin and its doors open.
    /// Everything is local to the rider, so any number of people can ride at once.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LoopLandElevator : UdonSharpBehaviour
    {
        public Transform[] riderSpots;
        public Transform[] doorsLeft;
        public Transform[] doorsRight;
        public TMP_Text[] displays;
        public Renderer[] rideLights;
        public AudioSource[] cabinAudio;
        public AudioClip dingClip;
        public AudioClip rideClip;
        public Color lightColor = new Color(1f, 1f, 1f, 1f);
        public float doorSlide = 0.72f;
        public float doorTime = 0.9f;
        public float rideTime = 3.2f;
        public int topFloor = 60;
        public string[] floorNames = { "PLAZA", "LOOP DECK" };

        private int state;
        private int from;
        private int to;
        private float t0;
        private float[] open = { 1f, 1f };
        private Vector3[] leftHome = new Vector3[2];
        private Vector3[] rightHome = new Vector3[2];

        private void Start()
        {
            for (int i = 0; i < 2; i++)
            {
                if (doorsLeft != null && i < doorsLeft.Length && doorsLeft[i] != null) leftHome[i] = doorsLeft[i].localPosition;
                if (doorsRight != null && i < doorsRight.Length && doorsRight[i] != null) rightHome[i] = doorsRight[i].localPosition;
                _Doors(i);
            }
            _Displays();
        }

        public void _GoUp() { _Begin(0); }
        public void _GoDown() { _Begin(1); }

        private void _Begin(int cabin)
        {
            if (state != 0) return;
            from = cabin;
            to = 1 - cabin;
            state = 1;
            t0 = Time.time;
            _Play(from, dingClip);
        }

        private void Update()
        {
            if (state == 0) return;
            float k = Time.time - t0;
            if (state == 1)
            {
                open[from] = Mathf.Clamp01(1f - k / doorTime);
                _Doors(from);
                if (k >= doorTime) { state = 2; t0 = Time.time; _Play(from, rideClip); }
            }
            else if (state == 2)
            {
                float u = Mathf.Clamp01(k / rideTime);
                float e = u * u * (3f - 2f * u);
                int floor = Mathf.RoundToInt(Mathf.Lerp(from == 0 ? 1f : topFloor, to == 0 ? 1f : topFloor, e));
                _SetDisplay(from, floor.ToString());
                _Lights(Color.HSVToRGB(Mathf.Repeat(k * 0.35f, 1f), 0.6f, 1f) * 2f);
                if (u >= 1f)
                {
                    VRCPlayerApi lp = Networking.LocalPlayer;
                    if (Utilities.IsValid(lp) && riderSpots != null && to < riderSpots.Length && riderSpots[to] != null)
                        lp.TeleportTo(riderSpots[to].position, riderSpots[to].rotation);
                    open[to] = 0f;
                    _Doors(to);
                    open[from] = 1f;
                    _Doors(from);
                    _Displays();
                    _Lights(lightColor);
                    _Play(to, dingClip);
                    state = 3;
                    t0 = Time.time;
                }
            }
            else if (state == 3)
            {
                open[to] = Mathf.Clamp01(k / doorTime);
                _Doors(to);
                if (k >= doorTime) state = 0;
            }
        }

        private void _Doors(int i)
        {
            if (doorsLeft != null && i < doorsLeft.Length && doorsLeft[i] != null) doorsLeft[i].localPosition = leftHome[i] + Vector3.left * (doorSlide * open[i]);
            if (doorsRight != null && i < doorsRight.Length && doorsRight[i] != null) doorsRight[i].localPosition = rightHome[i] + Vector3.right * (doorSlide * open[i]);
        }

        private void _Displays()
        {
            for (int i = 0; i < 2; i++) _SetDisplay(i, floorNames != null && i < floorNames.Length ? floorNames[i] : "");
        }

        private void _SetDisplay(int i, string text)
        {
            if (displays != null && i < displays.Length && displays[i] != null) displays[i].text = text;
        }

        private void _Lights(Color c)
        {
            if (rideLights == null) return;
            for (int i = 0; i < rideLights.Length; i++) if (rideLights[i] != null) rideLights[i].material.SetColor("_EmissionColor", c);
        }

        private void _Play(int i, AudioClip clip)
        {
            if (clip != null && cabinAudio != null && i < cabinAudio.Length && cabinAudio[i] != null) cabinAudio[i].PlayOneShot(clip, 0.8f);
        }
    }
}
