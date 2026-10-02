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
    /// LoopLand Scratch Cards: real scratch cards. Buy a Common, Rare, Epic or Legendary card on the screen and it lands
    /// on the counter. Scratch the silver off its 9 spots yourself: in VR rub it with your finger or hand, on desktop hold
    /// left click and look across it. Find 3 the same and you win that prize (Loop Coins or a store item you don't own
    /// yet); otherwise the card doesn't win. Scratching is tracked here in Udon (fingertip, hand or view ray against the
    /// card), so it does not depend on UI hover events. Coin prizes are worth less than the cards on average, so cards
    /// can't be farmed for coins. The prize is saved the moment the card is bought. Everything is local to the player.
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
        public GameObject deskIdle;
        public GameObject deskCard;

        [Header("Card on the counter")]
        public Transform cardSpace;     // the card's RectTransform: 1 unit = 1 mm, +Z points into the counter
        public Image cardBack;
        public TMP_Text cardRarity;
        public TMP_Text cardPrice;
        public TMP_Text cardTopPrize;
        public Image[] spotIcons;
        public TMP_Text[] spotLabels;
        public GameObject[] spotGlows;
        public GameObject[] cells;      // foil flakes, spot by spot
        [Range(0.3f, 1f)] public float spotRevealAt = 0.6f;
        public float brush = 34f;       // scratch radius in mm
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
        private bool[] spotDone = new bool[SPOTS];
        private int[] spotGone = new int[SPOTS];
        private float[] spotMinX = new float[SPOTS];
        private float[] spotMaxX = new float[SPOTS];
        private float[] spotMinY = new float[SPOTS];
        private float[] spotMaxY = new float[SPOTS];
        private int spotsDone;
        private int perSpot = 1;
        private int needPerSpot = 1;
        private int[] decoys = new int[16];
        private float[] flakeX;
        private float[] flakeY;
        private bool[] gone;

        // scratching: 0 left hand, 1 right hand, 2 desktop view
        private float[] lastX = new float[3];
        private float[] lastY = new float[3];
        private float[] lastT = new float[3];
        private bool useHeld;
        private int removedNow;
        private int scratchHand;
        private float dustX;
        private float dustY;
        private bool newSpot;
        private float nextScratch;

        private int[] poolCat = new int[64];
        private int[] poolItem = new int[64];
        private int[] poolTier = new int[64];
        private int poolCount;
        private string message = "";

        private void Start()
        {
            _BuildPool();
            _CacheFlakes();
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
            useHeld = value;
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
            if (progressText != null) progressText.text = "";
            _ShowCard(false);
            _Sfx(clickClip);
            _Refresh();
        }

        // ------------------------------------------------------------ scratching

        /// <summary>Remembers where every foil flake sits on the card (in mm), so scratching is plain distance maths.</summary>
        private void _CacheFlakes()
        {
            if (cells == null || cardSpace == null) return;
            int n = cells.Length;
            flakeX = new float[n];
            flakeY = new float[n];
            gone = new bool[n];
            perSpot = Mathf.Max(1, n / SPOTS);
            needPerSpot = Mathf.Max(1, Mathf.CeilToInt(perSpot * spotRevealAt));
            for (int s = 0; s < SPOTS; s++)
            {
                spotMinX[s] = 100000f;
                spotMaxX[s] = -100000f;
                spotMinY[s] = 100000f;
                spotMaxY[s] = -100000f;
            }
            for (int i = 0; i < n; i++)
            {
                if (cells[i] == null) continue;
                Vector3 p = cardSpace.InverseTransformPoint(cells[i].transform.position);
                flakeX[i] = p.x;
                flakeY[i] = p.y;
                int s = i / perSpot;
                if (s >= SPOTS) continue;
                spotMinX[s] = Mathf.Min(spotMinX[s], p.x);
                spotMaxX[s] = Mathf.Max(spotMaxX[s], p.x);
                spotMinY[s] = Mathf.Min(spotMinY[s], p.y);
                spotMaxY[s] = Mathf.Max(spotMaxY[s], p.y);
            }
        }

        private void Update()
        {
            if (!onCard || cardSpace == null || flakeX == null) return;
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me)) return;
            removedNow = 0;
            if (me.IsUserInVR())
            {
                _Hand(me, 0);
                _Hand(me, 1);
            }
            else if (useHeld) _View(me);
            else lastT[2] = 0f;
            if (removedNow > 0) _AfterScratch();
        }

        /// <summary>VR: the index fingertip (or the hand, if the avatar has no finger bones) scratches when it touches the card.</summary>
        private void _Hand(VRCPlayerApi me, int h)
        {
            bool left = h == 0;
            Vector3 p = me.GetBonePosition(left ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal);
            float reach = 70f;
            if (p == Vector3.zero)
            {
                p = me.GetTrackingData(left ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand).position;
                reach = 130f;
            }
            Vector3 lp = cardSpace.InverseTransformPoint(p);
            if (lp.z < -reach || lp.z > 80f)
            {
                lastT[h] = 0f;
                return;
            }
            _StrokeTo(h, lp.x, lp.y);
        }

        /// <summary>Desktop: while left click is held, the spot in the middle of the view scratches.</summary>
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
            if (t < 0f || t > 3000f)
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
            float r2 = brush * brush;
            for (int s = 0; s < SPOTS; s++)
            {
                if (x < spotMinX[s] - brush || x > spotMaxX[s] + brush || y < spotMinY[s] - brush || y > spotMaxY[s] + brush) continue;
                int end = Mathf.Min((s + 1) * perSpot, flakeX.Length);
                for (int i = s * perSpot; i < end; i++)
                {
                    if (gone[i]) continue;
                    float ex = flakeX[i] - x;
                    float ey = flakeY[i] - y;
                    if (ex * ex + ey * ey > r2) continue;
                    gone[i] = true;
                    if (cells[i] != null) cells[i].SetActive(false);
                    removedNow++;
                    scratchHand = h;
                    dustX = flakeX[i];
                    dustY = flakeY[i];
                    spotGone[s]++;
                    if (!spotDone[s] && spotGone[s] >= needPerSpot)
                    {
                        spotDone[s] = true;
                        spotsDone++;
                        newSpot = true;
                    }
                }
            }
        }

        private void _AfterScratch()
        {
            if (dust != null)
            {
                dust.transform.position = cardSpace.TransformPoint(new Vector3(dustX, dustY, -8f));
                dust.Emit(Mathf.Min(8, removedNow * 2));
            }
            if (Time.time >= nextScratch)
            {
                nextScratch = Time.time + 0.09f;
                _Sfx(scratchClip);
                _Buzz(scratchHand);
            }
            if (newSpot)
            {
                newSpot = false;
                _Sfx(spotClip);
            }
            if (revealed) return;
            if (progressText != null) progressText.text = "SPOTS\n<size=140%><color=#FFE14D>" + spotsDone + " / " + SPOTS + "</color></size>";
            if (spotsDone >= SPOTS) _Reveal();
        }

        // ------------------------------------------------------------ game logic

        private void _Buy(int p)
        {
            if (store == null || packPrices == null || p < 0 || p >= packPrices.Length) return;
            if (onCard && !revealed) { _Fail("Finish scratching your card first!"); return; }
            if (!store._IsReady()) { _Fail("Your save is still loading..."); return; }
            if (poolCount == 0) _BuildPool();
            if (flakeX == null) _CacheFlakes();
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
                spotGone[i] = 0;
            }
            spotsDone = 0;
            if (cells != null)
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i] != null) cells[i].SetActive(true);
                    if (gone != null && i < gone.Length) gone[i] = false;
                }
            for (int h = 0; h < 3; h++) lastT[h] = 0f;
            if (progressText != null) progressText.text = "SPOTS\n<size=140%><color=#FFE14D>0 / " + SPOTS + "</color></size>";
            if (infoPanel != null) infoPanel.SetActive(true);
            if (wonPanel != null) wonPanel.SetActive(false);
            _ShowCard(true);
        }

        /// <summary>Every spot has been scratched: show the result. Any foil left on the card can still be rubbed off.</summary>
        private void _Reveal()
        {
            revealed = true;
            bool win = prizeTier > 0;
            int winSym = _WinSymbol();
            if (spotGlows != null)
                for (int i = 0; i < SPOTS && i < spotGlows.Length; i++) if (spotGlows[i] != null) spotGlows[i].SetActive(win && spotSym[i] == winSym);
            if (infoPanel != null) infoPanel.SetActive(false);
            if (wonPanel != null) wonPanel.SetActive(true);
            if (progressText != null) progressText.text = win ? "<color=#FFE14D>WINNER!</color>" : "<color=#9FB3FF>NO WIN</color>";
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
            if (deskIdle != null) deskIdle.SetActive(!card);
            if (deskCard != null) deskCard.SetActive(card);
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

        /// <summary>A light buzz in the hand that is scratching (VR only).</summary>
        private void _Buzz(int h)
        {
            if (h > 1) return;
            VRCPlayerApi me = Networking.LocalPlayer;
            if (!Utilities.IsValid(me) || !me.IsUserInVR()) return;
            me.PlayHapticEventInHand(h == 0 ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right, 0.05f, 0.25f, 160f);
        }
    }
}
