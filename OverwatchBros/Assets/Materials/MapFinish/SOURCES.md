# Material sources

The photographic texture maps in `Textures/` are from Poly Haven, released under
[CC0 1.0](https://polyhaven.com/license). Commercial use and modification are permitted.
Downloaded 2026-10-11. Each set includes the 2K JPG diffuse, OpenGL normal and AO maps.

- [Leafy Grass](https://polyhaven.com/a/leafy_grass) — Charlotte Baglioni. Lawn surface; 2 m repeat.
- [Grass Ground](https://polyhaven.com/a/grass_ground) — Charlotte Baglioni. Worn grass material.
- [Gravelly Sand](https://polyhaven.com/a/gravelly_sand) — Dario Barresi. Gravel paths; 2.5 m repeat.
- [Painted Plaster Wall](https://polyhaven.com/a/painted_plaster_wall) — painted exterior plaster.
- [Grey Roof 01](https://polyhaven.com/a/grey_roof_01) — weathered dark roof tiles; 8 m repeat.
- [Stone Wall](https://polyhaven.com/a/stone_wall) — natural masonry; 2 m repeat.

Normal maps are imported as Unity normal maps. AO uses linear sampling, diffuse uses
sRGB. Original project textures are retained; pre-polish material copies are stored
in `Originals/` when the editor applies the finish for the first time.

The grass blades, flower geometry, window boxes and terracotta pots are generated
locally by `MapVisualPolishSetup` and `MapFinishGeometry`. They require no external models.
