# Bros-Overwatch — Roadmap

LAN hero-shooter pro partu kamarádů. Unity 6, URP, C#, Netcode for GameObjects (LAN-only), data-driven hrdinové/zbraně/abilities přes ScriptableObjecty, stylizovaný (ne fotoreálný) artstyle.

Postup: nejdřív dotáhnout gameplay loop na grey-box obsahu (M0–M7), teprve pak škálovat art pipeline (M8+). Cíl je hratelnost co nejdřív, ne hezký obsah.

## Rozhodnutí (fixed)

- Networking: Unity Netcode for GameObjects, LAN only, server-authoritative.
- Hrdinové/zbraně/abilities: ScriptableObject definice, ne hardcoded třídy na hrdinu.
- Postavy: sdílený humanoidní rig (Mixamo auto-rig), sdílená základní lokomoce pro všechny hrdiny.
- Obličeje: FaceBuilder jen na tvar/proporce, finální vzhled stylizovaný (ne photoreal).
- Target: 1080p/60fps normální notebook, 720p/low slabý notebook.

## Milestones

### M0 — Hello Cube (project skeleton) -- DONE

Unity 6 projekt (URP template), Git + Git LFS nastavené, .gitignore pro Unity. Jedna scéna, Cube, C# skript co ho hýbe šipkami/WASD.
**DoD:** Play mode hýbe kostkou, projekt jde i sestavit do .exe a spustit mimo editor.

### M1 — Local First-Person Controller -- DONE

Cube nahrazen player prefabem s FPS kamerou. Walk/run/jump/crouch, kolize se zemí. Grey-box testovací místnost v ProBuilderu.
**DoD:** chození po grey-box místnosti v první osobě, stabilní FPS.

### M2 — Shooting Core (data-driven) -- DONE

`WeaponDefinition` ScriptableObject (damage, rate of fire, ammo, hitscan/projectile). Raycast střelba na dummy terče, crosshair, ammo/reload UI.
**DoD:** střelba na dummy terč, vidíš poškození/log.

### M3 — Health & Respawn -- DONE

`Health` komponenta, damage pipeline, smrt, respawn na spawn pointu. Základní HUD (health, ammo).
**DoD:** hráč umře od terče/hazardu a respawnne se.

### M4 — LAN Multiplayer Core -- DONE

Netcode for GameObjects, host/join přes LAN (lokální síť). Síťovaný pohyb + síťovaná střelba/health, server-authoritative.
**DoD:** dva reálné notebooky na stejné síti se vidí, střílí po sobě, health se správně synchronizuje.

**Poznámka k nastavení hostitele pro reálný LAN test:** hostitel musí poslouchat na `0.0.0.0` (ne `127.0.0.1`, který poslouchá jen lokálně), jinak se z jiného stroje nejde připojit. Od M7 to dělá `GameConnection.Host` sám (`SetConnectionData("127.0.0.1", 7777, "0.0.0.0")`), takže ruční nastavení na `NetworkManageru` už není potřeba. Firewall hostitele musí povolit UDP port 7777 (hra) a 47777 (hledání her v LAN).

### M5 — First Hero (ScriptableObject architektura) -- DONE

`HeroDefinition` + `AbilityDefinition`. Jeden hrdina s 1–2 abilities (např. dash + ultimate). Team assignment, team spawny.
**DoD:** jeden plně hratelný hrdina s abilitou funguje přes LAN, 2 týmy.

Poznámka: implementovaný dash (`DashAbility` + `AbilityDefinition` s `power`/`duration`/`cooldown`), team assignment přes `PlayerTeam` (`OwnerClientId % 2`), team spawny přes pojmenované `SpawnPoint_TeamX` objekty. Ultimate zatím nebyl potřeba (DoD vyžaduje jen jednu abilitu) — zůstává jako nápad v sekci "Nápady k zapracování", případně přidat u druhého hrdiny v M9.

### M6 — First Real Map -- DONE

Skutečný layout mapy v ProBuilderu (cíl: deathmatch nebo capture point), správné měřítko pro počet hráčů. Pořád grey-box materiály.
**DoD:** hratelný LAN zápas na reálném layoutu, team spawny, win condition (skóre/cíl).

