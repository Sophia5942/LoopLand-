using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace LoopLand
{
    /// <summary>
    /// A real glass elevator. The car rides between floors on a timeline shared through server time, so every player
    /// sees it move. Anyone inside when it leaves is held in a standing station on the car, so riders travel up in real
    /// time and watch the plaza drop away. Car doors and landing doors slide open only at the floor where the car is.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class LoopLandLift : UdonSharpBehaviour
    {
        [Header("Car and floors")]
        public Transform car;
        public float[] floorHeights;
        public string[] floorNames;
        public int[] floorDoorSide;                 // per floor: 0 = the car's front doors open, 1 = the rear doors
        public Vector3 cabinHalfSize = new Vector3(1.55f, 1.5f, 1.4f);

        [Header("Doors (closed positions are read on start)")]
        public Transform[] carDoors;                // front L, front R, rear L, rear R
        public Transform[] landingDoors;            // floor 0 L, floor 0 R, floor 1 L, floor 1 R
        public float doorSlide = 0.72f;
        public float doorTime = 1.5f;
        public float maxSpeed = 3.5f;

        [Header("Riders")]
        public VRC.SDK3.Components.VRCStation[] seats;

        [Header("Feedback")]
        public TMP_Text[] displays;
        public AudioSource carHum;
        public AudioSource carFx;
        public AudioSource[] landingFx;
        public AudioClip dingClip;
        public AudioClip doorClip;

        [UdonSynced] private int syncFrom;
        [UdonSynced] private int syncTo;
        [UdonSynced] private double syncStart;
        [UdonSynced] private int syncId;

        private Vector3[] carDoorHome;
        private Vector3[] landingDoorHome;
        private int phase = 3;                      // 0 closing, 1 moving, 2 opening, 3 idle
        private int lastPhase = -1;
        private int lastId = -1;
        private bool seated;
        private bool riding;
        private int seat = -1;
        private int seatId = -1;
        private int seatTries;
        private int leaveTries;
        private float nextTry;
        private string shown = "";
        private VRCPlayerApi[] players = new VRCPlayerApi[90];

        private void Start()
        {
            carDoorHome = new Vector3[carDoors.Length];
            for (int i = 0; i < carDoors.Length; i++) if (carDoors[i] != null) carDoorHome[i] = carDoors[i].localPosition;
            landingDoorHome = new Vector3[landingDoors.Length];
            for (int i = 0; i < landingDoors.Length; i++) if (landingDoors[i] != null) landingDoorHome[i] = landingDoors[i].localPosition;
        }

        // ------------------------------------------------------------------ buttons

        public void _GoPlaza() { _Request(0); }
        public void _GoLoop() { _Request(1); }

        public void _SeatEntered(int index)
        {
            seated = true;
            seat = index;
        }

        public void _SeatExited(int index)
        {
            if (index != seat) return;
            seated = false;
            riding = false;
        }

        private void _Request(int floor)
        {
            if (floorHeights == null || floor < 0 || floor >= floorHeights.Length) return;
            if (phase != 3) return;                 // already on its way
            if (syncTo == floor)
            {
                _Play(floor, dingClip);             // it's here, doors are open
                return;
            }
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) return;
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(lp, gameObject);
            syncFrom = syncTo;
            syncTo = floor;
            syncStart = Networking.GetServerTimeInSeconds() + 0.25;
            syncId++;
            RequestSerialization();
        }

        // ------------------------------------------------------------------ motion

        private void Update()
        {
            if (car == null || floorHeights == null || floorHeights.Length < 2) return;
            int from = Mathf.Clamp(syncFrom, 0, floorHeights.Length - 1);
            int to = Mathf.Clamp(syncTo, 0, floorHeights.Length - 1);
            float travel = Mathf.Max(1f, Mathf.Abs(floorHeights[to] - floorHeights[from]) * 1.875f / maxSpeed);
            float t = syncId == 0 ? 100000f : (float)(Networking.GetServerTimeInSeconds() - syncStart);
            float u = 1f;
            float open = 1f;
            int at = to;
            if (t < doorTime)
            {
                phase = 0;
                at = from;
                u = 0f;
                open = 1f - Mathf.Clamp01(t / doorTime);
            }
            else if (t < doorTime + travel)
            {
                phase = 1;
                u = (t - doorTime) / travel;
                open = 0f;
            }
            else if (t < doorTime * 2f + travel)
            {
                phase = 2;
                open = (t - doorTime - travel) / doorTime;
            }
            else phase = 3;

            float e = u * u * u * (u * (u * 6f - 15f) + 10f);   // smootherstep: gentle start and stop
            float y = Mathf.Lerp(floorHeights[from], floorHeights[to], e);
            Vector3 p = car.localPosition;
            p.y = y;
            car.localPosition = p;
            _Doors(at, open);
            _Riders();
            _Feedback(from, to, y, u, t);
        }

        private void _Doors(int at, float open)
        {
            float slide = doorSlide * open * open * (3f - 2f * open);
            int side = floorDoorSide != null && at < floorDoorSide.Length ? floorDoorSide[at] : 0;
            for (int i = 0; i < carDoors.Length; i++)
            {
                if (carDoors[i] == null) continue;
                float s = i / 2 == side ? slide : 0f;
                carDoors[i].localPosition = carDoorHome[i] + Vector3.right * (i % 2 == 0 ? -s : s);
            }
            for (int i = 0; i < landingDoors.Length; i++)
            {
                if (landingDoors[i] == null) continue;
                float s = i / 2 == at ? slide : 0f;
                landingDoors[i].localPosition = landingDoorHome[i] + Vector3.right * (i % 2 == 0 ? -s : s);
            }
        }

        // ------------------------------------------------------------------ riders

        private void _Riders()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || seats == null || seats.Length == 0 || Time.time < nextTry) return;
            if (phase == 0 && !seated && _Inside(lp.GetPosition()))
            {
                // hold the rider in a standing spot on the car for the trip
                if (seatId != syncId)
                {
                    seatId = syncId;
                    seatTries = 0;
                }
                seat = (_Rank(lp) + seatTries) % seats.Length;
                seatTries++;
                riding = true;
                leaveTries = 0;
                nextTry = Time.time + 0.35f;
                if (seats[seat] != null) seats[seat].UseStation(lp);
            }
            else if (phase >= 2 && riding)
            {
                nextTry = Time.time + 0.4f;
                leaveTries++;
                if (seat >= 0 && seat < seats.Length && seats[seat] != null) seats[seat].ExitStation(lp);
                if (!seated || leaveTries > 5) riding = false;
            }
        }

        private bool _Inside(Vector3 world)
        {
            Vector3 q = car.InverseTransformPoint(world);
            return Mathf.Abs(q.x) < cabinHalfSize.x && Mathf.Abs(q.z) < cabinHalfSize.z && q.y > -0.6f && q.y < cabinHalfSize.y * 2f;
        }

        /// <summary>How many riders with a lower player id are in the car: spreads riders over the standing spots.</summary>
        private int _Rank(VRCPlayerApi me)
        {
            int n = VRCPlayerApi.GetPlayerCount();
            if (players.Length < n) players = new VRCPlayerApi[n + 16];
            VRCPlayerApi.GetPlayers(players);
            int rank = 0;
            for (int i = 0; i < n; i++)
            {
                VRCPlayerApi p = players[i];
                if (Utilities.IsValid(p) && p.playerId < me.playerId && _Inside(p.GetPosition())) rank++;
            }
            return rank;
        }

        // ------------------------------------------------------------------ sound and displays

        private void _Feedback(int from, int to, float y, float u, float t)
        {
            if (phase != lastPhase || syncId != lastId)
            {
                bool fresh = lastPhase != -1;       // no sounds for a late joiner's first frame
                if (fresh && phase == 0 && t < doorTime * 0.5f) _Play(from, doorClip);
                if (fresh && phase == 2)
                {
                    _Play(to, dingClip);
                    _Play(to, doorClip);
                }
                if (carHum != null)
                {
                    if (phase == 1 && !carHum.isPlaying) carHum.Play();
                    else if (phase != 1 && carHum.isPlaying) carHum.Stop();
                }
                lastPhase = phase;
                lastId = syncId;
            }
            if (phase == 1 && carHum != null)
            {
                float v = Mathf.Clamp01(16f * u * u * (1f - u) * (1f - u));   // 0 at the stops, 1 at full speed
                carHum.volume = 0.25f + 0.55f * v;
                carHum.pitch = 0.85f + 0.3f * v;
            }
            string s;
            if (phase == 1) s = (to > from ? "<size=60%>GOING UP</size>\n" : "<size=60%>GOING DOWN</size>\n") + Mathf.RoundToInt(y) + " m";
            else
            {
                int f = phase == 0 ? from : to;
                s = floorNames != null && f < floorNames.Length ? floorNames[f] : "";
            }
            if (s == shown || displays == null) return;
            shown = s;
            for (int i = 0; i < displays.Length; i++) if (displays[i] != null) displays[i].text = s;
        }

        private void _Play(int floor, AudioClip clip)
        {
            if (clip == null) return;
            if (carFx != null) carFx.PlayOneShot(clip, 0.7f);
            if (landingFx != null && floor < landingFx.Length && landingFx[floor] != null) landingFx[floor].PlayOneShot(clip, 0.9f);
        }
    }
}
