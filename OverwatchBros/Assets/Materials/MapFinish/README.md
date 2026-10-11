# Vzhled mapy Domašov

Jasný den kolem poledne: vysoko položené slunce, modrá obloha, neutrální denní barvy.
Fotografické povrchy trávy, štěrku a omítky doplňují nízká vegetace u okrajů cest,
květináče a truhlíky pod okny. Původní geometrie domů a stromů zůstává zachovaná.

## Nastavení v Unity

1. Nech vypnutý Play. Po prvním překladu skriptů se vzhled sestaví automaticky.
2. V horním menu otevři **BrosOverwatch → Mapa → Finalni vzhled → Nastaveni**.
3. V Inspectoru nastav slunce, sytost, odstín trávy nebo množství dekorací.
4. Klikni na **Pouzit vzhled a obnovit dekorace** a ulož scénu **Ctrl+S**.

Nastavení je v `Assets/Settings/MapFinishSettings.asset`. Samotný generátor je
`Assets/Editor/MapVisualPolishSetup.cs`. Za běhu hry se nic negeneruje.

Dekorace jsou v hierarchii **Map-Domasov → FinalniVzhled**. Tuto skupinu lze vypnout;
vypne to nové dekorace a barevný profil, nikoli úpravy sdílených materiálů či slunce.
Materiály před první úpravou jsou zálohované ve složce `Originals`.
Opětovné použití nastavení nahrazuje tuto skupinu: ruční úpravy jejích dětí je proto
potřeba nejdříve přesunout mimo skupinu. Generátor nesahá na rozmístění ani kolize mapy.

Tráva a květiny mají společné meshe po 16metrových úsecích. Nemají kolize, nevrhají
další stíny a vynechávají cesty, vstupy do tunelu a okolí spawnů/capture pointů.
Limit počtu trsů je horní mez: při nedostatku vhodných míst jich vznikne méně.

Kontrolní náhledy a statistiku generátor ukládá do `Temp/MapFinish_*.png` a
`Temp/MapFinishReport.txt`. Menu **Ulozit kontrolni nahledy** je vytvoří znovu.
Původní scéna se automaticky neukládá, aby se současně neuložily rozpracované úpravy.

Licence a zdroje textur jsou v [SOURCES.md](SOURCES.md).
