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
    /// LoopLand Scratch Cards: buy a pack with Loop Coins, rub the silver foil with your pointer to scratch it off, and win
    /// Loop Coins or a store item (dice, token, building style or trail) you don't own yet. Once every item of the rolled
    /// rarity is owned the card pays Loop Coins instead, and coin prizes average below the pack price, so cards can't be
    /// farmed for coins. The prize is granted the moment the card is bought, so leaving mid-scratch never loses it.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LoopLandScratch : UdonSharpBehaviour
    {
        public LoopLandStore store;

        [Header("Packs: common, rare, epic, legendary")]
        public string[] packNames;
        public int[] packPrices;
        public Color[] packColors;
        public int[] odds;          // 5 per pack, in percent: coins, common, rare, epic, legendary item
        public int jackpotChance = 3; // percent of coin prizes that pay the jackpot (pack price x 3)
        public int[] coinMin;
        public int[] coinMax;
        public GameObject[] packGlows;

        [Header("Pages")]
        public GameObject packPage;
        public GameObject scratchPage;
        public GameObject infoPanel;
        public GameObject wonPanel;

        [Header("Card")]
        public Image cardBack;
        public TMP_Text cardRarity;
        public Image prizeArt;
        public TMP_Text prizeName;
        public GameObject[] cells;      // foil flakes: each one hides itself (Selectable + Animator) when the pointer rubs over it
        [Range(0.3f, 1f)] public float revealAt = 0.7f;
        public TMP_Text progressText;
        public ParticleSystem dust;
        public Sprite coinArt;

        [Header("Result")]
        public TMP_Text wonName;
        public TMP_Text wonKind;
        public TMP_Text wonNote;
        public GameObject[] stars;
        public GameObject equipButton;
        public TMP_Text equipLabel;

        [Header("Desk and collection")]
        public TMP_Text buyLabel;
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
        public AudioClip winClip;
        public ParticleSystem confetti;

        private const int TIERS = 5;
        private string[] tierNames = { "LOOP COINS", "COMMON", "RARE", "EPIC", "LEGENDARY" };
        private string[] catNames = { "DICE", "TOKEN", "BUILDING STYLE", "TRAIL" };

        private int pack;
        private bool onCard;
        private bool revealed;
        private int[] remain;
        private int remainCount;
        private int scratched;
        private float nextPoll;
        private float nextScratch;
        private bool leftHand;
        private int prizeCat = -1;
        private int prizeItem;
        private int prizeCoins;
        private int prizeTier;
        private bool allOwned;
        private bool jackpot;
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

        // ------------------------------------------------------------ buttons (UI calls these)

        public void _OnPack0() { _SelectPack(0); }
        public void _OnPack1() { _SelectPack(1); }
        public void _OnPack2() { _SelectPack(2); }
        public void _OnPack3() { _SelectPack(3); }

        public void _OnBuy()
        {
            if (store == null) return;
            if (onCard && !revealed) { _Fail("Scratch your card first!"); return; }
            if (!store._IsReady()) { _Fail("Your save is still loading..."); return; }
            if (poolCount == 0) _BuildPool();
            int price = packPrices[pack];
            if (!store._SpendCoins(price)) { _Fail("You need " + (price - store._Coins()) + " more Loop Coins."); return; }
            _Roll();
            _Grant();
            _StartCard();
            message = "";
            _Sfx(buyClip);
            _Refresh();
        }

        public override void InputUse(bool value, UdonInputEventArgs args)
        {
            if (value) leftHand = args.handType == HandType.LEFT; // the hand that last clicked is the one pointing
        }

        /// <summary>Watches the foil flakes: the UI hides each one the moment the pointer rubs over it.</summary>
        private void Update()
        {
            if (!onCard || revealed || cells == null || Time.time < nextPoll) return;
            nextPoll = Time.time + 0.05f;
            int fresh = 0;
            for (int k = remainCount - 1; k >= 0; k--)
            {
                GameObject cell = cells[remain[k]];
                if (cell != null && cell.activeSelf) continue;
                if (fresh < 3 && cell != null && dust != null)
                {
                    dust.transform.position = cell.transform.position;
                    dust.Emit(5);
                }
                fresh++;
                remainCount--;
                remain[k] = remain[remainCount];
            }
            if (fresh == 0) return;
            scratched += fresh;
            if (Time.time >= nextScratch)
            {
                nextScratch = Time.time + 0.09f;
                _Sfx(scratchClip);
                _Buzz();
            }
            float done = scratched / (float)cells.Length;
            if (progressText != null) progressText.text = "SCRATCHED <color=#FFE14D>" + Mathf.FloorToInt(done * 100f) + "%</color>";
            if (done >= revealAt) _Reveal();
        }

        public void _OnRevealAll()
        {
            if (onCard && !revealed) _Reveal();
        }

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

        // ------------------------------------------------------------ game logic

        private void _SelectPack(int p)
        {
            if (onCard || packPrices == null || p < 0 || p >= packPrices.Length) return;
            pack = p;
            message = "";
            _Sfx(clickClip);
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

        private void _Roll()
        {
            int r = Random.Range(0, 100);
            int acc = 0;
            prizeTier = 0;
            for (int t = 0; t < TIERS; t++)
            {
                acc += odds[pack * TIERS + t];
                if (r < acc)
                {
                    prizeTier = t;
                    break;
                }
            }
            prizeCat = -1;
            prizeItem = 0;
            prizeCoins = 0;
            allOwned = false;
            jackpot = false;
            if (prizeTier > 0)
            {
                // only items the player doesn't own yet can be won
                int n = 0;
                for (int i = 0; i < poolCount; i++) if (poolTier[i] == prizeTier && !store._OwnsItem(poolCat[i], poolItem[i])) n++;
                if (n == 0)
                {
                    allOwned = true;
                    prizeTier = 0;
                }
                else
                {
                    int k = Random.Range(0, n);
                    for (int i = 0; i < poolCount; i++)
                    {
                        if (poolTier[i] != prizeTier || store._OwnsItem(poolCat[i], poolItem[i])) continue;
                        if (k == 0)
                        {
                            prizeCat = poolCat[i];
                            prizeItem = poolItem[i];
                            break;
                        }
                        k--;
                    }
                }
            }
            if (prizeTier == 0)
            {
                prizeCoins = Random.Range(coinMin[pack], coinMax[pack] + 1) / 5 * 5;
                jackpot = Random.Range(0, 100) < jackpotChance;
                if (jackpot) prizeCoins = packPrices[pack] * 3;
            }
        }

        private void _Grant()
        {
            if (prizeCat < 0) store._GiveCoins(prizeCoins);
            else store._GiveItem(prizeCat, prizeItem);
        }

        private void _StartCard()
        {
            onCard = true;
            revealed = false;
            if (cardBack != null) cardBack.color = packColors[pack];
            if (cardRarity != null) cardRarity.text = packNames[pack] + " CARD";
            if (prizeArt != null) prizeArt.sprite = prizeCat < 0 ? coinArt : store._ArtOf(prizeCat, prizeItem);
            if (prizeName != null) prizeName.text = prizeCat < 0 ? "+" + prizeCoins + " COINS" : store._NameOf(prizeCat, prizeItem);
            if (remain == null || remain.Length < cells.Length) remain = new int[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] != null) cells[i].SetActive(true);
                remain[i] = i;
            }
            remainCount = cells.Length;
            scratched = 0;
            if (progressText != null) progressText.text = "SCRATCHED <color=#FFE14D>0%</color>";
            if (infoPanel != null) infoPanel.SetActive(true);
            if (wonPanel != null) wonPanel.SetActive(false);
            _ShowCard(true);
        }

        private void _Reveal()
        {
            revealed = true;
            for (int i = 0; i < cells.Length; i++) if (cells[i] != null) cells[i].SetActive(false);
            remainCount = 0;
            if (infoPanel != null) infoPanel.SetActive(false);
            if (wonPanel != null) wonPanel.SetActive(true);
            if (wonName != null) wonName.text = prizeCat < 0 ? prizeCoins + " LOOP COINS" : store._NameOf(prizeCat, prizeItem);
            if (wonKind != null) wonKind.text = prizeCat < 0 ? (jackpot ? "JACKPOT!" : "LOOP COINS") : tierNames[prizeTier] + " " + catNames[prizeCat];
            if (wonNote != null)
            {
                if (allOwned) wonNote.text = "You already own every item of that rarity, so this card paid out in Loop Coins!";
                else wonNote.text = prizeCat < 0 ? "Added to your Loop Coins." : "Added to your collection!";
            }
            if (stars != null)
                for (int s = 0; s < stars.Length; s++) if (stars[s] != null) stars[s].SetActive(prizeCat >= 0 && s < prizeTier);
            if (equipButton != null) equipButton.SetActive(prizeCat >= 0);
            if (equipLabel != null) equipLabel.text = "EQUIP";
            if (confetti != null) confetti.Emit(jackpot || prizeTier >= 3 ? 220 : 120);
            _Sfx(winClip);
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
            if (coinsText != null) coinsText.text = ready ? store._Coins().ToString() : "...";
            if (packGlows != null)
                for (int i = 0; i < packGlows.Length; i++) if (packGlows[i] != null) packGlows[i].SetActive(i == pack);
            if (buyLabel != null)
                buyLabel.text = onCard && !revealed ? "SCRATCH YOUR CARD!" : "BUY " + packNames[pack] + " CARD\n<size=70%>" + packPrices[pack] + " LOOP COINS</size>";
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
