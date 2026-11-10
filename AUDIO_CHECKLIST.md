# Zvuky k dokončení Bros Overwatch

Audit uložených souborů projektu k 8. 11. 2026. Zdrojem jsou aktuální hero assety v `OverwatchBros/Assets/Resources/Heroes`, jejich odkazy na zbraně a schopnosti, runtime skripty a uložená `Assets/Scenes/SampleScene.unity` včetně obou map: Chaty a Hanamura. Nejde o poslechové hodnocení nahrávek ani ověření neuložené scény v Unity.

**Jak číst seznam:**

- **SEHNAT** = chybí vlastní nahrávka; dnes se používá generovaný efekt nebo zvuk není implementovaný.
- **MÁME** = soubor a příslušný odkaz/načítání již existují. Ponechat, nebo vyměnit až podle poslechu.
- **DOPLNĚK** = další zvuk pro již existující akci či objekt; jeho přehrávání bude potřeba doprogramovat. Není podmínkou dokončení hry.
- Položky v jednom řádku jsou související zvukové události. Nemusí každá znamenat samostatný zakoupený soubor; lze sdílet knihovnu a vrstvit zdroje.
- Hlášky postav jsou oddělené od ruchů schopností. Existující hláška nenahrazuje zvuk zásahu, letu nebo exploze.

## 1. Pohyb a povrchy

Aktuální kroky používají jeden generovaný šum pro všechny povrchy. Samostatné materiálové sady zatím nejsou.

Pro každou hlavní sadu sehnat **6–8 jednotlivých kroků a 2–3 dopady po skoku**. Pro seno, piliny a další malé plochy stačí 3–4 kroky a 1–2 dopady. Běh, tichou chůzi a přikrčení lze nejdříve řešit hlasitostí a kadencí stejných nahrávek; samostatná nahrávka běhu je rozšíření.

| Sada k sehnání | Konkrétní použití |
|---|---|
| [ ] Dřevěná prkna, dutá podlaha | Podlahy a schody chat, rozhledna, lávky, terasy a dojo v Hanamuře. Občasné vrznutí jako další varianta. |
| [ ] Pevné dřevo | Trámy, bedny, stoly, kompostér a další dřevěné překážky; lze sdílet s prkny. |
| [ ] Beton a tvrdý kámen | Tunelové podlahy a rampy, kamenné terasy, zídky a skály; jedna základní sada může pokrýt obojí. |
| [ ] Dlažba / keramika | Terakotová dlažba obýváku, podesta krbu, dlážděné město v Hanamuře. |
| [ ] Štěrk / drobné kamínky | Cesty kolem chat a štěrková trasa Yarisu. |
| [ ] Tráva | Pozemek, zahrada, louka a trávníky Hanamury. |
| [ ] Hlína / měkká zemina | Zemina a okraje zahrady; lze sdílet se suchou měkkou půdou. Kompost použít jen tam, kde je skutečně dostupná jeho horní plocha. |
| [ ] Písek | Cesty a nádvoří v Hanamuře (`Hanamura/Sand`). |
| [ ] Koberec / textil | Koberec v obýváku, kobereček malé chaty; tlumené kroky a měkký dopad. |
| [ ] Guma | Gumová podlaha Pavoukova workshopu, případně pásy/pneumatiky rolby. |
| [ ] Kovový plech | Plošinka a karoserie rolby, kovové prahy a vhodné další překážky. |
| [ ] Střešní tašky | Střechy chat i Hanamury, když na ně hráč vyskočí. Dřevěný šindel rozhledny může sdílet dřevo. |
| [ ] Seno / suchá sláma | Balíky sena: suché zašustění a tlumené došlápnutí. |
| [ ] Piliny / pytel s výplní | Piliny a pytle u kotle: suché křupnutí/zašustění. U pytlů lze použít tlumený textil. |
| [ ] Mělká voda | Brodění, jednotlivé šplouchavé kroky a výstup z vody. Jízda proudem má vlastní sadu níže. |
| [ ] Hladký povrch skluzavky | Došlap a sklouznutí; samostatnou plastovou sadu lze nahradit tvrdým povrchem. |

**Další pohybové ruchy:**

