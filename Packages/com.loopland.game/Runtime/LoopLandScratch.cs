using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Persistence;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace LoopLand
{
    /// <summary>
    /// Free Loop Scratch: everyone gets free scratch tickets, one every refillMinutes (up to maxTickets) plus one for
    /// every finished game. Scratch a ticket on the counter to reveal a just-for-fun reward: Loop XP, 2x XP for your next
    /// game, a Lucky Start (Boost) or Shield Start for your next game, fireworks for everyone, or a collectible stamp.
    /// Tickets and rewards can't be bought, traded or cashed out, and nothing here costs Loop Coins or Credits.
    /// Saved per player with PlayerData. Sync mode is Manual (with no synced variables) so fireworks can be a network event.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class LoopLandScratch : UdonSharpBehaviour
    {
        [Header("Free tickets")]
        public int refillMinutes = 30;
        public int maxTickets = 5;
        public int firstTickets = 1;

        [Header("Ticket on the counter")]
        public LoopLandTicket ticket;

        [Header("Screen")]
        public TMP_Text ticketsText;
        public TMP_Text timerText;
        public TMP_Text xpText;
        public TMP_Text perksText;
        public TMP_Text stampText;
        public TMP_Text messageText;
        public TMP_Text buttonLabel;
        public Image[] stampIcons;

        [Header("Rewards")]
        public string[] stampNames = { "LUCKY STAR", "INFINITY", "NEON CITY", "GOLDEN DICE", "CRYSTAL TREE", "PORTAL", "ROCKET", "CROWN" };
        public Sprite[] stampArt;
        public Color[] stampColors;
        public Sprite xpArt;
        public Sprite doubleXpArt;
        public Sprite boostArt;
        public Sprite shieldArt;
        public Sprite fireworksArt;
        public ParticleSystem fireworks;

        [Header("Feedback")]
        public AudioSource sfx;
        public AudioClip clickClip;
        public AudioClip ticketClip;
        public AudioClip errorClip;
        public AudioClip fireworksClip;

        private const string K_TICKETS = "ll_tickets";
        private const string K_TIME = "ll_ticket_t";
        private const string K_XP = "ll_xp";
        private const string K_XP2 = "ll_xp2";
        private const string K_BOOST = "ll_start_boost";
        private const string K_SHIELD = "ll_start_shield";
        private const string K_STAMPS = "ll_stamps";
        private const string K_FINISH = "ll_tk_match";
        private const string K_START = "ll_start_match";

        private const int W_XP50 = 0;
        private const int W_XP100 = 1;
        private const int W_XP250 = 2;
        private const int W_XP2 = 3;
        private const int W_BOOST = 4;
        private const int W_SHIELD = 5;
        private const int W_FIREWORKS = 6;
        private const int W_STAMP = 7;
        private int[] weights = { 22, 15, 5, 12, 12, 12, 10, 12 };

        private bool restored;
        private int pendingStart;
        private int pendingFinish;
        private int pendingXp;
        private int reward = -1;
        private int rewardStamp;
        private bool revealed = true;
        private float nextTick;
        private string message = "";

        private void Start()
        {
            _Refresh();
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            restored = true;
            if (!PlayerData.HasKey(player, K_TICKETS))
            {
                PlayerData.SetInt(K_TICKETS, firstTickets);
                PlayerData.SetInt(K_TIME, _Now());
            }
            _Refill();
            if (pendingStart != 0) _MatchStarted(pendingStart);
            if (pendingFinish != 0) _MatchFinished(pendingFinish, pendingXp);
            _Refresh();
        }

        public override void OnPlayerDataUpdated(VRCPlayerApi player, PlayerData.Info[] infos)
        {
            if (Utilities.IsValid(player) && player.isLocal) _Refresh();
        }

        private void Update()
        {
            if (Time.time < nextTick) return;
            nextTick = Time.time + 5f;
            if (!restored) return;
            _Refill();
            _Refresh();
        }

        // ------------------------------------------------------------ buttons (UI calls these)

        public void _OnScratch()
        {
            if (!restored) { _Fail("Your save is still loading..."); return; }
            if (ticket != null && ticket._IsActive() && !revealed) { _Fail("Scratch the ticket on the counter first!"); return; }
            _Refill();
            int n = _Get(K_TICKETS);
            if (n <= 0) { _Fail("No free tickets right now. " + _NextText()); return; }
            // a full wallet doesn't refill, so the clock starts when you use a ticket
            if (n >= maxTickets) PlayerData.SetInt(K_TIME, _Now());
            PlayerData.SetInt(K_TICKETS, n - 1);
            _Roll();
            _Grant(); // saved straight away, so walking off mid-scratch never loses it
            revealed = false;
            if (ticket != null) ticket._Show(_RewardArt(), _RewardTitle(), _RewardSub(), "FREE LOOP SCRATCH");
            message = "Scratch your free ticket on the counter!";
            _Sfx(ticketClip);
            _Refresh();
        }

        /// <summary>The ticket on the counter has been scratched.</summary>
        public void _OnTicketScratched()
        {
            revealed = true;
            message = "<color=#FFE14D>You got: " + _RewardTitle() + "!</color>";
            if (reward == W_FIREWORKS) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetFireworks));
            _Refresh();
        }

        public void NetFireworks()
        {
            if (fireworks != null) fireworks.Play();
            _Sfx(fireworksClip);
        }

        // ------------------------------------------------------------ the game calls these

        /// <summary>A new game started with the local player in it: their start perks are used up.</summary>
        public void _MatchStarted(int match)
        {
            if (!restored) { pendingStart = match; return; }
            pendingStart = 0;
            if (_Get(K_START) == match) return;
            PlayerData.SetInt(K_START, match);
            if (_Get(K_BOOST) > 0) PlayerData.SetInt(K_BOOST, 0);
            if (_Get(K_SHIELD) > 0) PlayerData.SetInt(K_SHIELD, 0);
        }

        /// <summary>The local player finished a game: XP (doubled by a 2x XP reward) and a free ticket, once per game.</summary>
        public void _MatchFinished(int match, int xp)
        {
            if (!restored) { pendingFinish = match; pendingXp = xp; return; }
            pendingFinish = 0;
            if (_Get(K_FINISH) == match) return;
            PlayerData.SetInt(K_FINISH, match);
            bool dbl = _Get(K_XP2) > 0;
            if (dbl) PlayerData.SetInt(K_XP2, 0);
            int gain = dbl ? xp * 2 : xp;
            PlayerData.SetInt(K_XP, _Get(K_XP) + gain);
            int n = _Get(K_TICKETS);
            if (n < maxTickets) PlayerData.SetInt(K_TICKETS, n + 1);
            message = "<color=#7CFF4F>Game finished: +" + gain + " XP" + (dbl ? " (2x!)" : "") + " and a free ticket!</color>";
            _Refresh();
        }

        /// <summary>Text for the TICKETS panel on the dashboards.</summary>
        public string _Summary()
        {
            if (!restored) return "Loading your tickets...";
            int n = _Get(K_TICKETS);
            string s = "<size=130%><b><color=#FF3DCB>" + n + " FREE TICKET" + (n == 1 ? "" : "S") + "</color></b></size>\n";
            s += "<size=85%>" + (n >= maxTickets ? "Your ticket wallet is full!" : _NextText()) + "\nYou also get one for every finished game.</size>\n\n";
            s += "Scratch them at the <b>FREE LOOP SCRATCH</b> machine next to the store.\n\n";
            s += "LEVEL <b>" + _Level() + "</b>   <size=85%>" + _Get(K_XP) + " XP</size>\n";
            s += "STAMPS <b>" + _StampCount() + " / " + stampNames.Length + "</b>\n";
            string perks = _Perks();
            if (perks.Length > 0) s += "NEXT GAME: <color=#7CFF4F>" + perks + "</color>";
            return s;
        }

        // ------------------------------------------------------------ rewards

        private void _Roll()
        {
            int total = 0;
            for (int i = 0; i < weights.Length; i++) total += weights[i];
            int r = Random.Range(0, total);
            reward = W_XP50;
            for (int i = 0; i < weights.Length; i++)
            {
                if (r < weights[i])
                {
                    reward = i;
                    break;
                }
                r -= weights[i];
            }
            if (reward != W_STAMP) return;
            int stamps = _Get(K_STAMPS);
            int missing = 0;
            for (int i = 0; i < stampNames.Length; i++) if ((stamps & (1 << i)) == 0) missing++;
            if (missing == 0)
            {
                reward = W_XP100; // the book is full
                return;
            }
            int k = Random.Range(0, missing);
            for (int i = 0; i < stampNames.Length; i++)
            {
                if ((stamps & (1 << i)) != 0) continue;
                if (k == 0)
                {
                    rewardStamp = i;
                    break;
                }
                k--;
            }
        }

        private void _Grant()
        {
            if (reward == W_XP50 || reward == W_XP100 || reward == W_XP250) PlayerData.SetInt(K_XP, _Get(K_XP) + _RewardXp());
            else if (reward == W_XP2) PlayerData.SetInt(K_XP2, 1);
            else if (reward == W_BOOST) PlayerData.SetInt(K_BOOST, 1);
            else if (reward == W_SHIELD) PlayerData.SetInt(K_SHIELD, 1);
            else if (reward == W_STAMP) PlayerData.SetInt(K_STAMPS, _Get(K_STAMPS) | (1 << rewardStamp));
        }

        private int _RewardXp()
        {
            return reward == W_XP50 ? 50 : (reward == W_XP100 ? 100 : 250);
        }

        private string _RewardTitle()
        {
            if (reward == W_XP50 || reward == W_XP100 || reward == W_XP250) return "+" + _RewardXp() + " XP";
            if (reward == W_XP2) return "2x XP";
            if (reward == W_BOOST) return "LUCKY START";
            if (reward == W_SHIELD) return "SHIELD START";
            if (reward == W_FIREWORKS) return "FIREWORKS!";
            if (reward == W_STAMP && rewardStamp >= 0 && rewardStamp < stampNames.Length) return stampNames[rewardStamp] + " STAMP";
            return "LOOP XP";
        }

        private string _RewardSub()
        {
            if (reward == W_XP50 || reward == W_XP100 || reward == W_XP250) return "Loop XP for your level";
            if (reward == W_XP2) return "Double XP for your next finished game";
            if (reward == W_BOOST) return "Start your next game with a Boost (x2 coins)";
            if (reward == W_SHIELD) return "Start your next game with a Shield";
            if (reward == W_FIREWORKS) return "A fireworks show for everyone in the world";
            return "A new stamp for your collection";
        }

        private Sprite _RewardArt()
        {
            if (reward == W_XP2) return doubleXpArt;
            if (reward == W_BOOST) return boostArt;
            if (reward == W_SHIELD) return shieldArt;
            if (reward == W_FIREWORKS) return fireworksArt;
            if (reward == W_STAMP && stampArt != null && rewardStamp >= 0 && rewardStamp < stampArt.Length) return stampArt[rewardStamp];
            return xpArt;
        }

        // ------------------------------------------------------------ helpers

        /// <summary>Minutes on the network clock.</summary>
        private int _Now()
        {
            return (int)(Networking.GetNetworkDateTime().Ticks / 600000000L);
        }

        private int _Get(string key)
        {
            return PlayerData.GetInt(Networking.LocalPlayer, key);
        }

        /// <summary>Adds the tickets that have refilled since the last time.</summary>
        private void _Refill()
        {
            if (!restored) return;
            int n = _Get(K_TICKETS);
            if (n >= maxTickets) return;
            int last = _Get(K_TIME);
            int add = (_Now() - last) / Mathf.Max(1, refillMinutes);
            if (add <= 0) return;
            int give = Mathf.Min(add, maxTickets - n);
            PlayerData.SetInt(K_TICKETS, n + give);
            PlayerData.SetInt(K_TIME, last + give * Mathf.Max(1, refillMinutes));
        }

        private string _NextText()
        {
            int left = Mathf.Max(1, Mathf.Max(1, refillMinutes) - (_Now() - _Get(K_TIME)));
            return "Next free ticket in " + left + " min.";
        }

        private int _Level()
        {
            return 1 + _Get(K_XP) / 250;
        }

        private int _StampCount()
        {
            int stamps = _Get(K_STAMPS);
            int n = 0;
            for (int i = 0; i < stampNames.Length; i++) if ((stamps & (1 << i)) != 0) n++;
            return n;
        }

        private string _Perks()
        {
            string s = "";
            if (_Get(K_BOOST) > 0) s += "LUCKY START";
            if (_Get(K_SHIELD) > 0) s += (s.Length > 0 ? ", " : "") + "SHIELD START";
            if (_Get(K_XP2) > 0) s += (s.Length > 0 ? ", " : "") + "2x XP";
            return s;
        }

        private void _Refresh()
        {
            if (!restored)
            {
                if (ticketsText != null) ticketsText.text = "...";
                if (messageText != null) messageText.text = "Loading your tickets...";
                return;
            }
            int n = _Get(K_TICKETS);
            if (ticketsText != null) ticketsText.text = "<b>" + n + "</b> FREE TICKET" + (n == 1 ? "" : "S");
            if (timerText != null) timerText.text = n >= maxTickets ? "Your ticket wallet is full!" : _NextText();
            if (xpText != null) xpText.text = "LEVEL <b>" + _Level() + "</b>   <color=#9AF2FF>" + _Get(K_XP) + " XP</color>";
            string perks = _Perks();
            if (perksText != null) perksText.text = perks.Length > 0 ? "NEXT GAME: <color=#7CFF4F>" + perks + "</color>" : "Win perks for your next game!";
            if (stampText != null) stampText.text = "STAMP BOOK  <color=#FFE14D>" + _StampCount() + " / " + stampNames.Length + "</color>";
            int stamps = _Get(K_STAMPS);
            if (stampIcons != null)
                for (int i = 0; i < stampIcons.Length; i++)
                {
                    if (stampIcons[i] == null) continue;
                    bool got = (stamps & (1 << i)) != 0;
                    Color c = stampColors != null && i < stampColors.Length ? stampColors[i] : Color.white;
                    stampIcons[i].color = got ? c : new Color(0.25f, 0.25f, 0.35f, 0.5f);
                }
            if (buttonLabel != null) buttonLabel.text = n > 0 ? "SCRATCH A FREE TICKET" : "NO TICKETS RIGHT NOW";
            if (messageText != null) messageText.text = message;
        }

        private void _Fail(string msg)
        {
            message = "<color=#FF6B7A>" + msg + "</color>";
            _Sfx(errorClip);
            _Refresh();
        }

        private void _Sfx(AudioClip c)
        {
            if (sfx != null && c != null) sfx.PlayOneShot(c, 0.8f);
        }
    }
}
