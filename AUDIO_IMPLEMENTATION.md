# Zapojení vybraných zvuků

Zdroj výběru: `tools/sound-collector/collection.json`, revize **416**. Zapojeno je všech **72 vybraných položek**, včetně položek bez fajfky Hotovo, a pět výchozích doplnění. Banka má **117 sad událostí**, **298 použitých WAV souborů** a původ **310 zdrojových nahrávek**. Aktuálně přiřazené klipy mají přednost před výchozími náhradami. Honzovo přebití používá pět vybraných vložení náboje a vybrané mechanické pohyby podle délky animace. Původní sbírka i kandidáti zůstávají zachované.

Aktuální opravy, nevyzkoušené situace a poznámka o odšumění Honzových hlasů jsou v `AUDIO_PLAYTEST.md`.

Kandidátní audio v `Sound/sounds_dir` je nyní ignorovaná lokální cache; v Gitu zůstávají manifesty a výběry. Nepoužité kopie byly odstraněny, vybrané vstupy zůstávají lokálně pro export. Obnova všech kandidátů: `python tools/sound-collector/restore_candidates.py --all`. Export si potřebné zdroje obnoví automaticky a neodkazované meziklipy z herní složky zase odstraní. Pro hraní a build stačí verzované klipy v Assets.

## Co hra používá

### Respawn pickupů

Při skutečném obnovení pickupu hraje TF2 `items/spawn_item.wav` přes samostatnou sadu `pickup_respawn`. Server odešle jednu událost všem klientům z místa předmětu (hlasitost 0,65, prostorový dosah 18 m). První načtení mapy, připojení hráče a reset kola tuto událost neposílají; při replayi se použije zaznamenané přehrání.

Klip je přímo v `Assets/Audio/Collected/TF2/items` a zvukové bance, takže hra nepotřebuje externí knihovnu. `import_pickup_respawn.py` zapojil pouze tuto explicitně vyžádanou sadu a ponechal ostatní exportované zvuky i uložené volby ve sbírce beze změny. Pro další úplný export je zdroj uložený v `integration_overrides.json` jako výchozí `pickup_respawn`.

### Ostatní zapojené zvuky

- Mirek: plné a rychlé natažení tětivy, vypuštění, čtyři varianty průletu a dopady podle materiálu. Natahování končí při výstřelu, přerušení a výměně zbraně; zvuky slyší i ostatní hráči.
- Pova a Kondulič: výstřel háku, začátek navíjení a zastavení. Navíjení se při návratu ukončí.
- Honza: samostatné vypuštění balvanu, tři smyčky valení, kamenné dopady a vybraný závěrečný výbuch. Past používá vybrané ocelové sklapnutí pouze při zavření čelistí.
- Nově zvolené výstřely: TF2 granátomet pro Honzu, Ambassador pro Šindela a Capper pro Mošina. Viktor, David a Kondulič mají dál výchozí střelby, pokud u nich není přiřazená nová náhrada.
- David: vybraná uspávací šipka a crossbow nahrávka jako vrstva vypuštění biotické střely. Viktor: vybrané nabíjení zaměřovače a samostatný ping při získání cíle.
- Max: vybrané efekty Rozpolcení, stínové formy, dlouhého řezu a vpádu do stínu.
- Dědova slivovice: potvrzení pickupu, kontakt lahve, pití a následné Demomanovo grknutí. Tyto fáze se nevybírají náhodně místo sebe.
- Zabírání bodu: vybraný hodinový zvuk je vystřižený na jeden 0,161s tik. Zabrání bodu používá zvolenou znělku.

Automatické herní ověření hosta: `tools/sound-collector/research/SELECTED_AUDIO_PLAY_TESTS.xml`. Ověřeno bylo skutečné přehrávání, oddělení fází, ukončování zvuků a návaznost pití/grknutí. Samostatný test respawnu pickupů je v `PICKUP_RESPAWN_PLAY_TESTS.xml`. Zůstal zachovaný obsah všech 209 původních audio souborů mimo generovanou sbírku oproti současnému HEAD.