- [ ] Odraz při běžném skoku, 2–3 varianty; druhý skok Mirka a Mošina může použít odlišený krátký vzdušný efekt.
- [ ] Lezení po žebříku rozhledny: kontakt bot/rukou s příčkami, 4–6 variant.
- [ ] Vytažení na hranu u Mirka: úchop + přitažení těla; lze složit z pohybového šustění a kontaktu s povrchem.
- [ ] Mošinova jízda po zdi: začátek, krátká smyčka tření a odraz/ukončení.
- [ ] Šustění oblečení/výstroje při prudkém pohybu; volitelná společná sada pro hrdiny.
- [ ] Náraz odhozeného hráče do stěny: těžký tělesný úder, důležitý také pro Mošinův basový úder.

**Při implementaci:** koberec, terakotový povrch obýváku a gumová podlaha workshopu jsou vizuální vrstvy bez vlastního pevného collideru. Samotný raycast do podlahy pod nimi jejich materiál spolehlivě nepozná; použít zóny/označení povrchu. Piliny jsou rovněž vizuální vrstva. V uložené scéně nebyl potvrzen sněhový terén: existence sněžné rolby není důvod shánět kroky ve sněhu. Samostatný asfalt není pro současnou štěrkovou cestu potřeba.

## 2. Společné zbraně, zásahy a stav hráče

### Zbraně a kontakt s okolím

- [ ] Cvaknutí prázdné zbraně; dnes `ProceduralSfx.Empty`. Stačí společný mechanický základ.
- [ ] Přebíjení jednotlivých používaných střelných zbraní: Viktor, Honza, David, Kondulič, Mošino a Šindel. Dnes společný generovaný zvuk. Podrobnosti jsou u hrdinů níže.
- [ ] Kulka do dřeva: 3–5 variant.
- [ ] Kulka do kamene/betonu/omítky: 3–5 variant, lze sdílet.
- [ ] Kulka do kovu: 3–5 variant, kratší zvonivé rány.
- [ ] Kulka do skla: 2–3 varianty tvrdého cinknutí. Hra tím nezískává rozbíjení oken; plošný zvuk vysypaného skla není požadavek.
- [ ] Kulka do hlíny/písku/štěrku: 3–5 variant, lze sdílet a vrstvit kamínky.
- [ ] Kulka do trávy/listí/živého plotu: 2–3 tlumené varianty; DOPLNĚK pro materiálové rozlišení zásahu.
- [ ] Kulka do měkkého povrchu: textil, seno, guma a pytle; 2–3 varianty lze sdílet.
- [ ] Kulka do vody: 2–3 malé šplouchance; DOPLNĚK podle kolize vodní plochy.
- [ ] Sekera/kosa do pevného povrchu: dřevo, kámen/omítka/keramika a kov, po 3 variantách; měkký kontakt na seno/textil/gumu může sdílet tlumený zásah. Dnes generovaný `SurfaceSlash`.
- [ ] Šíp do dřeva, kamene/kovu a měkkého povrchu: 2–3 varianty; DOPLNĚK rozlišení dopadů šípu.
- [ ] Odraz kovového granátu od tvrdé země/stěny, 3–5 variant. Použití u Honzových odráživých projektilů a dalších granátů podle jejich chování.
- [ ] Výbuch malé výbušniny, 3 varianty: granát, nálož, granáty po smrti.
- [ ] Velký výbuch / těžký úder, 2–3 varianty: balvan a výrazné ultimátky. Magické/ohnivé vrstvy se přidávají zvlášť.

### Bojová zpětná vazba a stavy

