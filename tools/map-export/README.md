# Export mapy Dust II

Tyto dva skripty slouží k opakovatelnému vytvoření herního modelu:

1. `export_unity.py` exportuje SourceIO scénu z Blenderu do GLB a JSON metadat.
   Spuštění: `blender -b --disable-autoexec mapa.blend --python export_unity.py -- vystupni-slozka`
2. `compact_glb.py` zmenší GLB kvantizací barev a normál při zachování pozic a topologie.
   Spuštění: `python compact_glb.py vstup.glb vystup.glb`

Výsledný model a metadata patří do `OverwatchBros/Assets/Models/Dust2`.
Unity import a přípravu prefabu zajišťují `Dust2ModelImporter.cs` a `Dust2Setup.cs`.
Pracovní `.blend` kopie a jednorázové diagnostické výstupy uchovávej mimo Git.
