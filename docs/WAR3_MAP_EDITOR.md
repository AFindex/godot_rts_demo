# War3 Map Editor workflow

The `War3 Map Editor` add-on is the authoritative map-making workflow for the
Warcraft III RTS scene. A map is a saved package on disk, not state retained in
an editor scene node. `War3MapAuthoring3D` is only the live 3D view/controller
for that package.

## Package layout

Runtime discovery scans `res://war3_rts/maps/*/manifest.json` and sorts valid
entries by display name and id.

```text
war3_rts/maps/<map_id>/
  manifest.json       # catalog metadata and asset filename
  map.w3map.json      # versioned authoring + runtime payload
  preview.png         # optional catalog artwork
```

`manifest.json` format version 1 contains id, display name, description,
author, recommended player count, preview path, built-in flag and the map asset
filename. A malformed manifest or missing asset is skipped by the catalog.

`map.w3map.json` format version 1 contains metadata, either a migrated terrain
recipe or the canonical base64 payload of a `TerrainMapSnapshot` v2, explicit
object records, optional deterministic PCG recipes and a composite runtime hash.
The canonical payload preserves surface ids, pathing, buildability, discrete
cliff levels, ramps and every continuous fine-height point. Saving a migrated
built-in map materializes its terrain and generated objects. Save immediately
reloads the package and compares the composite hash.

## Authoring in Godot

1. Open a 3D scene and use the **War3 Map Editor** dock.
2. Choose **New** for a 128×96 starter map or **Open** for an existing
   `*.w3map.json`. The editor creates/selects `War3MapAuthoring3D`.
3. Select tool, shape, radius, strength and value. The yellow line is the
   authoritative current-tool state.
4. Drag left mouse in the 3D viewport. Fast motion is interpolated. Mouse-up
   commits one Godot UndoRedo action; no history item is added per frame. Escape
   cancels the stroke.
5. Run **Validate**. Errors include a stable code and, where applicable, cell
   coordinates and object id.
6. Use **Save**, **Save As** or **Validate + Runtime Export**. Invalid maps are
   blocked; a sibling manifest is written and the editor filesystem is rescanned.

Terrain tools cover surface painting; continuous raise, lower, smooth and
deterministic perturbation; discrete cliff levels; ramp placement/removal;
ground pathing and buildability. Object tools cover player spawns, gold mines,
trees and erasure. Circle, square and diamond brushes are available. The 3D
preview uses the runtime `Rts3DTerrainPresenter`, War3 dual-grid material,
classic cliff meshes and `CliffTrans` ramps.

The older `RtsTerrainAuthoring2D` workflow remains for existing showcases, but
does not define a complete War3 map package.

## Validation and runtime export

Export validates the schema and dimensions, terrain payload, ramp fields,
unique spawn slots, owned starting resources, object bounds and ids, spawn
traversability, resource exclusion zones and connectivity between spawns.

Resolved resource objects are expanded once. Their footprints create navigation
obstacles and the same records create economy nodes. Presentation enumerates
those simulation nodes, keeping visuals, interaction bounds and collision on the
same layout.

## Runtime selection

Entering `res://war3_rts/War3Rts.tscn` normally opens the map browser. Only after
selection does `War3Rts` build terrain, navigation, clearance, resources,
starting armies and camera focus. Use `--war3-map=<id>` for automation.

`lordaeron_crossroads` is the migrated 6400×3840 built-in default. Its compact
checked-in package references the legacy terrain and PCG migration recipes and
declares their resolved composite hash. Compatibility tests compare terrain
hash, spawns and all 286 resources with the old factories. Opening and saving it
materializes a fully explicit editable package.

## Extending object and PCG layers

Add new object kinds and runtime behavior in `War3MapAssets.cs`. Keep stable
object ids and make navigation and scenario setup consume the same resolved
record rather than recomputing geometry.

Register deterministic PCG generator ids in `War3MapCodec.TryExpand`. Store seed
and parameters in `War3MapPcgLayer`, produce stable object ids, validate exclusion
zones and cover the resulting hash in `War3MapAssetSelfTest`. Do not create an
independent obstacle-only or visual-only layout.

## Verification

```powershell
dotnet build .\rts-demo-1.csproj --no-restore
godot --headless --path . -- --self-test
godot --editor --headless --path . -- --war3-map-editor-smoke
godot --headless --path . res://war3_rts/War3Rts.tscn -- `
  --war3-rts-smoke --war3-map=lordaeron_crossroads
```

Visual QA uses `--war3-map-editor-capture` in editor mode and
`--war3-rts-terrain-capture --war3-map=lordaeron_crossroads` at runtime.

## Current limitations

- UndoRedo stores one full before/after JSON payload per stroke. It is correct
  and bounded by stroke count, but chunk diffs would use less history memory on
  very large maps.
- Decoration and arbitrary blocker records exist in the model but do not yet
  have dedicated dock tools.
- The built-in migration recipe has fixed parameters. Saving once creates the
  explicit editable asset.