Poznámka: mapa "Domašov" podle skutečného pozemku kamaráda — velký dům (2 patra), malá chata (na vyvýšené plošině), dřevěná rozhledna, kruhový bazén, jezírko, plot jako hranice pozemku. Herní mód: **Team Deathmatch** (`MatchManager` sčítá killy za tým, `scoreToWin` nastavitelné v menu). Vše ve `Map-Domašov` parentu v `SampleScene` (multi-scene rozdělení odloženo na M9, až bude druhá mapa potřeba). Bonus nápady (tunel, SFX, mřížkovaný terén) zůstávají v sekci "Nápady k zapracování".

### M7 — Vertical Slice Playable Loop

Druhý hrdina (ověří, že ScriptableObject systém škáluje bez nového kódu). Kompletní match loop: join → spawn → hraní → smrt/respawn → konec zápasu/restart. Voice lines (spawn/kill/death) přes `HeroVoice`.
**DoD:** ty + kamarádi odehrajete celý zápas od začátku do konce přes LAN se 2 hrdiny — na tomhle bodě je hra poprvé "hratelná jako hra".

**Druhý hrdina — Ayran, ultimate "skok+dopad":** vyskočí do vzduchu, až ~5s se drží ve vzduchu a míří kurzorem místo dopadu (raycast na terén). Pokud nepotvrdí do 5s, ultimate vyprchá bez efektu. Po potvrzení dopadne na vybrané místo a rozdá AOE damage (`Physics.OverlapSphere` + stejný `RequestDamageServerRpc` vzorec jako u střelby, jen pro víc cílů). Plamenné efekty (Particle System) a zvuky (AudioSource) při vzletu i dopadu — vizuál/audio polish navrch, nedotýká se herní logiky. `AbilityDefinition` bude potřebovat nové pole `radius` (poloměr AOE), protože `power` už je obsazené jako damage u jiných abilit. Během letu se kamera přepne z first-person do **3rd person** (za/nad postavu), ať hráč vidí svoji postavu ve vzduchu — po dopadu zpět na first-person. Samotné přepínání kamery je nezávislé a jde postavit dřív, ale vizuálně bude dávat smysl až s reálnou viditelnou postavou z M8 (do té doby by šlo vidět jen kapsli).

