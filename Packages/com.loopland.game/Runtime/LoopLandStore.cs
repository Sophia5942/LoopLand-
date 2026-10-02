using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.Economy;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace LoopLand
{
    /// <summary>
    /// LoopLand Store: persistent Loop Coins (PlayerData), cosmetic dice / tokens / buildings / trails bought with coins,
    /// and VRChat Creator Economy products (premium packs, VIP, coin packs) bought with VRChat Credits.
    /// Equipped cosmetics live in PlayerData, so every player sees everyone else's skins.
    /// Categories: 0 dice, 1 tokens, 2 buildings, 3 trails. Tab 4 is the premium (Credits) tab.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LoopLandStore : UdonSharpBehaviour
    {
        public LoopLandGame game;

        [Header("Dice skins (index 0 is free). Product = index into Products, -1 = buy with coins")]
        public string[] diceNames;
        public int[] dicePrices;
        public int[] diceProduct;
        public Material[] diceMaterials;
        public Color[] diceGlow;
        public string[] diceDesc;
        public Sprite[] diceArt;

        [Header("Token skins")]
        public string[] tokenNames;
        public int[] tokenPrices;
        public int[] tokenProduct;
        public Color[] tokenColors;
        public string[] tokenDesc;
        public Sprite[] tokenArt;
        public int rainbowToken = -1;

        [Header("Building styles (the Loops and Towers on your properties)")]
        public string[] buildingNames;
        public int[] buildingPrices;
        public int[] buildingProduct;
        public Color[] buildingColors;
        public string[] buildingDesc;
        public Sprite[] buildingArt;

        [Header("Particle trails")]
        public string[] trailNames;
        public int[] trailPrices;
        public int[] trailProduct;
        public Color[] trailColors;
        public string[] trailDesc;
        public Sprite[] trailArt;
        public int rainbowTrail = -1;

        [Header("Creator Economy: assign UdonProduct assets and listing IDs (prod_...)")]
        public UdonProduct[] products;
        public string[] productNames;
        public string[] productDescriptions;
        public string[] productListingIds;
        public string[] productPriceLabels;
        [Tooltip("Loop Coins granted per purchase (instant listings with quantity). 0 for unlock products.")]
        public int[] productCoins;
        public Sprite[] productArt;
        public int vipProduct = 2;
        public int vipMultiplier = 2;

        [Header("Economy")]
        public int startingCoins = 250;
        public int dailyBonus = 50;

        [Header("UI (auto-wired by the builder)")]
        public TMP_Text coinsText;
        public TMP_Text vipText;
        public TMP_Text[] tabLabels;
        public GameObject[] tabSelected;
        public GameObject[] itemButtons;
        public GameObject[] itemSelected;
        public Image[] itemIcons;
        public TMP_Text[] itemNames;
        public Image[] itemPills;
        public TMP_Text[] itemStatus;
        public TMP_Text detailName;
        public TMP_Text detailRarity;
        public Image detailRarityPill;
        public TMP_Text detailDesc;
        public Image detailPreview;
        public TMP_Text actionLabel;
        public TMP_Text bannerText;
        [Header("Store pop-up at each console (local to the player who opens it)")]
        public Transform storePanel;
        public Transform[] storeSpots;
        public AudioSource sfx;
        public AudioClip buyClip;
        public AudioClip errorClip;
        public AudioClip clickClip;

        [HideInInspector] public int pressedArg;

        private const int PREMIUM_TAB = 4;
        private const string K_COINS = "ll_coins";
        private const string K_MATCH = "ll_match";
        private const string K_MATCH_GOT = "ll_match_got";
        private const string K_DAILY = "ll_daily";
        private string[] kOwn = { "ll_own_dice", "ll_own_token", "ll_own_building", "ll_own_trail" };
        private string[] kEq = { "ll_eq_dice", "ll_eq_token", "ll_eq_building", "ll_eq_trail" };

        private bool restored;
        private bool[] owned = new bool[0];
        private int pendingCoins;
        private int pendingMatch;
        private int pendingEarned;
        private int tab;
        private int sel;
        private string message = "";
        private Transform homeParent;
        private Vector3 homePos;
        private Quaternion homeRot;
        private int openSeat = -1;

        private void Start()
        {
            owned = new bool[products == null ? 0 : products.Length];
            if (storePanel != null)
            {
                homeParent = storePanel.parent;
                homePos = storePanel.localPosition;
                homeRot = storePanel.localRotation;
            }
            _RefreshUI();
        }

        // ------------------------------------------------------------ persistence & CE events

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            restored = true;
            if (!PlayerData.HasKey(player, K_COINS)) PlayerData.SetInt(K_COINS, startingCoins);
            if (pendingCoins > 0) { int c = pendingCoins; pendingCoins = 0; _AddCoins(c); }
            if (pendingMatch != 0) _ApplyMatchReward(pendingMatch, pendingEarned);
            _RefreshOwned();
            sel = _DefaultSel(tab);
            _RefreshUI();
            if (game != null) game._OnCosmeticsChanged();
        }

        public override void OnPlayerDataUpdated(VRCPlayerApi player, PlayerData.Info[] infos)
        {
            if (game != null) game._OnCosmeticsChanged();
            if (Utilities.IsValid(player) && player.isLocal) _RefreshUI();
        }

        public override void OnPurchasesLoaded(IProduct[] result, VRCPlayerApi player)
        {
            if (Utilities.IsValid(player) && player.isLocal) { _RefreshOwned(); _ValidateEquips(); _RefreshUI(); }
            if (game != null) game._OnCosmeticsChanged();
        }

        public override void OnPurchaseConfirmedMultiple(IProduct result, VRCPlayerApi player, bool purchased, int quantity)
        {
            if (Utilities.IsValid(player) && player.isLocal)
            {
                _RefreshOwned();
                int idx = _ProductIndex(result);
                if (purchased && idx >= 0)
                {
                    int grant = productCoins != null && idx < productCoins.Length ? productCoins[idx] : 0;
                    if (grant > 0) _AddCoins(grant * Mathf.Max(1, quantity));
                    message = "<color=#FFE14D>Thank you for supporting LoopLand!</color> " + _ProductName(idx) + (grant > 0 ? " (+" + (grant * Mathf.Max(1, quantity)) + " coins)" : " unlocked!");
                    _Sfx(buyClip);
                }
                _RefreshUI();
            }
            if (game != null) game._OnCosmeticsChanged();
        }

        public override void OnPurchaseExpired(IProduct result, VRCPlayerApi player)
        {
            if (Utilities.IsValid(player) && player.isLocal) { _RefreshOwned(); _ValidateEquips(); _RefreshUI(); }
            if (game != null) game._OnCosmeticsChanged();
        }

        private void _RefreshOwned()
        {
            if (products == null) return;
            if (owned.Length != products.Length) owned = new bool[products.Length];
            VRCPlayerApi lp = Networking.LocalPlayer;
            for (int i = 0; i < products.Length; i++) owned[i] = products[i] != null && Utilities.IsValid(lp) && Store.DoesPlayerOwnProduct(lp, products[i]);
        }

        private int _ProductIndex(IProduct p)
        {
            if (products == null || p == null) return -1;
            for (int i = 0; i < products.Length; i++) if (products[i] != null && products[i].ID == p.ID) return i;
            return -1;
        }

        private void _ValidateEquips()
        {
            if (!restored) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            for (int cat = 0; cat < 4; cat++)
            {
                int eq = PlayerData.GetInt(lp, kEq[cat]);
                if (eq != 0 && !_Owns(cat, eq)) PlayerData.SetInt(kEq[cat], 0);
            }
        }

        // ------------------------------------------------------------ coins

        public int _Coins()
        {
            VRCPlayerApi lp = Networking.LocalPlayer;
            return restored && Utilities.IsValid(lp) ? PlayerData.GetInt(lp, K_COINS) : 0;
        }

        public bool _IsVip()
        {
            return vipProduct >= 0 && vipProduct < owned.Length && owned[vipProduct];
        }

        private void _AddCoins(int n)
        {
            if (!restored) { pendingCoins += n; return; }
            PlayerData.SetInt(K_COINS, _Coins() + n);
        }

        /// <summary>Called by the game with this player's cumulative coins earned in a match. Applied exactly once.</summary>
        public void _ApplyMatchReward(int match, int earned)
        {
            if (!restored) { pendingMatch = match; pendingEarned = earned; return; }
            pendingMatch = 0;
            VRCPlayerApi lp = Networking.LocalPlayer;
            int got = PlayerData.GetInt(lp, K_MATCH) == match ? PlayerData.GetInt(lp, K_MATCH_GOT) : 0;
            int delta = earned - got;
            if (delta <= 0) return;
            int gain = delta * (_IsVip() ? Mathf.Max(1, vipMultiplier) : 1);
            PlayerData.SetInt(K_MATCH, match);
            PlayerData.SetInt(K_MATCH_GOT, earned);
            PlayerData.SetInt(K_COINS, _Coins() + gain);
            message = "<color=#7CFF4F>+" + gain + " Loop Coins earned!</color>";
            _RefreshUI();
        }

        public void _OnDaily()
        {
            if (!restored) { _Fail("Your save is still loading..."); return; }
            int day = (int)(Networking.GetNetworkDateTime().Ticks / 864000000000L);
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (PlayerData.GetInt(lp, K_DAILY) == day) { _Fail("Daily bonus already claimed. Come back tomorrow!"); return; }
            int gain = dailyBonus * (_IsVip() ? Mathf.Max(1, vipMultiplier) : 1);
            PlayerData.SetInt(K_DAILY, day);
            PlayerData.SetInt(K_COINS, _Coins() + gain);
            message = "<color=#7CFF4F>Daily bonus: +" + gain + " Loop Coins!</color>";
            _Sfx(buyClip);
            _RefreshUI();
        }

        public void _OnWorldStore()
        {
            _Sfx(clickClip);
            Store.OpenWorldStorePage();
        }

        // ------------------------------------------------------------ store pop-up (only moves the local player's copy)

        public void _OpenStore0() { _ToggleStoreAt(0); }
        public void _OpenStore1() { _ToggleStoreAt(1); }
        public void _OpenStore2() { _ToggleStoreAt(2); }
        public void _OpenStore3() { _ToggleStoreAt(3); }
        public void _CloseStore() { _ToggleStoreAt(-1); }

        private void _ToggleStoreAt(int seat)
        {
            if (storePanel == null || homeParent == null) return;
            bool valid = seat >= 0 && storeSpots != null && seat < storeSpots.Length && storeSpots[seat] != null;
            if (!valid || seat == openSeat)
            {
                storePanel.SetParent(homeParent, false);
                storePanel.localPosition = homePos;
                storePanel.localRotation = homeRot;
                openSeat = -1;
            }
            else
            {
                storePanel.SetParent(storeSpots[seat], false);
                storePanel.localPosition = Vector3.zero;
                storePanel.localRotation = Quaternion.identity;
                openSeat = seat;
            }
            if (game != null) game._StoreSeatChanged(openSeat);
            _Sfx(clickClip);
        }

        // ------------------------------------------------------------ scratch cards (LoopLandScratch uses these)

        public bool _IsReady() { return restored; }
        public int _ItemCountOf(int cat) { return _Count(cat); }
        public int _PriceOf(int cat, int i) { return _ItemPrice(cat, i); }
        public int _ProductOf(int cat, int i) { return _ItemProduct(cat, i); }
        public string _NameOf(int cat, int i) { return _ItemName(cat, i); }
        public Sprite _ArtOf(int cat, int i) { return _ItemArt(cat, i); }
        public bool _OwnsItem(int cat, int i) { return _Owns(cat, i); }

        public bool _SpendCoins(int n)
        {
            if (!restored || n < 0 || _Coins() < n) return false;
            PlayerData.SetInt(K_COINS, _Coins() - n);
            _RefreshUI();
            return true;
        }

        public void _GiveCoins(int n)
        {
            if (n <= 0) return;
            _AddCoins(n);
            _RefreshUI();
        }

        public void _GiveItem(int cat, int i)
        {
            if (!restored || cat < 0 || cat > 3 || i <= 0 || i >= _Count(cat)) return;
            PlayerData.SetInt(kOwn[cat], PlayerData.GetInt(Networking.LocalPlayer, kOwn[cat]) | (1 << i));
            _RefreshUI();
        }

        public void _EquipItem(int cat, int i)
        {
            if (!restored || cat < 0 || cat > 3 || !_Owns(cat, i)) return;
            PlayerData.SetInt(kEq[cat], i);
            _RefreshUI();
            if (game != null) game._OnCosmeticsChanged();
        }

        // ------------------------------------------------------------ catalogue (also used by the game)

        public int _GetEquipped(VRCPlayerApi p, int cat)
        {
            if (!Utilities.IsValid(p) || cat < 0 || cat > 3) return 0;
            int v = PlayerData.GetInt(p, kEq[cat]);
            if (v <= 0 || v >= _Count(cat)) return 0;
            int prod = _ItemProduct(cat, v);
            if (prod >= 0 && (products == null || prod >= products.Length || products[prod] == null || !Store.DoesPlayerOwnProduct(p, products[prod]))) return 0;
            return v;
        }

        public Material _DiceMaterial(int i) { return diceMaterials != null && i >= 0 && i < diceMaterials.Length ? diceMaterials[i] : null; }
        public Color _DiceGlow(int i) { return diceGlow != null && i >= 0 && i < diceGlow.Length ? diceGlow[i] : Color.white; }
        public Color _TokenColor(int i) { return tokenColors != null && i >= 0 && i < tokenColors.Length ? tokenColors[i] : Color.white; }
        public Color _TokenGlow(int i, Color fallback) { return i <= 0 ? fallback : _TokenColor(i); }
        public Color _TrailColor(int i) { return trailColors != null && i >= 0 && i < trailColors.Length ? trailColors[i] : Color.white; }
        public Color _BuildingColor(int i) { return buildingColors != null && i >= 0 && i < buildingColors.Length ? buildingColors[i] : Color.white; }
        public bool _IsRainbowToken(int i) { return i > 0 && i == rainbowToken; }
        public bool _IsRainbowTrail(int i) { return i > 0 && i == rainbowTrail; }

        private int _Count(int cat)
        {
            string[] a = cat == 0 ? diceNames : (cat == 1 ? tokenNames : (cat == 2 ? buildingNames : (cat == 3 ? trailNames : productNames)));
            return a == null ? 0 : a.Length;
        }

        private string _ItemName(int cat, int i)
        {
            string[] a = cat == 0 ? diceNames : (cat == 1 ? tokenNames : (cat == 2 ? buildingNames : (cat == 3 ? trailNames : productNames)));
            return a != null && i >= 0 && i < a.Length ? a[i] : "";
        }

        private string _ItemDesc(int cat, int i)
        {
            string[] a = cat == 0 ? diceDesc : (cat == 1 ? tokenDesc : (cat == 2 ? buildingDesc : (cat == 3 ? trailDesc : productDescriptions)));
            return a != null && i >= 0 && i < a.Length ? a[i] : "";
        }

        private Sprite _ItemArt(int cat, int i)
        {
            Sprite[] a = cat == 0 ? diceArt : (cat == 1 ? tokenArt : (cat == 2 ? buildingArt : (cat == 3 ? trailArt : productArt)));
            return a != null && i >= 0 && i < a.Length ? a[i] : null;
        }

        private int _ItemPrice(int cat, int i)
        {
            int[] a = cat == 0 ? dicePrices : (cat == 1 ? tokenPrices : (cat == 2 ? buildingPrices : trailPrices));
            return a != null && i >= 0 && i < a.Length ? a[i] : 0;
        }

        private int _ItemProduct(int cat, int i)
        {
            if (cat == PREMIUM_TAB) return i;
            int[] a = cat == 0 ? diceProduct : (cat == 1 ? tokenProduct : (cat == 2 ? buildingProduct : trailProduct));
            return a != null && i >= 0 && i < a.Length ? a[i] : -1;
        }

        private string _ProductName(int i)
        {
            return productNames != null && i >= 0 && i < productNames.Length ? productNames[i] : "Premium item";
        }

        private bool _IsCoinPack(int prod)
        {
            return productCoins != null && prod >= 0 && prod < productCoins.Length && productCoins[prod] > 0;
        }

        private bool _Owns(int cat, int i)
        {
            if (cat == PREMIUM_TAB) return i < owned.Length && owned[i];
            if (i == 0) return true;
            int prod = _ItemProduct(cat, i);
            if (prod >= 0) return prod < owned.Length && owned[prod];
            if (!restored) return false;
            return ((PlayerData.GetInt(Networking.LocalPlayer, kOwn[cat]) >> i) & 1) == 1;
        }

        private int _Equipped(int cat)
        {
            if (cat >= PREMIUM_TAB || !restored) return 0;
            return Mathf.Max(0, PlayerData.GetInt(Networking.LocalPlayer, kEq[cat]));
        }

        private int _DefaultSel(int t)
        {
            return Mathf.Clamp(_Equipped(t), 0, Mathf.Max(0, _Count(t) - 1));
        }

        // ------------------------------------------------------------ UI events (UI buttons call these)

        public void _OnTab0() { pressedArg = 0; _OnTab(); }
        public void _OnTab1() { pressedArg = 1; _OnTab(); }
        public void _OnTab2() { pressedArg = 2; _OnTab(); }
        public void _OnTab3() { pressedArg = 3; _OnTab(); }
        public void _OnTab4() { pressedArg = 4; _OnTab(); }
        public void _OnItem0() { pressedArg = 0; _OnItem(); }
        public void _OnItem1() { pressedArg = 1; _OnItem(); }
        public void _OnItem2() { pressedArg = 2; _OnItem(); }
        public void _OnItem3() { pressedArg = 3; _OnItem(); }
        public void _OnItem4() { pressedArg = 4; _OnItem(); }
        public void _OnItem5() { pressedArg = 5; _OnItem(); }
        public void _OnItem6() { pressedArg = 6; _OnItem(); }
        public void _OnItem7() { pressedArg = 7; _OnItem(); }

        public void _OnTab()
        {
            tab = Mathf.Clamp(pressedArg, 0, PREMIUM_TAB);
            sel = _DefaultSel(tab);
            message = "";
            _Sfx(clickClip);
            _RefreshUI();
        }

        public void _OnItem()
        {
            if (pressedArg < 0 || pressedArg >= _Count(tab)) return;
            sel = pressedArg;
            message = "";
            _Sfx(clickClip);
            _RefreshUI();
        }

        public void _OnAction()
        {
            if (sel < 0 || sel >= _Count(tab)) { _Fail("Pick an item first."); return; }
            int prod = _ItemProduct(tab, sel);
            if (tab == PREMIUM_TAB || (prod >= 0 && !_Owns(tab, sel)))
            {
                if (products == null || prod >= products.Length || products[prod] == null) { _Fail("This premium item isn't set up yet (see README)."); return; }
                if (tab == PREMIUM_TAB && _Owns(PREMIUM_TAB, prod) && !_IsCoinPack(prod)) { _Fail("You already own this. Enjoy!"); return; }
                string id = productListingIds != null && prod < productListingIds.Length ? productListingIds[prod] : "";
                _Sfx(clickClip);
                if (id != null && id.Length > 0) Store.OpenListing(id);
                else Store.OpenWorldStorePage();
                return;
            }
            if (!restored) { _Fail("Your save is still loading..."); return; }
            if (_Owns(tab, sel))
            {
                PlayerData.SetInt(kEq[tab], sel);
                message = "<color=#7CFF4F>Equipped " + _ItemName(tab, sel) + "!</color>";
                _Sfx(clickClip);
                _RefreshUI();
                return;
            }
            int price = _ItemPrice(tab, sel);
            int coins = _Coins();
            if (coins < price) { _Fail("You need " + (price - coins) + " more Loop Coins. Play games, claim the daily bonus or grab a coin pack!"); return; }
            int mask = PlayerData.GetInt(Networking.LocalPlayer, kOwn[tab]);
            PlayerData.SetInt(K_COINS, coins - price);
            PlayerData.SetInt(kOwn[tab], mask | (1 << sel));
            PlayerData.SetInt(kEq[tab], sel);
            message = "<color=#7CFF4F>Purchased and equipped " + _ItemName(tab, sel) + "!</color>";
            _Sfx(buyClip);
            _RefreshUI();
        }

        private void _Fail(string msg)
        {
            message = "<color=#FF6B7A>" + msg + "</color>";
            _Sfx(errorClip);
            _RefreshUI();
        }

        private void _Sfx(AudioClip c)
        {
            if (sfx != null && c != null) sfx.PlayOneShot(c, 0.8f);
        }

        // ------------------------------------------------------------ UI refresh

        private void _RefreshUI()
        {
            int coins = _Coins();
            if (coinsText != null) coinsText.text = restored ? coins.ToString() : "...";
            if (vipText != null) vipText.text = _IsVip() ? "VIP x" + vipMultiplier : "";
            for (int t = 0; t <= PREMIUM_TAB; t++)
            {
                if (tabLabels != null && t < tabLabels.Length && tabLabels[t] != null) tabLabels[t].color = t == tab ? new Color(1f, 0.84f, 0.3f, 1f) : Color.white;
                if (tabSelected != null && t < tabSelected.Length && tabSelected[t] != null) tabSelected[t].SetActive(t == tab);
            }

            int count = _Count(tab);
            int eq = _Equipped(tab);
            if (itemButtons != null)
            {
                for (int i = 0; i < itemButtons.Length; i++)
                {
                    bool on = i < count;
                    if (itemButtons[i] != null) itemButtons[i].SetActive(on);
                    if (itemSelected != null && i < itemSelected.Length && itemSelected[i] != null) itemSelected[i].SetActive(on && i == sel);
                    if (!on) continue;
                    if (itemIcons != null && i < itemIcons.Length && itemIcons[i] != null)
                    {
                        Sprite s = _ItemArt(tab, i);
                        itemIcons[i].sprite = s;
                        itemIcons[i].enabled = s != null;
                    }
                    if (itemNames != null && i < itemNames.Length && itemNames[i] != null) itemNames[i].text = _ItemName(tab, i);
                    int kind = _StatusKind(tab, i, eq);
                    if (itemPills != null && i < itemPills.Length && itemPills[i] != null) itemPills[i].color = _KindColor(kind);
                    if (itemStatus != null && i < itemStatus.Length && itemStatus[i] != null) itemStatus[i].text = _KindText(tab, i, kind);
                }
            }

            string action = "SELECT ITEM";
            if (sel >= 0 && sel < count)
            {
                if (detailName != null) detailName.text = _ItemName(tab, sel);
                string rarity = _Rarity(tab, sel);
                if (detailRarity != null) detailRarity.text = rarity;
                if (detailRarityPill != null) detailRarityPill.color = _RarityColor(rarity);
                if (detailDesc != null) detailDesc.text = _ItemDesc(tab, sel);
                if (detailPreview != null)
                {
                    Sprite s = _ItemArt(tab, sel);
                    detailPreview.sprite = s;
                    detailPreview.enabled = s != null;
                }
                int prod = _ItemProduct(tab, sel);
                if (tab == PREMIUM_TAB) action = _Owns(PREMIUM_TAB, sel) && !_IsCoinPack(sel) ? "OWNED" : "GET  " + _PriceLabel(sel);
                else if (_Owns(tab, sel)) action = sel == eq ? "EQUIPPED" : "EQUIP";
                else if (prod >= 0) action = "UNLOCK  " + _PriceLabel(prod);
                else action = "BUY  " + _ItemPrice(tab, sel) + " COINS";
            }
            if (actionLabel != null) actionLabel.text = action;

            if (bannerText != null)
            {
                string first = message.Length > 0 ? message : "Earn <color=#FFE14D>Loop Coins</color> by playing LoopLand, then spend them on dice, tokens, buildings, trails and more!";
                bannerText.text = first + "\n<color=#FFE14D>" + (restored ? coins + " Loop Coins saved." : "Loading your save...") + "</color>";
            }
        }

        /// <summary>0 price in coins, 1 owned, 2 equipped, 3 premium (locked), 4 Credits price, 5 owned premium.</summary>
        private int _StatusKind(int cat, int i, int eq)
        {
            if (cat == PREMIUM_TAB) return _Owns(PREMIUM_TAB, i) && !_IsCoinPack(i) ? 5 : 4;
            if (i == eq) return 2;
            if (_Owns(cat, i)) return 1;
            if (_ItemProduct(cat, i) >= 0) return 3;
            return 0;
        }

        private Color _KindColor(int kind)
        {
            if (kind == 1 || kind == 5) return new Color(0.13f, 0.42f, 0.86f, 1f);
            if (kind == 2) return new Color(0.1f, 0.62f, 0.36f, 1f);
            if (kind == 3 || kind == 4) return new Color(0.72f, 0.53f, 0.06f, 1f);
            return new Color(0.2f, 0.18f, 0.38f, 1f);
        }

        private string _KindText(int cat, int i, int kind)
        {
            if (kind == 1 || kind == 5) return "OWNED";
            if (kind == 2) return "EQUIPPED";
            if (kind == 3) return "PREMIUM";
            if (kind == 4) return _PriceLabel(i);
            return _ItemPrice(cat, i) + " COINS";
        }

        private string _Rarity(int cat, int i)
        {
            if (cat == PREMIUM_TAB) return _IsCoinPack(i) ? "COIN PACK" : "PREMIUM";
            if (_ItemProduct(cat, i) >= 0) return "PREMIUM";
            int p = _ItemPrice(cat, i);
            if (p <= 0) return "STARTER";
            if (p <= 250) return "COMMON";
            if (p <= 450) return "RARE";
            return "EPIC";
        }

        private Color _RarityColor(string r)
        {
            if (r == "STARTER") return new Color(0.3f, 0.36f, 0.55f, 1f);
            if (r == "COMMON") return new Color(0.1f, 0.55f, 0.75f, 1f);
            if (r == "RARE") return new Color(0.48f, 0.28f, 0.85f, 1f);
            if (r == "EPIC") return new Color(0.9f, 0.4f, 0.1f, 1f);
            return new Color(0.75f, 0.55f, 0.05f, 1f);
        }

        private string _PriceLabel(int prod)
        {
            return productPriceLabels != null && prod >= 0 && prod < productPriceLabels.Length && productPriceLabels[prod].Length > 0 ? productPriceLabels[prod] : "Credits";
        }
    }
}