- [ ] Potvrzení zásahu hráče: krátké čisté cinknutí/úder. Odlišit od prostorového zvuku na zasaženém místě.
- [ ] Potvrzení headshotu a potvrzení zabití: dvě odlišitelné varianty; jejich samostatné rozlišení je DOPLNĚK.
- [ ] Kulka do těla a sečný zásah těla, po 3–5 variantách; DOPLNĚK prostorových ruchů vedle existujícího hitmarkeru.
- [ ] Přijetí poškození: 3–5 krátkých tělesných reakcí, pokud nejsou pokryté hláškami konkrétní postavy. Dnes existuje generovaný `Hurt`.
- [ ] Smrt / pád těla: 2–3 varianty; dnes se při smrti pouští generovaný tón i vedle hlášky.
- [ ] Oživení / příchod do hry: krátký efekt; dnes generovaný `Spawn`.
- [ ] Omráčení: nástup + krátký dozvuk; dnes `Stun`.
- [ ] Potvrzení úspěšného omráčení/uspání pro útočníka; dnes `StunConfirm`.
- [ ] Uspání a probuzení: krátký nástup + konec; DOPLNĚK k Davidově šipce.
- [ ] Zpomalení/znehybnění a uvolnění: společné jemné signály; DOPLNĚK, mechanické sklapnutí pasti se řeší zvlášť.
- [ ] Umlčení a blokace léčení: dvě rozlišitelné krátké značky; DOPLNĚK pro Šindela a Davida.
- [ ] Získání štítu, zásah do štítu a rozbití štítu; DOPLNĚK společný pro více zdrojů štítu.
- [ ] Přijetí léčení, zesílení a vypršení dočasného buffu: společné jemné signály; DOPLNĚK, který nemusí hrát při každém léčivém tiku.
- [ ] Neplatná akce / chybějící cíl: krátké zamítnutí. Používá se například u háku, posílení a Maxovy ultimátky; dnes často `Empty`.
- [ ] Nabití ultimátky na 100 %, obnovení náboje schopnosti a nedostupná schopnost: DOPLNĚK do HUD, lze sdílet s UI.
- [ ] Příprava/spuštění ultimátky: společná záloha místo `UltCharge`; hlavní ultimátky by měly dostat vlastní rozpoznatelný nástup.

## 3. Každá hratelná postava

Názvy v závorkách označují soubory, nikoli další postavy. **V aktivním registru je devět hrdinů.** Všech 33 přiřazených ability assetů má pole `sound` prázdné; některé schopnosti přesto mají hlášky nebo nahrávku načítanou přímo ze skriptu.

### Pova (`Ayran`)

- [ ] **Sekyrky:** lehký švih vlevo/vpravo, těžší třetí úder komba; tělesné a materiálové zásahy sdílet se společnou knihovnou. Nemají přebíjení.
- [ ] **Modrý plamen — Shift:** rozhoření/nástup, smyčka plamene za pohybu, zásah a uhasnutí. Dnes generovaný vzdušný efekt; vlastní hlášky existují.
- [ ] **Sekera na řetězu — E:** vyhození, rozvinutí/let řetězu, zachycení cíle, napnutí a přitažení, návrat při minutí. Dnes obecné generované efekty.
- [ ] **Zkřížené sekyry — RMB:** nastavení bloku, kovový zásah do bloku, vyčerpání/proražení bloku; případně tiché složení seker. Dnes generované potvrzení zásahu/výbuch.
- [ ] **Ohnivý dopad — Q:** silný rozpoznatelný nástup, stoupání/let, prudký sestup a ohnivý dopad. Pole vlastního efektu je prázdné; volání pro začátek sestupu přímo přehrává toto pole.
- [ ] **Pasivní léčení a zkrácení cooldownů po takedownu:** lze použít společné signály léčení/obnovení; další vlastní sada není nutná.
- [ ] **Hlas smrti:** v hero assetu chybí.

### Honza

- **MÁME:** výstřel granátometu `gun_honza.wav` a hlášky všech tří schopností.
- [ ] **Granátomet:** otevření/mechanika, vložení granátu či zásobníku podle animace a zavření; odrazy granátu a exploze ze společné sady.
- [ ] **Nálož — Shift / odpálení RMB:** hod, dopad/přichycení, krátké potvrzení odpálení a výbuch. Případné pípání ozbrojené nálože je DOPLNĚK, aby záměrně neměnilo její odhalitelnost.
- [ ] **Past na medvědy — E:** natažení, hod, kovový dopad/položení, sklapnutí na noze a rozstřelení/zničení.
- [ ] **Balvan — Q:** nástup, vypuštění, smyčka valení, náraz do hráče/stěny, rozstřelení nebo koncový výbuch.
- [ ] **Čtyři granáty po smrti:** rozsypání/kovové odrazy, případný krátký zápal a exploze. Sdílet základ granátu a výbuchu.

### Viktor