**Stav implementace M7 (kód hotový a ověřený automatickým headless testem na kopii projektu — host + protihráč, střelba/kill/skóre, Ayranův skok s AOE, vyplýtvání ultimate, dash, konec zápasu, restart, přepnutí hrdiny; zbývá už jen ruční test ve 2 lidech přes reálnou LAN):**
- Hrdinové jako data: `HeroDefinition` (jméno, barva těla, HP, zbraň, typ schopnosti, voice lines) v `Assets/Resources/Heroes/` (Viktor = dash, Ayran = skok+AOE). Hrdina se vybírá v menu před Host/Join (panel vlevo), `PlayerHero` ho pošle serveru a aplikuje zbraň/HP/schopnost na všech klientech. Nový hrdina = nový `HeroDefinition` asset v té složce.
- Jeden sdílený `Player.prefab` (komponenty schopností se zapínají podle hrdiny) — místo Prefab Variants, jednodušší a bez dalších úprav `NetworkManageru`.
- Ayranův ultimate `LeapStrikeAbility` (klávesa Q): vzlet, 3rd person kamera, míření kurzorem (5 s), klik = dopad + AOE, bez potvrzení = vyplýtváno. AOE nezraňuje vlastní tým, kredit killů jde týmu.
- Konec zápasu: `MatchManager` (`matchOver`, `winnerTeam`, `RestartMatch`), obrazovka výhry, host restartuje klávesou R / tlačítkem. Friendly fire vypnutý.
- **Úvodní menu + lobby** (`MainMenuUI`, `LobbyUI`, `GameConnection`, `LanDiscovery`): hráč zadá přezdívku a buď **založí hru** (název hry), nebo se **připojí** — buď z **automaticky nalezeného seznamu her v LAN** (UDP broadcast na portu 47777), nebo ručně přes IP. Host poslouchá na všech rozhraních sám (už není potřeba ručně přepínat `Allow Remote Connections`/`0.0.0.0` na `NetworkManageru`). Po připojení všichni skončí v **lobby**: vidí ostatní, každý si vybere **tým** (nový hráč se automaticky zařadí do menšího) a **hrdinu**, host nastaví počet zabití na výhru a spustí zápas. Po konci zápasu host zvolí nový zápas (stejné týmy) nebo zpět do lobby. V lobby je hráč zmrazený a nedostává damage; hrdinu/tým jde měnit jen v lobby. Odchod ze hry přes Esc → nastavení nebo lobby.
- Ohnivý dopad v interiéru: vzlet se zastaví u stropu, míří se od hlavy (jen na plochy podlahového typu), kamera 3. osoby nezajede do zdí a výbuch zraní jen cíle s přímou viditelností (ne přes zeď).
- Dolet Ohnivého dopadu: `AbilityDefinition.range` (vodorovně od místa vzletu); mířit dál se dá, bod dopadu se přichytí na hranu dosahu (značka zežloutne).
- **Projektily**: `WeaponDefinition` má `fireMode` (Hitscan/Projectile), `projectileSpeed`, `projectileGravity`, `projectileRadius`, `explosionRadius`, `projectileColor`, `projectileTrail`, volitelný `projectilePrefab` (vlastní model); `range` a `damage` platí i pro projektily. Server simuluje projektil (`ProjectileSim`, bez `NetworkObject`, sweep kolize, výbuch přes `Combat.Explode`), klienti kreslí vizuál (`ProjectileVisual`, záře + stopa v barvě zbraně). Společná serverová logika zásahů je v `Combat` (friendly fire, viditelnost, kredit killů). Nový hrdina **Honza** má Raketomet (výbuch, 60 dmg, žádná schopnost). Ověřeno headless testem: let, blokování zdí, výbuch, dostřel, kill kredit.
- **Melee zbraně**: `FireMode.Melee` ve `WeaponDefinition` (`range` = dosah, `meleeRadius` = šířka úderu, bez munice). Nový melee hrdina = nový weapon asset s tímto režimem, žádná změna kódu. **Hitscan i melee** zobrazují na místě zásahu malý výbuch (`Fx.BulletImpact`, vidí ho všichni).
- **Zbraně v rukou** (`HeldWeapons`): hrdina drží zbraň (sekyra, pistole, raketomet z kostek jako placeholder), majitel ji vidí u kamery, ostatní na těle hráče. `WeaponDefinition.dualWield` = zbraň v obou rukou (útoky se střídají), `heldModel` (Auto/None/Gun/Axe) a volitelný `heldPrefab` pro vlastní model. Při kliknutí levým tlačítkem zbraň zamává (sekyra) nebo cukne (střelná). Sekyry používají stažený model `Assets/Models/StylizedAxe` (Sketchfab, Karthik Naidu — před případným zveřejněním hry zkontrolovat licenci); `HeldAxeSetup` z něj sám vytvoří `Prefab/HeldAxe.prefab` (otočí ho, zmenší na 0,62 m, přiřadí materiál s texturou) a nastaví ho jako `heldPrefab`. **Ayran má nově dvě sekyry (melee)** místo revolveru — `M7Setup` mu upraví asset `Ayran_Weapon` (jen pokud je ještě v původním stavu).
- **3D postavy** (Quaternius Universal Base Characters + Universal Animation Library, CC0): každý hrdina má prefab postavy (`Prefab/Characters/Char_*.prefab`, tělo + vlasy) v `HeroDefinition.characterPrefab`. `CharacterVisual` (přidává se za běhu) ji ukáže ostatním hráčům a animuje podle rychlosti pohybu (klid, chůze, klus, sprint, skok, smrt, útok mečem/střelba jen horní polovinou těla) přes Playables, zbraně jim `HeldWeapons` připevní ke kostem rukou. Majitel vidí model jen při Ohnivém dopadu (3. osoba). `CharacterSetup` (Editor) sám nastaví Humanoid import, sadu animací (`Resources/Characters/CharacterAnimations.asset`) a prefaby. Nový vzhled = jiný `characterPrefab` (jiné vlasy/kůže/model) na hrdinovi. Zatím bez animací chůze do stran/dozadu a bez přikrčení.
- **Druhá schopnost hrdiny** (`HeroDefinition.secondaryAbility`, klávesa Left Shift): Ayranův **Modrý plamen** (`RushAbility`) — jeden plynulý výpad dopředu ve směru pohledu (`range` = 12 m za `duration` = 0,45 s, na konci zpomalí), kamera ve 3. osobě, postava v útočné póze (pravá sekera vpřed, levá u těla, tělo nakloněné – zmrazený snímek animace úderu + náklon) a za ní zůstává modrá ohnivá stopa. Každého nepřítele v dosahu `radius` po cestě zasáhne jednou (`power` = 20) a Ayran si o způsobené poškození léčí (`healRatio`). Zeď nebo hráč v cestě výpad ukončí. Parametry v `Data/Rush_Data.asset`. Částice efektů mají měkkou kulatou texturu (dřív čtverce).
- **Blok** (`HeroDefinition.blockAbility`, `BlockAbility`, pravé tlačítko myši): Ayranovy **zkřížené sekyry** — při držení zablokuje 80 % příchozího poškození (`blockAbsorb`) a pohyb se zpomalí na 50 % (`blockSpeed`). Zablokovaná část ubírá z baru, jehož velikost je plné zdraví hrdiny; když se bar vyčerpá, blok přestane platit (zbytek zásahu projde) a hrdina zase chodí normálně. Bar se sám obnovuje po `regenDelay` (5 s) bez poškození, celý za `regenTime` (6 s). Malý bar pod zaměřovačem (`MatchUI`). Při bloku nejde útočit ani spustit Modrý plamen / Ohnivý dopad. Parametry v `Data/Block_Data.asset`. Poškození se filtruje centrálně v `Health.TakeDamage` (server), takže platí pro všechno (střely, výbuchy, sekery, nebezpečné zóny).
- **Ayranovy vlastní zvuky** (`OverwatchBros/Sound/pova/`, mimo `Assets`): `AyranSoundSetup` je zkopíruje do `Assets/Audio/Ayran` a přiřadí — `pova_q_boosted` se přehraje v okamžiku, kdy se potvrdí cíl a Ayran se začne řítit dolů (ne až při dopadu); ostatní generované zvuky Ohnivého dopadu (vzlet, vyplýtvání, výbuch) byly odstraněny, zůstávají jen vizuální efekty. Nahrávky prošly loudness normalizací (byly výrazně tišší než generované zvuky). Všechny tři (`q_boosted`, `shift_boosted`, `kill_boosted`) hrají přes `Fx.PlaySpatial` — opravdový 3D zdroj u postavy, plná hlasitost do ~4 m, útlum do ticha za ~30 m (místo výchozích 500 m u `PlayClipAtPoint`), takže je slyší i blízcí spoluhráči, ne jen majitel, `pova_shift_boosted` při aktivaci Modrého plamene (`AbilityDefinition.sound`, nahrazuje/doplňuje placeholder), `pova_kill_boosted` při zabití protihráče (přidá se do `HeroDefinition.killLines`, přehraje `HeroVoice`). Stejný mechanismus (pole `sound` v `AbilityDefinition`) jde použít pro zvuky dalších schopností.
- **Ayran podle fotky**: celá postava z MetaPerson Creator (`Assets/Models/Ayran/AyranAvatar.fbx`, bezplatný export = licence **non-commercial**, nesmí do zveřejněné/prodávané hry). `AyranAvatarSetup` nastaví Humanoid import, vytáhne textury, vytvoří `Prefab/Characters/Char_Ayran_Avatar.prefab` a přiřadí ho Ayranovi (`HeroDefinition.tintCharacter = false`, aby se nezabarvovala kůže). Animace a sekyry v rukou fungují stejně jako u ostatních postav (Humanoid retarget). Balíček `com.unity.cloud.gltfast` je přidaný pro případné GLB modely.
- **Ruce v pohledu z první osoby** (`FirstPersonArms`): u kamery je druhá kopie modelu postavy bez hlavy; paže se každý snímek natáhnou ke zbraním (dvoukloubová IK) a prsty jsou sevřené v pěst (Humanoid svaly), takže ruce sledují zbraň při mávání i bloku. Funguje pro každý Humanoid model; bez modelu postavy se použije jednoduchá pěst s rukávem (`HeroDefinition.sleeveColor` / `skinColor`). Úchop je odladěný na sekery, u pistole/raketometu je jen přibližný.
- **Růžové materiály v buildu**: objekty vytvořené kódem (`GameObject.CreatePrimitive`) a výchozí materiál ProBuilderu se v URP buildu vykreslovaly růžově (v editoru ne). Kódem vytvářené objekty teď používají `Fx.NewLit` (základ `Resources/Fx/Lit.mat`), a `MaterialFallback` po načtení scény nahradí materiály s nepodporovaným shaderem (celá mapa Domašov) šedým Lit materiálem. Čistší řešení do budoucna: přiřadit objektům mapy vlastní materiály v editoru.
- **Výška očí a dřep**: kamera z první osoby je 1,62 m nad chodidly (`FirstPersonController.eyeHeight`, dřív 0,6 m – vypadalo to jako postava zabořená v zemi), při dřepu klesne na 1,0 m. Kapsle se při dřepu zkracuje shora (střed se posouvá), takže se postava už nezaboří o půl metru do země.
- **HUD ve stylu Overwatch** (`HudUI`, staví se kódem): vlevo dole životy (číslo + bar dělený po 25 HP, pod 30 % červený), vpravo dole schopnosti hrdiny jako ikony s klávesou – připravená má oranžový proužek, na cooldownu je ztmavená s odpočtem sekund, aktivní je zvýrazněná, blok ukazuje procenta baru; nad nimi munice / název zbraně. Ikony jsou v `Resources/Icons` (bílé piktogramy, pole `AbilityDefinition.icon`). **Zásah**: okraje obrazovky krátce zčervenají (při nízkém zdraví zůstávají lehce červené) a přehraje se zvuk zásahu – `HeroDefinition.hurtLines` (zatím prázdné = generovaný placeholder), slyší ho i hráči v okolí.
- **Honza podle fotky**: druhý MetaPerson avatar (`Assets/Models/Honza/HonzaAvatar.fbx`, také **non-commercial**), `AyranAvatarSetup` teď obsluhuje víc hrdinů (tabulka `Entries`) a vytvoří `Char_Honza_Avatar.prefab`. Na hrudi trička je zapečený potisk „Doktor z hor" (do `Textures/KARI_Color_1K.jpg`, 2K); zdroje a skript na přepečení jsou v `OverwatchBros/Art/Honza` (`bake_print.py` – střed a výška potisku jako parametry).
- **Oprava Shiftu a portu**: stisk Modrého plamene se 0,4 s pamatuje, jde spustit z bloku a o nepřítele se nezarazí předčasně; `Editor/NetworkPortGuard` zavře síť před překompilováním v Play módu (jinak zůstal viset port 7777).
- **Honzovy schopnosti (inspirace Junkrat)**: `Granátomet` (granáty letí obloukem, odrážejí se – `WeaponDefinition.projectileBounce/projectileFuse` – a vybuchnou o hráče nebo po chvíli), **Nálož** (`MineAbility`: Shift hodí a přilepí, pravé tlačítko odpálí všechny naráz – jako Junkrat; zraní a odhodí nepřátele, Honzu jen vystřelí do vzduchu; 2 náboje, venku až 2 nálože), **Past na medvědy** na E (`TrapAbility`: 40 dmg + 2 s znehybnění), **Balvan** na Q (`BoulderAbility`: řiditelný valící se balvan s kamerou za ním, výbuch na kliknutí / po 10 s, jde rozstřílet – `BoulderHitbox`), pasivně granáty po smrti (`HeroDefinition.deathGrenades`). Nové mechaniky: odhození a znehybnění hráče (`FirstPersonController.ServerKnockback/ServerRoot`, `Combat.Knockback`), třetí schopnost na E (`HeroDefinition.altAbility`), počet nábojů v HUD. Cooldowny jsou zatím 1 s kvůli testování; zamýšlené hodnoty: nálož 8 s, past 10 s, balvan 30 s.
- **Přebíjení**: trvá `WeaponDefinition.reloadTime` (náboje přibydou až na konci, během přebíjení nejde střílet), prázdný zásobník se přebije sám, HUD ukazuje „PŘEBÍJÍM…". Animace: granátomet má vlastní (`reloadAnimation` – hlaveň nahoru, tři zasunutí), ostatní zbraně se jen skloní; postava ve 3. osobě hraje `Pistol_Reload` na horní polovině těla (`CharacterAnimSet.reload`, vrstva 5).
- **Připojení do rozehraného zápasu**: kdo se připojí, když zápas už běží, dostane obrazovku výběru (lobby s nadpisem „ZÁPAS UŽ BĚŽÍ", tlačítko VSTOUPIT DO HRY) a může si zvolit tým i hrdinu. Do té doby je `PlayerHero.joining` = true: stojí mimo mapu, nejde zranit a nemůže nic dělat; ostatní hrají dál. Restart zápasu / návrat do lobby příznak zruší.
- **Sticky Keys**: Windows funkce „Jedním prstem" způsobovala, že hra viděla jen každý druhý stisk Shiftu (Modrý plamen, Nálož). `StickyKeysGuard` ji vypne, dokud má hra fokus, a při odchodu vrátí původní stav (stejně jako velké hry).
- **Hlášky hrdinů (schránky)**: `HeroDefinition` má seznamy `spawnLines` (výběr hrdiny / oživení), `killLines`, `deathLines`, `hurtLines`, `idleLines` (náhodně při chůzi, jednou za 25–50 s), `snareLines` (když ho někdo znehybní / zpomalí, např. past) a každá schopnost má vlastní `AbilityDefinition.voiceLines`. Z více nahrávek se vybírá náhodně (výběr dělá server, takže všichni slyší totéž – `PlayerHero.Say`), důležitější hláška přeruší méně důležitou (`HeroVoice`). Prázdný seznam = ticho. Nahrávky stačí nakopírovat do složek `Assets/Audio/<Hrdina>/{spawn,kill,death,hurt,idle,snare,ability_Q,ability_Shift,ability_E,ability_Block}` – `Editor/VoiceLineSetup` složky vytvoří a nahrávky přiřadí sám (menu BrosOverwatch → Přiřadit hlášky ze složek).
- **Potvrzení zásahu**: útočník vidí křížek kolem zaměřovače, když někomu ubere životy, a při zabití červený křížek s lebkou (`Combat.DamagePlayer` → `PlayerHero.ServerNotifyHit` → `HudUI.NotifyHit`).
- **Rozlišení týmů, zabití a tabulka** (`MatchOverlayUI`): nad hlavami hráčů jmenovka se životy – spoluhráči modře (vidět i přes zeď), nepřátelé červeně (jen když jsou opravdu vidět); vpravo nahoře seznam zabití (kdo » koho, vlastní zvýrazněná); Tab ukáže tabulku hráčů po týmech se zabitími a smrtmi (`PlayerHero.kills/deaths`, nulují se s novým zápasem). Granátomet: 60 dmg, přímý zásah dává plné poškození, větší koule (0,22 m).
- **Viktorovy schopnosti (inspirace Soldier 76 / Cassidy)**: **Úskok** na Shiftu (`DashAbility`: ve směru pohybu, přebije zbraň), **Léčivé pole** na E (`HealFieldAbility`: 20 HP/s pro něj a spoluhráče, 5 s, poloměr 5 m), **Oslepující granát** na pravém tlačítku (`FlashAbility`: 15 dmg + omráčení 0,8 s – `FirstPersonController.ServerStun`, oběť má bílou obrazovku), **Taktický zaměřovač** na Q (`VisorAbility`: 6 s střely samy míří na nepřítele nejblíž středu obrazovky v kuželu 30°, žlutá značka cíle). Nový slot `HeroDefinition.rmbAbility` (hlášky ve složce `ability_RMB`). Nálož má 60 dmg. Cooldowny zatím 1 s; zamýšlené: úskok 6 s, pole 15 s, granát 10 s, zaměřovač 30 s.
- **Play of the game**: každý hráč si u sebe průběžně nahrává posledních pár sekund svého obrazu (pohled z první osoby i s HUD, 640×360, 15 snímků/s, JPG – `PotgRecorder`). Po zabití (nebo velkém léčení spoluhráčů) mu server pošle skóre akce za poslední ~4 s – zabití 100, další v řadě +50, poškození 0,4/bod, léčení spoluhráčů 0,7/bod, zabití ultimátkou +50, zabití nepřítele při jeho ultimátce +75, z dálky +30, rozhodující zabití +50, omráčení +20 – a klient si odloží 5s klip; lepší akce přepíše horší. Úvodní karta ukazuje i typ akce. 2,5 s po konci zápasu server vybere hráče s nejlepší akcí, ten pošle snímky a všem se přehrají (`PotgUI`: úvodní karta se jménem, pak video) i se zvukem hry (`PotgAudioTap` odposlouchává výstup u AudioListeneru, mono 24 kHz). Nový zápas přehrávání zruší. Omráčení má zvuk (`ProceduralSfx.Stun`, pro střelce `StunConfirm`).
- `MatchUI` (celé UI vytvářené kódem): menu, lobby, skóre týmů, stav schopnosti, zaměřovač, konec zápasu, nastavení (Esc: citlivost, hlasitost). Původní Host/Join tlačítka a HP/Ammo texty ve scéně se nově schovávají (texty se ukazují jen za hry).
- Zvuky (`ProceduralSfx`) a efekty (`Fx`: plameny, exploze, značka dopadu) jsou zatím generované kódem jako placeholder. Voice lines: přetáhnout nahrávky do polí `Spawn/Kill/Death Lines` v hero assetu (`HeroVoice` je přehraje).
- `Assets/Editor/M7Setup.cs` při prvním otevření projektu sám vytvoří hrdinské assety, materiál efektů a doplní komponenty do `Player.prefab` (jde spustit i ručně: menu `BrosOverwatch > Setup M7`).

> Gameplay loop je teď ověřený → přechod na škálování art pipeline.

### M8 — Character Art Pipeline (stylizovaný)

Base humanoidní tělo (male/female), Mixamo auto-rig, Humanoid avatar v Unity. První skutečný obličej kamaráda: FaceBuilder → decimace → stylizovaný materiál/shader (ne photoreal). Sdílená sada animací z Mixama (idle/walk/run/jump/shoot/reload/death) retargetovaná na Humanoid rig.
**DoD:** první "skutečná" postava (poznatelný kamarád) nahradí grey-box hrdinu vizuálně ve hře, animace fungují i po síti.

### M9 — Scale Content

Zopakovat M8 pipeline pro zbytek kamarádů/hrdinů. Zopakovat M6 proces pro další mapy. Přidat zbývající zbraně/abilities jako nové ScriptableObject assety. LOD setup, performance pass na cílový min-spec.

### M10 — Polish & Release-to-friends

UI polish, settings menu, audio mix, bugfixing, sestavené buildy pro LAN párty.

## Nápady k zapracování (zatím neimplementováno)

Věci, na které jsme narazili v diskuzi, zapadají do architektury beze změny existujícího kódu, ale zatím nejsou potřeba. Až na ně dojde řada, patří sem:

- ~~**Projektilové zbraně**~~ — hotovo v M7 (viz níže u "Projektily"); zbývá jen vlastní modely projektilů (pole `projectilePrefab`) v M8+.
- **Hitboxy po částech těla** (headshoty) — víc menších colliderů (head/body) na skutečné postavě, `hit.collider` určí násobič damage. Nemá smysl bez rigovaných postav. → **M8+**.
- **Animace zbraně** (viewmodel, idle/fire/reload) — 3D model zbraně jako child kamery + `Animator`, spouštěný z `WeaponShooting` (`animator.SetTrigger(...)`) na místech, kde už dnes voláme `Debug.Log`. → **M7 (placeholder) / M9-M10 (finální model)**.
- **Melee útok** — stejný princip jako `WeaponShooting`, jen `Physics.SphereCast`/`OverlapSphere` na krátký dosah místo raycastu na `weapon.range`. Volání `TakeDamage` beze změny. → **M5**, jako druhý typ zbraně (test, že data-driven systém zvládne i jiný typ).
- **Meele obrana/blok (parry)** — hrdinská ability (`AbilityDefinition`), ne zbraň. `Health.TakeDamage()` dostane na začátku podmínku `if (isBlocking) ...`. → **M5**.
- Dead Cam, pokud hrac zemre -> uvidi svoji 3D animaci z 3ti osoby
- **Podzemní tunel** (u velké chaty na M6 mapě) — samostatná uzavřená podzemní místnost (Y ~ -3 až -0.5) propojená se schodišťovou šachtou nahoru. Vyžaduje rozdělit `Ground` na víc dlaždic místo jedné plochy, aby u vstupu zůstala mezera pro sestup. Bonus rozšíření mapy, ne core požadavek M6 — přidat až po dokončení základního layoutu (chaty, rozhledna, jezírko/bazén, spawny).
- **Zvukové efekty (SFX)** — výstřel, prázdný zásobník, reload, zásah/damage, smrt, footsteps, UI kliky. Zatím v roadmapě jen Voice lines (M7). Placeholder zvuky (`AudioSource.PlayOneShot` na stejných místech, kde dnes voláme `Debug.Log` ve `WeaponShooting`/`Health`) → **M7** spolu s Voice lines; finální namixované/vyladěné zvuky → **M10 (Polish)**, kde je i zmíněný "audio mix".

## Poznámka k postupu

Nepokračuj na M8+ (art), dokud M7 (vertical slice) není hratelné a zábavné. Riziko projektu je v gameplay loopu, ne v grafice.
