using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Persistence;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace LoopLand
{
    /// <summary>
    /// LoopLand game brain: race around the infinity loop, win challenges, scratch Lucky Loop tickets, survive the chaos
    /// and finish with the most coins. Each lap the Loop gets wilder (bigger rewards, more Mystery tiles, jackpots).
    /// The owner of this object runs the rules and syncs the state (manual sync). Every other player sends commands to
    /// the owner with a [NetworkCallable] event; the sender is NetworkCalling.CallingPlayer, so nobody can act for
    /// someone else. All visuals are rebuilt from the synced state, so late joiners always see the correct board.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class LoopLandGame : UdonSharpBehaviour
    {
        private const int MAXP = 6;
        private const int SPACES = 40;
        private const int PANELS = 4;

        private const int PH_LOBBY = 0;
        private const int PH_PLAY = 1;
        private const int PH_OVER = 2;

        private const int ST_ROLL = 0;
        private const int ST_MOVING = 1;
        private const int ST_TICKET = 2;
        private const int ST_CHALLENGE = 3;
        private const int ST_DONE = 4;

        public const int T_START = 0;
        public const int T_COINS = 1;
        public const int T_LUCKY = 2;
        public const int T_CHALLENGE = 3;
        public const int T_POWER = 4;
        public const int T_MYSTERY = 5;
        public const int T_PORTAL = 6;

        private const int C_JOIN = 1;
        private const int C_LEAVE = 2;
        private const int C_START = 3;
        private const int C_ROLL = 4;
        private const int C_TICKET = 5;
        private const int C_CHALLENGE = 6;
        private const int C_RESET = 7;

        // sound and effect cues (index into fxClips)
        public const int FX_CLICK = 0;
        public const int FX_COIN = 1;
        public const int FX_LOSE = 2;
        public const int FX_TICKET = 3;
        public const int FX_WIN = 4;
        public const int FX_POWER = 5;
        public const int FX_MYSTERY = 6;
        public const int FX_LEVEL = 7;
        public const int FX_START = 8;
        public const int FX_LAP = 9;
        public const int FX_CHALLENGE = 10;
        public const int FX_PORTAL = 11;

        // result pictures (index into resultArt)
        public const int I_COIN = 0;
        public const int I_LOSE = 1;
        public const int I_TICKET = 2;
        public const int I_CHALLENGE = 3;
        public const int I_SHIELD = 4;
        public const int I_BOOST = 5;
        public const int I_SWAP = 6;
        public const int I_ROLL = 7;
        public const int I_MYSTERY = 8;
        public const int I_START = 9;
        public const int I_PORTAL = 10;
        public const int I_NONE = 11;
        public const int I_JACKPOT = 12;

        // Lucky Loop ticket rewards (gameplay only: nothing here can be bought, traded or cashed out)
        private const int R_COINS = 0;
        private const int R_BIG_COINS = 1;
        private const int R_DOUBLE = 2;
        private const int R_SHIELD = 3;
        private const int R_MOVE3 = 4;
        private const int R_EXTRA_ROLL = 5;
        private const int R_SWAP = 6;
        private const int R_JACKPOT_GAME = 7;
        private const int R_NOTHING = 8;
        private const int R_LOOP_JACKPOT = 9;
        private int[] ticketWeights = { 18, 14, 14, 12, 10, 10, 8, 6, 8, 6 };

        [Header("Rules")]
        [Tooltip("The player with the most coins after this many rounds wins.")]
        public int roundsToPlay = 10;
        public int lapBonus = 100;
        [Tooltip("Set to 2 for public worlds. 1 lets you test solo in ClientSim.")]
        public int minPlayersToStart = 1;
        [Tooltip("Seconds before an idle player's turn is played for them. 0 = never.")]
        public float afkSeconds = 60f;
        [Tooltip("How long TURN COMPLETE shows before the next player's turn.")]
        public float turnDoneSeconds = 2f;
        public int maxPowerUps = 3;

        [Header("Loop Coin rewards (persistent store currency)")]
        public int coinsPerLap = 5;
        public int coinsPerChallenge = 3;
        public int coinsFinish = 20;
        public int coinsWin = 100;

        [Header("References (auto-wired by LoopLand > Build Game In Scene)")]
        public LoopLandStore store;
        public LoopLandScratch freeScratch;
        public LoopLandTicket ticket;
        public LoopLandChallenge challenge;
        public LoopLandToken[] tokens;
        public LoopLandDice[] dice;
        public Transform[] spaceAnchors;
        public Transform turnMarker;
        public Renderer turnMarkerRenderer;
        public GameObject[] mysteryBadges;
        public Transform spinner;
        public TMP_Text[] statusTexts;
        public TMP_Text[] playerTexts;
        public TMP_Text[] cardTexts;
        public TMP_Text[] captionTexts;
        public TMP_Text[] viewLabels;
        public GameObject[] seatScreens;
        public GameObject centerScreens;
        public LoopLandCamera liveCam;
        public ParticleSystem celebrateFx;
        public ParticleSystem moneyFx;
        public AudioSource sfx;
        public AudioClip[] fxClips;
        public Sprite[] resultArt;
        public Color[] slotColors;
        public string[] slotHex = { "00E5FF", "FF3DCB", "FFD23F", "7CFF4F", "FF8A3D", "A57BFF" };

        [Header("Dashboards (one per console)")]
        public Transform[] dashSpots;
        public TMP_Text[] primaryLabels;
        public TMP_Text[] infoTexts;
        public GameObject[] panelRoots;     // console * 4 + panel: my status, tickets, power-ups, how to play
        public TMP_Text[] statusBodies;
        public TMP_Text[] ticketBodies;
        public TMP_Text[] powerBodies;
        public GameObject[] resultCards;
        public TMP_Text[] resultTitles;
        public TMP_Text[] resultSubs;
        public Image[] resultIcons;
        public GameObject[] introCards;
        public TMP_Text[] introTitles;
        public TMP_Text[] introBodies;

        [Header("Board (edit, then rebuild the board). Types: 0 start, 1 coins, 2 lucky loop, 3 challenge, 4 power, 5 mystery, 6 portal")]
        public string[] spaceName = {
            "LOOP START", "Pixel Alley", "Lucky Loop", "Byte Bay", "Challenge", "North Portal", "Neon Row", "Power Up", "Synth Street", "Mystery",
            "Challenge Arena", "Avatar Ave", "Lucky Loop", "Glitch Leak", "Power Up", "East Portal", "Udon Lane", "Challenge", "Sync Square", "Mystery",
            "Lucky Plaza", "Shader Strip", "Power Up", "Bloom Blvd", "Challenge", "South Portal", "Hologram Hill", "Lucky Loop", "Lag Spike", "Mystery",
            "Laser Dome", "Crystal Coast", "Power Up", "Aurora Arcade", "Lucky Loop", "West Portal", "Challenge", "Quantum Quay", "Mystery", "Infinity Tower" };
        public int[] spaceType = { 0, 1, 2, 1, 3, 6, 1, 4, 1, 5, 3, 1, 2, 1, 4, 6, 1, 3, 1, 5, 2, 1, 4, 1, 3, 6, 1, 2, 1, 5, 3, 1, 4, 1, 2, 6, 3, 1, 5, 1 };
        [Tooltip("Coins tiles: coins gained (negative = coins lost).")]
        public int[] spaceValue = { 0, 100, 0, 50, 0, 0, 150, 0, 100, 0, 0, 100, 0, -50, 0, 0, 200, 0, 100, 0, 0, 150, 0, 100, 0, 0, 50, 0, -50, 0, 0, 200, 0, 100, 0, 0, 0, 150, 0, 200 };
        [Tooltip("1 = this tile turns into a Mystery tile from Loop 3.")]
        public int[] spaceWake = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };

        // ---------------- synced state ----------------
        [UdonSynced] private int phase;
        [UdonSynced] private int[] slotPid = new int[MAXP];
        [UdonSynced] private int[] coins = new int[MAXP];
        [UdonSynced] private int[] pos = new int[MAXP];
        [UdonSynced] private int[] laps = new int[MAXP];
        [UdonSynced] private int[] shields = new int[MAXP];
        [UdonSynced] private int[] boosts = new int[MAXP];
        [UdonSynced] private int[] wins = new int[MAXP];
        [UdonSynced] private int[] moveMode = new int[MAXP];
        [UdonSynced] private int[] coinsEarned = new int[MAXP];
        [UdonSynced] private int turnSlot;
        [UdonSynced] private int stage;
        [UdonSynced] private int die1 = 1;
        [UdonSynced] private int die2 = 1;
        [UdonSynced] private int rollSeq;
        [UdonSynced] private int lastSteps;
        [UdonSynced] private int round;
        [UdonSynced] private int maxRounds = 10;
        [UdonSynced] private int loopLevel = 1;
        [UdonSynced] private int winner;
        [UdonSynced] private int matchId;
        [UdonSynced] private int ticketSeq;
        [UdonSynced] private int ticketReward;
        [UdonSynced] private int challengeSeq;
        [UdonSynced] private int challengeType;
        [UdonSynced] private bool challengeJackpot;
        [UdonSynced] private int resultSeq;
        [UdonSynced] private int resultIcon;
        [UdonSynced] private string resultTitle = "";
        [UdonSynced] private string resultSub = "";
        [UdonSynced] private int cardSeq;
        [UdonSynced] private string cardText = "";
        [UdonSynced] private int fxSeq;
        [UdonSynced] private int fxType;
        [UdonSynced] private string log = "";

        // ---------------- owner-only state ----------------
        private float stageTime;
        private float seatCheckTime;
        private int chain;
        private bool moveIsWarp;
        private bool resolveOnArrive = true;
        private string arriveTitle = "";
        private string arriveSub = "";
        private int arriveIcon;

        // ---------------- local state ----------------
        private int seenRoll = -1;
        private int seenCard = -1;
        private int seenFx = -1;
        private int seenResult = -1;
        private int seenPhase = -1;
        private float captionUntil;
        private float cardHideTime;
        private float resultUntil;
        private float toastUntil;
        private float resetArmTime = -10f;
        private float nextLocalRefresh;
        private int panel;
        private bool topView;
        private int viewSeat = -1;
        private int storeSeat = -1;
        private int camSeenPos = -1;
        private int camSeenSlot = -1;
        private int shownTicketSeq = -1;
        private int ticketSentSeq = -1;
        private int challengeSentSeq = -1;
        private int startHookMatch;
        private int finishHookMatch;

        private void Start()
        {
            _ApplyViews();
            _RefreshAll();
        }

        // =====================================================================
        //  Input from buttons (local)
        // =====================================================================

        /// <summary>The big context button: JOIN GAME, START GAME, ROLL, PLAY CHALLENGE, SCRATCH TICKET, NEW GAME.</summary>
        public void _OnPrimary()
        {
            int me = _LocalSlot();
            if (phase == PH_LOBBY) { _Send(me < 0 ? C_JOIN : C_START, 0); return; }
            if (phase == PH_OVER) { _Send(C_RESET, 0); return; }
            if (me < 0) { _Toast("A game is running. Join the next one from the lobby!"); return; }
            if (me != turnSlot) { _Toast("Wait for your turn!"); return; }
            if (stage == ST_ROLL) _Send(C_ROLL, 0);
            else if (stage == ST_CHALLENGE) _StartChallenge();
            else if (stage == ST_TICKET) _Toast("Rub the silver on your ticket to scratch it!");
            else if (stage == ST_DONE) _Toast("Turn complete!");
        }

        public void _OnLeave()
        {
            if (_LocalSlot() < 0) { _Toast("You're not in the game."); return; }
            _Send(C_LEAVE, 0);
        }

        public void _OnReset()
        {
            if (Time.time - resetArmTime > 3f)
            {
                resetArmTime = Time.time;
                _Toast("Press RESET again to end the game for everyone.");
                return;
            }
            resetArmTime = -10f;
            _Send(C_RESET, 0);
        }

        public void _OnPanel0() { _SetPanel(0); }
        public void _OnPanel1() { _SetPanel(1); }
        public void _OnPanel2() { _SetPanel(2); }
        public void _OnPanel3() { _SetPanel(3); }

        private void _SetPanel(int k)
        {
            panel = k;
            _PlayFx(FX_CLICK);
            _RefreshDashboards();
        }

        // VIEW (local only): either the center screens OR the screen above the console you pressed it on
        public void _OnView0() { _ToggleView(0); }
        public void _OnView1() { _ToggleView(1); }
        public void _OnView2() { _ToggleView(2); }
        public void _OnView3() { _ToggleView(3); }

        private void _ToggleView(int d)
        {
            if (topView && viewSeat == d) topView = false;
            else
            {
                topView = true;
                viewSeat = d;
            }
            _ApplyViews();
            _PlayFx(FX_CLICK);
        }

        /// <summary>Called by the store when it pops up at a seat (or -1 when it goes back to the kiosk).</summary>
        public void _StoreSeatChanged(int seat)
        {
            storeSeat = seat;
            _ApplyViews();
        }

        private void _ApplyViews()
        {
            if (centerScreens != null) centerScreens.SetActive(!topView);
            for (int d = 0; d < 4; d++)
            {
                bool mine = topView && viewSeat == d;
                if (seatScreens != null && d < seatScreens.Length && seatScreens[d] != null) seatScreens[d].SetActive(mine && storeSeat != d);
                if (viewLabels != null && d < viewLabels.Length && viewLabels[d] != null) viewLabels[d].text = mine ? "VIEW: TOP" : "VIEW: CENTER";
            }
        }

        /// <summary>The Lucky Loop ticket reports that it has been scratched.</summary>
        public void _OnTicketScratched()
        {
            if (phase != PH_PLAY || stage != ST_TICKET || _LocalSlot() != turnSlot || ticketSentSeq == ticketSeq) return;
            ticketSentSeq = ticketSeq;
            _Send(C_TICKET, ticketSeq);
        }

        /// <summary>The challenge mini-game reports the player's hits.</summary>
        public void _ChallengeFinished(int hits)
        {
            if (phase != PH_PLAY || stage != ST_CHALLENGE || _LocalSlot() != turnSlot || challengeSentSeq == challengeSeq) return;
            challengeSentSeq = challengeSeq;
            _Send(C_CHALLENGE, hits);
            _RefreshDashboards();
        }

        private void _StartChallenge()
        {
            if (challenge == null || challenge._IsRunning() || challengeSentSeq == challengeSeq) return;
            if (challenge.root != null) _Dock(challenge.root.transform);
            challenge._Begin(challengeType, challengeJackpot);
            _RefreshDashboards();
        }

        /// <summary>Moves a ticket or challenge panel onto the dashboard closest to the local player.</summary>
        private void _Dock(Transform t)
        {
            if (t == null || dashSpots == null || dashSpots.Length == 0) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            Vector3 me = Utilities.IsValid(lp) ? lp.GetPosition() : Vector3.zero;
            int best = 0;
            float bestDist = 1e9f;
            for (int d = 0; d < dashSpots.Length; d++)
            {
                if (dashSpots[d] == null) continue;
                float dist = (dashSpots[d].position - me).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = d;
                }
            }
            if (dashSpots[best] == null) return;
            t.SetParent(dashSpots[best], false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
        }

        // =====================================================================
        //  Networking: commands go to the owner
        // =====================================================================

        private void _Send(int cmd, int arg)
        {
            if (Networking.IsOwner(gameObject)) _Exec(Networking.LocalPlayer, cmd, arg);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetCmd), cmd, arg);
        }

        [NetworkCallable(maxEventsPerSecond: 10)]
        public void NetCmd(int cmd, int arg)
        {
            if (!Networking.IsOwner(gameObject)) return;
            _Exec(NetworkCalling.CallingPlayer, cmd, arg);
        }

        public override void OnDeserialization() { _RefreshAll(); }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            stageTime = Time.time;
            resolveOnArrive = true;
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!Networking.IsOwner(gameObject) || !Utilities.IsValid(player)) return;
            int s = _SlotOf(player.playerId);
            if (s < 0) return;
            _RemoveSlot(s, " left the instance.");
            _Commit();
        }

        private void _Exec(VRCPlayerApi sender, int cmd, int arg)
        {
            if (!Utilities.IsValid(sender)) return;
            int s = _SlotOf(sender.playerId);
            switch (cmd)
            {
                case C_JOIN:
                    if (phase != PH_LOBBY || s >= 0) return;
                    for (int i = 0; i < MAXP; i++)
                    {
                        if (slotPid[i] != 0) continue;
                        slotPid[i] = sender.playerId;
                        _ResetSlot(i);
                        _Log(_Name(i) + " joined the table.");
                        _Fx(FX_COIN);
                        break;
                    }
                    break;
                case C_LEAVE:
                    if (s < 0) return;
                    _RemoveSlot(s, " left the game.");
                    break;
                case C_START:
                    if (phase != PH_LOBBY || s < 0 || _SeatedCount() < Mathf.Max(1, minPlayersToStart)) return;
                    _StartGame();
                    break;
                case C_RESET:
                    if (phase == PH_PLAY && s < 0) return;
                    _ToLobby();
                    break;
                default:
                    if (phase != PH_PLAY || s < 0 || s != turnSlot) return;
                    if (cmd == C_ROLL && stage == ST_ROLL) _DoRoll();
                    else if (cmd == C_TICKET && stage == ST_TICKET && arg == ticketSeq) _ApplyTicket(s);
                    else if (cmd == C_CHALLENGE && stage == ST_CHALLENGE) _ApplyChallenge(s, arg);
                    else return;
                    break;
            }
            _Commit();
        }

        // =====================================================================
        //  Owner-side rules
        // =====================================================================

        private void Update()
        {
            if (spinner != null) spinner.Rotate(0f, 10f * Time.deltaTime, 0f);
            if (captionUntil > 0f && Time.time > captionUntil)
            {
                captionUntil = 0f;
                _SetTexts(captionTexts, "");
            }
            if (cardHideTime > 0f && Time.time > cardHideTime)
            {
                cardHideTime = 0f;
                _SetTexts(cardTexts, "");
            }
            if (Time.time > nextLocalRefresh)
            {
                // result cards and toasts expire, the challenge ends, free tickets refill
                nextLocalRefresh = Time.time + 0.5f;
                _RefreshDashboards();
                if (Time.time >= toastUntil) _RefreshTexts();
            }
            if (phase != PH_PLAY || !Networking.IsOwner(gameObject)) return;

            float elapsed = Time.time - stageTime;
            if (stage == ST_MOVING)
            {
                if (elapsed > _MoveTime()) { _Arrive(); _Commit(); }
            }
            else if (stage == ST_DONE)
            {
                if (elapsed > turnDoneSeconds) { _NextTurn(); _Commit(); }
            }
            else if (afkSeconds > 0f && elapsed > afkSeconds + (stage == ST_ROLL ? 0f : 30f))
            {
                if (stage == ST_ROLL)
                {
                    _Log(_Name(turnSlot) + " is AFK - rolling for them.");
                    _DoRoll();
                }
                else if (stage == ST_TICKET) _ApplyTicket(turnSlot);
                else if (stage == ST_CHALLENGE) _ApplyChallenge(turnSlot, 0);
                _Commit();
            }

            if (Time.time > seatCheckTime)
            {
                seatCheckTime = Time.time + 3f;
                bool changed = false;
                for (int i = 0; i < MAXP; i++)
                {
                    if (slotPid[i] == 0) continue;
                    if (Utilities.IsValid(VRCPlayerApi.GetPlayerById(slotPid[i]))) continue;
                    _RemoveSlot(i, " disconnected.");
                    changed = true;
                }
                if (changed) _Commit();
            }
        }

        private float _MoveTime()
        {
            if (moveIsWarp) return 1.9f;
            int steps = Mathf.Max(1, lastSteps);
            return 1.35f + steps * Mathf.Min(0.24f, 4f / steps) + 0.45f;
        }

        private void _SetStage(int st)
        {
            stage = st;
            stageTime = Time.time;
        }

        private void _ResetSlot(int i)
        {
            coins[i] = 0;
            pos[i] = 0;
            laps[i] = 0;
            shields[i] = 0;
            boosts[i] = 0;
            wins[i] = 0;
            moveMode[i] = 1;
            coinsEarned[i] = 0;
        }

        private void _StartGame()
        {
            matchId = Random.Range(1, 2000000000);
            maxRounds = Mathf.Max(1, roundsToPlay);
            int first = -1;
            for (int i = 0; i < MAXP; i++)
            {
                if (slotPid[i] == 0) continue;
                _ResetSlot(i);
                if (first < 0) first = i;
                // perks won on free Loop Scratch tickets
                VRCPlayerApi p = VRCPlayerApi.GetPlayerById(slotPid[i]);
                if (!Utilities.IsValid(p)) continue;
                if (PlayerData.GetInt(p, "ll_start_boost") > 0) boosts[i] = 1;
                if (PlayerData.GetInt(p, "ll_start_shield") > 0) shields[i] = 1;
            }
            phase = PH_PLAY;
            round = 1;
            winner = 0;
            loopLevel = 1;
            turnSlot = first;
            chain = 0;
            log = "";
            _Announce("<color=#FFE14D>LOOP 1</color>  Race around the Loop!\nMost coins after " + maxRounds + " rounds wins.");
            _Log("Game on! " + _Name(first) + " rolls first.");
            _Fx(FX_START);
            _SetStage(ST_ROLL);
        }

        private void _ToLobby()
        {
            phase = PH_LOBBY;
            for (int i = 0; i < MAXP; i++) if (slotPid[i] != 0) _ResetSlot(i);
            winner = 0;
            loopLevel = 1;
            stage = ST_ROLL;
            log = "";
            _Log("Back to the lobby. Join and press START GAME!");
        }

        private void _RemoveSlot(int s, string why)
        {
            string who = _Name(s);
            slotPid[s] = 0;
            _Log(who + why);
        }

        private void _DoRoll()
        {
            int s = turnSlot;
            die1 = Random.Range(1, 7);
            die2 = Random.Range(1, 7);
            rollSeq++;
            chain = 0;
            _Log(_Name(s) + " rolled " + die1 + " + " + die2 + ".");
            resolveOnArrive = true;
            _Move(s, die1 + die2, 0);
        }

        /// <summary>Moves slot s forward (mode 0 hops, 1 warps) or back (mode 2) and waits for the token to arrive.</summary>
        private void _Move(int s, int steps, int mode)
        {
            int from = pos[s];
            if (mode != 2 && steps > 0 && from + steps >= SPACES) _Lap(s);
            int to = mode == 2 ? (from - steps + SPACES * 4) % SPACES : (from + steps) % SPACES;
            pos[s] = to;
            moveMode[s] = mode;
            moveIsWarp = mode == 1;
            lastSteps = Mathf.Max(1, steps);
            _SetStage(ST_MOVING);
        }

        private void _Lap(int s)
        {
            laps[s]++;
            coins[s] += lapBonus;
            coinsEarned[s] += coinsPerLap;
            _Log(_Name(s) + " completed lap " + laps[s] + ": +" + lapBonus + " coins!");
            _Fx(FX_LAP);
            _CheckLevel();
        }

        /// <summary>The Loop gets wilder as the leader completes laps.</summary>
        private void _CheckLevel()
        {
            int best = 0;
            for (int i = 0; i < MAXP; i++) if (slotPid[i] != 0 && laps[i] > best) best = laps[i];
            int lv = Mathf.Min(4, best + 1);
            if (lv <= loopLevel) return;
            loopLevel = lv;
            if (lv == 2) _Announce("<color=#FFE14D>LOOP 2: BIGGER REWARDS!</color>\nCoin tiles, challenges and tickets now pay x1.5.");
            else if (lv == 3) _Announce("<color=#7CFF4F>LOOP 3: MYSTERY TILES AWAKEN!</color>\nFour more tiles just turned into Mystery tiles.");
            else _Announce("<color=#FF3DCB>LOOP 4: JACKPOT TICKETS!</color>\nLucky Loop tickets can now hit the LOOP JACKPOT.");
            _Fx(FX_LEVEL);
        }

        private void _Arrive()
        {
            if (!resolveOnArrive)
            {
                resolveOnArrive = true;
                _Done(arriveTitle, arriveSub, arriveIcon);
                return;
            }
            _Resolve();
        }

        /// <summary>The tile under the current player does its thing.</summary>
        private void _Resolve()
        {
            int s = turnSlot;
            int p = pos[s];
            int t = _TileType(p);
            chain++;
            if (t == T_START)
            {
                _Done("LOOP START", "+" + lapBonus + " coins every lap. Keep looping!", I_START);
            }
            else if (t == T_COINS)
            {
                int v = spaceValue[p];
                if (v >= 0)
                {
                    bool boosted = boosts[s] > 0;
                    int got = _Gain(s, _Scaled(v));
                    _Done("+" + _Num(got) + " COINS", spaceName[p] + (boosted ? "  <color=#FFE14D>BOOST x2!</color>" : ""), I_COIN);
                }
                else
                {
                    int lost = _Lose(s, -v);
                    if (lost > 0) _Done("-" + lost + " COINS", spaceName[p] + ": a few coins slipped away.", I_LOSE);
                    else _Done("SHIELD BLOCKED IT!", spaceName[p] + " couldn't take any coins.", I_SHIELD);
                }
            }
            else if (t == T_LUCKY)
            {
                ticketReward = _RollTicket();
                ticketSeq++;
                _Log(_Name(s) + " got a Lucky Loop ticket!");
                _Fx(FX_TICKET);
                _SetStage(ST_TICKET);
            }
            else if (t == T_CHALLENGE) _OfferChallenge(s, false);
            else if (t == T_POWER) _Power(s);
            else if (t == T_MYSTERY) _Mystery(s);
            else if (t == T_PORTAL)
            {
                int dest = (p + 10) % SPACES;
                _Log(_Name(s) + " jumped through " + spaceName[p] + "!");
                _Fx(FX_PORTAL);
                resolveOnArrive = false;
                arriveTitle = "PORTAL!";
                arriveSub = "Warped ahead to " + spaceName[dest] + ".";
                arriveIcon = I_PORTAL;
                _Move(s, 10, 1);
            }
            else _Done(spaceName[p], "", I_NONE);
        }

        private int _TileType(int p)
        {
            int t = spaceType[p];
            if (t == T_COINS && loopLevel >= 3 && spaceWake[p] != 0) return T_MYSTERY;
            return t;
        }

        /// <summary>Rewards grow from Loop 2 on.</summary>
        private int _Scaled(int v)
        {
            return loopLevel >= 2 ? v * 3 / 2 / 5 * 5 : v;
        }

        /// <summary>Gives coins (a Boost doubles them) and returns how many were given.</summary>
        private int _Gain(int s, int amount)
        {
            if (amount <= 0) return 0;
            if (boosts[s] > 0)
            {
                boosts[s]--;
                amount *= 2;
            }
            coins[s] += amount;
            _Fx(FX_COIN);
            return amount;
        }

        /// <summary>Takes coins (a Shield blocks it) and returns how many were lost.</summary>
        private int _Lose(int s, int amount)
        {
            if (shields[s] > 0)
            {
                shields[s]--;
                _Fx(FX_POWER);
                return 0;
            }
            int lost = Mathf.Min(amount, coins[s]);
            coins[s] -= lost;
            _Fx(FX_LOSE);
            return lost;
        }

        private void _Result(string title, string sub, int icon)
        {
            resultSeq++;
            resultTitle = title;
            resultSub = sub;
            resultIcon = icon;
            _Log(_Name(turnSlot) + ": " + title);
        }

        private void _Done(string title, string sub, int icon)
        {
            _Result(title, sub, icon);
            _SetStage(ST_DONE);
        }

        private void _RollAgain(string title, string sub)
        {
            _Result(title, sub, I_ROLL);
            chain = 0;
            _SetStage(ST_ROLL);
        }

        private void _OfferChallenge(int s, bool jackpot)
        {
            challengeType = Random.Range(0, 2);
            challengeJackpot = jackpot;
            challengeSeq++;
            _Log(_Name(s) + " faces a " + (jackpot ? "JACKPOT " : "") + "challenge!");
            _Fx(FX_CHALLENGE);
            _SetStage(ST_CHALLENGE);
        }

        private void _ApplyChallenge(int s, int hits)
        {
            hits = Mathf.Clamp(hits, 0, 15);
            int baseCoins = challengeType == 0 ? Mathf.Min(hits, 5) * 30 : Mathf.Min(hits, 14) * 15;
            int reward = _Scaled(baseCoins) * (challengeJackpot ? 3 : 1);
            bool won = challengeType == 0 ? hits >= 3 : hits >= 6;
            if (won)
            {
                wins[s]++;
                coinsEarned[s] += coinsPerChallenge;
            }
            string what = (challengeType == 0 ? "LASER LOOP: " + hits + " / 5 hits" : "COIN RUSH: " + hits + " coins grabbed") + (challengeJackpot ? "  <color=#FFE14D>JACKPOT x3!</color>" : "");
            if (reward > 0) _Done("+" + _Num(_Gain(s, reward)) + " COINS", what, I_CHALLENGE);
            else _Done("NO COINS THIS TIME", what, I_CHALLENGE);
        }

        private int _RollTicket()
        {
            int n = loopLevel >= 4 ? ticketWeights.Length : ticketWeights.Length - 1;
            int total = 0;
            for (int i = 0; i < n; i++) total += ticketWeights[i];
            int r = Random.Range(0, total);
            for (int i = 0; i < n; i++)
            {
                if (r < ticketWeights[i]) return i;
                r -= ticketWeights[i];
            }
            return R_NOTHING;
        }

        private void _ApplyTicket(int s)
        {
            int r = ticketReward;
            if (r == R_COINS || r == R_BIG_COINS || r == R_LOOP_JACKPOT)
            {
                int got = _Gain(s, _TicketCoins(r));
                _Done("+" + _Num(got) + " COINS", r == R_LOOP_JACKPOT ? "<color=#FFE14D>LOOP JACKPOT!</color>" : "Lucky Loop ticket", r == R_LOOP_JACKPOT ? I_JACKPOT : I_COIN);
                if (r == R_LOOP_JACKPOT)
                {
                    _Announce("<color=#FFE14D>LOOP JACKPOT!</color>\n" + _Name(s) + " won " + _Num(got) + " coins!");
                    _Fx(FX_WIN);
                }
            }
            else if (r == R_DOUBLE)
            {
                boosts[s] = Mathf.Min(maxPowerUps, boosts[s] + 1);
                _Done("DOUBLE BOOST", "Your next coin reward is x2.", I_BOOST);
                _Fx(FX_POWER);
            }
            else if (r == R_SHIELD)
            {
                shields[s] = Mathf.Min(maxPowerUps, shields[s] + 1);
                _Done("SHIELD", "Blocks your next coin loss, bad Mystery or Swap.", I_SHIELD);
                _Fx(FX_POWER);
            }
            else if (r == R_MOVE3)
            {
                _Result("MOVE +3", "Zoom three spaces ahead!", I_ROLL);
                resolveOnArrive = chain < 3;
                arriveTitle = "MOVE +3";
                arriveSub = "Zoomed three spaces ahead.";
                arriveIcon = I_ROLL;
                _Move(s, 3, 0);
            }
            else if (r == R_EXTRA_ROLL) _RollAgain("EXTRA ROLL", "Roll the dice again!");
            else if (r == R_SWAP) _Swap(s);
            else if (r == R_JACKPOT_GAME)
            {
                _Result("JACKPOT CHALLENGE", "Play a challenge now: coins x3!", I_CHALLENGE);
                _OfferChallenge(s, true);
            }
            else _Done("NOTHING THIS TIME", "Better luck on the next Lucky Loop!", I_NONE);
        }

        private int _TicketCoins(int r)
        {
            return _Scaled(r == R_COINS ? 100 : (r == R_BIG_COINS ? 250 : 1000));
        }

        /// <summary>Swaps places with a random other player (their Shield blocks it).</summary>
        private void _Swap(int s)
        {
            int n = 0;
            for (int i = 0; i < MAXP; i++) if (i != s && slotPid[i] != 0) n++;
            if (n == 0)
            {
                _Done("SWAP", "Nobody to swap with!", I_SWAP);
                return;
            }
            int k = Random.Range(0, n);
            int o = -1;
            for (int i = 0; i < MAXP; i++)
            {
                if (i == s || slotPid[i] == 0) continue;
                if (k == 0)
                {
                    o = i;
                    break;
                }
                k--;
            }
            if (shields[o] > 0)
            {
                shields[o]--;
                _Done("SWAP BLOCKED", _Name(o) + "'s Shield blocked the swap!", I_SHIELD);
                return;
            }
            int tmp = pos[s];
            pos[s] = pos[o];
            pos[o] = tmp;
            moveMode[s] = 1;
            moveMode[o] = 1;
            moveIsWarp = true;
            lastSteps = 10;
            _Fx(FX_PORTAL);
            resolveOnArrive = false;
            arriveTitle = "SWAPPED!";
            arriveSub = "You traded places with " + _Name(o) + ".";
            arriveIcon = I_SWAP;
            _SetStage(ST_MOVING);
        }

        private void _Power(int s)
        {
            _Fx(FX_POWER);
            int r = Random.Range(0, 100);
            if (r < 32)
            {
                shields[s] = Mathf.Min(maxPowerUps, shields[s] + 1);
                _Done("SHIELD", "Blocks your next coin loss, bad Mystery or Swap.", I_SHIELD);
            }
            else if (r < 64)
            {
                boosts[s] = Mathf.Min(maxPowerUps, boosts[s] + 1);
                _Done("BOOST", "Your next coin reward is x2.", I_BOOST);
            }
            else if (r < 80) _Swap(s);
            else _RollAgain("BONUS ROLL", "Roll the dice again!");
        }

        /// <summary>A random event for this player or everyone.</summary>
        private void _Mystery(int s)
        {
            _Fx(FX_MYSTERY);
            int e = Random.Range(0, 9);
            if (e == 0)
            {
                int amount = _Scaled(loopLevel >= 3 ? 100 : 50);
                for (int i = 0; i < MAXP; i++) if (slotPid[i] != 0) coins[i] += amount;
                _Announce("<color=#7CFF4F>MYSTERY: " + (loopLevel >= 3 ? "COIN STORM" : "COIN SHOWER") + "</color>\nEveryone gets +" + amount + " coins!");
                _Done(loopLevel >= 3 ? "COIN STORM" : "COIN SHOWER", "Everyone gets +" + amount + " coins!", I_MYSTERY);
            }
            else if (e == 1)
            {
                int lost = _Lose(s, 75);
                if (lost > 0) _Done("GLITCH TAX", "You lost " + lost + " coins.", I_LOSE);
                else _Done("SHIELD BLOCKED IT!", "The Glitch Tax couldn't touch you.", I_SHIELD);
            }
            else if (e == 2)
            {
                int lead = _Leader(s);
                if (lead >= 0 && coins[lead] > coins[s])
                {
                    int paid = _Lose(lead, 75);
                    coins[s] += paid;
                    if (paid > 0) _Done("ROBIN LOOP", _Name(lead) + " pays you " + paid + " coins!", I_MYSTERY);
                    else _Done("ROBIN LOOP", _Name(lead) + "'s Shield kept their coins safe.", I_SHIELD);
                }
                else
                {
                    int gift = 0;
                    for (int i = 0; i < MAXP; i++)
                    {
                        if (i == s || slotPid[i] == 0) continue;
                        int g = Mathf.Min(25, coins[s]);
                        coins[s] -= g;
                        coins[i] += g;
                        gift += g;
                    }
                    _Done("SHARING IS CARING", "You're in the lead, so you gave " + gift + " coins away.", I_MYSTERY);
                }
            }
            else if (e == 3)
            {
                boosts[s] = Mathf.Min(maxPowerUps, boosts[s] + 1);
                _Done("LUCKY FIND: BOOST", "Your next coin reward is x2.", I_BOOST);
            }
            else if (e == 4)
            {
                shields[s] = Mathf.Min(maxPowerUps, shields[s] + 1);
                _Done("FORCE FIELD: SHIELD", "Blocks your next coin loss, bad Mystery or Swap.", I_SHIELD);
            }
            else if (e == 5)
            {
                _Result("WARP AHEAD", "Jump four spaces forward!", I_PORTAL);
                resolveOnArrive = chain < 3;
                arriveTitle = "WARP AHEAD";
                arriveSub = "Jumped four spaces forward.";
                arriveIcon = I_PORTAL;
                _Move(s, 4, 0);
            }
            else if (e == 6)
            {
                _Result("TIME SLIP", "Go back three spaces!", I_MYSTERY);
                resolveOnArrive = chain < 3;
                arriveTitle = "TIME SLIP";
                arriveSub = "Slipped back three spaces.";
                arriveIcon = I_MYSTERY;
                _Move(s, 3, 2);
            }
            else if (e == 7) _Swap(s);
            else
            {
                _Result("SURPRISE CHALLENGE", "A mini-game appears!", I_CHALLENGE);
                _OfferChallenge(s, false);
            }
        }

        /// <summary>The player with the most coins (ties: most laps), not counting 'except'.</summary>
        private int _Leader(int except)
        {
            int best = -1;
            for (int i = 0; i < MAXP; i++)
            {
                if (i == except || slotPid[i] == 0) continue;
                if (best < 0 || coins[i] > coins[best] || (coins[i] == coins[best] && laps[i] > laps[best])) best = i;
            }
            return best;
        }

        private void _NextTurn()
        {
            int s = turnSlot;
            for (int k = 1; k <= MAXP; k++)
            {
                int n = (s + k) % MAXP;
                if (slotPid[n] == 0) continue;
                if (n <= s) round++;
                turnSlot = n;
                break;
            }
            chain = 0;
            if (round > maxRounds)
            {
                _EndGame();
                return;
            }
            _SetStage(ST_ROLL);
        }

        private void _EndGame()
        {
            phase = PH_OVER;
            int w = _Leader(-1);
            winner = w + 1;
            for (int i = 0; i < MAXP; i++) if (slotPid[i] != 0) coinsEarned[i] += coinsFinish;
            if (w >= 0)
            {
                coinsEarned[w] += coinsWin;
                _Announce("<color=#FFE14D>" + _Name(w) + " WINS LOOPLAND!</color>\n" + _Num(coins[w]) + " coins after " + maxRounds + " rounds.");
                _Log(_Name(w) + " WINS with " + _Num(coins[w]) + " coins!");
            }
            else _Log("Game over.");
            _Fx(FX_WIN);
        }

        private void _Commit()
        {
            if (phase == PH_PLAY)
            {
                if (_SeatedCount() == 0) _ToLobby();
                else if (slotPid[turnSlot] == 0) _NextTurn();
            }
            RequestSerialization();
            _RefreshAll();
        }

        private void _Log(string msg)
        {
            log = msg + "\n" + log;
            int idx = -1;
            for (int n = 0; n < 4; n++)
            {
                idx = log.IndexOf('\n', idx + 1);
                if (idx < 0) return;
            }
            log = log.Substring(0, idx);
        }

        private void _Announce(string msg)
        {
            cardSeq++;
            cardText = msg;
        }

        private void _Fx(int type)
        {
            fxType = type;
            fxSeq++;
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        private int _SlotOf(int playerId)
        {
            for (int i = 0; i < MAXP; i++) if (slotPid[i] == playerId && playerId != 0) return i;
            return -1;
        }

        private int _LocalSlot()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            return Utilities.IsValid(lp) ? _SlotOf(lp.playerId) : -1;
        }

        private int _SeatedCount()
        {
            int n = 0;
            for (int i = 0; i < MAXP; i++) if (slotPid[i] != 0) n++;
            return n;
        }

        private string _Name(int s)
        {
            if (s < 0 || s >= MAXP) return "Nobody";
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(slotPid[s]);
            string n = Utilities.IsValid(p) ? p.displayName : "Player " + (s + 1);
            return "<color=#" + slotHex[s] + "><noparse>" + n + "</noparse></color>";
        }

        public VRCPlayerApi _SlotPlayer(int s)
        {
            if (s < 0 || s >= MAXP || slotPid[s] == 0) return null;
            return VRCPlayerApi.GetPlayerById(slotPid[s]);
        }

        /// <summary>1250 -> "1,250".</summary>
        public string _Num(int v)
        {
            string sign = v < 0 ? "-" : "";
            string s = (v < 0 ? -v : v).ToString();
            string o = "";
            for (int i = s.Length; i > 0; i -= 3) o = (i > 3 ? "," : "") + s.Substring(Mathf.Max(0, i - 3), Mathf.Min(3, i)) + o;
            return sign + o;
        }

        public string _TileTitle(int t)
        {
            if (t == T_START) return "LOOP START";
            if (t == T_COINS) return "COINS";
            if (t == T_LUCKY) return "LUCKY LOOP";
            if (t == T_CHALLENGE) return "CHALLENGE";
            if (t == T_POWER) return "POWER";
            if (t == T_MYSTERY) return "MYSTERY";
            return "PORTAL";
        }

        private string _TileDesc(int p)
        {
            int t = _TileType(p);
            if (t == T_START) return "+" + lapBonus + " coins every time you complete a lap.";
            if (t == T_COINS) return spaceValue[p] >= 0 ? "Gain " + _Scaled(spaceValue[p]) + " coins." : "Lose " + (-spaceValue[p]) + " coins (a Shield blocks it).";
            if (t == T_LUCKY) return "Scratch a ticket and reveal a reward.";
            if (t == T_CHALLENGE) return "A quick mini-game: win it for coins.";
            if (t == T_POWER) return "Get a Shield, Boost, Swap or Bonus Roll.";
            if (t == T_MYSTERY) return "A random event for you or everyone!";
            return "Warp to the next section of the Loop.";
        }

        private string _LevelDesc()
        {
            if (loopLevel <= 1) return "Normal rewards. Each new loop makes the board wilder!";
            if (loopLevel == 2) return "Bigger rewards: coins x1.5.";
            if (loopLevel == 3) return "Bigger rewards, and Mystery tiles have awakened.";
            return "Bigger rewards, more Mystery and LOOP JACKPOT tickets!";
        }

        private string _RewardTitle(int r)
        {
            if (r == R_COINS || r == R_BIG_COINS) return "+" + _Num(_TicketCoins(r)) + " COINS";
            if (r == R_LOOP_JACKPOT) return "LOOP JACKPOT  +" + _Num(_TicketCoins(r));
            if (r == R_DOUBLE) return "DOUBLE BOOST";
            if (r == R_SHIELD) return "SHIELD";
            if (r == R_MOVE3) return "MOVE +3";
            if (r == R_EXTRA_ROLL) return "EXTRA ROLL";
            if (r == R_SWAP) return "SWAP";
            if (r == R_JACKPOT_GAME) return "JACKPOT CHALLENGE";
            return "NOTHING THIS TIME";
        }

        private string _RewardSub(int r)
        {
            if (r == R_COINS || r == R_BIG_COINS || r == R_LOOP_JACKPOT) return "Coins for this game";
            if (r == R_DOUBLE) return "Your next coin reward is x2";
            if (r == R_SHIELD) return "Blocks your next loss, bad Mystery or Swap";
            if (r == R_MOVE3) return "Zoom three spaces ahead";
            if (r == R_EXTRA_ROLL) return "Roll the dice again";
            if (r == R_SWAP) return "Trade places with a random player";
            if (r == R_JACKPOT_GAME) return "Play a challenge now: coins x3";
            return "Better luck on the next Lucky Loop";
        }

        private int _RewardIcon(int r)
        {
            if (r == R_COINS || r == R_BIG_COINS) return I_COIN;
            if (r == R_LOOP_JACKPOT) return I_JACKPOT;
            if (r == R_DOUBLE) return I_BOOST;
            if (r == R_SHIELD) return I_SHIELD;
            if (r == R_MOVE3 || r == R_EXTRA_ROLL) return I_ROLL;
            if (r == R_SWAP) return I_SWAP;
            if (r == R_JACKPOT_GAME) return I_CHALLENGE;
            return I_NONE;
        }

        private Sprite _Art(int icon)
        {
            return resultArt != null && icon >= 0 && icon < resultArt.Length ? resultArt[icon] : null;
        }

        // =====================================================================
        //  Presentation (runs on every client)
        // =====================================================================

        public void _OnCosmeticsChanged()
        {
            _RefreshTokens(false);
            _RefreshMarker();
        }

        private void _RefreshAll()
        {
            bool rolled = seenRoll >= 0 && rollSeq != seenRoll;
            bool firstSync = seenRoll < 0;

            if (dice != null && dice.Length >= 2 && dice[0] != null && dice[1] != null)
            {
                _ApplyDiceSkin();
                if (rolled) { dice[0]._Roll(die1, 0f); dice[1]._Roll(die2, 0.08f); }
                else if (firstSync) { dice[0]._Show(die1); dice[1]._Show(die2); }
            }
            seenRoll = rollSeq;

            if (seenPhase != phase) { seenPhase = phase; _RefreshTokens(false); }
            _RefreshTokens(rolled);
            _DirectCamera(rolled);

            if (cardSeq != seenCard)
            {
                if (seenCard >= 0) { _SetTexts(cardTexts, cardText); cardHideTime = Time.time + 7f; }
                seenCard = cardSeq;
            }
            if (fxSeq != seenFx)
            {
                if (seenFx >= 0) _PlayFx(fxType);
                seenFx = fxSeq;
            }
            if (resultSeq != seenResult)
            {
                if (seenResult >= 0) resultUntil = Time.time + Mathf.Max(2.2f, turnDoneSeconds + 0.4f);
                seenResult = resultSeq;
            }

            if (mysteryBadges != null)
                for (int i = 0; i < mysteryBadges.Length; i++)
                    if (mysteryBadges[i] != null) mysteryBadges[i].SetActive(phase == PH_PLAY && loopLevel >= 3);
            _RefreshMarker();
            _RefreshTexts();
            _RefreshDashboards();
            _MatchHooks();
        }

        /// <summary>Persistent rewards: Loop Coins (store) plus XP, tickets and start perks (Free Loop Scratch).</summary>
        private void _MatchHooks()
        {
            int me = _LocalSlot();
            if (me < 0 || matchId == 0 || phase == PH_LOBBY) return;
            if (store != null) store._ApplyMatchReward(matchId, coinsEarned[me]);
            if (freeScratch == null) return;
            if (startHookMatch != matchId)
            {
                startHookMatch = matchId;
                freeScratch._MatchStarted(matchId);
            }
            if (phase == PH_OVER && finishHookMatch != matchId)
            {
                finishHookMatch = matchId;
                int xp = 25 + laps[me] * 10 + wins[me] * 5 + (winner == me + 1 ? 50 : 0);
                freeScratch._MatchFinished(matchId, xp);
            }
        }

        private void _DirectCamera(bool rolled)
        {
            if (liveCam == null || tokens == null || turnSlot < 0 || turnSlot >= tokens.Length || tokens[turnSlot] == null) return;
            int tp = pos[turnSlot];
            if (phase == PH_PLAY)
            {
                Transform land = spaceAnchors != null && tp < spaceAnchors.Length ? spaceAnchors[tp] : null;
                if (rolled && dice != null && dice.Length >= 2 && dice[0] != null && dice[1] != null)
                {
                    liveCam._ShowRoll(dice[0].transform, dice[1].transform, tokens[turnSlot], land);
                    _Caption(_Name(turnSlot) + " rolled <color=#FFE14D>" + die1 + " + " + die2 + "</color>");
                }
                else if (camSeenSlot == turnSlot && tp != camSeenPos) liveCam._ShowMove(tokens[turnSlot], land);
            }
            else liveCam._Overview();
            camSeenPos = tp;
            camSeenSlot = turnSlot;
        }

        /// <summary>Called by the camera when it reaches the close-up of the landing space.</summary>
        public void _OnCameraLanded()
        {
            if (phase != PH_PLAY) return;
            int p = pos[turnSlot];
            _Caption(_Name(turnSlot) + " landed on <color=#FFE14D>" + spaceName[p] + "</color>  " + _TileTitle(_TileType(p)));
        }

        private void _Caption(string msg)
        {
            _SetTexts(captionTexts, "<mark=#000000B0> " + msg + " </mark>");
            captionUntil = Time.time + 4f;
        }

        private void _ApplyDiceSkin()
        {
            if (store == null) return;
            VRCPlayerApi roller = _SlotPlayer(turnSlot);
            int skin = Utilities.IsValid(roller) ? store._GetEquipped(roller, 0) : 0;
            Material m = store._DiceMaterial(skin);
            Color glow = store._DiceGlow(skin);
            dice[0]._SetSkin(m, glow);
            dice[1]._SetSkin(m, glow);
        }

        private void _RefreshTokens(bool rolled)
        {
            if (tokens == null) return;
            for (int s = 0; s < tokens.Length && s < MAXP; s++)
            {
                LoopLandToken tk = tokens[s];
                if (tk == null) continue;
                bool show = slotPid[s] != 0;
                tk.gameObject.SetActive(show);
                if (!show) continue;
                VRCPlayerApi p = _SlotPlayer(s);
                string label = Utilities.IsValid(p) ? p.displayName : "";
                Color baseCol = slotColors != null && s < slotColors.Length ? slotColors[s] : Color.white;
                if (store != null && Utilities.IsValid(p))
                {
                    int skin = store._GetEquipped(p, 1);
                    int trail = store._GetEquipped(p, 3);
                    Color body = skin == 0 ? baseCol : store._TokenColor(skin);
                    tk._Skin(body, store._TokenGlow(skin, baseCol), store._TrailColor(trail), store._IsRainbowToken(skin), store._IsRainbowTrail(trail), label);
                }
                else tk._Skin(baseCol, baseCol, baseCol, false, false, label);
                tk._SetSlot(_TokenSpot(s));
                tk._MoveTo(pos[s], moveMode[s], rolled ? 1.3f : 0.9f);
            }
        }

        /// <summary>A token alone on a card stands in its centre; tokens sharing a card form a group centred on it (rows of two).</summary>
        private Vector3 _TokenSpot(int s)
        {
            int count = 0;
            int rank = 0;
            for (int o = 0; o < MAXP && o < tokens.Length; o++)
            {
                if (slotPid[o] == 0 || pos[o] != pos[s]) continue;
                if (o < s) rank++;
                count++;
            }
            int rows = (count + 1) / 2;
            int row = rank / 2;
            bool single = row == rows - 1 && count % 2 == 1;
            float x = single ? 0f : (rank % 2 == 0 ? -0.065f : 0.065f);
            float z = ((rows - 1) * 0.5f - row) * 0.11f;
            return new Vector3(x, 0f, z);
        }

        /// <summary>A glowing frame on the current player's tile, in their building style colour.</summary>
        private void _RefreshMarker()
        {
            if (turnMarker == null) return;
            bool on = phase == PH_PLAY && stage != ST_MOVING && slotPid[turnSlot] != 0 && spaceAnchors != null && pos[turnSlot] < spaceAnchors.Length;
            turnMarker.gameObject.SetActive(on);
            if (!on) return;
            Transform a = spaceAnchors[pos[turnSlot]];
            turnMarker.position = a.position;
            turnMarker.rotation = a.rotation;
            Color c = slotColors != null && turnSlot < slotColors.Length ? slotColors[turnSlot] : Color.white;
            VRCPlayerApi p = _SlotPlayer(turnSlot);
            if (store != null && Utilities.IsValid(p))
            {
                int style = store._GetEquipped(p, 2);
                if (style > 0) c = store._BuildingColor(style);
            }
            if (turnMarkerRenderer != null) turnMarkerRenderer.material.SetColor("_TintColor", c);
        }

        private void _RefreshTexts()
        {
            int me = _LocalSlot();
            string status;
            if (phase == PH_LOBBY)
            {
                status = "<size=150%><b>LOOPLAND</b></size>\n<color=#9AF2FF>LOBBY</color>  " + _SeatedCount() + "/" + MAXP + " players\nMost coins after " + Mathf.Max(1, roundsToPlay) + " rounds wins!";
            }
            else if (phase == PH_OVER)
            {
                status = "<size=150%><b>GAME OVER</b></size>\n" + (winner > 0 ? _Name(winner - 1) + " <color=#FFE14D>WINS!</color>" : "No winner");
            }
            else
            {
                status = "<size=120%><b>ROUND " + round + " / " + maxRounds + "</b>   <color=#FF3DCB>LOOP " + loopLevel + "</color></size>\n" + _Name(turnSlot) + " " + _StageHint();
            }
            if (log.Length > 0) status += "\n<size=75%><color=#C8C8E0>" + log + "</color></size>";
            _SetTexts(statusTexts, status);

            string players = "";
            for (int i = 0; i < MAXP; i++)
            {
                if (slotPid[i] == 0) continue;
                string line = (phase == PH_PLAY && i == turnSlot ? "> " : "  ") + _Name(i);
                if (phase != PH_LOBBY)
                {
                    line += "  <color=#FFE14D>" + _Num(coins[i]) + "</color>  lap " + laps[i];
                    if (shields[i] > 0) line += "  <color=#9AF2FF>SHIELD</color>";
                    if (boosts[i] > 0) line += "  <color=#FF3DCB>BOOST</color>";
                }
                players += line + "\n";
            }
            _SetTexts(playerTexts, players.Length == 0 ? "Press JOIN GAME to take a seat!" : players);

            _SetTexts(primaryLabels, _PrimaryLabel(me));
            if (Time.time >= toastUntil) _SetTexts(infoTexts, _Hint(me));
        }

        private string _PrimaryLabel(int me)
        {
            if (phase == PH_LOBBY) return me < 0 ? "JOIN GAME" : (_SeatedCount() < Mathf.Max(1, minPlayersToStart) ? "WAITING FOR PLAYERS" : "START GAME");
            if (phase == PH_OVER) return "NEW GAME";
            if (me < 0) return "WATCHING";
            if (me != turnSlot) return "WAITING...";
            if (stage == ST_ROLL) return "ROLL";
            if (stage == ST_MOVING) return "MOVING...";
            if (stage == ST_TICKET) return "SCRATCH TICKET";
            if (stage == ST_CHALLENGE) return challenge != null && challenge._IsRunning() ? "PLAYING..." : "PLAY CHALLENGE";
            return "TURN COMPLETE!";
        }

        private string _Hint(int me)
        {
            if (phase == PH_LOBBY) return me < 0 ? "Press JOIN GAME to take a seat (up to 6 players)." : "You're in! Press START GAME when everyone has joined.";
            if (phase == PH_OVER) return "Press NEW GAME to play again.";
            if (me != turnSlot || me < 0) return _Name(turnSlot) + " " + _StageHint();
            if (stage == ST_ROLL) return "<color=#FFE14D>Your turn!</color> Press ROLL.";
            if (stage == ST_TICKET) return "<color=#FFE14D>Scratch your Lucky Loop ticket!</color>";
            if (stage == ST_CHALLENGE) return "<color=#FFE14D>Press PLAY CHALLENGE when you're ready!</color>";
            if (stage == ST_DONE) return "<color=#7CFF4F>Turn complete!</color>";
            return "On the move...";
        }

        private string _StageHint()
        {
            if (stage == ST_ROLL) return "is rolling the dice";
            if (stage == ST_MOVING) return "is on the move...";
            if (stage == ST_TICKET) return "is scratching a Lucky Loop ticket";
            if (stage == ST_CHALLENGE) return "is playing " + (challenge != null ? challenge._Name(challengeType) : "a challenge");
            return "finished their turn";
        }

        /// <summary>Dashboard content (local): status panels, the Lucky Loop ticket, the challenge and result cards.</summary>
        private void _RefreshDashboards()
        {
            int me = _LocalSlot();
            bool myTurn = phase == PH_PLAY && me >= 0 && me == turnSlot;
            bool ticketOn = myTurn && stage == ST_TICKET && ticketSentSeq != ticketSeq;
            bool gameOn = challenge != null && challenge._IsRunning();
            bool introOn = myTurn && stage == ST_CHALLENGE && !gameOn && challengeSentSeq != challengeSeq;
            bool resultOn = Time.time < resultUntil && phase == PH_PLAY;

            if (ticket != null)
            {
                if (ticketOn && shownTicketSeq != ticketSeq)
                {
                    shownTicketSeq = ticketSeq;
                    if (ticket.root != null) _Dock(ticket.root.transform);
                    ticket._Show(_Art(_RewardIcon(ticketReward)), _RewardTitle(ticketReward), _RewardSub(ticketReward), "LUCKY LOOP TICKET");
                }
                else if (!(myTurn && stage == ST_TICKET) && ticket._IsActive()) ticket._Hide();
            }
            if (challenge != null && gameOn && !(myTurn && stage == ST_CHALLENGE))
            {
                challenge._Stop();
                gameOn = false;
            }
            bool ticketShown = ticket != null && ticket._IsActive();
            bool overlay = ticketShown || gameOn || introOn || resultOn;
            string status = _StatusBody(me);
            string powers = _PowerBody(me);
            string tickets = freeScratch != null ? freeScratch._Summary() : "";
            for (int d = 0; d < 4; d++)
            {
                for (int k = 0; k < PANELS; k++)
                {
                    int i = d * PANELS + k;
                    if (panelRoots != null && i < panelRoots.Length && panelRoots[i] != null) panelRoots[i].SetActive(!overlay && k == panel);
                }
                if (statusBodies != null && d < statusBodies.Length && statusBodies[d] != null) statusBodies[d].text = status;
                if (powerBodies != null && d < powerBodies.Length && powerBodies[d] != null) powerBodies[d].text = powers;
                if (ticketBodies != null && d < ticketBodies.Length && ticketBodies[d] != null) ticketBodies[d].text = tickets;
                if (introCards != null && d < introCards.Length && introCards[d] != null) introCards[d].SetActive(introOn && !ticketShown);
                if (introOn)
                {
                    if (introTitles != null && d < introTitles.Length && introTitles[d] != null)
                        introTitles[d].text = (challengeJackpot ? "<color=#FFE14D>JACKPOT</color> " : "") + (challenge != null ? challenge._Name(challengeType) : "CHALLENGE");
                    if (introBodies != null && d < introBodies.Length && introBodies[d] != null)
                        introBodies[d].text = (challenge != null ? challenge._Rules(challengeType) : "") + "\n<size=80%>" + (challengeType == 0 ? "+30" : "+15") + " coins a hit" + (challengeJackpot ? ", then x3!" : "") + "</size>\n\n<color=#7CFF4F>Press PLAY CHALLENGE to start.</color>";
                }
                bool showResult = resultOn && !ticketShown && !gameOn && !introOn;
                if (resultCards != null && d < resultCards.Length && resultCards[d] != null) resultCards[d].SetActive(showResult);
                if (showResult)
                {
                    if (resultTitles != null && d < resultTitles.Length && resultTitles[d] != null) resultTitles[d].text = resultTitle;
                    if (resultSubs != null && d < resultSubs.Length && resultSubs[d] != null)
                        resultSubs[d].text = _Name(turnSlot) + "\n" + resultSub + (stage == ST_DONE ? "\n<color=#7CFF4F><b>TURN COMPLETE</b></color>" : "");
                    if (resultIcons != null && d < resultIcons.Length && resultIcons[d] != null) resultIcons[d].sprite = _Art(resultIcon);
                }
            }
        }

        private string _StatusBody(int me)
        {
            if (me < 0)
                return phase == PH_PLAY ? "A game is running.\nWatch the board, then join the next game from the lobby!" : "Press <b>JOIN GAME</b> to take a seat.\n\nRace around the Loop, win challenges, scratch Lucky Loop tickets and finish with the most coins!";
            if (phase == PH_LOBBY) return "<b>You're in!</b>\nPress <b>START GAME</b> when everyone has joined.\n\nTip: open <b>HOW TO PLAY</b> for the tiles.";
            int p = pos[me];
            string s = "<size=130%><b><color=#FFE14D>" + _Num(coins[me]) + " COINS</color></b></size>\n";
            s += "LAPS <b>" + laps[me] + "</b>     CHALLENGES WON <b>" + wins[me] + "</b>\n";
            s += "SHIELD <b>x" + shields[me] + "</b>     BOOST <b>x" + boosts[me] + "</b>\n\n";
            s += "You're on <b>" + spaceName[p] + "</b>  <color=#9AF2FF>" + _TileTitle(_TileType(p)) + "</color>\n<size=85%>" + _TileDesc(p) + "</size>\n\n";
            s += "ROUND " + round + " / " + maxRounds + "     <color=#FF3DCB>LOOP " + loopLevel + "</color>\n<size=85%>" + _LevelDesc() + "</size>";
            if (phase == PH_OVER) s += "\n\n<color=#FFE14D>GAME OVER</color>  " + (winner > 0 ? _Name(winner - 1) + " wins!" : "");
            return s;
        }

        private string _PowerBody(int me)
        {
            int sh = me >= 0 ? shields[me] : 0;
            int bo = me >= 0 ? boosts[me] : 0;
            string s = "<b><color=#9AF2FF>SHIELD</color></b>  x" + sh + "\n<size=85%>Blocks your next coin loss, bad Mystery or Swap.</size>\n\n";
            s += "<b><color=#FF3DCB>BOOST</color></b>  x" + bo + "\n<size=85%>Your next coin reward is doubled.</size>\n\n";
            s += "<size=85%><color=#C8C8E0>Power tiles and Lucky Loop tickets can also give a <b>SWAP</b> (trade places with a random player) or a <b>BONUS ROLL</b>. Shields and Boosts work automatically; hold up to " + maxPowerUps + " of each.</color></size>";
            return s;
        }

        private void _SetTexts(TMP_Text[] arr, string value)
        {
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++) if (arr[i] != null) arr[i].text = value;
        }

        private void _Toast(string msg)
        {
            _SetTexts(infoTexts, "<color=#FFE14D>" + msg + "</color>");
            toastUntil = Time.time + 3f;
            _PlayFx(FX_CLICK);
        }

        private void _PlayFx(int type)
        {
            if (sfx != null && fxClips != null && type >= 0 && type < fxClips.Length && fxClips[type] != null) sfx.PlayOneShot(fxClips[type], 0.8f);
            if ((type == FX_WIN || type == FX_LEVEL) && celebrateFx != null) celebrateFx.Play();
            if ((type == FX_COIN || type == FX_LAP) && moneyFx != null) moneyFx.Emit(40);
        }
    }
}