- **MÁME:** výstřel `gun_viktor.wav` a hlášky Q, Shift, E i RMB.
- [ ] **Základní střelná zbraň:** přebití — vyjmutí/vložení zásobníku a závěr. Asset se jmenuje `Pistol_Data`, název zbraně je `Pistol`; nepočítat sem nový odlišný typ zbraně jen podle vzhledu.
- [ ] **Úskok — Shift:** krátký rychlý pohyb a došlap.
- [ ] **Léčivé pole — E:** vytažení/položení zařízení, spuštění, jemná léčivá smyčka a vypnutí. Dnes generované přebití/oživení.
- [ ] **Pulzní granát — RMB:** vypuštění, let a elektrický/omračující výbuch. Pro zásah hráče sdílet omráčení.
- [ ] **Taktický zaměřovač — Q:** zapnutí elektroniky, skenování/zaměření a konec. Jemné potvrzení zachycení cíle je DOPLNĚK.

### Mirek

- [ ] **Luk:** založení šípu, napínání tětivy, puštění tětivy, průlet šípu a dopad. Puštění a průlet dnes mají generované efekty. Luk nemá zásobník ani přebíjení klávesou R.
- [ ] **Výskok — Shift:** prudký odraz a krátké prosvištění; dopad sdílet s povrchy.
- [ ] **Průzkumný šíp — E:** odlišené vypuštění, dopad a aktivace skenu. Opakující se sonarový puls během odhalování je DOPLNĚK.
- [ ] **Rychlopalba — RMB:** aktivace a připravení rychlé série; jednotlivé šípy sdílejí luk, není potřeba pět různých výstřelů.
- [ ] **Smršť — Q:** výrazný nástup, vypuštění větrného tělesa, smyčka letícího víru, kontakt/poškozování a rozplynutí.
- [ ] **Dvojskok a šplhání na hranu:** společná pohybová sada.
- **MÁME:** hlášky Q, Shift, E i RMB.

### David (`Anna`)

- **MÁME:** výstřel biotické pušky `gun_anna.wav`, hlášky schopností, léčení, headshotu, minuté uspávací šipky i zamíření.
- [ ] **Biotická puška:** přebití; odlišit kontakt léčivé střely se spojencem a zásah nepřítele. Krátké přiblížení/oddálení optiky je DOPLNĚK.
- [ ] **Uspávací šipka — Shift:** vystřelení malé šipky, kontakt a uspání; případný let lze sdílet s projektilem, probuzení se společnými stavy.
- [ ] **Biotický granát — E:** hod, rozbití/rozstřik kapaliny, léčivý účinek na spojence a negativní účinek/blokace léčení na nepřátele.
- [ ] **Posílení — Q:** hod plechovky Monsteru, průlet/dopad na spojence, energetický nástup a konec posílení. Otevření plechovky lze využít jako vrstvu.
- **MÁME:** hudba posílení `monster_boost.wav`; novou hudbu není nutné shánět.

### Kondulič (`Sniper`)

- **MÁME:** střelba bez přiblížení `gun_sniper_smg.wav`, odstřel s optikou `gun_sniper_scoped.wav`, hlášky Q, Shift a E.
- [ ] **Odstřelovačka:** přebití, přepnutí do/z optiky, nabíhání síly zaměřené rány a dosažení plného nabití. Nabití je existující mechanika, samostatný zvukový signál je DOPLNĚK.
- [ ] **Hák — Shift:** vystřelení, kontakt s povrchem, napnutí lana, navíjení/přitažení a uvolnění; neplatný zásah sdílí zamítnutí.
- [ ] **Odkopnutí — E:** švih nohou a pevný tělesný zásah; minutí potřebuje jen švih.
- [ ] **Infravize — Q:** elektronický/sonarový nástup, jemný aktivní stav a vypnutí.

### Mošino (`Bard`)

- **MÁME:** výstřel zvukometu `gun_bard.wav`, dvě hudební smyčky `bard_heal.ogg` a `bard_speed.ogg`, hlášky všech čtyř schopností.
- [ ] **Zvukomet:** přebití/obnovení zdroje a zvukový/energetický dopad projektilu.
- [ ] **Přepnutí rytmu — Shift:** dvě krátká přepnutí — na léčivou a na rychlostní auru. Hudební základy už existují.
- [ ] **Zesílení — E:** rytmický nástup, zdůrazněný aktivní stav a návrat do běžné aury; lze vrstvit existující hudbu.
- [ ] **Basový úder — RMB:** krátký silný basový tlak, odraz hráče a případný tělesný náraz oběti do zdi.
- [ ] **Drop the Beat — Q:** výskok, velký basový dopad, přidání štítu, taneční/zesilující hudební vrstva a konec. Dnes je hlavní fanfára generovaná.
- [ ] **Rytmický bonus za střelbu do taktu:** krátké pozitivní potvrzení; dnes se používá generovaný signál.
- [ ] **Jízda po zdi a dvojskok:** společná pohybová sada.

