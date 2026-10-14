# Ornate frame textures

These scripts generate the textures in `../Textures` for `../Models/OrnateFrame.fbx`, which holds two stacked
frames that share one UV atlas. Unity ignores this folder (`~` suffix).

| Script | Output |
| --- | --- |
| `gen_frame.py` | bakes the carved relief (twisted rope, acanthus vine, corner rosettes, bead-and-reel) into the frame UVs |
| `shade_frame.py` | `Frame_<Finish>_Albedo / _MetallicSmoothness`, `Frame_Normal`, `Frame_Occlusion` (AntiqueGold, DarkBronze, BlackGold) |
| `gen_canvas.py` | `Canvas_Set<N>_*_Albedo` atlases (one painting per frame, top and bottom), `Canvas_Normal`, `Canvas_MetallicSmoothness` |
| `preview.py` | a lit front-view preview, for checking the result |
| `package.py` | copies everything into the package and writes the `.meta` and `.mat` files |

```
python3 -I gen_frame.py Frame.fbx . out 4096
python3 -I shade_frame.py out/bake.npz out
python3 -I gen_canvas.py reference.webp canvas 2048
python3 -I package.py out canvas Frame.fbx ../
```

The materials use the Built-in Standard shader with Metallic in R and Smoothness in A.
Frame materials map `Material.013` (the moulding). Canvas materials map `Material.014` (the picture panels).
