# LoopLand - Board Game for VRChat

LoopLand is a networked party board game for VRChat worlds. Race around an infinity-shaped Loop, win quick
challenges, scratch Lucky Loop tickets, survive the chaos and finish with the most coins.

- **2-6 players** on an **infinity-loop (figure-8) board** with 40 tiles on a glowing gradient ribbon.
- **Seven simple tile types**: Coins, Lucky Loop, Challenge, Power, Mystery, Portal and Loop Start. No buying,
  rent, building or mortgages.
- **The Loop gets wilder every lap**: bigger rewards from Loop 2, more Mystery tiles from Loop 3, jackpot tickets from Loop 4.
- **Lucky Loop tickets**: a real scratch ticket appears on your dashboard. Scratch it with your finger (VR) or
  your view (desktop) to reveal a gameplay reward.
- **Head-to-head competition**: Challenge tiles are **DUELS** against your closest rival, everyone plays a
  **LOOP BATTLE** every 2 rounds (plus a FINAL BATTLE), and landing on another player **BUMPS** them for coins.
- **Mini-games**: 15-second games on your dashboard (LASER LOOP and COIN RUSH).
- **Particle tokens** that hop space-by-space with trails, landing bursts and auras.
- **Animated 3D dice** that always land on the synced result, with skin-specific glow trails.
- **Loop Coins**, a persistent currency saved with VRChat PlayerData. You earn them for laps, challenges,
  finishing and winning games, plus a daily bonus.
- **In-game store** with dice skins, token skins, **tile glows** (the light on the tile you land on) and particle
  trails bought with Loop Coins. Equipped cosmetics are visible to everyone.
- **Free Loop Scratch**: a machine beside the store with **free** tickets only (one every 30 minutes and one per
  finished game). They give just-for-fun rewards: Loop XP, 2x XP, a Lucky Start or Shield Start for your next
  game, fireworks for everyone, and collectible stamps. Nothing there can be bought, traded or cashed out.
- **VRChat Creator Economy (CE) integration**: premium bundles with fixed, listed contents, VIP (2x coins), and
  Loop Coin packs bought with VRChat Credits through Udon Products. After a purchase, you scratch to reveal the
  bundle you just got. It's the same bundle every time, exactly as listed.
- **Live camera and seat screens** that follow the dice and the moving token, with captions.
- **One-click builder** that generates the whole game, including materials, dice, particles and sounds.

## Requirements
- A VRChat **world** project from the VRChat Creator Companion (Unity 2022.3).
- VRChat Worlds SDK **3.8.1 or newer**, which has `[NetworkCallable]` events, PlayerData and Udon Products.
- TextMesh Pro Essential Resources. The builder offers to import them if they're missing.

## Install
**Option A: Creator Companion (local package)**
1. Download this repository.
2. Copy the `Packages/com.loopland.game` folder into your world project's `Packages/` folder.
   If you're updating, **delete the old folder first** instead of copying over it.
3. Open the project. VCC and Unity list it as "LoopLand - Premium Board Game".

**Option B: Unity Package Manager**
`Window > Package Manager > + > Add package from git URL`:
`https://github.com/Sophia5942/LoopLand-.git?path=/Packages/com.loopland.game`

## Build the game (one click)
1. Open your world scene.
2. Click **LoopLand > Build Game In Scene** (choose **Replace** if a LoopLand is already there).
3. (Optional) Click **LoopLand > Add Demo World Setup** for a floor, a light and a VRCWorld if your scene is empty.
4. Press Play (ClientSim) or **Build & Test**.

Generated assets go to `Assets/LoopLand/Generated`, and a reusable prefab is saved at
`Assets/LoopLand/LoopLand Game.prefab`. Run the builder again any time to rebuild.

If your scene still has the LoopLand tower world from an earlier version, click
**LoopLand > Remove Tower World (back to the floor)** once, then save the scene.

## How to play
**Goal:** beat everyone. Finish with the **most coins** after the last round (10 rounds by default).

1. Walk to a console at the table edge and press **JOIN GAME**, then **START GAME**.
2. On your turn, the big button tells you what to do: **ROLL**, then **PLAY CHALLENGE** or **SCRATCH TICKET** when a
   tile asks for it. When the tile is done you see **TURN COMPLETE**, and the next player's turn starts
   automatically after 2 seconds.

