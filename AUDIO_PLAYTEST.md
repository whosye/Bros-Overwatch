# Poznámky z hraní a další ověření

## Opraveno po prvním průchodu

- Prázdný zásobník už necvaká po úspěšném přebití. Cvaknutí zůstává jen při pokusu o střelbu bez nábojů.
- Travnaté díly Domašova `Tile_*` používají trávu. `Terracotta` je dlažba, `DarkBoards`, bříza, sudy a další názvy prken jsou dřevo. Seno je měkký povrch, nikoli dřevo.
- Jezírko a potok se při zvucích zohledňují i bez fyzického collideru. Kroky, dopady a střelba mohou zasáhnout skutečnou vodní plochu.
- Pova má krátký kontakt s prostředím a delší zásah hráče. Při jednom zásahu se nepřidává druhý kontaktní zvuk nože.
- Ve sbírce je nová hlavní položka **Sekera / kosa → hráč (společná sada)** se 13 kandidáty. Vlastní soubory lze dát do `OverwatchBros/Sound/sounds_dir/Vlastni_zasahy_do_hracu` a přiřadit je k této položce. Nový výběr se zapojí spuštěním exportu; do té doby hráčské zásahy sdílejí již vybrané bodnutí.
- Poslední výběr (revize 416) je zapojený celý. Honza má TF2 granátomet, Šindel Ambassador a zvolené přebití z Glocku, Mošino Capper. Viktor má výchozí M4A1, David tlumenou M4A1-S, Kondulič AWP přes optiku a MP5 bez ní. Aktuální volby z přehledu mají přednost před staršími náhradami.
- Honzovy projektilové granáty používají vybranou explozi, stejně jako nálož. Přebití obsahuje pět vybraných vložení náboje z Mag7 a vybrané pohyby pumpy před a po vložení. Celá sekvence se vejde do animace a na konci se nepřidává cvaknutí prázdné zbraně.
- Balvan má samostatné vybrané vypuštění, tři kamenné smyčky valení, dopady a závěrečný výbuch. Hlasitost/výška valení reaguje na pohyb a ve vzduchu utichá. Zůstává jeden skok ve vzduchu a šplhání po svislé zdi při držení mezerníku, nejvýše 2,5 m mezi dotyky se zemí.
- Luk má zvolené natažení, krátké natažení pro rychlopalbu, vypuštění, průlety a materiálové dopady. Natahování se při přerušení/výměně zbraně zastaví. Oba háky mají zvolené navíjení a konec; past vybrané sklapnutí čelistí.
- Slivovice má oddělené potvrzení, pití a grknutí. Obnovení pickupů používá `spawn_item` pouze při skutečném respawnu. Tyto situace ověřily automatické herní testy; hlasitosti a charakter je ještě vhodné posoudit během delšího hraní.

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
- [ ] Honzova past: další vlastní nahrávky pro natažení, položení a zničení. Sklapnutí už je zapojené.
- [ ] Doladit charakter tří vybraných smyček valení kamene; případně doplnit zvuk jeho skoku/šplhání.
- [ ] Další dosud nevybrané ultimátky a položky původního checklistu. Čtyři vybrané Maxovy schopnosti už jsou připojené.
- [ ] Šindelovo RMB: `SayWhat_Data` má prázdné `sound` i `voiceLines`; mlčení není důkaz chyby přehrávače. Doplnit vhodnou vlastní nahrávku této schopnosti.

Změny banky se projeví po novém spuštění Play. Po dalším výběru zvuků ve sbírce spusť `python tools/sound-collector/export_to_unity.py`.
