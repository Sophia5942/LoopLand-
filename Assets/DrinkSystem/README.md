# Drink System - FBT Mayfair Drink Station for VRChat

A networked black and gold bar drink station for VRChat worlds (desktop, PC VR and **Quest**), laid out as one clear
line, left to right. Each step has a big gold number, and the sign on top shows the whole line:

| Step | Where | What you do |
|------|-------|-------------|
| **1 TAKE A GLASS** | Glass rack (left) | Grab any glass: wine, champagne, martini or shot (your `Glasses2025` glasses). |
| **2 PLACE IT** | Drink machine | Set it under the drink you want. It **locks into place** with a click, and a gold ring shows where it'll lock. |
| **3 POUR** | Drink machine | Pick **STRENGTH** (LOW / MED / STRONG = how drunk it gets) and **FROTH**, then press **POUR** on the card. The card always says what to do next. |
| **4 FINISH IT** | Finishing station (right) | Put the glass on the pad, then tap a **STRAW**, a **FRUIT** garnish, a **GLASS** tint and the **FBT logo** (gold, silver or none). It changes right away. |
| **5 DRINK** | Anywhere | Hold **Use** (trigger / left click) with the glass at your mouth. |

There are also pour / fizz / slurp / click / lock **sounds**, mixing, and **glass respawn** (a RESET GLASSES button,
plus automatic respawn).

## Requirements
- A VRChat **world** project (Unity 2022.3) with VRChat Worlds SDK **3.8.1 or newer** (includes UdonSharp).
- TextMesh Pro Essential Resources. The builder offers to import them if they're missing.
- Your **`Glasses2025`** prefab in the project. Without it, the builder makes simple glasses instead.

## Install
In Unity: **Assets > Import Package > Custom Package...** and pick `DrinkSystem.unitypackage`. It imports into
`Assets/DrinkSystem` (no assembly definitions, just like your own scripts).

**Upgrading from an older zip:** first close Unity and delete these if you have them:
- `Packages/com.drinksystem`
- `Packages/com.loopland.game`, if you installed the first zip
- `Assets/DrinkSystem/Generated`

Then open Unity and import.

## Build it (one click)
1. **Select your bar counter** in the scene (optional). The station is placed on top of it.
   With nothing selected, it comes with its own counter where your Scene view is looking.
2. Click **Drink System > Build Drink Machine In Scene** in the top menu (also under Tools). Right after importing, a pop-up offers to build it for you.
3. Press Play (ClientSim) or **Build & Test**.

What the builder does with your glasses:
- It takes every `Glass_*` in `Glasses2025` (with its `Liquid_*` child, your models and materials) and builds
  `CopiesPerGlass` (2) of each onto the rack as networked `DrinkCup`s.
- It turns on **Read/Write** for the glasses' model, since the liquid line is measured from the mesh.
- It doesn't use the old `LiquidGlass` / `TransformSync` scripts on these. Your prefab itself isn't changed.
- Each glass gets a box collider, a pickup, a straw, a fruit wheel and an **FBT Mayfair logo** wrapped around its
  widest part.
- The machine is made tall enough for your tallest glass.

Everything else is generated into `Assets/DrinkSystem/Generated`, and a prefab is saved at
`Assets/DrinkSystem/Drink Machine.prefab`. Run it again any time to rebuild.

## Strength = drunk post-processing
STRENGTH decides how hard a drink hits. While you drink, it pushes your drunk post-processing up: chromatic
aberration, distortion and neon, the same material properties as `ManagerBar.UpdatePP`. The more you drink, the
stronger it gets, and it wears off over time.
- The builder finds your **ManagerBar** in the scene and reuses its `Drunk Effect Transform`. **Disable that
  ManagerBar** afterwards, because the station now drives the effect.
- If it isn't found, drag your effect object into `Drunk Effect Transform` on `Drink Machine`.
- Tune it with `Strength Levels` (LOW / MED / STRONG), `Intoxication Factor` and `Detoxication Speed`.

## Networking and performance
- Manual sync everywhere. Nothing is sent while nothing changes.
- **Machine:** 2 bytes (strength, froth), sent only when someone taps them.
- **Glass in a slot or on the pad:** no position is sent. Every client puts it there itself.
- **Held glass:** the offset to the holder's hand bone is sent once. Everyone attaches it to that hand locally.
- **Dropped glass:** a few position updates per second while it falls, then a final one when it settles.
- **Pouring / drinking:** only start and stop are sent. Every client simulates the fill in between.
- **Mix and look:** one byte per flavor, plus a handful of bytes for strength, froth, straw, fruit, tint and logo.
- **Drunk effect:** the full-screen effect is **switched off when sober**. While active, its material is updated at
  most 10 times a second, and only the values that actually changed.
- Idle glasses skip all their per-frame work. Liquid is hidden when empty.
- The station parts are static (batched). Shaders are single pass, Quest-safe, and have no real-time lights.
- Each card has its own canvas, so a filling bar only redraws its own card.
- VR finger presses are only checked when you're near the station.

## VR and Quest
- Every panel is a world-space UI canvas with `VRC Ui Shape`, so the **laser pointer** (PC VR and Quest) and the
  desktop mouse work. In VR you can also **press buttons with your index finger**, with haptics.
- In VR you drink when the glass is at your mouth (`Mouth Distance`). Desktop drinks right away.

## Glass respawn
- **RESET GLASSES** (on the finishing panel) puts every glass nobody is holding back on the rack.
- A glass left alone is respawned after `Respawn Idle Seconds` (default 5 min), or `Respawn Empty Seconds` if it's empty
  and not in a slot (default 20 s). A respawned glass is empty, with no straw or fruit, and back to its original look.
- A glass that falls `Fall Respawn Distance` below its spot is respawned right away.
- `Keep Cup Ready`: when someone takes the last glass from the rack, the glass abandoned the longest is recycled onto it.

## Customize
- **Drinks:** edit them under `Drink Machine/Flavors` (the first 6 are the drink cards).
- **Glasses:** change `GlassesPrefabName` / `CopiesPerGlass` at the top of `Editor/DrinkMachineBuilder.cs`.
- **Logo:** replace `Assets/DrinkSystem/Textures/FBT_Mayfair_Logo.png` (transparent PNG) and build again.
- **Sounds:** the builder makes procedural stand-ins. Drop your own clips into the **Sounds** section of
  `DrinkMachine`.
- **Liquid shader:** `DrinkCup` sets the same properties as `LiquidGlass` (`_FillHeight`, `_GlassRemap`, `_ColorMain`,
  `_FoamColor`, `_EdgeColor`, `_Transparency`, `_FoamHeight`, `_FoamLineSmoothness`, `_EdgeStrength`, `_EdgePower`),
  so your own liquid material on `Liquid_*` keeps working.
- **Glass tint:** sets `_Color` on the glass material. "Original" leaves it exactly as it is.
- Don't move the station at runtime (slot positions are cached at start).