| Tile | What happens |
|------|--------------|
| **Coins** | Gain 50-200 coins. Two tiles leak 50 coins instead (a Shield blocks it). |
| **Lucky Loop** | A ticket appears on your dashboard. Scratch it to reveal a reward (below). |
| **Challenge** | A **DUEL** against your closest rival (see below). In a solo game it's a normal mini-game: LASER LOOP (+30 coins a hit, 5 targets) or COIN RUSH (+15 a coin). |
| **Power** | Get a Shield, a Boost, a Swap or a Bonus Roll. |
| **Mystery** | A random event: Coin Shower for everyone, Glitch Tax, Robin Loop, Warp Ahead, Time Slip, Swap Chaos, a surprise challenge... |
| **Portal** | Warp to the next section of the Loop (10 tiles ahead). |
| **Loop Start** | +100 coins every time you complete a lap. |

**Competing with each other:**
- **Duels:** with 2+ players, a Challenge tile (or a Surprise Challenge) is a duel against your **closest rival**:
  the player just ahead of you in coins, or just behind you if you're leading. You both press **PLAY DUEL** and
  play the same mini-game on your own dashboards (no target cap: the most hits wins). The winner gets 100 coins and
  the loser pays 100 (a Shield blocks it). A draw costs nobody anything.
- **Loop Battles:** after every 2nd round **everyone** presses **PLAY BATTLE** and plays the same mini-game.
  1st place +200, 2nd +100, 3rd and below +50, and the **last place pays 50**. After the last round there's a
  **FINAL BATTLE** with everything doubled.
- **Bumps:** land on a tile where another player stands and you grab 50 of their coins (a Shield blocks it).
- Duel stakes, battle prizes and bumps grow x1.5 from Loop 2, like everything else.
- You have about 50 seconds to start a duel or battle. Anyone who doesn't play scores 0.
- The player list shows everyone's place (1st, 2nd...) and live duel and battle scores. **MY STATUS** shows how far
  ahead or behind you are.

**Lucky Loop rewards:** +100 or +250 coins, Double Boost (your next coin reward is x2), Shield, Move +3, Extra Roll,
Swap places with a random player, Jackpot Challenge (a challenge with coins x3), or Nothing this time. From Loop 4
on, a ticket can hit the **LOOP JACKPOT** (+1,000).

**Power-ups:** a **Shield** blocks your next coin loss, bad Mystery or Swap. A **Boost** doubles your next coin
reward. Both work automatically, and you can hold up to 3 of each.

