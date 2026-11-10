# Zvuková sbírka

## Co patří do Gitu

Do hry a Gitu patří použité klipy v `OverwatchBros/Assets/Audio`, zvuková banka a jejich `.meta` soubory. Audio v `OverwatchBros/Sound/sounds_dir` je pouze **lokální ignorovaná pracovní cache**. Manifesty `KANDIDATI.json`, recepty, `collection.json` a poznámky zůstávají verzované.

Po úklidu jsou lokálně zachované vybrané zdroje potřebné pro sbírku/export. Nepoužité kopie byly odstraněné po ověření shodného originálu mimo projekt. Smazaní kandidáti se v přehledu nenabízejí.

Obnova všech kandidátů z původních knihoven (z kořene repozitáře):

```powershell
python tools/sound-collector/restore_candidates.py --all
```

Bez `--all` se obnovují jen vybrané zdroje a vstupy přebíjecích sekvencí. Export do Unity tuto obnovu provede automaticky a potom odstraní neodkazované pracovní kopie z `Assets/Audio/Collected`. Pro spuštění nebo sestavení hry nejsou externí knihovny potřebné; všechny použité klipy už jsou v Assets.

Původní umístění knihoven je uložené v manifestech jako `source_root`. Při přestěhování zdrojů uprav tuto cestu. Obnova ověřuje SHA-256 a nepřepisuje lokálně změněné kandidáty.

Další úklid lze nejdřív prohlédnout příkazem `python tools/sound-collector/cleanup_unused_audio.py`; `--apply` odstraní jen ověřené nevybrané kopie a neodkazované generované klipy. `--untrack` vyřadí kandidátní audio z indexu Gitu, nikoli použité herní Assets. Ignore pravidla sama již sledované soubory nevyřazují.

Vyřazení souborů ovlivní další commity. Již odeslaná data zůstávají v historii / Git LFS; tento úklid historii nepřepisuje.

Spusť **`Zvuky.cmd` v kořeni projektu** dvojklikem. Otevře se místní přehled v prohlížeči. Python 3.10+ už je na tomto počítači dostupný, žádné další balíčky nejsou potřeba. Okno spuštěného přehledu nech běžet; ukončíš ho zavřením okna nebo Ctrl+C. Další dvojklik použije již běžící přehled.

## Jak sbírat zvuky

### Doporučení z knihoven jsou propojená

Používej jednu stránku: **http://127.0.0.1:8765/**. Původní GDC zůstává v `sounds_dir/GDC2026_kandidati`, další výběr je v `sounds_dir/CS2_kandidati`. Každá knihovna má svůj `KANDIDATI.json` s doporučenými použitími; soubory ani uložené volby původní knihovny se nepřesouvají.

1. Klikni na **Začít na Sekyrkách** nebo vyber jinou položku vlevo.
2. Vpravo v **Doporučených zvucích** přehraj ukázku a klikni na **Přiřadit k této položce**. Vybraný soubor se přesune v přehledu do **Tvého výběru**, fyzicky zůstává na místě.
3. Až máš potřebné varianty či vrstvy, zaškrtni **Hotovo** nahoře u položky.

Opačný postup nabízí **Poslech kandidátů** vlevo: filtrem **Knihovna zvuků** vyber GDC nebo CS2, vyhledej nahrávku, poslechni ji a klikni na navržené použití. Otevře se odpovídající položka s uloženým přiřazením. Doporučení sama nikdy neoznačují nic jako vybrané/hotové. Při přiřazení se doplní původ konkrétní knihovny, pokud už nemáš vlastní údaje. Označení licence GDC se nepřenáší na CS2.

Přímý poslech nové knihovny: **http://127.0.0.1:8765/?view=candidates&source=cs2**. Zdrojové formáty `.vsnd` se nekopírují; vybrané WAV/MP3 zůstávají v původních formátech a složkách. Výběr podle názvů vyžaduje ověření poslechem, zvlášť jednotlivé fáze přebíjení a zvuky použité jen jako vrstva.

