# Drink System - Soda Machine for VRChat

A networked soda fountain inspired by fast-food sims: set a cup down under a nozzle and it **locks into place** with a
click, press the nozzle's **push button** to pour, and the cup fills with a rising-pitch pour sound, then fizzes.
Pick it up and hold **Use** (trigger / left click) to drink. It works on desktop, PC VR and **Quest**.

## Requirements
- A VRChat **world** project (Unity 2022.3) with VRChat Worlds SDK **3.8.1 or newer** (includes UdonSharp).
- TextMesh Pro Essential Resources. The builder offers to import them if they're missing.

## Install
Copy the `com.drinksystem` folder into your project's **`Packages/`** folder (not `Assets/`), then open the project.
It shows up as "Drink System - Soda Machine". The `Drink_Machine_1` model is included in `Models/`.

## Build it (one click)
1. Click **Tools > Drink System > Build Drink Machine In Scene**. It goes where your Scene view is looking.
2. Press Play (ClientSim) or **Build & Test**.

It builds `Drink Machine` with the `Drink_Machine_1` model on a counter, 6 slots (one per nozzle, 6 flavors), a stack
of 6 cups next to the machine and a **RESPAWN CUPS** button. Assets go to `Assets/DrinkSystem/Generated`, and a
prefab is saved at `Assets/DrinkSystem/Drink Machine.prefab`.

## Scripts
| Script | Like your... | What it does |
|--------|--------------|--------------|
| `DrinkMachine` | `ManagerBar` | Finds flavors / slots / cups under `FlavorsParent` / `SlotsParent` / `CupsParent`, VR finger presses, cup respawn, sounds, optional drunk effect (same `UpdatePP` material properties). |
| `DrinkFlavor` | `LiquidParticles` | A drink: colors, foam, edge, transparency, pour speed, effect intensities. |
| `DrinkCup` | `LiquidGlass` + `TransformSync` | Locking, filling, drinking, mixing, respawn and all of the cup's networking. |
| `DrinkSlot` | (new) | One nozzle: snap point, stream, splash, pour sounds and its button panel. |
| `DrinkButton` | (new) | UI push button: click sound, press animation, sends an event. |

## Networking (optimized)
Every cup uses **manual sync** and only the owner sends. Nothing is sent while nothing changes:
- **Locked in a slot:** no position is sent. Every client puts the cup on the slot.
- **Held:** the cup's offset to the holder's hand bone is sent once. Everyone attaches it to that hand locally, so it's
  smooth without streaming positions every frame.
- **Dropped:** a few position updates per second while it falls, then a final one when it settles.
- **Pouring / drinking:** only start and stop are sent. Every client simulates the fill, and the final value fixes
  any drift.
- The drink mix is one byte per flavor. Late joiners get the current state.

## VR and Quest
- The panels are world-space UI canvases with `VRC Ui Shape`, so the **laser pointer** (PC VR and Quest) and the
  desktop mouse work. In VR you can also **press the buttons with your index finger**, with haptics.
- While you hold a cup near a free slot, a ring shows where it will lock.
- In VR you drink when the cup is at your mouth (`Mouth Distance`). Desktop drinks right away.
- The shaders (`DrinkSystem/Liquid`, `Cup`, `Stream`) are single pass, with no GrabPass or geometry shaders,
  and work with single-pass stereo. Liquid is hidden when empty, and the drunk effect is switched off when sober.

## Cup respawn
- **RESPAWN CUPS** puts every cup that nobody is holding back on the stack, empty.
- A cup left alone is respawned after `Respawn Idle Seconds` (default 5 min), or `Respawn Empty Seconds` if it's empty
  and not in the machine (default 20 s).
- A cup that falls `Fall Respawn Distance` below its spawn is respawned right away.
- `Keep Cup Ready`: when someone takes the last cup from the stack, the cup abandoned the longest is recycled onto it.
- To respawn somewhere else, set a cup's `Spawn Point`.

## Customize
- **Flavors:** edit them under `Drink Machine/Flavors`, and pick each slot's flavor with `Flavor Index`.
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