**Each lap gets wilder** (the leader's lap sets the level for everyone):
- Loop 1: normal.
- Loop 2: bigger rewards (coins x1.5).
- Loop 3: four Coins tiles turn into Mystery tiles.
- Loop 4+: Lucky Loop tickets can hit the LOOP JACKPOT.

**Scratching** works the same everywhere (Lucky Loop tickets, free tickets and purchase reveals):
- **VR:** rub the silver with your finger or hand. It comes off wherever you touch.
- **Desktop:** look across the silver. It comes off wherever the middle of your view goes.

**Dashboard buttons:**
- **MY STATUS:** your coins, laps, power-ups and the tile you're on.
- **TICKETS:** your free Loop Scratch tickets, level and stamps.
- **POWER-UPS:** what you're holding.
- **VIEW:** swaps your live board between the center screens and a screen above your console (only for you).
- **HOW TO PLAY:** the tiles.
- **STORE:** pops the store up above your dashboard.
- **LEAVE**, and **RESET**, which you press twice to send everyone back to the lobby.

**Free Loop Scratch** (the machine beside the store):
1. Press **SCRATCH A FREE TICKET** on the screen.
2. Rub the ticket that lands on the counter.

You get a free ticket every 30 minutes (you can hold up to 5) and one for every game you finish.

## Creator Economy setup (selling with VRChat Credits)
The store ships with 6 premium slots on the `LoopLand/Store` object (`Products` array). Every slot has **fixed,
listed contents**:

| # | Default name | Suggested listing type | Contents |
|---|--------------|------------------------|----------|
| 0 | Premium Dice Pack | Permanent | Royal Gold + Galaxy Holo dice |
| 1 | Holo Token Pack | Permanent | Diamond + animated Prism tokens |
| 2 | LoopLand VIP | Permanent or Temporary | 2x Loop Coins, Inferno Plasma dice + tile glow, Golden + Rainbow trails |
| 3 | Coin Pouch | **Instant** (enable quantity purchases) | +500 Loop Coins per purchase |
| 4 | Coin Vault | **Instant** (enable quantity purchases) | +3000 Loop Coins per purchase |
| 5 | Skyline Pack | Permanent | Royal Gold + Galaxy Holo tile glows |

1. On VRChat.com, create an **Udon product** and a **listing** for each one you want to sell.
2. In Unity, open **VRChat SDK > UdonProducts Manager**, or use **Assets > Create > VRChat > UdonProduct**,
   and create the UdonProduct assets.
3. Select `LoopLand/Store` and drag the UdonProducts into **Products** (same order as the table).
   Paste each listing ID (`prod_...`) into **Product Listing Ids**. Set **Product Price Labels**
   (for example "300 Credits"). VRChat requires prices to be shown in Credits.
4. Test with ClientSim plus the UdonProducts Manager, or with Build & Test. Listings are free for the world owner.

Notes:
- Unassigned slots show "not set up yet", so the world works fine with no products.
- Coin packs are credited only in `OnPurchaseConfirmedMultiple` with `purchased == true`, multiplied by quantity.
- **No gambling, by design.**
  - Nothing random is ever sold. Every purchase is a fixed bundle, and the scratch-to-reveal after buying only
    shows what the listing already promised.
  - The only random rewards come from **free** tickets (Lucky Loop tiles and Free Loop Scratch). They give
    gameplay perks, XP, fireworks and stamps, which can't be bought, traded or cashed out.
  - Credits are never a prize, and there are no paid scratches, jackpots on paid items or losing paid tickets.
  - Keep it that way if you customise the store.
- "World Store" opens your world's VRChat store page (needs a published world store).

## Custom card art (board tiles)
The 40 board tiles are UI cards on one flat canvas (`LoopLand > Board > Board UI > Card NN ...`).
- **Easiest:** put images in `Assets/LoopLand/Space Art/` named by tile number (`00.png` ... `39.png`) or by
  exact tile name (`Neon Row.png`), then run **LoopLand > Build Game In Scene** again. Portrait images
  around **560 x 1000 px** fit best. They're switched to Sprite import automatically.
- Tile numbers start at 00 = LOOP START and go clockwise. The card object names show the number and name.

## Custom store art
Every store picture is generated automatically, but you can replace any of them with your own images.
Put them in `Assets/LoopLand/Store Art/` and run **Build Game In Scene** again:
- `dice_0.png` ... `dice_7.png`, `token_0.png` ... `token_5.png`, `building_0.png` ... `building_7.png` (tile glows),
  `trail_0.png` ... `trail_4.png`, `premium_0.png` ... `premium_5.png` (square-ish, around 512 x 512)
- `Background.png` - a full background picture for the store panel (for example a neon city)

## Customize
- **Rules** (rounds to play, lap bonus, AFK timer, min players, Loop Coin rewards): `LoopLand/Game` inspector.
  Set *Min Players To Start* to **2** for public worlds (1 lets you test solo).
- **Board**: `Board` on `LoopLand/Game`, then rebuild.
  - *Space Name* and *Space Type*: 0 start, 1 coins, 2 lucky loop, 3 challenge, 4 power, 5 mystery, 6 portal.
  - *Space Value*: coins gained (negative = lost).
  - *Space Wake*: 1 = becomes a Mystery tile from Loop 3.
- **Catalogue** (names, prices, colors, which product unlocks an item): `LoopLand/Store` inspector.
  Product index `-1` means "buy with Loop Coins".
- **Competition** (duel stake, how often Loop Battles happen or 0 for none, battle prizes, what the last place pays,
  bump amount, how long players get to play): the *Competition* section of the `LoopLand/Game` inspector.
- **Free Loop Scratch** (refill time, max tickets, reward odds): `LoopLand/Free Loop Scratch` inspector.

## Technical notes
- The game uses a manual-sync authority pattern: the owner of `LoopLand/Game` runs the rules, and players send
  commands with `[NetworkCallable]` events (sender = `NetworkCalling.CallingPlayer`). If the owner leaves,
  VRChat hands ownership to another player and the game continues.
- Persistent keys (PlayerData):
  - Store: `ll_coins`, `ll_own_*`, `ll_eq_*`, `ll_match`, `ll_match_got`, `ll_daily`.
  - Free Loop Scratch: `ll_tickets`, `ll_ticket_t`, `ll_xp`, `ll_xp2`, `ll_start_boost`, `ll_start_shield`,
    `ll_stamps`, `ll_tk_match`, `ll_start_match`.
  - Match rewards are applied exactly once per match, even after a rejoin.
- Everything is driven by synced state, so late joiners see the correct board, tokens and dice.
- Scratching never relies on UI hover events. `LoopLandTicket` tracks the player in Udon and hides the foil flakes
  within the scratch radius, along the path since the last frame:
  - in VR, the index fingertip (or the hand, for avatars without finger bones) when it touches the ticket;
  - on desktop, the point in the middle of the view.
- The Lucky Loop ticket and the challenge panel are single objects that the game moves to the dashboard nearest
  the player when their turn needs them.
