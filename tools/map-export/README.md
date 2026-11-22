# Export mapy Dust II

Tyto dva skripty slouží k opakovatelnému vytvoření herního modelu:

1. `export_unity.py` exportuje SourceIO scénu z Blenderu do GLB a JSON metadat.
   Spuštění: `blender -b --disable-autoexec mapa.blend --python export_unity.py -- vystupni-slozka`
2. `compact_glb.py` zmenší GLB kvantizací barev a normál při zachování pozic a topologie.
   Spuštění: `python compact_glb.py vstup.glb vystup.glb`

Výsledný model a metadata patří do `OverwatchBros/Assets/Models/Dust2`.
Unity import a přípravu prefabu zajišťují `Dust2ModelImporter.cs` a `Dust2Setup.cs`.
Pracovní `.blend` kopie a jednorázové diagnostické výstupy uchovávej mimo Git.

# Sapa (importovaná Hanamura)

Sapa je samostatná mapa; původní Hanamura zůstává zachovaná.
Zdrojový `.blend` patří mimo Unity `Assets` a mimo Git.

Export v Blenderu 5.2:

```text
blender -b --disable-autoexec --python-exit-code 1 mapa.blend --python tools/map-export/export_overwatch.py -- OverwatchBros/Assets/Models/Sapa
```

`export_overwatch.py` zachovává viditelnou geometrii, UV souřadnice a obě
vrstvy vertex barev. Původní zabalené PNG kopíruje beze změny bajtů a rozlišení.
`overwatch_shader.py` a `overwatch_shader.template` převádějí použité OWM uzly
na výpočty materiálů URP, včetně vrstvení, masek a zabalených normál.
Nepodporovaný typ uzlu ukončí export chybou. Zdrojový `.blend` se neukládá.
Nepoužívá se decimace, kvantizace geometrie ani zmenšování textur.

V Unity spusť `BrosOverwatch > Mapa > Sapa - importovat a overit`.
`SapaSetup` vytvoří materiály, kolize, spawny u brány a chrámové zahrady,
dva dobývací body a samostatný prefab `Map_Sapa`. Potom ověří navigaci
a vloží prefab do `SampleScene`. Tento krok obnovuje generovaný prefab
a jeho herní značky; další změny modelu prováděj ve zdrojovém `.blend`.

Textury se importují bez GPU komprese a bez změny rozměrů. Distantní pozadí,
sklo a projekční obtisky zůstávají viditelné, ale nedostávají pevnou herní kolizi.
Mapa používá vlastní světlo a prostředí, které se při přepnutí mapy obnoví.

Validace pro Unity batch mode (editor projektu musí být zavřený):

- `SapaSetup.ImportAndValidate`: import, rozměry textur, kolize, spawny a cesty k bodům.
- `SapaPlaytest.Deathmatch`: skutečný localhost host, hráč, bot a pohyb.
- `SapaPlaytest.Capture`: skutečné zabrání A/B, přesun spawnů a výměna stran.
- `SapaPlaytest.Preview`: náhled z hráčovy kamery bez zbraně po inicializaci herního rendereru.
- `SapaPlaytest.Lighting`: vykreslení kontrolní plochy se sluncem, bez slunce,
  bez stínů a s bodovým světlem; ověřuje skutečnou reakci shaderu ve Forward+.
- `SapaSetup.DistributionValidation`: náhled do menu a samostatný LZ4 balíček bez omezení velikosti.

Před `DistributionValidation` spusť `SapaPlaytest.Preview`. Náhled vzniká
až po inicializaci herního rendereru, aby odpovídal zobrazení při hraní.

Výstupy kontrol jsou ve `_MIMO_GIT_K_PRESUNU/Sapa` a nepatří do Gitu.
`Sapa.glb` používá Git LFS; exportovaný model překračuje velikost běžného Git souboru.
