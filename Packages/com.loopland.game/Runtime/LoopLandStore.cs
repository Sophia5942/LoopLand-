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
    /// LoopLand Store: persistent Loop Coins (PlayerData), cosmetic dice / tokens / trails bought with coins,
    /// and VRChat Creator Economy products (premium packs, VIP, coin packs) bought with VRChat Credits.
    /// Equipped cosmetics live in PlayerData, so every player sees everyone else's skins.
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

        [Header("Token skins")]
        public string[] tokenNames;
        public int[] tokenPrices;
        public int[] tokenProduct;
        public Color[] tokenColors;
        public int rainbowToken = -1;

        [Header("Particle trails")]
        public string[] trailNames;
        public int[] trailPrices;
        public int[] trailProduct;
        public Color[] trailColors;
        public int rainbowTrail = -1;

        [Header("Creator Economy: assign UdonProduct assets and listing IDs (prod_...)")]
        public UdonProduct[] products;
        public string[] productNames;
        public string[] productDescriptions;
        public string[] productListingIds;
        public string[] productPriceLabels;
        [Tooltip("Loop Coins granted per purchase (instant listings with quantity). 0 for unlock products.")]
        public int[] productCoins;
        public int vipProduct = 2;
        public int vipMultiplier = 2;

        [Header("Economy")]
        public int startingCoins = 250;
        public int dailyBonus = 50;

        [Header("UI (auto-wired by the builder)")]
        public TMP_Text coinsText;
        public TMP_Text detailText;
        public TMP_Text actionLabel;
        public TMP_Text[] tabLabels;
        public GameObject[] itemButtons;
        public TMP_Text[] itemLabels;
        public Image[] itemSwatches;
        public Renderer previewDie;
        public Renderer[] previewToken;
        public ParticleSystem previewTrail;
        public Transform previewSpinner;
        public AudioSource sfx;
        public AudioClip buyClip;
        public AudioClip errorClip;
        public AudioClip clickClip;

        [HideInInspector] public int pressedArg;

        private const string K_COINS = "ll_coins";
        private const string K_MATCH = "ll_match";
        private const string K_MATCH_GOT = "ll_match_got";
        private const string K_DAILY = "ll_daily";
        private string[] kOwn = { "ll_own_dice", "ll_own_token", "ll_own_trail" };
        private string[] kEq = { "ll_eq_dice", "ll_eq_token", "ll_eq_trail" };
        private string[] tabNames = { "DICE", "TOKENS", "TRAILS", "PREMIUM" };

        private bool restored;
        private bool[] owned = new bool[0];
        private int pendingCoins;
        private int pendingMatch;
        private int pendingEarned;
        private int tab;
        private int sel = -1;
        private string message = "";

        private void Start()
        {
            owned = new bool[products == null ? 0 : products.Length];
            _RefreshUI();
        }

        private void Update()
        {
            if (previewSpinner != null) previewSpinner.Rotate(0f, 35f * Time.deltaTime, 0f);
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
                    message = "<color=#FFE14D>Thank you for supporting LoopLand!</color>\n" + _ProductName(idx) + (grant > 0 ? " (+" + (grant * Mathf.Max(1, quantity)) + " coins)" : " unlocked!");
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
            for (int cat = 0; cat < 3; cat++)
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

        // ------------------------------------------------------------ catalogue (used by the game too)

        public int _GetEquipped(VRCPlayerApi p, int cat)
        {
            if (!Utilities.IsValid(p)) return 0;
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
        public bool _IsRainbowToken(int i) { return i > 0 && i == rainbowToken; }
        public bool _IsRainbowTrail(int i) { return i > 0 && i == rainbowTrail; }

        private int _Count(int cat)
        {
            string[] a = cat == 0 ? diceNames : (cat == 1 ? tokenNames : (cat == 2 ? trailNames : productNames));
            return a == null ? 0 : a.Length;
        }

        private string _ItemName(int cat, int i)
        {
            if (cat == 0) return diceNames[i];
            if (cat == 1) return tokenNames[i];
            if (cat == 2) return trailNames[i];
            return _ProductName(i);
        }

        private int _ItemPrice(int cat, int i)
        {
            int[] a = cat == 0 ? dicePrices : (cat == 1 ? tokenPrices : trailPrices);
            return a != null && i < a.Length ? a[i] : 0;
        }

        private int _ItemProduct(int cat, int i)
        {
            if (cat == 3) return i;
            int[] a = cat == 0 ? diceProduct : (cat == 1 ? tokenProduct : trailProduct);
            return a != null && i < a.Length ? a[i] : -1;
        }

        private Color _ItemColor(int cat, int i)
        {
            if (cat == 0) return _DiceGlow(i);
            if (cat == 1) return i == 0 ? new Color(0f, 0.9f, 1f) : _TokenColor(i);
            if (cat == 2) return _TrailColor(i);
            return new Color(1f, 0.85f, 0.25f);
        }

        private string _ProductName(int i)
        {
            return productNames != null && i >= 0 && i < productNames.Length ? productNames[i] : "Premium item";
        }

        private bool _Owns(int cat, int i)
        {
            if (cat == 3) return i < owned.Length && owned[i];
            if (i == 0) return true;
            int prod = _ItemProduct(cat, i);
            if (prod >= 0) return prod < owned.Length && owned[prod];
            if (!restored) return false;
            return ((PlayerData.GetInt(Networking.LocalPlayer, kOwn[cat]) >> i) & 1) == 1;
        }

        // ------------------------------------------------------------ UI events

        public void _OnTab()
        {
            tab = Mathf.Clamp(pressedArg, 0, 3);
            sel = -1;
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
            _Preview();
            _RefreshUI();
        }

        public void _OnTab0() { pressedArg = 0; _OnTab(); }
        public void _OnTab1() { pressedArg = 1; _OnTab(); }
        public void _OnTab2() { pressedArg = 2; _OnTab(); }
        public void _OnTab3() { pressedArg = 3; _OnTab(); }
        public void _OnItem0() { pressedArg = 0; _OnItem(); }
        public void _OnItem1() { pressedArg = 1; _OnItem(); }
        public void _OnItem2() { pressedArg = 2; _OnItem(); }
        public void _OnItem3() { pressedArg = 3; _OnItem(); }
        public void _OnItem4() { pressedArg = 4; _OnItem(); }
        public void _OnItem5() { pressedArg = 5; _OnItem(); }
        public void _OnItem6() { pressedArg = 6; _OnItem(); }
        public void _OnItem7() { pressedArg = 7; _OnItem(); }

        public void _OnAction()
        {
            if (sel < 0) { _Fail("Pick an item first."); return; }
            int prod = _ItemProduct(tab, sel);
            if (tab == 3 || (prod >= 0 && !_Owns(tab, sel)))
            {
                bool coinPack = productCoins != null && prod < productCoins.Length && productCoins[prod] > 0;
                if (products == null || prod >= products.Length || products[prod] == null) { _Fail("This premium item isn't set up yet (see README)."); return; }
                if (tab == 3 && _Owns(3, prod) && !coinPack) { _Fail("You already own this. Enjoy!"); return; }
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
            if (coins < price) { _Fail("Need " + (price - coins) + " more Loop Coins. Play games, claim the daily bonus, or grab a coin pack!"); return; }
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

        private void _Preview()
        {
            if (sel < 0) return;
            if (tab == 0 && previewDie != null && _DiceMaterial(sel) != null) previewDie.sharedMaterial = _DiceMaterial(sel);
            Color c = _ItemColor(tab, sel);
            if (tab == 1 && previewToken != null)
            {
                for (int i = 0; i < previewToken.Length; i++)
                {
                    if (previewToken[i] == null) continue;
                    previewToken[i].material.SetColor("_Color", c);
                    previewToken[i].material.SetColor("_EmissionColor", c * 0.6f);
                }
            }
            if (tab == 2 && previewTrail != null)
            {
                Renderer r = previewTrail.GetComponent<Renderer>();
                if (r != null) r.material.SetColor("_TintColor", c * 0.6f);
                previewTrail.Emit(60);
            }
        }

        private void _RefreshUI()
        {
            if (coinsText != null)
                coinsText.text = restored ? "<b>" + _Coins() + "</b> <size=70%>LOOP COINS</size>" + (_IsVip() ? "  <color=#FFE14D>VIP x" + vipMultiplier + "</color>" : "") : "<size=70%>Loading save...</size>";
            if (tabLabels != null)
                for (int i = 0; i < tabLabels.Length && i < 4; i++)
                    if (tabLabels[i] != null) tabLabels[i].text = i == tab ? "<color=#FFE14D>" + tabNames[i] + "</color>" : tabNames[i];

            int count = _Count(tab);
            int eq = tab < 3 && restored ? PlayerData.GetInt(Networking.LocalPlayer, kEq[tab]) : -1;
            if (itemLabels != null)
            {
                for (int i = 0; i < itemLabels.Length; i++)
                {
                    bool on = i < count;
                    if (itemButtons != null && i < itemButtons.Length && itemButtons[i] != null) itemButtons[i].SetActive(on);
                    if (!on || itemLabels[i] == null) continue;
                    itemLabels[i].text = (i == sel ? "<color=#FFE14D>" : "") + "<b>" + _ItemName(tab, i) + "</b>" + (i == sel ? "</color>" : "") + "\n<size=75%>" + _StatusLine(tab, i, eq) + "</size>";
                    if (itemSwatches != null && i < itemSwatches.Length && itemSwatches[i] != null)
                    {
                        itemSwatches[i].color = _ItemColor(tab, i);
                    }
                }
            }

            string detail = "";
            string action = "SELECT AN ITEM";
            if (sel >= 0 && sel < count)
            {
                int prod = _ItemProduct(tab, sel);
                detail = "<b>" + _ItemName(tab, sel) + "</b>\n";
                if (tab == 3)
                {
                    detail += productDescriptions != null && sel < productDescriptions.Length ? productDescriptions[sel] : "";
                    bool coinPack = productCoins != null && sel < productCoins.Length && productCoins[sel] > 0;
                    action = _Owns(3, sel) && !coinPack ? "OWNED" : "GET  " + _PriceLabel(sel);
                }
                else if (_Owns(tab, sel)) action = sel == eq ? "EQUIPPED" : "EQUIP";
                else if (prod >= 0) { detail += "Premium item - included with " + _ProductName(prod) + "."; action = "GET  " + _PriceLabel(prod); }
                else { detail += "Unlock with Loop Coins earned by playing."; action = "BUY  " + _ItemPrice(tab, sel) + " COINS"; }
            }
            else detail = "Earn <color=#FFE14D>Loop Coins</color> by playing LoopLand, then spend them on dice, tokens and particle trails.\nPremium packs support the creator.";
            if (message.Length > 0) detail += "\n" + message;
            if (detailText != null) detailText.text = detail;
            if (actionLabel != null) actionLabel.text = action;
        }

        private string _StatusLine(int cat, int i, int eq)
        {
            if (cat == 3)
            {
                bool coinPack = productCoins != null && i < productCoins.Length && productCoins[i] > 0;
                if (_Owns(3, i) && !coinPack) return "<color=#7CFF4F>OWNED</color>";
                return "<color=#FFE14D>" + _PriceLabel(i) + "</color>";
            }
            if (i == eq || (eq <= 0 && i == 0)) return "<color=#7CFF4F>EQUIPPED</color>";
            if (_Owns(cat, i)) return "OWNED";
            int prod = _ItemProduct(cat, i);
            if (prod >= 0) return "<color=#FFE14D>PREMIUM</color>";
            return _ItemPrice(cat, i) + " coins";
        }

        private string _PriceLabel(int prod)
        {
            return productPriceLabels != null && prod >= 0 && prod < productPriceLabels.Length && productPriceLabels[prod].Length > 0 ? productPriceLabels[prod] : "Credits";
        }
    }
}
