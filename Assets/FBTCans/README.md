# FBT Mayfair – Can Fridge + Bin (Mayfair Energy)

Uses **your own fridge model**. You click inside the fridge ("Get A New Can"), a can appears in front on the shelf, you grab it, open it, and bin it.

## Setup
1. Drag the **FBTCans** folder into your project's **Assets** folder.
   - It needs your **TransformSync.cs** in the project. It's already there if your bar works.
   - Already have an older FBTCans? Delete it first, then drag in this one.
2. Wait for Unity to finish compiling.
3. In the Hierarchy, **click your fridge** to select it.
4. Click **FBT Mayfair > Build Can Fridge + Bin** in the top menu. If you built an older version, choose **Replace**.
5. Select **Get A New Can** (inside "FBT Can Fridge + Bin") and move it onto a shelf inside your fridge:
   - Its **blue arrow** must point **out of the fridge**, towards the glass.
   - The **see-through box** is the click area.
   - The **see-through can** shows where new cans appear, in front of the box.
   - Both helpers are only visible in the editor. They aren't uploaded.
   - Got two fridge doors? Duplicate the "Get A New Can" object (Ctrl+D) and put one behind each door. Both use the same cans.
6. Move **Trash Bin** where you want it.
7. Save the scene (Ctrl+S).

## The can
- The cans are **your can model** (`Models/MayfairEnergyCan.fbx`) with the **Mayfair Energy** label (`Textures/MayfairEnergy_Can.png`):
  - Black can with a torn neon-green **M** and **MAYFAIR ENERGY** on the front.
  - The back has a small M and some text.
- On the lid there's a ring-pull and rivet, and the opening appears when you open it.
- The front of the label faces out of the fridge when a can appears.

## In game
- **Click inside the fridge** ("Get A New Can"). A can appears on the shelf. Each player can get one every 10 seconds.
  - It stays put until someone grabs it, so it can't fall through the fridge.
- **Grab it** and press **Use** (trigger / left click) to open it. Everyone sees the opening animation, with a can-opening snap and fizz:
  1. The ring-pull lifts and the can gives a little jolt.
  2. The hole opens in the lid.
  3. The tab settles back down.
- **Drop it on the floor** and it clanks.
- **Drop it in the bin** to clean it up.
- A can left lying around cleans itself up after 5 minutes.

## Settings
- **Get A New Can** (CanFridge):
  - **Cooldown Seconds**: 10 per player.
  - To make the click box bigger or smaller, change the Box Collider's Size. The see-through box is only a guide.
- **Cans** (FBTCan on each can):
  - **Cleanup After Seconds**: the auto clean-up time. 0 turns it off.
  - **Drop Sound Speed**: how hard a can has to hit something to clank.
- There are **8 cans** in the pool. When all 8 are out, the oldest one nobody is holding is reused.

## Anti-abuse (hacked / modded clients)
- **There can never be more than 8 cans.** Spamming can't create new objects. Worst case, it moves the same 8 cans around.
- **The cooldown is checked by other players too.** Every player sees who spawned a can and when. To take a free can, a player has to get it from its current owner, and that owner's game refuses if the player spawned one in the last ~7 seconds. So a hacked client that skips its own cooldown still gets refused.
- **Nobody can snatch a can out of your hand.** The owner refuses, and the pickup has Disallow Theft on.
- **Spamming the drop clank does nothing.** Each player's game plays it at most 4 times a second per can.
- Internal script functions can't be called over the network (they start with `_`).

## Networking / performance
- Everyone sees the same cans: appearing, opened, held, moved and binned.
- Each can syncs 3 bools and a spawn counter (Manual sync), and only when something changes. Movement uses your TransformSync.
- The floor clank is one small network event, at most about 3 per second per can.
- The spawner and bin don't sync anything.
- There's no Update loop. The opening animation only runs for under a second when a can opens.
- It uses mobile shaders and small meshes, so it's fine on Quest.
- Your scripts are not changed.
