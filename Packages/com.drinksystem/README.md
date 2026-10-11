# Drink System - Bar Drink Station for VRChat

A networked black and gold bar drink station for VRChat worlds (desktop, PC VR and **Quest**):
- **Choose your drink:** 5 drink cards plus a Custom Drink. Each card has a painted drink icon and its own **POUR**
  button above a gold nozzle with a warm light.
- **Cups lock into place** with a click when you set them down under a nozzle. A glowing ring shows where it'll lock.
- **Options tower:** FROTH, STRENGTH and SPIRIT dials (LOW / MED / HIGH), FRUIT (Citrus, Berry, Tropical, Exotic,
  Apple, Grape) and STRAW (Gold Metal, Black Metal, Glass, Bamboo, Paper, Custom). The next drink you pour takes these
  options: foam height, color strength, drink effects, a fruit wheel on the rim and your straw. Custom Drink pours
  the fruit you picked.
- **Customize Cup kiosk:** set a glass on the pad, then pick a glass tint (Clear, Smoke, Amber, Rose, Violet, Emerald)
  and logo (Gold, Silver, None).
- **Glowing logo glasses** on the shelf on top, a garnish drawer with fruit, and a neon sign.
- Pour, fizz, slurp, click and lock **sounds**, drinking with Use, mixing, and **cup respawn** (button + automatic).

## Requirements
- A VRChat **world** project (Unity 2022.3) with VRChat Worlds SDK **3.8.1 or newer** (includes UdonSharp).
- TextMesh Pro Essential Resources. The builder offers to import them if they're missing.

## Install
Copy the `com.drinksystem` folder into your project's **`Packages/`** folder (not `Assets/`), then open the project.
It shows up as "Drink System - Bar Drink Station".

## Build it (one click)
1. **Select your bar counter** in the scene (optional). The station is placed on top of it.
   With nothing selected, it comes with its own counter where your Scene view is looking.
2. Click **Tools > Drink System > Build Drink Machine In Scene**.
3. Press Play (ClientSim) or **Build & Test**.

Everything is generated into `Assets/DrinkSystem/Generated` (meshes, painted icons, materials, sounds), and a
prefab is saved at `Assets/DrinkSystem/Drink Machine.prefab`. Run it again any time to rebuild.

To change the brand name, the logo letter, the neon text or the number of glasses, edit the constants at the top of
`Editor/DrinkMachineBuilder.cs` (`Brand`, `CupLogoText`, `NeonText`, `CupCount`) and build again.

## Scripts
| Script | Like your... | What it does |
|--------|--------------|--------------|
| `DrinkMachine` | `ManagerBar` | Finds flavors / slots / cups under `FlavorsParent` / `SlotsParent` / `CupsParent`. Handles the options panel, the cup customizer, VR finger presses, cup respawn, sounds and the optional drunk effect (same `UpdatePP` material properties). |
| `DrinkFlavor` | `LiquidParticles` | A drink: colors, foam, edge, transparency, pour speed, effect intensities. |
| `DrinkCup` | `LiquidGlass` + `TransformSync` | Locking, filling, drinking, mixing, options, style, respawn and all of the glass's networking. |
| `DrinkSlot` | (new) | One nozzle (snap point, stream, splash, pour sounds, its card), or the customizer pad. |
| `DrinkButton` | (new) | UI push button: click sound, press animation, sends an event (with a choice number for options). |

## Networking (optimized)
Manual sync everywhere. Nothing is sent while nothing changes:
- **Options panel:** 5 bytes on the machine, sent only when someone taps an option. Everyone sees the same choices.
- **Locked in a slot:** no position is sent. Every client puts the glass on the slot.
- **Held:** the glass's offset to the holder's hand bone is sent once. Everyone attaches it to that hand locally.
- **Dropped:** a few position updates per second while it falls, then a final one when it settles.
- **Pouring / drinking:** only start and stop are sent. Every client simulates the fill, and the final value fixes
  any drift.
- **Drink mix:** one byte per flavor. Options and style are a handful of bytes. Late joiners get the current state.

## VR and Quest
- Every panel is a world-space UI canvas with `VRC Ui Shape`, so the **laser pointer** (PC VR and Quest) and the
  desktop mouse work. In VR you can also **press buttons with your index finger**, with haptics.
- In VR you drink when the glass is at your mouth (`Mouth Distance`). Desktop drinks right away.
- Shaders (`DrinkSystem/Liquid`, `Cup`, `Stream`) are single pass, with no GrabPass or geometry shaders, and work
  with single-pass stereo. There are no real-time lights: the warm glow is emissive materials and soft additive
  light cones. The station parts are marked static for batching. Empty glasses hide their liquid, and the drunk
  effect is switched off when sober.

## Cup respawn
- A cup left alone is respawned after `Respawn Idle Seconds` (default 5 min), or `Respawn Empty Seconds` if it's empty
  and not in a slot (default 20 s). A respawned cup is empty, with no straw or garnish, and back to the default style.
- A cup that falls `Fall Respawn Distance` below its spawn is respawned right away.
- `Keep Cup Ready`: when someone takes the last glass from the shelf, the glass abandoned the longest is recycled onto it.
- To respawn somewhere else, set a cup's `Spawn Point`.
- **RESPAWN GLASSES** (on the options panel) puts every glass nobody is holding back on the shelf.

## Customize
- **Drinks and fruits:** edit them under `Drink Machine/Flavors`. The first 5 are the drink cards, and the 6 after
  them are the fruits (`Fruit Flavor Start` = 5). Card names and icons are made by the builder.
- **Option strength:** `Froth Levels` (foam multiplier) and `Spirit Levels` (extra drink effect) on `DrinkMachine`.
- **Sounds:** the builder makes procedural stand-ins (button click, deny buzz, glass lock clack, unlock, valve open,
  pour loop, valve close + drips, full ding, fizz, slurp loop, respawn pop). Drop your own clips into the **Sounds**
  section of `DrinkMachine`.
- **Your own liquid shader:** `DrinkCup` sets the same properties as `LiquidGlass` (`_FillHeight`, `_GlassRemap`,
  `_ColorMain`, `_FoamColor`, `_EdgeColor`, `_Transparency`, `_FoamHeight`, `_FoamLineSmoothness`, `_EdgeStrength`,
  `_EdgePower`), plus `_WobbleX` / `_WobbleZ`. Put your material on the glass's `Liquid` renderer.
- **Your own glass model:** the cup's pivot must be at its bottom center, and its `Liquid` child's mesh must sit on
  the cup's axis. Enable Read/Write on imported liquid meshes, or the liquid's bounds are used instead.
- Don't move the station at runtime (slot positions are cached at start).