### Šindel (`Sindel`)

- **MÁME:** střelba z Devítky `gun_sindel.wav`, hlášky Q, Shift a E.
- [ ] **Devítka:** přebití pistole — zásobník a závěr.
- [ ] **Spravedlivý skok — Shift:** odraz, vznášení a doznění; krátký očistný efekt lze sdílet se stavovými efekty. Dopad sdílet s povrchy.
- [ ] **Svatý granát — E:** hod, tvrdý kontakt/odraz a výrazná exploze se svatou či sborovou vrstvou.
- **MÁME:** předvýbuchová nahrávka `sindel_granat.wav`. Její délka ovlivňuje nejdřívější výbuch granátu; při náhradě zachovat záměrné načasování.
- [ ] **Say what again?! — RMB:** hlasový výkřik a krátká tlaková/zastrašující vlna; hlasové pole této schopnosti je prázdné. Účinek umlčení/zpomalení sdílet se stavy.
- [ ] **Ezechiel 25:17 — Q:** nástup a vzlet, napětí během kázání, příprava světelného úderu a mohutný úder. Hlasové kázání už je přiřazené; nechávat mu v mixu prostor.
- [ ] **Hlas smrti:** v hero assetu chybí.

### Max

- [ ] **Kosa:** běžný švih, výraznější třetí úder komba; tělesné i povrchové zásahy sdílet se společnou sadou. Bez přebíjení.
- [ ] **Rozpolcení — Shift:** výpad, rotační švih kosy a zásah. Hláška této schopnosti chybí.
- [ ] **Stínová forma — E:** přechod do stínu, jemná smyčka průchodu a zhmotnění; jde o průchod zdmi, ne fyzické rozbíjení zdi.
- [ ] **Dlouhý řez — RMB:** těžký sečný švih, rozlet stínové vlny a zásah.
- [ ] **Vpád do stínu — Q:** nástup/zaměření oběti, vstup do oběti, krátký vnitřní stínový stav a vyražení s úderem; neplatný cíl sdílí zamítnutí.
- [ ] **Lifesteal:** sdílet jemné potvrzení léčení, není potřeba další vlastní knihovna.
- **MÁME:** hlášky Q, E, RMB a běžné reakce. Existuje také rozhovor Max–Honza v `Resources/Banter/max_honza`.

## 4. Mapa, prostředí, vozidla a interakce

