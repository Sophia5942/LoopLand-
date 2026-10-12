# FBT Mayfair – Can Fridge + Bin

Uses **your own fridge model**. You click inside the fridge ("Get A New Can"), a can appears in front on the shelf, you grab it, open it, drink, and bin it.

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

## In game
- **Click inside the fridge** ("Get A New Can [10min Cooldown]"). A can appears on the shelf.
  - It stays put until someone grabs it, so it can't fall through the fridge.
- **Grab it** and press **Use** (trigger / left click) to open it. The tab flips up, with a real can-opening snap and fizz.
- **Hold Use** to drink. You hear quiet gulps.
- **Drop it on the floor** and it clanks.
- **Drop it in the bin** to clean it up.
- A can left lying around cleans itself up after 5 minutes.

## Settings
- **Get A New Can** (CanFridge):
  - **Cooldown Seconds**: 600 (10 min) per player. If you change it, also change the text: select the object, then Udon Behaviour > Interaction Text.
  - To make the click box bigger or smaller, change the Box Collider's Size. The see-through box is only a guide.
- **Cans** (FBTCan on each can):
  - **Cleanup After Seconds**: the auto clean-up time. 0 turns it off.
  - **Drop Sound Speed**: how hard a can has to hit something to clank.
- There are **8 cans** in the pool. When all 8 are out, the oldest one nobody is holding is reused.

## Networking / performance
- Everyone sees the same cans: appearing, opened, held, moved and binned.
- Each can syncs 3 bools (Manual sync), and only when something changes. Movement uses your TransformSync.
- The floor clank is one small network event, at most about 3 per second per can.
- The spawner and bin don't sync anything.
- There's no Update loop. The tab animation only runs for a quarter of a second when a can opens.
- It uses mobile shaders and small meshes, so it's fine on Quest.
- Your scripts are not changed.
