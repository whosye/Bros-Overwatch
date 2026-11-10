# Poznámky z hraní a další ověření

## Opraveno po prvním průchodu

- Prázdný zásobník už necvaká po úspěšném přebití. Cvaknutí zůstává jen při pokusu o střelbu bez nábojů.
- Travnaté díly Domašova `Tile_*` používají trávu. `Terracotta` je dlažba, `DarkBoards`, bříza, sudy a další názvy prken jsou dřevo. Seno je měkký povrch, nikoli dřevo.
- Jezírko a potok se při zvucích zohledňují i bez fyzického collideru. Kroky, dopady a střelba mohou zasáhnout skutečnou vodní plochu.
- Pova má krátký kontakt s prostředím a delší zásah hráče. Při jednom zásahu se nepřidává druhý kontaktní zvuk nože.
- Ve sbírce je nová hlavní položka **Sekera / kosa → hráč (společná sada)** se 13 kandidáty. Vlastní soubory lze dát do `OverwatchBros/Sound/sounds_dir/Vlastni_zasahy_do_hracu` a přiřadit je k této položce. Nový výběr se zapojí spuštěním exportu; do té doby hráčské zásahy sdílejí již vybrané bodnutí.
- Nahrazené jsou pouze známé původní generované střelby `gun_viktor` a `gun_anna`, aktuálně netlumenou M4A1 pro Viktora a tlumenou M4A1-S pro Davida z CS2. Šindel používá výstřel R8 a přebíjecí sekvenci z Deaglu v délce jeho animace, Kondulič AWP při střelbě přes optiku a MP5 bez optiky. Ostatní ručně přiřazené výstřely mají dál přednost.
- Honzovy projektilové granáty používají vybranou explozi, stejně jako nálož. Přebití používá pětkrát vložení náboje z Novy ve zrychlené sekvenci, která se celá vejde do přebíjecí animace, bez přidaného závěru/pumpy a bez cvaknutí prázdné zbraně na konci.
- Balvan: vybraný kamenný úder je nyní dopad po pádu, hlasitější a oddělený od exploze. Prozatímní smyčka valení je složená z vybraných štěrkových zvuků a její výška/hlasitost reaguje na pohyb. Ve vzduchu utichá. Přidaný je jeden skok ve vzduchu a šplhání po svislé zdi při držení mezerníku, nejvýše 2,5 m mezi dotyky se zemí.

## Další herní zkoušky

- [ ] Tráva mimo cesty, cesta Yarisu, dlažba obýváku, koberec, dřevo menší chaty, seno, potok a koncové jezírko.
- [ ] Pova a Max: vzduch → zeď → hráč; na hráči musí být společná sada a nesmí se míchat s kontaktem se zdí.
- [ ] Přebití všech střelných postav, přerušení přebíjení smrtí/změnou postavy a střelba s prázdným zásobníkem.
- [ ] Honza: přímý zásah granátem, odrazy, mina, granáty po smrti. Balvan: dvojskok, výstup po zdi, dopad z výšky, valení, odpálení a rozstřílení soupeřem.
- [ ] Kopnutí Konduliče do hráče, Davidovo léčení a posílení spoluhráče, uspání, past a další schopnosti vyžadující cíl.
- [ ] S druhým klientem ověřit dosah, hlasitost a polohu kroků, dopadů, střel a zásahů; zvuky se nemají duplikovat u vlastníka.
- [ ] Killcam / POTG: stejná vybraná varianta, správná rychlost přebití, správná poloha zásahů. Přepnutí mapy: nesmí dál znít voda/krb předchozí mapy.
- [ ] Pickupy kromě karburátoru: zdraví, Tramal, slivovice; smrt a respawn, voda při jízdě z tobogánu.

## Ještě shánět nebo upravit

- [ ] **Honzovy hlášky odšumět v Audacity.** Původní nahrávky zatím zůstávají beze změny.
- [ ] Luk: natažení, vypuštění tětivy, let a dopady šípu. V CS2 ani podle jmen souborů GDC se vhodná skutečná sada nenašla; nahrávka nože nenahrazuje tětivu.
- [ ] Honzova past: natažení, dopad, sklapnutí, zničení.
- [ ] Vlastní souvislé valení kamene místo provizorní štěrkové vrstvy; zvuk skoku/šplhání balvanu.
- [ ] Maxovy schopnosti, další dosud nevybrané ultimátky a další položky původního checklistu.
- [ ] Šindelovo RMB: `SayWhat_Data` má prázdné `sound` i `voiceLines`; mlčení není důkaz chyby přehrávače. Doplnit vhodnou vlastní nahrávku této schopnosti.

Změny banky se projeví po novém spuštění Play. Po dalším výběru zvuků ve sbírce spusť `python tools/sound-collector/export_to_unity.py`.
