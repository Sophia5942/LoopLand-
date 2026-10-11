# LoopLand - Premium Board Game for VRChat

LoopLand is a networked, Monopoly-style board game for VRChat worlds:

- **2-6 players** on a glowing circular "loop" board with 40 spaces, 8 color groups, Portals, utilities,
  Twist / Loop Chest cards, the Glitch Zone (jail), Loops and Towers (houses / hotels), mortgages,
  auto-liquidation and bankruptcy, an optional round limit, and AFK auto-play.
- **Particle tokens** that hop space-by-space with trails, landing bursts and auras.
- **Animated 3D dice** that always land on the synced result, with skin-specific glow trails.
- **Loop Coins**, a persistent in-game currency saved with VRChat PlayerData. You earn coins for passing
  LOOP START, buying, building, finishing and winning games, plus a daily bonus.
- **In-game store** with dice skins, token skins and particle trails bought with Loop Coins. Equipped
  cosmetics are visible to everyone.
- **VRChat Creator Economy (CE) integration**: premium packs, VIP (2x coins), and Loop Coin packs bought
  with VRChat Credits through Udon Products.
- **One-click builder** that generates the whole game, including materials, dice, particles and sounds.

## Requirements
- A VRChat **world** project from the VRChat Creator Companion (Unity 2022.3).
- VRChat Worlds SDK **3.8.1 or newer**, which has `[NetworkCallable]` events, PlayerData and Udon Products.
- TextMesh Pro Essential Resources. The builder offers to import them if they're missing.

## Install
**Option A: Creator Companion (local package)**
1. Download this repository.
2. Copy the `Packages/com.loopland.game` folder into your world project's `Packages/` folder.
3. Open the project. VCC and Unity list it as "LoopLand - Premium Board Game".

**Option B: Unity Package Manager**
`Window > Package Manager > + > Add package from git URL`:
`https://github.com/Sophia5942/LoopLand-.git?path=/Packages/com.loopland.game`

## Build the game (one click)
1. Open your world scene.
2. Click **LoopLand > Build Game In Scene**.
3. (Optional) Click **LoopLand > Add Demo World Setup** for a floor, a light and a VRCWorld if your scene is empty.
4. Press Play (ClientSim) or **Build & Test**.

Generated assets go to `Assets/LoopLand/Generated`, and a reusable prefab is saved at
`Assets/LoopLand/LoopLand Game.prefab`. Run the builder again any time to rebuild.

## How to play
- Walk to a console at the table edge and press **JOIN GAME**, then **START GAME**.
- The big button is context-sensitive: ROLL DICE / BUY / END TURN / ROLL DOUBLES...
- The second button does PASS, USE GLITCH PASS, PAY BAIL or LEAVE.
- Tap any board space to inspect it, then use **BUILD / SELL / MORTGAGE** during your turn.
- Own a whole color group to build Loops (up to 4), then a Tower. You must build evenly.
- Landing on **GLITCHED!** or rolling 3 doubles sends you to the Glitch Zone. To get out, roll doubles,
  pay $50, or use a Glitch Pass.
- **ROUNDS** (in the lobby) sets a round limit. When it's reached, the richest player wins.
  **RESET** (press it twice) returns to the lobby.

## Creator Economy setup (selling with VRChat Credits)
The store ships with 5 premium slots on the `LoopLand/Store` object (`Products` array):

| # | Default name | Suggested listing type | What it does |
|---|--------------|------------------------|--------------|
| 0 | Premium Dice Pack | Permanent | Unlocks Royal Gold + Galaxy Holo dice |
| 1 | Holo Token Pack | Permanent | Unlocks Diamond + animated Prism tokens |
| 2 | LoopLand VIP | Permanent or Temporary | 2x Loop Coins, Inferno dice, Golden + Rainbow trails |
| 3 | Coin Pouch | **Instant** (enable quantity purchases) | +500 Loop Coins per purchase |
| 4 | Coin Vault | **Instant** (enable quantity purchases) | +3000 Loop Coins per purchase |

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
- **CE guidelines:** LoopLand never puts purchased coins at stake. Coins are earned or bought, and spent only on
  direct cosmetic unlocks. There are no loot boxes, and nobody gets a worse game experience for not paying.
  Keep it that way.
- "World Store" opens your world's VRChat store page (needs a published world store).

## Customize
- **Rules** (cash, salary, fines, coin rewards, AFK timer, min players): `LoopLand/Game` inspector.
  Set *Min Players To Start* to **2** for public worlds (1 lets you test solo).
- **Board names / prices / rents**: `Board data` on `LoopLand/Game`, then rebuild.
- **Catalogue** (names, prices, colors, which product unlocks an item): `LoopLand/Store` inspector.
  Product index `-1` means "buy with Loop Coins".

## Technical notes
- The game uses a manual-sync authority pattern: the owner of `LoopLand/Game` runs the rules, and players send
  commands with `[NetworkCallable]` events (sender = `NetworkCalling.CallingPlayer`). If the owner leaves,
  VRChat hands ownership to another player and the game continues.
