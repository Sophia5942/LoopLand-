# FBT Mayfair – Can Fridge + Bin

Get a can from the fridge, open it, drink it, then bin it.

## Setup (one click)
1. Drag the **FBTCans** folder into your project's **Assets** folder.
   - It needs your **TransformSync.cs** in the project. It's already there if your bar works.
2. Wait for Unity to finish compiling.
3. In the Scene view, look at the spot where you want the fridge.
4. Click **FBT Mayfair > Build Can Fridge + Bin** in the top menu.
5. "FBT Mayfair Can Fridge" appears on the floor, with your trash can next to it. Move or rotate it as one object if you like.

## In game
- **Click the fridge** ("Get a new can") and a can pops out on top.
- **Pick it up** and press **Use** (trigger / left click) to open it. The tab flips up with a "pssht".
- **Hold Use** after that to sip.
- **Drop it in the bin** to clean it up. It clanks and disappears.
- A can left lying around cleans itself up after 5 minutes.

## Settings
- **Fridge** (CanFridge on "Fridge"):
  - **Cooldown Seconds**: the wait before the same player can take another can. Set it to 600 for a 10-minute cooldown.
- **Cans** (FBTCan on each can):
  - **Cleanup After Seconds**: the auto clean-up time. 0 turns it off.
- There are **8 cans** in the pool. When all 8 are out, the oldest one nobody is holding is reused.

## Networking / performance
- Everyone sees the same cans: spawned, opened, held, moved and binned.
- Each can syncs 3 bools (Manual sync), and only when something changes. Movement uses your TransformSync.
- The fridge and bin don't sync anything.
- There's no Update loop. The tab animation only runs for a quarter of a second when a can opens.
- It uses mobile shaders and small meshes, so it's fine on Quest.
- Your scripts are not changed.
