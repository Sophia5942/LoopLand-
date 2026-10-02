using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Persistence;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace LoopLand
{
    /// <summary>
    /// LoopLand Scratch Cards: real scratch cards. Buy a Common, Rare, Epic or Legendary card with Loop Coins, then rub the
    /// silver off its 9 spots with your pointer. Find 3 the same and you win that prize (Loop Coins or a store item you
    /// don't own yet); otherwise the card doesn't win. Coin prizes are worth less than the cards on average, so cards
    /// can't be farmed for coins. The prize is saved the moment the card is bought, so leaving mid-scratch never loses it.
    /// Everything is local to the player.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LoopLandScratch : UdonSharpBehaviour
    {
        public LoopLandStore store;

        [Header("Cards: common, rare, epic, legendary")]
        public string[] packNames;
        public int[] packPrices;
        public Sprite[] cardArts;
        public int[] odds;                                   // 6 per card, in percent: no win, coins, common, rare, epic, legendary item
        public int[] coinMult = { 50, 100, 200, 500, 2500 };  // coin prizes, in percent of the card price
        public int[] coinWeights = { 45, 30, 17, 7, 1 };      // how often each coin prize is won

        [Header("Pages")]
        public GameObject packPage;
        public GameObject scratchPage;
        public GameObject infoPanel;
        public GameObject wonPanel;

        [Header("Card")]
        public Image cardBack;
        public TMP_Text cardRarity;
        public TMP_Text cardPrice;
        public TMP_Text cardTopPrize;
        public Image[] spotIcons;
        public TMP_Text[] spotLabels;
        public GameObject[] spotGlows;
        public GameObject[] cells;      // foil flakes, spot by spot: each hides itself (Selectable + Animator) when the pointer rubs over it
        [Range(0.3f, 1f)] public float spotRevealAt = 0.6f;
        public TMP_Text progressText;
        public ParticleSystem dust;
        public Sprite coinArt;
        public Sprite noPrizeArt;

        [Header("Result")]
        public TMP_Text wonTitle;
        public TMP_Text wonName;
        public TMP_Text wonKind;
        public TMP_Text wonNote;
        public GameObject[] stars;
        public GameObject equipButton;
        public TMP_Text equipLabel;

        [Header("Screen and collection")]
        public TMP_Text coinsText;
        public TMP_Text messageText;
        public Image[] collectionIcons;
        public TMP_Text collectionText;

        [Header("Feedback")]
        public AudioSource sfx;
        public AudioClip clickClip;
        public AudioClip buyClip;
        public AudioClip errorClip;
        public AudioClip scratchClip;
        public AudioClip spotClip;
        public AudioClip winClip;
        public AudioClip loseClip;
        public ParticleSystem confetti;

        private const int TIERS = 6;
        private const int SPOTS = 9;
        private const int SYM_COINS = 100;  // symbol codes: pool item index, SYM_COINS + coin prize index, or SYM_NONE
        private const int SYM_NONE = 200;
        private string[] tierNames = { "", "LOOP COINS", "COMMON", "RARE", "EPIC", "LEGENDARY" };
        private string[] catNames = { "DICE", "TOKEN", "BUILDING STYLE", "TRAIL" };

        private int pack;
        private bool onCard;
        private bool revealed;
        private int prizeTier;          // 0 no win, 1 coins, 2-5 common to legendary item
        private int prizeCat = -1;
        private int prizeItem;
        private int prizePool = -1;
        private int prizeCoinIndex;
        private int prizeCoins;
        private bool allOwned;
        private bool jackpot;
        private int[] spotSym = new int[SPOTS];
        private int[] spotLeft = new int[SPOTS];
        private bool[] spotDone = new bool[SPOTS];
        private int spotsDone;
        private int perSpot = 1;
        private int[] decoys = new int[16];
        private int[] remain;
        private int remainCount;
        private float nextPoll;
        private float nextScratch;
        private bool leftHand;
        private int[] poolCat = new int[64];
        private int[] poolItem = new int[64];
        private int[] poolTier = new int[64];
        private int poolCount;
        private string message = "";

        private void Start()
        {
            _BuildPool();
            _ShowCard(false);
            _Refresh();
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            // the store may restore after this behaviour does, so refresh again a moment later
            if (Utilities.IsValid(player) && player.isLocal) SendCustomEventDelayedFrames(nameof(_Refresh), 2);
        }

        public override void OnPlayerDataUpdated(VRCPlayerApi player, PlayerData.Info[] infos)
        {
            if (Utilities.IsValid(player) && player.isLocal) _Refresh();
        }

        public override void InputUse(bool value, UdonInputEventArgs args)
        {
            if (value) leftHand = args.handType == HandType.LEFT; // the hand that last clicked is the one pointing
        }

        // ------------------------------------------------------------ buttons (UI calls these)

        public void _OnBuy0() { _Buy(0); }
        public void _OnBuy1() { _Buy(1); }
        public void _OnBuy2() { _Buy(2); }
        public void _OnBuy3() { _Buy(3); }

        public void _OnEquip()
        {
            if (prizeCat < 0 || store == null) return;
            store._EquipItem(prizeCat, prizeItem);
            if (equipLabel != null) equipLabel.text = "EQUIPPED!";
            _Sfx(clickClip);
        }

        public void _OnAgain()
        {
            if (onCard && !revealed) return;
            onCard = false;
            revealed = false;
            _ShowCard(false);
            _Sfx(clickClip);
            _Refresh();
        }

        // ------------------------------------------------------------ scratching

        /// <summary>Watches the foil flakes: the UI hides each one the moment the pointer rubs over it.</summary>
        private void Update()
        {
            if (!onCard || revealed || cells == null || remain == null || Time.time < nextPoll) return;
            nextPoll = Time.time + 0.05f;
            int fresh = 0;
            bool newSpot = false;
            int need = perSpot - Mathf.CeilToInt(perSpot * spotRevealAt);
            for (int k = remainCount - 1; k >= 0; k--)
            {
                int idx = remain[k];
                GameObject cell = cells[idx];
                if (cell != null && cell.activeSelf) continue;
                if (fresh < 3 && cell != null && dust != null)
                {
                    dust.transform.position = cell.transform.position;
                    dust.Emit(5);
                }
                fresh++;
                remainCount--;
                remain[k] = remain[remainCount];
                int s = idx / perSpot;
                if (s >= SPOTS) continue;
                spotLeft[s]--;
                if (!spotDone[s] && spotLeft[s] <= need)
                {
                    spotDone[s] = true;
                    spotsDone++;
                    newSpot = true;
                }
            }
            if (fresh == 0) return;
            if (Time.time >= nextScratch)
            {
                nextScratch = Time.time + 0.09f;
                _Sfx(scratchClip);
                _Buzz();
            }
            if (newSpot) _Sfx(spotClip);
            if (progressText != null) progressText.text = "SPOTS SCRATCHED  <color=#FFE14D>" + spotsDone + " / " + SPOTS + "</color>";
            if (spotsDone >= SPOTS) _Reveal();
        }

        // ------------------------------------------------------------ game logic

        private void _Buy(int p)
        {
            if (store == null || packPrices == null || p < 0 || p >= packPrices.Length) return;
            if (onCard && !revealed) { _Fail("Finish scratching your card first!"); return; }
            if (!store._IsReady()) { _Fail("Your save is still loading..."); return; }
            if (poolCount == 0) _BuildPool();
            int price = packPrices[p];
            if (!store._SpendCoins(price)) { _Fail("You need " + (price - store._Coins()) + " more Loop Coins for a " + packNames[p] + " card."); return; }
            pack = p;
            _Roll();
            _Grant();
            _Layout();
            _StartCard();
            message = "";
            _Sfx(buyClip);
            _Refresh();
        }

        /// <summary>Every coin-priced store item can be won; its price decides its rarity.</summary>
        private void _BuildPool()
        {
            poolCount = 0;
            if (store == null) return;
            for (int cat = 0; cat < 4; cat++)
            {
                int n = store._ItemCountOf(cat);
                for (int i = 1; i < n && poolCount < poolCat.Length; i++)
                {
                    int price = store._PriceOf(cat, i);
                    if (price <= 0 || store._ProductOf(cat, i) >= 0) continue;
                    poolCat[poolCount] = cat;
                    poolItem[poolCount] = i;
                    poolTier[poolCount] = price <= 200 ? 1 : (price <= 350 ? 2 : (price <= 450 ? 3 : 4));
                    poolCount++;
                }
            }
        }

        /// <summary>Decides the card's result before it is shown: no win, Loop Coins, or an item the player doesn't own yet.</summary>
        private void _Roll()
        {
            int r = Random.Range(0, 100);
            int acc = 0;
            int t = 0;
            for (int k = 0; k < TIERS; k++)
            {
                acc += odds[pack * TIERS + k];
                if (r < acc)
                {
                    t = k;
                    break;
                }
            }
            prizeCat = -1;
            prizeItem = 0;
            prizePool = -1;
            prizeCoins = 0;
            allOwned = false;
            jackpot = false;
            if (t >= 2)
            {
                int tier = t - 1;
                int n = 0;
                for (int i = 0; i < poolCount; i++) if (poolTier[i] == tier && !store._OwnsItem(poolCat[i], poolItem[i])) n++;
                if (n == 0)
                {
                    allOwned = true;
                    t = 1;
                }
                else
                {
                    int k = Random.Range(0, n);
                    for (int i = 0; i < poolCount; i++)
                    {
                        if (poolTier[i] != tier || store._OwnsItem(poolCat[i], poolItem[i])) continue;
                        if (k == 0)
                        {
                            prizePool = i;
                            prizeCat = poolCat[i];
                            prizeItem = poolItem[i];
                            break;
                        }
                        k--;
                    }
                }
            }
            if (t == 1)
            {
                int total = 0;
                for (int k = 0; k < coinWeights.Length; k++) total += coinWeights[k];
                int x = Random.Range(0, total);
                prizeCoinIndex = 0;
                for (int k = 0; k < coinWeights.Length; k++)
                {
                    if (x < coinWeights[k])
                    {
                        prizeCoinIndex = k;
                        break;
                    }
                    x -= coinWeights[k];
                }
                prizeCoins = _CoinPrize(prizeCoinIndex);
                jackpot = prizeCoinIndex == coinMult.Length - 1;
            }
            prizeTier = t;
        }

        private void _Grant()
        {
            if (prizeTier == 1) store._GiveCoins(prizeCoins);
            else if (prizeTier >= 2) store._GiveItem(prizeCat, prizeItem);
        }

        /// <summary>Fills the 9 spots: a winning card shows its prize exactly 3 times; nothing else ever shows up 3 times.</summary>
        private void _Layout()
        {
            int nd = 0;
            for (int k = 0; k < coinMult.Length && nd < decoys.Length - 4; k++) decoys[nd++] = SYM_COINS + k;
            decoys[nd++] = SYM_NONE;
            for (int k = 0; k < 3 && poolCount > 0; k++) decoys[nd++] = Random.Range(0, poolCount);
            int win = _WinSymbol();
            int filled = 0;
            if (win >= 0) for (int k = 0; k < 3; k++) spotSym[filled++] = win;
            for (int guard = 0; filled < SPOTS && guard < 200; guard++)
            {
                int s = decoys[Random.Range(0, nd)];
                if (s == win || _CountSym(s, filled) >= 2) continue;
                spotSym[filled++] = s;
            }
            for (; filled < SPOTS; filled++)
            {
                spotSym[filled] = SYM_NONE;
                for (int d = 0; d < nd; d++)
                {
                    if (decoys[d] == win || _CountSym(decoys[d], filled) >= 2) continue;
                    spotSym[filled] = decoys[d];
                    break;
                }
            }
            for (int i = SPOTS - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                int tmp = spotSym[i];
                spotSym[i] = spotSym[j];
                spotSym[j] = tmp;
            }
        }

        private int _WinSymbol()
        {
            if (prizeTier == 1) return SYM_COINS + prizeCoinIndex;
            if (prizeTier >= 2) return prizePool;
            return -1;
        }

        private int _CountSym(int s, int upTo)
        {
            int n = 0;
            for (int i = 0; i < upTo; i++) if (spotSym[i] == s) n++;
            return n;
        }

        private int _CoinPrize(int k)
        {
            return packPrices[pack] * coinMult[k] / 100;
        }

        private void _StartCard()
        {
            onCard = true;
            revealed = false;
            if (cardBack != null && cardArts != null && pack < cardArts.Length) cardBack.sprite = cardArts[pack];
            if (cardRarity != null) cardRarity.text = packNames[pack] + " CARD";
            if (cardPrice != null) cardPrice.text = packPrices[pack].ToString();
            if (cardTopPrize != null) cardTopPrize.text = "TOP PRIZE\n<size=135%>" + _Num(_CoinPrize(coinMult.Length - 1)) + "</size>\nLOOP COINS";
            for (int i = 0; i < SPOTS; i++)
            {
                int s = spotSym[i];
                Sprite art;
                string label;
                if (s >= SYM_NONE)
                {
                    art = noPrizeArt;
                    label = "NO PRIZE";
                }
                else if (s >= SYM_COINS)
                {
                    art = coinArt;
                    label = _Num(_CoinPrize(s - SYM_COINS)) + "\nLOOP COINS";
                }
                else
                {
                    art = store._ArtOf(poolCat[s], poolItem[s]);
                    label = store._NameOf(poolCat[s], poolItem[s]) + "\n" + catNames[poolCat[s]];
                }
                if (spotIcons != null && i < spotIcons.Length && spotIcons[i] != null) spotIcons[i].sprite = art;
                if (spotLabels != null && i < spotLabels.Length && spotLabels[i] != null) spotLabels[i].text = label;
                if (spotGlows != null && i < spotGlows.Length && spotGlows[i] != null) spotGlows[i].SetActive(false);
                spotDone[i] = false;
            }
            perSpot = Mathf.Max(1, cells.Length / SPOTS);
            for (int i = 0; i < SPOTS; i++) spotLeft[i] = perSpot;
            spotsDone = 0;
            if (remain == null || remain.Length < cells.Length) remain = new int[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] != null) cells[i].SetActive(true);
                remain[i] = i;
            }
            remainCount = cells.Length;
            if (progressText != null) progressText.text = "SPOTS SCRATCHED  <color=#FFE14D>0 / " + SPOTS + "</color>";
            if (infoPanel != null) infoPanel.SetActive(true);
            if (wonPanel != null) wonPanel.SetActive(false);
            _ShowCard(true);
        }

        /// <summary>Every spot has been scratched: show the result. Any foil left on the card stays for the player to rub off.</summary>
        private void _Reveal()
        {
            revealed = true;
            bool win = prizeTier > 0;
            int winSym = _WinSymbol();
            if (spotGlows != null)
                for (int i = 0; i < SPOTS && i < spotGlows.Length; i++) if (spotGlows[i] != null) spotGlows[i].SetActive(win && spotSym[i] == winSym);
            if (infoPanel != null) infoPanel.SetActive(false);
            if (wonPanel != null) wonPanel.SetActive(true);
            if (wonTitle != null) wonTitle.text = win ? "<color=#FFE14D>WINNER!</color>" : "<color=#9FB3FF>NOT A WIN\nTHIS TIME</color>";
            if (wonName != null)
                wonName.text = !win ? "NO PRIZE" : (prizeTier == 1 ? _Num(prizeCoins) + " LOOP COINS" : store._NameOf(prizeCat, prizeItem));
            if (wonKind != null)
                wonKind.text = !win ? "TRY AGAIN!" : (prizeTier == 1 ? (jackpot ? "JACKPOT!" : "LOOP COINS") : tierNames[prizeTier] + " " + catNames[prizeCat]);
            if (wonNote != null)
            {
                if (!win) wonNote.text = "No 3 the same this time. Better luck next time, more rewards are waiting!";
                else if (allOwned) wonNote.text = "You already own every item of that rarity, so this card paid out in Loop Coins!";
                else wonNote.text = prizeTier == 1 ? "3 the same! Added to your Loop Coins." : "3 the same! Added to your collection!";
            }
            if (stars != null)
                for (int s = 0; s < stars.Length; s++) if (stars[s] != null) stars[s].SetActive(prizeTier >= 2 && s < prizeTier - 1);
            if (equipButton != null) equipButton.SetActive(prizeTier >= 2);
            if (equipLabel != null) equipLabel.text = "EQUIP";
            if (win && confetti != null) confetti.Emit(jackpot || prizeTier >= 4 ? 220 : 120);
            _Sfx(win ? winClip : loseClip);
            _Refresh();
        }

        // ------------------------------------------------------------ UI

        private void _ShowCard(bool card)
        {
            if (packPage != null) packPage.SetActive(!card);
            if (scratchPage != null) scratchPage.SetActive(card);
        }

        public void _Refresh()
        {
            bool ready = store != null && store._IsReady();
            if (coinsText != null) coinsText.text = ready ? _Num(store._Coins()) : "...";
            if (messageText != null) messageText.text = message;
            int have = 0;
            for (int i = 0; i < poolCount; i++) if (ready && store._OwnsItem(poolCat[i], poolItem[i])) have++;
            if (collectionIcons != null)
                for (int i = 0; i < collectionIcons.Length; i++)
                {
                    Image icon = collectionIcons[i];
                    if (icon == null) continue;
                    bool on = i < poolCount;
                    icon.gameObject.SetActive(on);
                    if (!on) continue;
                    icon.sprite = store._ArtOf(poolCat[i], poolItem[i]);
                    icon.color = ready && store._OwnsItem(poolCat[i], poolItem[i]) ? Color.white : new Color(0.25f, 0.25f, 0.35f, 0.55f);
                }
            if (collectionText != null)
                collectionText.text = poolCount > 0 && have == poolCount ? "<color=#FFE14D>COLLECTION COMPLETE!</color>  " + have + " / " + poolCount
                    : "YOUR COLLECTION  <color=#FFE14D>" + have + " / " + poolCount + "</color>";
        }

        /// <summary>1250 -> "1,250".</summary>
        private string _Num(int n)
        {
            string s = n.ToString();
            string o = "";
            for (int i = s.Length; i > 0; i -= 3) o = (i > 3 ? "," : "") + s.Substring(Mathf.Max(0, i - 3), Mathf.Min(3, i)) + o;
            return o;
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

        /// <summary>A light buzz in the pointing hand while scratching (VR only).</summary>
        private void _Buzz()
        {
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me) || !me.IsUserInVR()) return;
            me.PlayHapticEventInHand(leftHand ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right, 0.05f, 0.2f, 160f);
        }
    }
}