- Kroky podle povrchu: dřevěná podlaha, pevné dřevo, beton/kámen, dlažba, štěrk, tráva, hlína, písek, koberec, guma, kov a mělká voda. Náhodný výběr neopakuje bezprostředně předchozí variantu pro danou postavu a událost.
- Dopady po skoku mají samostatné sady. Klipy `land*` se nepouštějí při běžné chůzi. Vstup do vody, výstup a větší splouchnutí jsou oddělené.
- Odraz, dvojskok, lezení po žebříku, vytažení na hranu a náraz odhozené postavy do stěny.
- Zásahy kulek a sečných zbraní podle povrchu. U sekery a kosy odděleně švih a kontakt; kontaktní vrstva může doplnit zásah materiálu. Bez zásahu se kontaktní zvuk nepřehrává.
- Přebíjení Viktora, Honzy, Konduliče, Davida, Mošina a Šindela: vybrané části jsou sestavené do sekvence, jejíž délka se přizpůsobí přebití. Přerušení zastaví zvuk.
- Optika Konduliče: přiblížení, odchod z optiky a připravená nabitá rána; aktivace infra vidění. Nabíjení Viktorova taktického zaměřovače.
- Fyzická část Šindelova granátu, Viktorův pulzní granát, hod Davidova granátu, kontakt a výbuch Honzovy nálože, jednotlivé fáze balvanu a odrazy granátů.
- Omráčení, tělesný ruch při smrti, zamítnutá akce a prázdná střelná zbraň.
- Sebrání zdraví, Tramalu, slivovice a karburátoru. Větší lékárna sdílí zdravotní potvrzení, pokud nemá vlastní vybraný klip.
- Místní smyčka vody podél skutečných vodních zón, oheň kotle a motor rolby. Krb a gril sdílejí vybraný oheň, pokud nemají samostatný výběr. Při vypnutí mapy se její zvuky umlčí.
- Pohybové zvuky se přenášejí ostatním hráčům. Replay dohledá konkrétní vybraný klip a zachová výšku a dosah zaznamenaného efektu.

## Zachované nahrávky

Dosavadní soubory audia, hlášky, ručně přiřazené zvuky zbraní a schopností, hudba, zvuk svatého granátu při přípravě, Bardovy skladby, Monster, tobogán a televizní zvuky se nepřesouvají ani nepřepisují. Nové nahrávky nahrazují generované efekty v odpovídajících událostech nebo doplňují dosud chybějící ruch. Tam, kde už existuje vlastní zvuk zbraně/schopnosti, má přednost. Bez vhodného výběru zůstává dosavadní fallback.

Před importem byly uloženy kontrolní otisky 454 původních souborů (audio a jeho nastavení, data a scéna); výsledek porovnání je v `tools/sound-collector/research/AUDIO_PRESERVATION_RESULT.json`.

## Uspořádání

```text
OverwatchBros/Assets/Audio/Collected/
  CS2/
    player/footsteps/
    weapons/
    physics/
    ambient/
    items/
  GDC2026/Effects/
  TF2/                    vybrané TF2 klipy v původních podsložkách
  Designed/Honza/         vybrané fáze balvanu a sklapnutí pasti
  Weapons/Reload/          sestavené přebíjecí sekvence
  IMPORT_MANIFEST.json     původ souborů, kontrolní otisky a přiřazení
OverwatchBros/Assets/Resources/CollectedAudioBank.asset
```

Nové herní klipy jsou mono WAV 44,1 kHz se sjednocenými špičkami a krátkými náběhy/dozněními; smyčky mají upravený spoj. Dlouhé efekty jsou omezené na 8 sekund, podklady na 16 sekund. Původní nahrávky ve sbírce se neupravují. Manifest zachovává zdroj každého vybraného souboru.

Případné špatně rozpoznané místo lze opravit komponentou `AudioSurface` a volbou konkrétního povrchu. Jinak se povrch určuje z terénní vrstvy, materiálu/textury a názvu objektu.

## Další změny výběru

Po novém vybírání ve sbírce spusť z kořene projektu:

```powershell
python tools/sound-collector/export_to_unity.py
```

Export mění pouze novou složku `Collected` a novou zvukovou banku. Nový typ herní události může navíc vyžadovat připojení v kódu; pouhé zaškrtnutí libovolné nové položky není automatický program herní logiky. Komponenty a stávající datové soubory export nepřepisuje.

Aktuální kontroly Unity: `tools/sound-collector/research/SELECTED_AUDIO_PLAY_TESTS.xml` a `PICKUP_RESPAWN_PLAY_TESTS.xml`. Finální hlasitost a charakter vybraných nahrávek dál dolaď během hraní.
