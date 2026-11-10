# Zapojení vybraných zvuků

Zdroj výběru: `tools/sound-collector/collection.json`, revize **341**. Použito je všech **55 položek s přiřazenými soubory**, včetně položek bez fajfky Hotovo. Po připomínkách z hraní banka obsahuje **247 přímých zdrojových klipů**, složené přebití z Novy a **81 sad pro konkrétní události**. Náhrady výstřelů a pětinásobné nabití Honzy jsou evidované v `integration_overrides.json`; uložené volby ve sbírce se tím nemění. Původní sbírka a kandidáti zůstávají na místě.

Aktuální opravy, nevyzkoušené situace a poznámka o odšumění Honzových hlasů jsou v `AUDIO_PLAYTEST.md`.

Kandidátní audio v `Sound/sounds_dir` je nyní ignorovaná lokální cache; v Gitu zůstávají manifesty a výběry. Nepoužité kopie byly odstraněny, vybrané vstupy zůstávají lokálně pro export. Obnova všech kandidátů: `python tools/sound-collector/restore_candidates.py --all`. Export si potřebné zdroje obnoví automaticky a neodkazované meziklipy z herní složky zase odstraní. Pro hraní a build stačí verzované klipy v Assets.

## Co hra používá

- Kroky podle povrchu: dřevěná podlaha, pevné dřevo, beton/kámen, dlažba, štěrk, tráva, hlína, písek, koberec, guma, kov a mělká voda. Náhodný výběr neopakuje bezprostředně předchozí variantu pro danou postavu a událost.
- Dopady po skoku mají samostatné sady. Klipy `land*` se nepouštějí při běžné chůzi. Vstup do vody, výstup a větší splouchnutí jsou oddělené.
- Odraz, dvojskok, lezení po žebříku, vytažení na hranu a náraz odhozené postavy do stěny.
- Zásahy kulek a sečných zbraní podle povrchu. U sekery a kosy odděleně švih a kontakt; kontaktní vrstva může doplnit zásah materiálu. Bez zásahu se kontaktní zvuk nepřehrává.
- Přebíjení Viktora, Honzy, Konduliče, Davida, Mošina a Šindela: vybrané části jsou sestavené do sekvence, jejíž délka se přizpůsobí přebití. Přerušení zastaví zvuk.
- Optika Konduliče: přiblížení, odchod z optiky a připravená nabitá rána; aktivace infra vidění. Nabíjení Viktorova taktického zaměřovače.
- Fyzická část Šindelova granátu, Viktorův pulzní granát, hod Davidova granátu, kontakt a výbuch Honzovy nálože, dopad/rozbití balvanu a odrazy granátů.
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

Kontroly Unity: `tools/sound-collector/research/UNITY_AUDIO_VALIDATION.txt` a `UNITY_AUDIO_PLAY_TESTS.xml`. Finální hlasitost, charakter zbraní a vhodnost jednotlivých vybraných nahrávek je potřeba posoudit během hraní.