Filtr **Doporučené** zobrazuje nedokončené položky s dostupným doporučením, **Přiřazeno** tvůj výběr a **Hotovo** potvrzené položky. Některé doporučené zvuky jsou jen jedna vrstva složené akce; pod přehrávačem je poznámka k použití.

### Vlastní další nahrávky

1. Stahuj zvuky do `OverwatchBros/Sound/sounds_dir`. Podsložky jsou povolené. Archivy ZIP nejdřív rozbal.
2. V checklistu zvol povrch, postavu nebo událost. Klikni na **Přiřadit soubory**.
3. Nahrávky si přehraj a přiřaď jeden či více souborů. Soubor může patřit k více položkám a nemusíš ho přejmenovat. U kroků může být všech osm variant pod jednou položkou.
4. Podle potřeby rozbal **Zdroj a licence**, doplň odkaz/licenci/autora a klikni na **Uložit zdroj**. Informace se sdílí napříč použitími stejného souboru.
5. Až jsi s výběrem spokojený, zaškrtni **Hotovo**. Samotné přiřazení zatím znamená jen kandidáta. Připojení do Unity je další práce.

Souborový seznam se kontroluje každých pět sekund. Lze ho obnovit i ručně. Doporučené názvy jsou pomůcka, ne podmínka. Podobné názvy se ve výběru nabídnou výš; k žádnému automatickému přiřazení nedochází.

Filtry rozlišují chybějící položky, kandidáty a hotový výběr. Volitelné doplňky jsou zpočátku skryté. Sekce **Už ve hře** obsahuje již připojené nahrávky, které není potřeba znovu shánět. Běžné existující hlášky postav nejsou jednotlivě duplikované; v sekci **Hlášky** jsou jejich konkrétní mezery.

## Ukládání a zálohy

- `collection.json` vedle serveru obsahuje všechna přiřazení, fajfky, poznámky a zdroje. Ukládá se na disk, nikoli pouze do paměti prohlížeče.
- `collection.json.bak` je předchozí uložená verze. Zápis probíhá přes dočasný soubor a atomické nahrazení; nečitelná sbírka se automaticky nezahodí.
- **Záloha** stáhne další JSON kopii. Obnovit ji můžeš v nápovědě přes **Obnovit ze zálohy**. Export obsahuje evidenci a odkazy, nikoli samotné zvukové soubory.
- Při přesunu na jiný počítač vezmi složku nástroje i `sounds_dir`. Přiřazení používají relativní cesty.
- Při přejmenování nebo přesunu zvuku uvnitř složky jej znovu přiřaď. Starý odkaz bude označený jako chybějící. Nástroj soubory nepřejmenovává, nepřevádí, nekopíruje do Assets ani nemaže.
- Změny v jiném otevřeném okně se načítají průběžně; zastaralý zápis se odmítne místo přepsání novější práce.

## Formáty a technické informace

Přehled eviduje WAV, OGG, MP3, FLAC, AIFF, M4A a Opus. Přehrávání záleží na podpoře kodeku v prohlížeči, nejpraktičtější jsou WAV/MP3/OGG. U nepodporovaného formátu můžeš soubor stále přiřadit. Git LFS pointer bez skutečného audia se označí jako nedostupný.

Server poslouchá jen na `127.0.0.1`, standardně port 8765. Soubory se neposílají do cloudu. Pokud je port obsazený jinou aplikací, použije další z rozsahu 8765–8774. Cestu lze pro samostatnou sbírku nastavit argumentem `--inbox`, standardně je přednastavená uživatelova stávající složka.

Ruční spuštění z kořene projektu:

```powershell
python tools/sound-collector/server.py --open
```

Katalog je explicitně sestavený podle `AUDIO_CHECKLIST.md`; některé příbuzné řádky jsou sloučené a materiálové zásahy jsou rozdělené. Původní audit se nemění. `build_catalog.py` znovu vygeneruje `catalog.json` se stejnými stabilními ID a nemění osobní sbírku.

Kontrola nástroje:

```powershell
python -m unittest discover -s tools/sound-collector -p "test_*.py"
node --check tools/sound-collector/web/app.js
```