| Objekt / událost | Co sehnat | Stav |
|---|---|---|
| Venkovní prostor chat | Slabý vítr a šum stromů, případně oddělení ptáků/hmyzu pro variabilitu | MÁME vesnický ambient `village_ambient_spatial.ogg`; další vrstvy jsou DOPLNĚK. |
| Hanamura | Jemný vítr, zahradní atmosféra a voda u jezírka | DOPLNĚK, samostatný připojený ambient této mapy nebyl potvrzen. |
| Potok / proud vody | Klidné tekoucí zurčení jako smyčka | SEHNAT; současná jízda používá generovaný vodní šum. |
| Jízda po vodě | Silnější smyčka šplouchání, nástup a výstup z proudu | SEHNAT místo `WaterRush`; hudbu jízdy už máme. |
| Vstup do vody | Malý splash a velký splash po pádu do jezírka | SEHNAT místo `Splash` a `BigSplash`. |
| Skluzavka / tobogan | Kontakt při nasednutí a klouzání těla po hladkém povrchu | DOPLNĚK; `tobogan.wav` už zajišťuje hudbu jízdy. |
| Bazén / jezírka | Tichý vodní pohyb | DOPLNĚK; sdílet základ vody, nevymýšlet mechaniku plavání. |
| Kotel — páka | Kovové zatažení/zacvaknutí | SEHNAT místo generovaného přebití. |
| Kotel — topení | Zapálení/rozhoření, smyčka hoření a prudké hučení při přehřátí, dohasnutí | SEHNAT; dnešní hučení je zpomalený generovaný šum vody. |
| Přehřívání horních pater | Krátké varování / syčení horka | DOPLNĚK k existujícímu nebezpečí. |
| Krb / oheň grilu | Smyčka praskajícího dřeva | DOPLNĚK atmosféry u ohnišť. |
| Sněžná rolba | Start motoru, volnoběh, záběr pod zátěží, pohyb pásů, zastavení/vypnutí | SEHNAT; motor je nyní generovaný. Smyčky musí navazovat. |
| Rolba — kontakt | Náraz/porážení hráče, otřes karoserie; krátké nasednutí/sesednutí jako DOPLNĚK | SEHNAT/odlišit od obecného výbuchu; sdílet tělesný zásah. |
| Yaris | Motor a průjezd, pneumatiky na štěrku | DOPLNĚK existujícího provozu. |
| Yaris — klakson | Žádný povinný nový soubor | MÁME `YarisHorn.wav`. |
| Yaris — sražení | Fyzický náraz vozidla do hráče pod komickou hlášku | MÁME přiřazené `bohuno_cut.mp3`; fyzická vrstva je DOPLNĚK. |
| Yaris — rádio | Žádný povinný nový soubor | MÁME hudbu `why_is_this_dealer_loop.mp3`; existuje i další varianta v projektu. |
| Televize v obýváku | Žádný povinný nový podkres | Zvuk je součástí `Assets/Video/TV_loop_video.mp4`; obsah zvukové stopy je potřeba ověřit poslechem. |
| Buff Respin Joker | Krátká výherní/automatová znělka | SEHNAT místo generovaného `JokerJingle`; cesta pro vlastní soubor už je `Resources/Audio/buff_respin_joker`. |
| Toaleta | Žádná povinná nová sada | MÁME tři nahrávky `toilet_1.wav` až `toilet_3.wav`. Spláchnutí navíc je DOPLNĚK. |
| Pavoukův workshop | Žádná povinná nová hudba | MÁME `tunnel_vision.mp3`; video timelapse je záměrně bez svého audia. |
| Workshop — počítač | Větrák, jemné psaní a klikání | DOPLNĚK atmosféry. Dveře se tím nestávají interaktivní. |
| Tunel | Jemný prostorový hukot, případně elektrické bzučení světel | DOPLNĚK. Ozvěna kroků/výstřelů se řeší efektem v Unity, další nahrávky nejsou nutné. |

## 5. Předměty a bonusy

Systém podporuje následujících osm typů. V uložené scéně jsou přímo potvrzené lékárničky, Tramal, dědova slivovice a karburátor; původní rozmísťovací kód obsahuje i velkou lékárničku, pivo, běžnou slivovici a štít. Tyto čtyři další typy zařadit podle finálního rozmístění v mapě, nebrat samotný enum jako důkaz přítomnosti.

- [ ] Lékárnička: sebrání + krátký léčivý efekt.
- [ ] Velká lékárnička: výraznější varianta stejného efektu, pokud zůstane v mapě.
- [ ] Pivo / rychlost: otevření plechovky/lahve + pozitivní rychlostní efekt, pokud zůstane v mapě.
- [ ] Slivovice / síla: sklo/zátka + silový efekt, pokud zůstane v mapě.
- [ ] Štít: energetický nástup, pokud zůstane v mapě; sdílet se společným štítem.
- [ ] Dědova slivovice / nesmrtelnost: skleněný/pití efekt + rozpoznatelný nástup ochrany.
- [ ] Tramal: zašustění obalu/blistru + krátké léčivé potvrzení.
- [ ] Honzův karburátor: sebrání kovové součástky + potvrzení možnosti startu rolby.
- [ ] Obnovení předmětu na místě, vypršení buffu a poslední varovné tiky: DOPLNĚK, stačí společné signály; varování není potřeba pro každý typ zvlášť.

Dnes se při sebrání používá převážně společný generovaný `Spawn` nebo `CaptureUnlock`. Zvuk otevření/vypití má doprovázet existující okamžité sebrání; nevyžaduje novou animaci pití.

## 6. Zápas, rozhraní a hudba

