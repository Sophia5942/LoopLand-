using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace LoopLand
{
    /// <summary>
    /// LoopLand game brain. The owner of this object runs the rules and syncs the state (manual sync).
    /// Every other player sends commands to the owner with a [NetworkCallable] event; the sender is
    /// taken from NetworkCalling.CallingPlayer so nobody can act for someone else.
    /// All visuals (tokens, dice, texts, markers) are rebuilt from the synced state, so late joiners
    /// always see the correct board.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class LoopLandGame : UdonSharpBehaviour
    {
        private const int MAXP = 6;
        private const int SPACES = 40;

        private const int PH_LOBBY = 0;
        private const int PH_PLAY = 1;
        private const int PH_OVER = 2;

        private const int ST_ROLL = 0;
        private const int ST_MOVING = 1;
        private const int ST_BUY = 2;
        private const int ST_END = 3;
        private const int ST_JAIL = 4;

        private const int T_GO = 0;
        private const int T_PROP = 1;
        private const int T_PORTAL = 2;
        private const int T_UTIL = 3;
        private const int T_TAX = 4;
        private const int T_TWIST = 5;
        private const int T_CHEST = 6;
        private const int T_JAIL = 7;
        private const int T_FREE = 8;
        private const int T_GOJAIL = 9;

        private const int C_JOIN = 1;
        private const int C_LEAVE = 2;
        private const int C_START = 3;
        private const int C_ROLL = 4;
        private const int C_BUY = 5;
        private const int C_PASS = 6;
        private const int C_END = 7;
        private const int C_BAIL = 8;
        private const int C_CARD = 9;
        private const int C_BUILD = 10;
        private const int C_SELL = 11;
        private const int C_MORT = 12;
        private const int C_RESET = 13;
        private const int C_ROUNDS = 14;

        public const int FX_BUY = 1;
        public const int FX_RENT = 2;
        public const int FX_JAIL = 3;
        public const int FX_CARD = 4;
        public const int FX_WIN = 5;
        public const int FX_BUILD = 6;
        public const int FX_BANKRUPT = 7;
        public const int FX_START = 8;
        public const int FX_COIN = 9;

        [Header("Rules")]
        public int startingCash = 1500;
        public int goSalary = 200;
        public int jailFine = 50;
        [Tooltip("Set to 2 for public worlds. 1 lets you test solo in ClientSim.")]
        public int minPlayersToStart = 1;
        [Tooltip("Seconds before an idle player's turn is auto-played. 0 = never.")]
        public float afkSeconds = 75f;

        [Header("Loop Coin rewards (persistent store currency)")]
        public int coinsPassGo = 5;
        public int coinsBuy = 2;
        public int coinsBuild = 1;
        public int coinsFinish = 20;
        public int coinsWin = 100;

        [Header("References (auto-wired by LoopLand > Build Game In Scene)")]
        public LoopLandStore store;
        public LoopLandToken[] tokens;
        public LoopLandDice[] dice;
        public Transform[] spaceAnchors;
        public Renderer[] ownerBars;
        public GameObject[] buildMarkers;
        public Transform selectionMarker;
        public Transform spinner;
        public TextMeshPro[] statusTexts;
        public TextMeshPro[] playerTexts;
        public TextMeshPro[] infoTexts;
        public TextMeshPro[] primaryLabels;
        public TextMeshPro[] secondaryLabels;
        public TextMeshPro[] roundsLabels;
        public TextMeshPro[] cardTexts;
        public ParticleSystem celebrateFx;
        public ParticleSystem moneyFx;
        public AudioSource sfx;
        public AudioClip[] fxClips;
        public Color[] slotColors;
        public string[] slotHex = { "00E5FF", "FF3DCB", "FFD23F", "7CFF4F", "FF8A3D", "A57BFF" };

        [Header("Board data (edit, then rebuild the board)")]
        public string[] spaceName = {
            "LOOP START", "Pixel Alley", "Loop Chest", "Byte Bay", "Server Tax", "North Portal", "Neon Row", "Twist", "Synth Street", "Glow Garden",
            "Glitch Zone", "Avatar Ave", "Power Core", "Mirror Mall", "Pedestal Plaza", "East Portal", "Udon Lane", "Loop Chest", "Sync Square", "Prefab Park",
            "Chill Zone", "Shader Strip", "Twist", "Bloom Blvd", "Particle Pier", "South Portal", "Hologram Hill", "Laser Lagoon", "Data Well", "Vapor Vista",
            "GLITCHED!", "Crystal Coast", "Aurora Arcade", "Loop Chest", "Nebula Nook", "West Portal", "Twist", "Quantum Quay", "Luxury Tax", "Infinity Tower" };
        public int[] spaceType = { 0, 1, 6, 1, 4, 2, 1, 5, 1, 1, 7, 1, 3, 1, 1, 2, 1, 6, 1, 1, 8, 1, 5, 1, 1, 2, 1, 1, 3, 1, 9, 1, 1, 6, 1, 2, 5, 1, 4, 1 };
        public int[] spaceGroup = { -1, 0, -1, 0, -1, 8, 1, -1, 1, 1, -1, 2, 9, 2, 2, 8, 3, -1, 3, 3, -1, 4, -1, 4, 4, 8, 5, 5, 9, 5, -1, 6, 6, -1, 6, 8, -1, 7, -1, 7 };
        public int[] spacePrice = { 0, 60, 0, 60, 200, 200, 100, 0, 100, 120, 0, 140, 150, 140, 160, 200, 180, 0, 180, 200, 0, 220, 0, 220, 240, 200, 260, 260, 150, 280, 0, 300, 300, 0, 320, 200, 0, 350, 100, 400 };
        public int[] propSpaces = { 1, 3, 6, 8, 9, 11, 13, 14, 16, 18, 19, 21, 23, 24, 26, 27, 29, 31, 32, 34, 37, 39 };
        public int[] propRents = {
            2, 10, 30, 90, 160, 250,   4, 20, 60, 180, 320, 450,
            6, 30, 90, 270, 400, 550,  6, 30, 90, 270, 400, 550,  8, 40, 100, 300, 450, 600,
            10, 50, 150, 450, 625, 750,  10, 50, 150, 450, 625, 750,  12, 60, 180, 500, 700, 900,
            14, 70, 200, 550, 750, 950,  14, 70, 200, 550, 750, 950,  16, 80, 220, 600, 800, 1000,
            18, 90, 250, 700, 875, 1050,  18, 90, 250, 700, 875, 1050,  20, 100, 300, 750, 925, 1100,
            22, 110, 330, 800, 975, 1150,  22, 110, 330, 800, 975, 1150,  24, 120, 360, 850, 1025, 1200,
            26, 130, 390, 900, 1100, 1275,  26, 130, 390, 900, 1100, 1275,  28, 150, 450, 1000, 1200, 1400,
            35, 175, 500, 1100, 1300, 1500,  50, 200, 600, 1400, 1700, 2000 };
        public int[] houseCost = { 50, 50, 100, 100, 150, 150, 200, 200 };
        public string[] groupHex = { "B07CFF", "34E1FF", "FF5FBF", "FF9D3D", "FF4D5E", "FFE14D", "3DFF8A", "4D8BFF", "E6E6F0", "9AF2FF" };
        public string[] groupNames = { "Violet", "Cyan", "Pink", "Orange", "Red", "Yellow", "Green", "Blue", "Portal", "Utility" };

        private string[] twistCards = {
            "Warp to LOOP START. Collect your salary!",
            "Advance to Infinity Tower.",
            "Advance to Avatar Ave.",
            "Jump to the nearest Portal. Pay double rent if owned!",
            "GLITCH! Go directly to the Glitch Zone.",
            "Lag spike! Go back 3 spaces.",
            "Bug bounty! Collect $150.",
            "Speeding through the void. Pay $15.",
            "Elected server admin. Pay each player $50.",
            "Free Glitch Pass. Keep it to escape the Glitch Zone.",
            "Upgrade inspection: pay $25 per Loop and $100 per Tower.",
            "Your stream went viral! Collect $100." };
        private string[] chestCards = {
            "Advance to LOOP START. Collect your salary!",
            "Creator payout! Collect $200.",
            "Avatar commission sold. Collect $100.",
            "Server outage. Pay $100.",
            "It's your birthday! Collect $10 from every player.",
            "GLITCH! Go directly to the Glitch Zone.",
            "Free Glitch Pass. Keep it to escape the Glitch Zone.",
            "Your world got featured! Collect $50.",
            "Hardware upgrade. Pay $50.",
            "Tip jar overflow. Collect $20.",
            "Bandwidth bill: pay $40 per Loop and $115 per Tower.",
            "You found a pixel fortune! Collect $100." };

        // ---------------- synced state ----------------
        [UdonSynced] private int phase;
        [UdonSynced] private int[] slotPid = new int[MAXP];
        [UdonSynced] private int[] cash = new int[MAXP];
        [UdonSynced] private int[] pos = new int[MAXP];
        [UdonSynced] private int[] jail = new int[MAXP];
        [UdonSynced] private int[] alive = new int[MAXP];
        [UdonSynced] private int[] passes = new int[MAXP];
        [UdonSynced] private int[] moveMode = new int[MAXP];
        [UdonSynced] private int[] coinsEarned = new int[MAXP];
        [UdonSynced] private int[] owner = new int[SPACES];
        [UdonSynced] private int[] level = new int[SPACES];
        [UdonSynced] private int turnSlot;
        [UdonSynced] private int stage;
        [UdonSynced] private int die1 = 1;
        [UdonSynced] private int die2 = 1;
        [UdonSynced] private int rollSeq;
        [UdonSynced] private int doubles;
        [UdonSynced] private int lastSteps;
        [UdonSynced] private int round;
        [UdonSynced] private int maxRounds;
        [UdonSynced] private int startedPlayers;
        [UdonSynced] private int winner;
        [UdonSynced] private int matchId;
        [UdonSynced] private int cardSeq;
        [UdonSynced] private string cardText = "";
        [UdonSynced] private int fxSeq;
        [UdonSynced] private int fxType;
        [UdonSynced] private string log = "";

        // ---------------- local state ----------------
        [HideInInspector] public int pressedArg;
        private int[] propIdx = new int[SPACES];
        private int seenRoll = -1;
        private int seenCard = -1;
        private int seenFx = -1;
        private int seenPhase = -1;
        private float stageTime;
        private float seatCheckTime;
        private float cardHideTime;
        private float resetArmTime = -10f;
        private int rentMult = 1;
        private int selected = -1;

        private void Start()
        {
            for (int i = 0; i < SPACES; i++) propIdx[i] = -1;
            for (int k = 0; k < propSpaces.Length; k++) propIdx[propSpaces[k]] = k;
            if (selectionMarker != null) selectionMarker.gameObject.SetActive(false);
            _RefreshAll();
        }

        // =====================================================================
        //  Input from buttons (local)
        // =====================================================================

        public void _OnPrimary()
        {
            int me = _LocalSlot();
            if (phase == PH_LOBBY) { _Send(me < 0 ? C_JOIN : C_START, 0); return; }
            if (phase == PH_OVER) { _Send(C_RESET, 0); return; }
            if (me != turnSlot) { _Toast("Wait for your turn!"); return; }
            if (stage == ST_ROLL || stage == ST_JAIL) _Send(C_ROLL, 0);
            else if (stage == ST_BUY) _Send(C_BUY, 0);
            else if (stage == ST_END) _Send(C_END, 0);
        }

        public void _OnSecondary()
        {
            int me = _LocalSlot();
            if (me < 0) return;
            if (phase == PH_PLAY && me == turnSlot)
            {
                if (stage == ST_BUY) { _Send(C_PASS, 0); return; }
                if (stage == ST_JAIL) { _Send(passes[me] > 0 ? C_CARD : C_BAIL, 0); return; }
            }
            _Send(C_LEAVE, 0);
        }

        public void _OnBuild() { if (_NeedSelection()) _Send(C_BUILD, selected); }
        public void _OnSell() { if (_NeedSelection()) _Send(C_SELL, selected); }
        public void _OnMortgage() { if (_NeedSelection()) _Send(C_MORT, selected); }
        public void _OnRounds() { _Send(C_ROUNDS, 0); }

        public void _OnReset()
        {
            if (Time.time - resetArmTime > 3f) { resetArmTime = Time.time; _Toast("Press RESET again to confirm."); return; }
            resetArmTime = -10f;
            _Send(C_RESET, 0);
        }

        public void _OnTile()
        {
            selected = Mathf.Clamp(pressedArg, 0, SPACES - 1);
            if (selectionMarker != null && spaceAnchors != null && spaceAnchors.Length == SPACES)
            {
                selectionMarker.gameObject.SetActive(true);
                selectionMarker.position = spaceAnchors[selected].position;
                selectionMarker.rotation = spaceAnchors[selected].rotation;
            }
            _PlayFx(0);
            _RefreshInfo();
        }

        private bool _NeedSelection()
        {
            if (selected >= 0) return true;
            _Toast("Tap a space on the board first.");
            return false;
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
            rentMult = 1;
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
                case C_ROUNDS:
                    if (phase != PH_LOBBY) return;
                    maxRounds = maxRounds == 0 ? 10 : (maxRounds >= 30 ? 0 : maxRounds + 10);
                    break;
                case C_RESET:
                    if (phase == PH_PLAY && s < 0) return;
                    _ToLobby();
                    break;
                default:
                    if (phase != PH_PLAY || s < 0 || alive[s] == 0) return;
                    _GameCmd(s, cmd, arg);
                    break;
            }
            _Commit();
        }

        private void _GameCmd(int s, int cmd, int arg)
        {
            bool myTurn = s == turnSlot;
            if (cmd == C_ROLL) { if (myTurn && (stage == ST_ROLL || stage == ST_JAIL)) _DoRoll(); return; }
            if (cmd == C_BUY) { if (myTurn && stage == ST_BUY) _Buy(s); return; }
            if (cmd == C_PASS) { if (myTurn && stage == ST_BUY) { _Log(_Name(s) + " passed on " + spaceName[pos[s]] + "."); _SetStage(ST_END); } return; }
            if (cmd == C_END) { if (myTurn && stage == ST_END) _EndTurn(); return; }
            if (cmd == C_BAIL)
            {
                if (!myTurn || stage != ST_JAIL) return;
                _Charge(s, jailFine, -1);
                jail[s] = 0;
                _Log(_Name(s) + " paid $" + jailFine + " bail.");
                _SetStage(ST_ROLL);
                return;
            }
            if (cmd == C_CARD)
            {
                if (!myTurn || stage != ST_JAIL || passes[s] <= 0) return;
                passes[s]--;
                jail[s] = 0;
                _Log(_Name(s) + " used a Glitch Pass!");
                _SetStage(ST_ROLL);
                return;
            }
            if (arg < 0 || arg >= SPACES || !myTurn || stage == ST_MOVING) return;
            if (cmd == C_BUILD) _Build(s, arg);
            else if (cmd == C_SELL) _Sell(s, arg);
            else if (cmd == C_MORT) _Mortgage(s, arg);
        }

        // =====================================================================
        //  Owner-side rules
        // =====================================================================

        private void Update()
        {
            if (spinner != null) spinner.Rotate(0f, 10f * Time.deltaTime, 0f);
            if (cardHideTime > 0f && Time.time > cardHideTime)
            {
                cardHideTime = 0f;
                _SetTexts(cardTexts, "");
            }
            if (phase != PH_PLAY || !Networking.IsOwner(gameObject)) return;

            float elapsed = Time.time - stageTime;
            if (stage == ST_MOVING)
            {
                if (elapsed > _MoveTime()) { _Resolve(); _Commit(); }
            }
            else if (afkSeconds > 0f && elapsed > afkSeconds)
            {
                _Log(_Name(turnSlot) + " is AFK - auto play.");
                if (stage == ST_ROLL || stage == ST_JAIL) _DoRoll();
                else if (stage == ST_BUY) _SetStage(ST_END);
                else if (stage == ST_END) _EndTurn();
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
            cash[i] = startingCash;
            pos[i] = 0;
            jail[i] = 0;
            passes[i] = 0;
            alive[i] = 1;
            moveMode[i] = 1;
            coinsEarned[i] = 0;
        }

        private void _StartGame()
        {
            matchId = Random.Range(1, 2000000000);
            for (int i = 0; i < SPACES; i++) { owner[i] = 0; level[i] = 0; }
            startedPlayers = 0;
            int first = -1;
            for (int i = 0; i < MAXP; i++)
            {
                if (slotPid[i] == 0) { alive[i] = 0; continue; }
                _ResetSlot(i);
                startedPlayers++;
                if (first < 0) first = i;
            }
            phase = PH_PLAY;
            round = 1;
            winner = 0;
            doubles = 0;
            turnSlot = first;
            log = "";
            _Log("Game on! " + _Name(first) + " rolls first.");
            _Fx(FX_START);
            _SetStage(ST_ROLL);
        }

        private void _ToLobby()
        {
            phase = PH_LOBBY;
            for (int i = 0; i < SPACES; i++) { owner[i] = 0; level[i] = 0; }
            for (int i = 0; i < MAXP; i++) { if (slotPid[i] != 0) _ResetSlot(i); else alive[i] = 0; }
            winner = 0;
            stage = ST_ROLL;
            log = "";
            _Log("Back to the lobby. Join and press START!");
        }

        private void _RemoveSlot(int s, string why)
        {
            string who = _Name(s);
            if (phase == PH_PLAY && alive[s] != 0) _Eliminate(s, -1);
            slotPid[s] = 0;
            alive[s] = 0;
            _Log(who + why);
        }

        private void _DoRoll()
        {
            int s = turnSlot;
            die1 = Random.Range(1, 7);
            die2 = Random.Range(1, 7);
            rollSeq++;
            bool dbl = die1 == die2;
            int total = die1 + die2;
            if (jail[s] > 0)
            {
                if (dbl)
                {
                    jail[s] = 0;
                    doubles = 0;
                    _Log(_Name(s) + " rolled doubles and escaped the Glitch Zone!");
                    _Move(s, total);
                    return;
                }
                jail[s]++;
                if (jail[s] > 3)
                {
                    _Log(_Name(s) + " pays $" + jailFine + " and leaves the Glitch Zone.");
                    _Charge(s, jailFine, -1);
                    jail[s] = 0;
                    if (alive[s] != 0) _Move(s, total);
                    return;
                }
                _Log(_Name(s) + " rolled " + die1 + "+" + die2 + " - still glitched.");
                _SetStage(ST_END);
                return;
            }
            doubles = dbl ? doubles + 1 : 0;
            if (doubles >= 3)
            {
                _Log(_Name(s) + " rolled 3 doubles - GLITCHED!");
                _SendToJail(s);
                return;
            }
            _Log(_Name(s) + " rolled " + die1 + " + " + die2 + (dbl ? " (doubles!)" : ""));
            _Move(s, total);
        }

        private void _Move(int s, int steps)
        {
            int from = pos[s];
            int to = (from + steps) % SPACES;
            if (to < from) _PassGo(s);
            pos[s] = to;
            moveMode[s] = 0;
            lastSteps = steps;
            _SetStage(ST_MOVING);
        }

        private void _MoveTo(int s, int target)
        {
            _Move(s, (target - pos[s] + SPACES) % SPACES);
        }

        private void _PassGo(int s)
        {
            cash[s] += goSalary;
            coinsEarned[s] += coinsPassGo;
            _Log(_Name(s) + " looped LOOP START: +$" + goSalary);
        }

        private void _SendToJail(int s)
        {
            pos[s] = 10;
            jail[s] = 1;
            moveMode[s] = 1;
            doubles = 0;
            lastSteps = 0;
            _Fx(FX_JAIL);
            _SetStage(ST_END);
        }

        private void _Resolve()
        {
            int s = turnSlot;
            int p = pos[s];
            int t = spaceType[p];
            int mult = rentMult;
            rentMult = 1;
            _SetStage(ST_END);

            if (t == T_PROP || t == T_PORTAL || t == T_UTIL)
            {
                int o = owner[p];
                if (o == 0)
                {
                    if (cash[s] >= spacePrice[p]) _SetStage(ST_BUY);
                    else _Log(_Name(s) + " can't afford " + spaceName[p] + ".");
                }
                else if (o - 1 != s && (level[p] & 8) == 0 && alive[o - 1] != 0)
                {
                    int rent = _Rent(p, die1 + die2) * mult;
                    _Log(_Name(s) + " pays $" + rent + " rent to " + _Name(o - 1) + ".");
                    _Charge(s, rent, o - 1);
                    _Fx(FX_RENT);
                }
            }
            else if (t == T_TAX)
            {
                _Log(_Name(s) + " pays $" + spacePrice[p] + " " + spaceName[p] + ".");
                _Charge(s, spacePrice[p], -1);
                _Fx(FX_RENT);
            }
            else if (t == T_TWIST || t == T_CHEST)
            {
                _DrawCard(s, t == T_TWIST);
            }
            else if (t == T_GOJAIL)
            {
                _Log(_Name(s) + " got GLITCHED!");
                _SendToJail(s);
            }
        }

        private void _DrawCard(int s, bool twist)
        {
            int c = Random.Range(0, 12);
            cardSeq++;
            cardText = (twist ? "<color=#FF5FBF>TWIST</color>\n" : "<color=#FFE14D>LOOP CHEST</color>\n") + (twist ? twistCards[c] : chestCards[c]);
            _Log(_Name(s) + " drew: " + (twist ? twistCards[c] : chestCards[c]));
            _Fx(FX_CARD);
            int p = pos[s];
            if (twist)
            {
                if (c == 0) _MoveTo(s, 0);
                else if (c == 1) _MoveTo(s, 39);
                else if (c == 2) _MoveTo(s, 11);
                else if (c == 3) { rentMult = 2; _MoveTo(s, ((p + 5) / 10 * 10 + 5) % SPACES); }
                else if (c == 4) _SendToJail(s);
                else if (c == 5) { pos[s] = (p + SPACES - 3) % SPACES; moveMode[s] = 2; lastSteps = 3; _SetStage(ST_MOVING); }
                else if (c == 6) cash[s] += 150;
                else if (c == 7) _Charge(s, 15, -1);
                else if (c == 8) { for (int o = 0; o < MAXP; o++) if (o != s && alive[o] != 0 && alive[s] != 0) _Charge(s, 50, o); }
                else if (c == 9) passes[s]++;
                else if (c == 10) _Charge(s, _RepairCost(s, 25, 100), -1);
                else cash[s] += 100;
            }
            else
            {
                if (c == 0) _MoveTo(s, 0);
                else if (c == 1) cash[s] += 200;
                else if (c == 2) cash[s] += 100;
                else if (c == 3) _Charge(s, 100, -1);
                else if (c == 4) { for (int o = 0; o < MAXP; o++) if (o != s && alive[o] != 0) _Charge(o, 10, s); }
                else if (c == 5) _SendToJail(s);
                else if (c == 6) passes[s]++;
                else if (c == 7) cash[s] += 50;
                else if (c == 8) _Charge(s, 50, -1);
                else if (c == 9) cash[s] += 20;
                else if (c == 10) _Charge(s, _RepairCost(s, 40, 115), -1);
                else cash[s] += 100;
            }
        }

        private int _RepairCost(int s, int perLoop, int perTower)
        {
            int total = 0;
            for (int i = 0; i < SPACES; i++)
            {
                if (owner[i] != s + 1) continue;
                int b = level[i] & 7;
                total += b == 5 ? perTower : b * perLoop;
            }
            return total;
        }

        private int _Rent(int p, int roll)
        {
            int o = owner[p];
            int t = spaceType[p];
            if (t == T_PORTAL)
            {
                int n = _CountOwnedInGroup(o, 8);
                return 25 << Mathf.Clamp(n - 1, 0, 3);
            }
            if (t == T_UTIL) return roll * (_CountOwnedInGroup(o, 9) >= 2 ? 10 : 4);
            int k = propIdx[p];
            if (k < 0) return 0;
            int b = level[p] & 7;
            int rent = propRents[k * 6 + b];
            if (b == 0 && _OwnsGroup(o - 1, spaceGroup[p])) rent *= 2;
            return rent;
        }

        private int _CountOwnedInGroup(int ownerValue, int g)
        {
            int n = 0;
            for (int i = 0; i < SPACES; i++) if (spaceGroup[i] == g && owner[i] == ownerValue) n++;
            return n;
        }

        private bool _OwnsGroup(int s, int g)
        {
            if (g < 0) return false;
            for (int i = 0; i < SPACES; i++) if (spaceGroup[i] == g && owner[i] != s + 1) return false;
            return true;
        }

        private void _Buy(int s)
        {
            int p = pos[s];
            if (owner[p] != 0 || cash[s] < spacePrice[p]) return;
            cash[s] -= spacePrice[p];
            owner[p] = s + 1;
            coinsEarned[s] += coinsBuy;
            _Log(_Name(s) + " bought " + spaceName[p] + " for $" + spacePrice[p] + "!");
            _Fx(FX_BUY);
            _SetStage(ST_END);
        }

        private void _Build(int s, int p)
        {
            int g = spaceGroup[p];
            if (spaceType[p] != T_PROP || owner[p] != s + 1) { _Log("Select one of your own properties to build."); return; }
            if (!_OwnsGroup(s, g)) { _Log("Own every " + groupNames[g] + " space to build Loops."); return; }
            int b = level[p] & 7;
            int min = 9;
            for (int i = 0; i < SPACES; i++)
            {
                if (spaceGroup[i] != g) continue;
                if ((level[i] & 8) != 0) { _Log("Unmortgage the " + groupNames[g] + " group first."); return; }
                min = Mathf.Min(min, level[i] & 7);
            }
            if (b >= 5 || b > min) { _Log("Build evenly across the " + groupNames[g] + " group."); return; }
            int cost = houseCost[g];
            if (cash[s] < cost) { _Log("Not enough cash to build on " + spaceName[p] + "."); return; }
            cash[s] -= cost;
            level[p] = b + 1;
            coinsEarned[s] += coinsBuild;
            _Log(_Name(s) + " built " + (b + 1 == 5 ? "a TOWER" : "a Loop") + " on " + spaceName[p] + ".");
            _Fx(FX_BUILD);
        }

        private void _Sell(int s, int p)
        {
            int g = spaceGroup[p];
            int b = level[p] & 7;
            if (owner[p] != s + 1 || b == 0 || spaceType[p] != T_PROP) return;
            for (int i = 0; i < SPACES; i++) if (spaceGroup[i] == g && (level[i] & 7) > b) { _Log("Sell evenly across the group."); return; }
            level[p] = b - 1;
            cash[s] += houseCost[g] / 2;
            _Log(_Name(s) + " sold an upgrade on " + spaceName[p] + ".");
            _Fx(FX_COIN);
        }

        private void _Mortgage(int s, int p)
        {
            if (owner[p] != s + 1) return;
            int half = spacePrice[p] / 2;
            if ((level[p] & 8) != 0)
            {
                int cost = half + spacePrice[p] / 20;
                if (cash[s] < cost) { _Log("Need $" + cost + " to unmortgage."); return; }
                cash[s] -= cost;
                level[p] = 0;
                _Log(_Name(s) + " unmortgaged " + spaceName[p] + ".");
                return;
            }
            int g = spaceGroup[p];
            if (spaceType[p] == T_PROP)
                for (int i = 0; i < SPACES; i++) if (spaceGroup[i] == g && (level[i] & 7) > 0) { _Log("Sell the group's upgrades first."); return; }
            level[p] = 8;
            cash[s] += half;
            _Log(_Name(s) + " mortgaged " + spaceName[p] + " for $" + half + ".");
            _Fx(FX_COIN);
        }

        /// <summary>Takes money from slot s. Auto-sells upgrades and mortgages if needed; bankrupts if still short.</summary>
        private void _Charge(int s, int amount, int creditor)
        {
            if (amount <= 0 || alive[s] == 0) return;
            cash[s] -= amount;
            if (cash[s] < 0) _Liquidate(s);
            int paid = cash[s] < 0 ? amount + cash[s] : amount;
            if (creditor >= 0 && alive[creditor] != 0) cash[creditor] += paid;
            if (cash[s] < 0) _Eliminate(s, creditor);
        }

        private void _Liquidate(int s)
        {
            for (int round5 = 0; round5 < 5 && cash[s] < 0; round5++)
            {
                for (int i = 0; i < SPACES && cash[s] < 0; i++)
                {
                    if (owner[i] != s + 1 || (level[i] & 7) == 0) continue;
                    level[i]--;
                    cash[s] += houseCost[spaceGroup[i]] / 2;
                }
            }
            for (int i = 0; i < SPACES && cash[s] < 0; i++)
            {
                if (owner[i] != s + 1 || level[i] != 0) continue;
                level[i] = 8;
                cash[s] += spacePrice[i] / 2;
            }
        }

        private void _Eliminate(int s, int creditor)
        {
            alive[s] = 0;
            cash[s] = 0;
            jail[s] = 0;
            passes[s] = 0;
            for (int i = 0; i < SPACES; i++)
            {
                if (owner[i] != s + 1) continue;
                if (creditor >= 0 && alive[creditor] != 0) { owner[i] = creditor + 1; level[i] = level[i] & 8; }
                else { owner[i] = 0; level[i] = 0; }
            }
            _Log(_Name(s) + " went BANKRUPT!");
            _Fx(FX_BANKRUPT);
        }

        private void _EndTurn()
        {
            int s = turnSlot;
            if (doubles > 0 && jail[s] == 0 && alive[s] != 0) { _Log(_Name(s) + " rolls again!"); _SetStage(ST_ROLL); return; }
            _NextTurn();
        }

        private void _NextTurn()
        {
            int s = turnSlot;
            for (int k = 1; k <= MAXP; k++)
            {
                int n = (s + k) % MAXP;
                if (alive[n] == 0 || slotPid[n] == 0) continue;
                if (n <= s) round++;
                turnSlot = n;
                break;
            }
            doubles = 0;
            if (maxRounds > 0 && round > maxRounds) { _EndGame(_Richest()); return; }
            _SetStage(jail[turnSlot] > 0 ? ST_JAIL : ST_ROLL);
        }

        private void _CheckWin()
        {
            int count = 0;
            int last = -1;
            for (int i = 0; i < MAXP; i++) if (alive[i] != 0 && slotPid[i] != 0) { count++; last = i; }
            if (count == 0 || (startedPlayers >= 2 && count <= 1)) _EndGame(last);
        }

        private void _EndGame(int w)
        {
            phase = PH_OVER;
            winner = w + 1;
            for (int i = 0; i < MAXP; i++) if (slotPid[i] != 0) coinsEarned[i] += coinsFinish;
            if (w >= 0)
            {
                coinsEarned[w] += coinsWin;
                _Log(_Name(w) + " WINS LOOPLAND!");
            }
            else _Log("Game over.");
            _Fx(FX_WIN);
        }

        private int _Richest()
        {
            int best = -1;
            int bestWorth = -1;
            for (int i = 0; i < MAXP; i++)
            {
                if (alive[i] == 0 || slotPid[i] == 0) continue;
                int w = _NetWorth(i);
                if (w > bestWorth) { bestWorth = w; best = i; }
            }
            return best;
        }

        private int _NetWorth(int s)
        {
            int w = cash[s];
            for (int i = 0; i < SPACES; i++)
            {
                if (owner[i] != s + 1) continue;
                w += (level[i] & 8) != 0 ? spacePrice[i] / 2 : spacePrice[i];
                int g = spaceGroup[i];
                if (g >= 0 && g < houseCost.Length) w += (level[i] & 7) * houseCost[g];
            }
            return w;
        }

        private void _Commit()
        {
            if (phase == PH_PLAY)
            {
                _CheckWin();
                if (phase == PH_PLAY && (alive[turnSlot] == 0 || slotPid[turnSlot] == 0)) _NextTurn();
            }
            RequestSerialization();
            _RefreshAll();
        }

        private void _Log(string msg)
        {
            log = msg + "\n" + log;
            int idx = -1;
            for (int n = 0; n < 5; n++)
            {
                idx = log.IndexOf('\n', idx + 1);
                if (idx < 0) return;
            }
            log = log.Substring(0, idx);
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

        // =====================================================================
        //  Presentation (runs on every client)
        // =====================================================================

        public void _OnCosmeticsChanged()
        {
            _RefreshTokens(false);
        }

        private void _RefreshAll()
        {
            bool rolled = seenRoll >= 0 && rollSeq != seenRoll;
            bool firstSync = seenRoll < 0;

            if (dice != null && dice.Length >= 2 && dice[0] != null && dice[1] != null)
            {
                _ApplyDiceSkin();
                if (rolled) { dice[0]._Roll(die1, 0f); dice[1]._Roll(die2, 0.08f); }
                else if (firstSync || rollSeq != seenRoll) { dice[0]._Show(die1); dice[1]._Show(die2); }
            }
            seenRoll = rollSeq;

            if (seenPhase != phase) { seenPhase = phase; _RefreshTokens(false); }
            _RefreshTokens(rolled);

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

            _RefreshBoard();
            _RefreshTexts();
            _RefreshInfo();

            int me = _LocalSlot();
            if (me >= 0 && matchId != 0 && phase != PH_LOBBY && store != null) store._ApplyMatchReward(matchId, coinsEarned[me]);
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
                bool show = slotPid[s] != 0 && (alive[s] != 0 || phase == PH_LOBBY);
                tk.gameObject.SetActive(show);
                if (!show) continue;
                VRCPlayerApi p = _SlotPlayer(s);
                string label = Utilities.IsValid(p) ? p.displayName : "";
                Color baseCol = slotColors != null && s < slotColors.Length ? slotColors[s] : Color.white;
                if (store != null && Utilities.IsValid(p))
                {
                    int skin = store._GetEquipped(p, 1);
                    int trail = store._GetEquipped(p, 2);
                    Color body = skin == 0 ? baseCol : store._TokenColor(skin);
                    tk._Skin(body, store._TokenGlow(skin, baseCol), store._TrailColor(trail), store._IsRainbowToken(skin), store._IsRainbowTrail(trail), label);
                }
                else tk._Skin(baseCol, baseCol, baseCol, false, false, label);
                tk._MoveTo(pos[s], moveMode[s], rolled ? 1.3f : 0.9f);
            }
        }

        private void _RefreshBoard()
        {
            for (int i = 0; i < SPACES; i++)
            {
                int o = owner[i];
                if (ownerBars != null && i < ownerBars.Length && ownerBars[i] != null)
                {
                    Renderer r = ownerBars[i];
                    r.enabled = o != 0;
                    if (o != 0 && slotColors != null && o - 1 < slotColors.Length)
                    {
                        Color c = slotColors[o - 1];
                        if ((level[i] & 8) != 0) c = c * 0.25f;
                        r.material.SetColor("_Color", c);
                        r.material.SetColor("_EmissionColor", c * 1.4f);
                    }
                }
                if (buildMarkers == null || buildMarkers.Length < SPACES * 5) continue;
                int b = (level[i] & 8) != 0 ? 0 : level[i] & 7;
                for (int k = 0; k < 5; k++)
                {
                    GameObject m = buildMarkers[i * 5 + k];
                    if (m == null) continue;
                    bool on = b == 5 ? k == 4 : k < b;
                    if (m.activeSelf != on) m.SetActive(on);
                }
            }
        }

        private void _RefreshTexts()
        {
            int me = _LocalSlot();
            string status;
            string prim = "";
            string sec = "";
            if (phase == PH_LOBBY)
            {
                status = "<size=150%><b>LOOPLAND</b></size>\n<color=#9AF2FF>LOBBY</color>  " + _SeatedCount() + "/" + MAXP + " players";
                status += "\nRounds: " + (maxRounds == 0 ? "No limit" : maxRounds.ToString());
                prim = me < 0 ? "JOIN GAME" : "START GAME";
                sec = me < 0 ? "-" : "LEAVE";
            }
            else if (phase == PH_OVER)
            {
                status = "<size=150%><b>GAME OVER</b></size>\n" + (winner > 0 ? _Name(winner - 1) + " <color=#FFE14D>WINS!</color>" : "No winner");
                prim = "NEW GAME";
                sec = me < 0 ? "-" : "LEAVE";
            }
            else
            {
                status = "<size=120%><b>ROUND " + round + (maxRounds > 0 ? " / " + maxRounds : "") + "</b></size>\n" + _Name(turnSlot) + " " + _StageHint();
                status += "\n<color=#9AF2FF>Dice  " + die1 + " + " + die2 + "</color>";
                bool myTurn = me == turnSlot && me >= 0;
                if (me < 0) { prim = "SPECTATING"; sec = "-"; }
                else if (!myTurn) { prim = "WAITING..."; sec = "LEAVE GAME"; }
                else if (stage == ST_ROLL) { prim = doubles > 0 ? "ROLL AGAIN" : "ROLL DICE"; sec = "LEAVE GAME"; }
                else if (stage == ST_JAIL) { prim = "ROLL DOUBLES"; sec = passes[me] > 0 ? "USE GLITCH PASS" : "PAY $" + jailFine; }
                else if (stage == ST_BUY) { prim = "BUY $" + spacePrice[pos[me]]; sec = "PASS"; }
                else if (stage == ST_END) { prim = doubles > 0 && jail[me] == 0 ? "ROLL AGAIN" : "END TURN"; sec = "LEAVE GAME"; }
                else { prim = "MOVING..."; sec = "LEAVE GAME"; }
            }
            if (log.Length > 0) status += "\n<size=75%><color=#C8C8E0>" + log + "</color></size>";
            _SetTexts(statusTexts, status);
            _SetTexts(primaryLabels, prim);
            _SetTexts(secondaryLabels, sec);
            _SetTexts(roundsLabels, "ROUNDS\n" + (maxRounds == 0 ? "INF" : maxRounds.ToString()));

            string players = "";
            for (int i = 0; i < MAXP; i++)
            {
                if (slotPid[i] == 0) continue;
                string line = (phase == PH_PLAY && i == turnSlot ? "> " : "  ") + _Name(i);
                if (phase != PH_LOBBY)
                {
                    if (alive[i] == 0) line += "  <color=#777777>BANKRUPT</color>";
                    else
                    {
                        line += "  <color=#7CFF4F>$" + cash[i] + "</color>  " + _CountProps(i) + " spaces";
                        if (jail[i] > 0) line += "  <color=#FF4D5E>GLITCHED</color>";
                        if (passes[i] > 0) line += "  pass x" + passes[i];
                    }
                }
                players += line + "\n";
            }
            _SetTexts(playerTexts, players.Length == 0 ? "Press JOIN GAME to take a seat!" : players);
        }

        private int _CountProps(int s)
        {
            int n = 0;
            for (int i = 0; i < SPACES; i++) if (owner[i] == s + 1) n++;
            return n;
        }

        private string _StageHint()
        {
            if (stage == ST_ROLL) return "is rolling the dice";
            if (stage == ST_MOVING) return "is on the move...";
            if (stage == ST_BUY) return "is deciding to buy " + spaceName[pos[turnSlot]];
            if (stage == ST_JAIL) return "is stuck in the Glitch Zone";
            return "can end their turn";
        }

        private void _RefreshInfo()
        {
            if (selected < 0) { _SetTexts(infoTexts, "<color=#9AF2FF>Tap any board space</color>\nto inspect, build, sell\nor mortgage it."); return; }
            int p = selected;
            int t = spaceType[p];
            int g = spaceGroup[p];
            string s = "<b>" + spaceName[p] + "</b>";
            if (g >= 0) s += "  <color=#" + groupHex[g] + ">" + groupNames[g] + "</color>";
            if (t == T_PROP)
            {
                int k = propIdx[p];
                s += "\nPrice $" + spacePrice[p] + "   Loop $" + houseCost[g];
                if (k >= 0) s += "\nRent " + propRents[k * 6] + " | " + propRents[k * 6 + 1] + " | " + propRents[k * 6 + 2] + " | " + propRents[k * 6 + 3] + " | " + propRents[k * 6 + 4] + " | T " + propRents[k * 6 + 5];
            }
            else if (t == T_PORTAL) s += "\nPrice $" + spacePrice[p] + "\nRent 25 / 50 / 100 / 200";
            else if (t == T_UTIL) s += "\nPrice $" + spacePrice[p] + "\nRent 4x dice (10x with both)";
            else if (t == T_TAX) s += "\nPay $" + spacePrice[p];
            else if (t == T_GO) s += "\nCollect $" + goSalary + " every loop";
            else if (t == T_TWIST || t == T_CHEST) s += "\nDraw a card";
            else if (t == T_GOJAIL) s += "\nStraight to the Glitch Zone!";
            int o = owner[p];
            if (o != 0)
            {
                int b = level[p] & 7;
                s += "\nOwner " + _Name(o - 1) + ((level[p] & 8) != 0 ? "  <color=#FF4D5E>MORTGAGED</color>" : (b == 5 ? "  TOWER" : (b > 0 ? "  Loops " + b : "")));
            }
            _SetTexts(infoTexts, s);
        }

        private void _SetTexts(TextMeshPro[] arr, string value)
        {
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++) if (arr[i] != null) arr[i].text = value;
        }

        private void _Toast(string msg)
        {
            _SetTexts(infoTexts, "<color=#FFE14D>" + msg + "</color>");
            _PlayFx(0);
        }

        private void _PlayFx(int type)
        {
            if (sfx != null && fxClips != null && type >= 0 && type < fxClips.Length && fxClips[type] != null) sfx.PlayOneShot(fxClips[type], 0.8f);
            if (type == FX_WIN && celebrateFx != null) celebrateFx.Play();
            if ((type == FX_BUY || type == FX_RENT || type == FX_BUILD) && moneyFx != null) moneyFx.Emit(40);
        }
    }
}