- Persistent keys (PlayerData): `ll_coins`, `ll_own_*`, `ll_eq_*`, `ll_match`, `ll_match_got`, `ll_daily`.
  Match rewards are applied exactly once per match, even after a rejoin.
- Everything is driven by synced state, so late joiners see the correct board, tokens and dice.

## Drink machine (soda fountain)
A networked soda fountain inspired by fast-food sims: set a cup down under a nozzle and it **locks into place** with a
click, press the nozzle's **push button** to pour, and the cup fills with a rising-pitch pour sound, then fizzes.
Pick it up and hold **Use** (trigger / left click) to drink. It works on desktop, PC VR and **Quest**.

### Build it (one click)
1. Click **LoopLand > Build Drink Machine In Scene**. It goes where your Scene view is looking.
2. Press Play (ClientSim) or **Build & Test**.

It builds `LoopLand Drinks` with the `Drink_Machine_1` model on a counter, 6 slots (one per nozzle, 6 flavors), a stack
of 6 cups next to the machine and a **RESPAWN CUPS** button. Assets go to `Assets/LoopLand/Generated/Drinks`, and a
prefab is saved at `Assets/LoopLand/LoopLand Drink Machine.prefab`.

### Scripts
| Script | Like your... | What it does |
|--------|--------------|--------------|
| `DrinkMachine` | `ManagerBar` | Finds flavors / slots / cups under `FlavorsParent` / `SlotsParent` / `CupsParent`, VR finger presses, cup respawn, sounds, optional drunk effect (same `UpdatePP` material properties). |
| `DrinkFlavor` | `LiquidParticles` | A drink: colors, foam, edge, transparency, pour speed, effect intensities. |
| `DrinkCup` | `LiquidGlass` + `TransformSync` | Locking, filling, drinking, mixing, respawn and all of the cup's networking. |
| `DrinkSlot` | (new) | One nozzle: snap point, stream, splash, pour sounds and its button panel. |
| `DrinkButton` | (new) | UI push button: click sound, press animation, sends an event. |

### Networking (optimized)
Every cup uses **manual sync** and only the owner sends. Nothing is sent while nothing changes:
- **Locked in a slot:** no position is sent. Every client puts the cup on the slot.
- **Held:** the cup's offset to the holder's hand bone is sent once. Everyone attaches it to that hand locally, so it's
  smooth without streaming positions every frame.
- **Dropped:** a few position updates per second while it falls, then a final one when it settles.
- **Pouring / drinking:** only start and stop are sent. Every client simulates the fill, and the final value fixes
  any drift.
- The drink mix is one byte per flavor. Late joiners get the current state.

### VR and Quest
- The panels are world-space UI canvases with `VRC Ui Shape`, so the **laser pointer** (PC VR and Quest) and the
  desktop mouse work. In VR you can also **press the buttons with your index finger**, with haptics.
- While you hold a cup near a free slot, a ring shows where it will lock.
- In VR you drink when the cup is at your mouth (`Mouth Distance`). Desktop drinks right away.
- The shaders (`LoopLand/Drinks/Liquid`, `Cup`, `Stream`) are single pass, with no GrabPass or geometry shaders,
  and work with single-pass stereo. Liquid is hidden when empty, and the drunk effect is switched off when sober.

### Cup respawn
- **RESPAWN CUPS** puts every cup that nobody is holding back on the stack, empty.
- A cup left alone is respawned after `Respawn Idle Seconds` (default 5 min), or `Respawn Empty Seconds` if it's empty
  and not in the machine (default 20 s).
- A cup that falls `Fall Respawn Distance` below its spawn is respawned right away.
- `Keep Cup Ready`: when someone takes the last cup from the stack, the cup abandoned the longest is recycled onto it.
- To respawn somewhere else, set a cup's `Spawn Point`.

### Customize
- **Flavors:** edit them under `LoopLand Drinks/Flavors`, and pick each slot's flavor with `Flavor Index`.
- **Sounds:** the builder makes procedural stand-ins (button click, deny buzz, cup lock clack, unlock, valve open,
  pour loop, valve close + drips, full ding, fizz, slurp loop, respawn pop, machine hum). Drop your own clips into
  the **Sounds** section of `DrinkMachine`.
- **Your own liquid shader:** `DrinkCup` sets the same properties as `LiquidGlass` (`_FillHeight`, `_GlassRemap`,
  `_ColorMain`, `_FoamColor`, `_EdgeColor`, `_Transparency`, `_FoamHeight`, `_FoamLineSmoothness`, `_EdgeStrength`,
  `_EdgePower`), plus `_WobbleX` / `_WobbleZ`. Put your material on the cup's `Liquid` renderer.
- **Your own cup model:** the cup's pivot must be at its bottom center, and its `Liquid` child's mesh must sit on the
  cup's axis. Enable Read/Write on imported liquid meshes, or the liquid's bounds are used instead.
- **Placement:** the builder places slots from measurements of `Drink_Machine_1`. Move a slot's `SnapPoint`, `Nozzle`
  or `Button Panel` to fine-tune it. Don't move the machine at runtime.
