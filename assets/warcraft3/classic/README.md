# Warcraft III Classic asset pack

This directory mirrors the converted Warcraft III Classic archive used by the
asset lab. The original virtual paths are preserved so GLB files can resolve
their external PNG dependencies.

- `models/`: runtime-loaded GLB models and baked transform animations.
- `textures/`: BLP textures converted to PNG, plus native image assets.
- `metadata/`: Warcraft-specific particle, Ribbon and event data (`*.war3.json`).
- `audio/`: extracted sound assets retained for later event reconstruction.
- `fonts/`: extracted font assets.
- `catalog/manifest.json`: model index used by the Godot asset lab.

The `.gdignore` file is intentional. Importing thousands of archive assets at
editor startup is wasteful; `War3AssetLab` loads GLB, image and JSON files by
absolute path at runtime. This asset pack is for local research and requires a
filesystem-backed project. It is not automatically embedded in exported PCKs.