- [ ] Odpočet do začátku zápasu: krátký tik a finální startovní signál — DOPLNĚK.
- [ ] Zahájení zápasu, poslední časové varování a konec zápasu — DOPLNĚK.
- [ ] Vítězství a porážka: dvě krátké rozlišitelné znělky — DOPLNĚK.
- [ ] Odemčení dalšího capture pointu — vlastní náhrada za `CaptureUnlock`.
- [ ] Tik průběhu zabírání — vlastní náhrada za `CaptureTick`.
- [ ] Úspěšné zabrání bodu — vlastní náhrada za `CaptureWon`.
- [ ] Sporný bod a bod získaný soupeřem — DOPLNĚK rozlišení stavů.
- [ ] UI: klik/potvrzení, návrat/zrušení, neplatná volba a výběr hrdiny — DOPLNĚK společné krátké sady.
- [ ] Připojení/odpojení hráče a připravenost v lobby — DOPLNĚK, lze sdílet UI signály.
- **MÁME:** hudbu menu `bros_overwatch_live_improv.ogg`.
- **MÁME:** znělku POTG `Resources/Audio/potg_intro.mp3`; generovaná alternativa v kódu není důvod objednávat další skladbu.
- **MÁME:** hudbu Mošinových dvou aur, Monster boostu, toboganu, workshopu a rádia Yarisu.
- Další bojová hudba, announcer či dabing hlášení zápasu jsou tvůrčí rozšíření, nejsou potřeba k ozvučení současného obsahu.

## 7. Hlášky a jejich mezery

Počty jsou počty přiřazených odkazů v hero assetech; ne poslechová kontrola kvality nebo unikátnosti. Nahrávky mohou existovat jinde a přesto nebýt přiřazené.

| Postava | Spawn | Kill | Death | Hurt | Idle | Snare | Hlasy schopností Q / Shift / E / RMB nebo blok |
|---|---:|---:|---:|---:|---:|---:|---|
| Pova | 3 | 5 | **0** | 2 | 5 | 2 | 2 / 2 / 1 / 1 |
| Honza | 1 | 3 | 3 | 3 | **0** | **0** | 2 / 3 / 1 / — |
| Viktor | 1 | 2 | 1 | 2 | 3 | 1 | 1 / 1 / 1 / 1 |
| Mirek | 1 | 3 | 2 | 3 | 4 | 2 | 1 / 2 / 1 / 1 |
| David | 1 | 1 | 1 | 1 | 2 | 1 | 1 / 2 / 1 / — |
| Kondulič | 3 | 4 | 2 | 3 | 5 | 2 | 2 / 2 / 2 / — |
| Mošino | 1 | 3 | 2 | 2 | **0** | 2 | 1 / 1 / 1 / 1 |
| Šindel | 1 | 2 | **0** | 3 | 4 | 2 | 1 / 1 / 1 / **0** |
| Max | 1 | 4 | 1 | 2 | 9 | 1 | 1 / **0** / 1 / 1 |

Přímé chybějící kategorie k doplnění:

- [ ] Pova: smrt, ideálně 2–3 krátké reakce.
- [ ] Šindel: smrt, ideálně 2–3 krátké reakce.
- [ ] Honza: reakce na past/znehybnění, ideálně 2 varianty.
- [ ] Honza: idle hlášky, pokud je chcete používat, 2–3 varianty.
- [ ] Mošino: idle hlášky, pokud je chcete používat, 2–3 varianty.
- [ ] Max: hláška pro Rozpolcení na Shiftu, 1–2 varianty.
- [ ] Šindel: výkřik pro Say what again?! na RMB, 1–2 varianty.

Rozšíření, nikoli nutné mezery: Viktor nemá přiřazené attack hlášky; David má prázdné attack, ale má léčivé, headshot, miss a scope; Kondulič nemá samostatné scope/headshot; Mirek nemá samostatné attack. Každé prázdné volitelné hlasové pole neznamená, že je potřeba nahrát novou repliku. Botům se používají stejné hlasy a ruchy jako příslušné postavě.

Před novým nahráváním zkontrolovat také zdroje mimo importovanou knihovnu: `OverwatchBros/Sound`, `honza_hlasky` a `nex_hlasky`. Ve zdrojích jsou například další Mošinovy varianty Q a neurčená Maxova replika `max_dontknow_use_it_for_something_not picked_yet.wav`; lze je nejdřív poslechnout a případně využít. Pro výše uvedené chybějící kategorie nebyly podle názvů potvrzené jednoznačně odpovídající soubory.

