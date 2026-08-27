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

**Poznámka k nastavení hostitele pro reálný LAN test:** na `NetworkManager` → `Unity Transport` u buildu, který hostuje, musí být zaškrtnuté **Allow Remote Connections** a **Address** nastavená na `0.0.0.0` (ne `127.0.0.1`, který poslouchá jen lokálně). Bez tohoto se z jiného stroje nejde připojit, i když je vše ostatní správně.

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

- **Projektilové zbraně** (rakety, šípy) — vedle `WeaponDefinition` s hitscan přidat variantu s `Instantiate` + `Rigidbody` a damage přes `OnCollisionEnter`. Síťově dražší než hitscan (nutno spawnout `NetworkObject`). → **M5**, u druhé zbraně/hrdiny.
- **Hitboxy po částech těla** (headshoty) — víc menších colliderů (head/body) na skutečné postavě, `hit.collider` určí násobič damage. Nemá smysl bez rigovaných postav. → **M8+**.
- **Animace zbraně** (viewmodel, idle/fire/reload) — 3D model zbraně jako child kamery + `Animator`, spouštěný z `WeaponShooting` (`animator.SetTrigger(...)`) na místech, kde už dnes voláme `Debug.Log`. → **M7 (placeholder) / M9-M10 (finální model)**.
- **Melee útok** — stejný princip jako `WeaponShooting`, jen `Physics.SphereCast`/`OverlapSphere` na krátký dosah místo raycastu na `weapon.range`. Volání `TakeDamage` beze změny. → **M5**, jako druhý typ zbraně (test, že data-driven systém zvládne i jiný typ).
- **Meele obrana/blok (parry)** — hrdinská ability (`AbilityDefinition`), ne zbraň. `Health.TakeDamage()` dostane na začátku podmínku `if (isBlocking) ...`. → **M5**.
- Dead Cam, pokud hrac zemre -> uvidi svoji 3D animaci z 3ti osoby
- **Podzemní tunel** (u velké chaty na M6 mapě) — samostatná uzavřená podzemní místnost (Y ~ -3 až -0.5) propojená se schodišťovou šachtou nahoru. Vyžaduje rozdělit `Ground` na víc dlaždic místo jedné plochy, aby u vstupu zůstala mezera pro sestup. Bonus rozšíření mapy, ne core požadavek M6 — přidat až po dokončení základního layoutu (chaty, rozhledna, jezírko/bazén, spawny).
- **Zvukové efekty (SFX)** — výstřel, prázdný zásobník, reload, zásah/damage, smrt, footsteps, UI kliky. Zatím v roadmapě jen Voice lines (M7). Placeholder zvuky (`AudioSource.PlayOneShot` na stejných místech, kde dnes voláme `Debug.Log` ve `WeaponShooting`/`Health`) → **M7** spolu s Voice lines; finální namixované/vyladěné zvuky → **M10 (Polish)**, kde je i zmíněný "audio mix".

## Poznámka k postupu

Nepokračuj na M8+ (art), dokud M7 (vertical slice) není hratelné a zábavné. Riziko projektu je v gameplay loopu, ne v grafice.
