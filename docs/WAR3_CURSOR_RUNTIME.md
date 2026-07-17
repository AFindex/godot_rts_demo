# War3 cursor runtime

The War3 skirmish scene uses the exported Warcraft cursor atlases directly.
The export pack stays below `.gdignore`, so cursors are loaded through
`War3RuntimeAssets` rather than Godot's resource importer.

## Themes

Four complete themes are available:

- Human (the default for the current Human skirmish)
- Orc
- Undead
- Night Elf

Use a different theme for integration testing with:

```powershell
godot --path . res://war3_rts/War3Rts.tscn -- `
  --war3-map=lordaeron_crossroads --war3-cursor-race=orc
```

Accepted values are `human`, `orc`, `undead`, and `nightelf`.

## Runtime modes

The original 32×32 atlas cells and 15 Hz animation cadence are used for:

- normal and selectable hover
- command target and animated valid target
- invalid target
- building/item placement
- all eight edge-scroll directions
- UI pointing-hand, crosshair, forbidden, drag, drop, and move shapes

Point and unit abilities use the read-only ability command preview before the
cursor is changed. Valid targets use the animated target cursor; invalid,
hidden, out-of-range, cooldown, or insufficient-mana targets use the invalid
cursor. The world presenter also draws the effective cast-range ring and the
ability area/target ring without issuing or mutating a simulation command.

## Verification

```powershell
godot --headless --path . -- --war3-cursor-self-test
godot --headless --path . -- --ability-self-test
godot --headless --path . res://war3_rts/War3Rts.tscn -- `
  --war3-rts-smoke --war3-map=lordaeron_crossroads `
  --war3-cursor-race=orc
```

The cursor self-test validates all four 256×128 atlases, race parsing, and the
normal/target/invalid/scroll frame mapping. It also validates the original
`UI\Feedback\Confirmation\Confirmation.mdx` selected-point model: accepted
move/right-click ground orders play its three-arrow focus animation in green,
while attack-move and hostile-target orders play it in red. The model is
prewarmed with the normal skirmish presentation assets, so the first command
does not parse the GLB on the click frame. The ability test verifies that target
preview returns the same validation codes as issue while leaving mana, health,
cooldowns, events, and command logs unchanged.
