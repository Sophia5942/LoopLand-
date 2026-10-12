# FBT Mayfair – Can Fridge + Bin

Get a can from the fridge, open it, drink it, then bin it.

## Setup (one click)
1. Drag the **FBTCans** folder into your project's **Assets** folder.
   - It needs your **TransformSync.cs** in the project. It's already there if your bar works.
2. Wait for Unity to finish compiling.
3. In the Scene view, look at the spot where you want the fridge.
4. Click **FBT Mayfair > Build Can Fridge + Bin** in the top menu.
5. "FBT Can Fridge + Bin" appears on the floor, with your trash can next to it. Move or rotate it as one object if you like.
   - Already built the old version? Replace the FBTCans folder, then click the menu again and pick **Replace**.

## In game
- **Click the fridge door** ("Open / close fridge"). The door swings open with a seal-pop sound, and a fresh can is waiting at the front of the middle shelf.
- **Take the can out** (grab it). The door closes by itself after 8 seconds, or click it again to close it.
- Press **Use** (trigger / left click) to open the can. The tab flips up with a "pssht".
- **Hold Use** after that to sip.
- **Drop it on the floor** and it clanks. Everyone nearby hears it.
- **Drop it in the bin** to clean it up. It clunks and disappears.
- A can left lying around cleans itself up after 5 minutes.

## Settings
- **Fridge** (CanFridge on "Fridge Door"):
  - **Cooldown Seconds**: the wait before the same player can get another can. Set it to 600 for a 10-minute cooldown. The door still opens during the cooldown, just with no new can.
  - **Auto Close Seconds**: how long until the door closes by itself. 0 = it stays open.
  - **Open Angle**: how far the door swings.
- **Cans** (FBTCan on each can):
  - **Cleanup After Seconds**: the auto clean-up time. 0 turns it off.
  - **Drop Sound Speed**: how hard a can has to hit something to clank.
- There are **8 cans** in the pool. When all 8 are out, the oldest one nobody is holding is reused.

## Networking / performance
- Everyone sees the same cans: spawned, opened, held, moved and binned.
- Each can syncs 3 bools (Manual sync), and only when something changes. Movement uses your TransformSync.
- The fridge syncs 1 bool (door open/closed). The bin doesn't sync anything.
- The floor clank is one small network event, at most about 3 per second per can.
- There's no Update loop. The door and tab animations only run while they're moving.
- It uses mobile shaders and small meshes, so it's fine on Quest.
- Your scripts are not changed.
