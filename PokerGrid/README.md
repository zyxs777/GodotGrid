# PokerGrid

Portable Godot C# poker-grid module.

Entry scene:

```text
res://PokerGrid/square_grid.tscn
```

Move the whole `PokerGrid` folder into another Godot C# project, then open or instance `PokerGrid/square_grid.tscn`.

Folder contents:

- `square_grid.tscn`: main 6x6 poker grid scene.
- `Scenes/grid_cell.tscn`: reusable card grid cell scene.
- `Scripts/`: C# scripts for card display, flipping, matching, UI buttons, and highlights.
- `Textures/`: square background, card back, and suit textures.
