# Pub bar set

Dark mahogany bar modelled on a classic hotel bar. It keeps the footprint of the original `Bar.fbx`
(6.535 m long, 1.149 m deep, 1.61 m tall), which is about 1.5x real-world scale.

| Model | Tris | Materials |
| --- | --- | --- |
| `Bar_Counter` | 7.7k | Mahogany, MahoganyTop, Brass |
| `Bar_Stool` | 2.9k | Leather, Brass, Mahogany |
| `Bar_BackBar` | 5.0k | Mahogany, MahoganyTop, Mirror |
| `Bar_BackBarBottles` | 23k | WineBottle_Rouge / Reserve / Blanc (from `Art/WineBottle`) |
| `Bar_PendantLamp` | 1.0k | Brass, FrostedGlass (emissive) |

All pivots are at floor level, centred. The pendant's pivot is at the ceiling and the lamp hangs 1.3 m down.

## Layout used in the preview (Blender X/Y = Unity X/Z)

- Counter at the origin. The customer side faces -Y, and the round end is at +X. The -X end is flat for a wall.
- Back bar and bottles at Y = +1.95 (a 1.15 m bartender aisle).
- Stools at Y = -1.0, every 0.95 m from X = -2.6.
- Pendants at ceiling height 4.4 m, over the counter (Y = 0), every 1.3 m from X = -2.4.

`Bar_Scene.blend` has this arrangement with lights. `Bar_Assets.blend` has just the five models.
For Unity lighting, put a warm point light (range ~3, colour 1, 0.72, 0.45) inside each pendant shade.

## Regenerating

```
python3 -I gen_bar_tex.py ../Textures 2048
python build_bar.py -- ../Textures ../../WineBottle/Tools~/WineBottle.blend out   # Blender / bpy
python3 -I pack_bar.py ../../Frames/Tools~/package.py out ../Textures . .. ../../Frames/Models/OrnateFrame.fbx.meta
```