## 8. Co aktuálně neobjednávat jako povinné

- Flanker není mezi devíti hero assety v aktuálním registru, přestože v projektu zůstaly jeho skripty a `gun_flanker.wav`. Pokud se má vrátit: přidat přebíjení dvou pistolí, Blink (odchod/příchod), Recall (zpětný časový efekt a návrat) a Pulse Bomb (hod, přilepení, zápal a výbuch) plus jeho hlášky.
- Neshánět samostatný zvuk pro každou barvu dřeva, typ omítky nebo jednotlivý objekt. Použít společné zvukové rodiny.
- Neshánět zvuky rozbíjení dveří/oken, novou interakci otevírání dveří, plavání nebo sněhové kroky bez odpovídající finální mechaniky/povrchu.
- Ozvěnu chaty a tunelu, tlumení přes dveře, hlasitost vlastních a cizích kroků i dosah zvuků řeší implementace a mix. Nejde o další soubory k sehnání.

## 9. Jaké soubory dodat

- Jednotlivé efekty ideálně jako čisté WAV, 48 kHz / 24 bit; jinou kvalitní původní nahrávku kvůli tomu neodmítat. Krátké ruchy bez dlouhého ticha na začátku.
- Prostorové efekty převážně mono; hudba a širší ambient mohou být stereo.
- Kroky dodat jednotlivě, nikoli jako dlouhou sekvenci chůze; pojmenovat např. `footstep_wood_01.wav` a `land_wood_01.wav`.
- Smyčky dodat plynule navazující, např. `boiler_fire_loop.wav` a `boulder_roll_loop.wav`. Nástup a konec ideálně zvlášť.
- Hlášky dodat odděleně od ruchu schopnosti, pokud nejde o záměrný nedělitelný gag. Nepřidávat do každého efektu předem velkou ozvěnu.
- Ke zdrojům uchovat licenci/původ v jednom soupisu. Tento audit nevybírá ani nestahuje zvukové knihovny.

## 10. Praktické pořadí shánění

1. Kroky a dopady: dřevo, beton/kámen, tráva, štěrk, dlažba, koberec; potom zbývající povrchy.
2. Švihy seker/kosy, luk a šest přebíjecích sad; stávající výstřely nejdřív poslechnout.
3. Společné materiálové zásahy, granáty a exploze.
4. Rozpoznatelné nástupy všech devíti ultimátek, potom zbývající schopnosti.
5. Motor rolby, kotel, voda a chybějící hlasové kategorie.
6. Pickupy, capture pointy a další UI/ambientové doplňky podle zbývajícího času.

## Technické zdroje auditu

- `Assets/Resources/Heroes/*.asset`, `HeroRegistry.cs`: skutečně registrovaní hrdinové, jména, přiřazené zbraně, schopnosti a hlášky.
- `Assets/Data/*.asset`, `WeaponDefinition.cs`, `AbilityDefinition.cs`: typy a audio reference zbraní a schopností.
- `FirstPersonController.cs`, `WeaponShooting.cs`, `ProceduralSfx.cs`, `Fx.cs`, `PlayerHero.cs`, `HeroVoice.cs`: současné spouštění pohybu, zásahů, procedurálních efektů a hlasů.
- `*Ability.cs`: konkrétní fáze schopností včetně hardcoded načítání existujících nahrávek.
- `Assets/Scenes/SampleScene.unity`, materiály a editorové setupy: obě mapy, jejich současné objekty a povrchové vrstvy.
- `PickupSpot.cs`, `MatchManagerPickups.cs`, `Tractor.cs`, `YarisTraffic.cs`, `Boiler.cs`, `WaterSplashFx.cs`, `JokerTvBuff.cs`, `TunnelWorkshop.cs`, `ToiletSound.cs`: interakce a ambientové zdroje.
- `GameAudioLibrary.asset`, `GameAudioLibrary.cs`, `PotgUI.cs`, `WeaponSoundSetup.cs`: již existující knihovna a přiřazení.

Pouhé doplnění souborů nebude stačit pro všechny položky. Materiálové kroky, vrstvy schopností, rozdílné zásahy a nové zvukové signály budou potřebovat připojit do kódu; současné pole `AbilityDefinition.sound` nepokrývá všechny fáze schopnosti a řada skriptů používá přímo generované zvuky.
