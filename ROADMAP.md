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

### M0 — Hello Cube (project skeleton)
Unity 6 projekt (URP template), Git + Git LFS nastavené, .gitignore pro Unity. Jedna scéna, Cube, C# skript co ho hýbe šipkami/WASD.
**DoD:** Play mode hýbe kostkou, projekt jde i sestavit do .exe a spustit mimo editor.

### M1 — Local First-Person Controller
Cube nahrazen player prefabem s FPS kamerou. Walk/run/jump/crouch, kolize se zemí. Grey-box testovací místnost v ProBuilderu.
**DoD:** chození po grey-box místnosti v první osobě, stabilní FPS.

### M2 — Shooting Core (data-driven)
`WeaponDefinition` ScriptableObject (damage, rate of fire, ammo, hitscan/projectile). Raycast střelba na dummy terče, crosshair, ammo/reload UI.
**DoD:** střelba na dummy terč, vidíš poškození/log.

### M3 — Health & Respawn
`Health` komponenta, damage pipeline, smrt, respawn na spawn pointu. Základní HUD (health, ammo).
**DoD:** hráč umře od terče/hazardu a respawnne se.

### M4 — LAN Multiplayer Core
Netcode for GameObjects, host/join přes LAN (lokální síť). Síťovaný pohyb + síťovaná střelba/health, server-authoritative.
**DoD:** dva reálné notebooky na stejné síti se vidí, střílí po sobě, health se správně synchronizuje.

### M5 — First Hero (ScriptableObject architektura)
`HeroDefinition` + `AbilityDefinition`. Jeden hrdina s 1–2 abilities (např. dash + ultimate). Team assignment, team spawny.
**DoD:** jeden plně hratelný hrdina s abilitou funguje přes LAN, 2 týmy.

### M6 — First Real Map
Skutečný layout mapy v ProBuilderu (cíl: deathmatch nebo capture point), správné měřítko pro počet hráčů. Pořád grey-box materiály.
**DoD:** hratelný LAN zápas na reálném layoutu, team spawny, win condition (skóre/cíl).

### M7 — Vertical Slice Playable Loop
Druhý hrdina (ověří, že ScriptableObject systém škáluje bez nového kódu). Kompletní match loop: join → spawn → hraní → smrt/respawn → konec zápasu/restart. Voice lines (spawn/kill/death) přes `HeroVoice`.
**DoD:** ty + kamarádi odehrajete celý zápas od začátku do konce přes LAN se 2 hrdiny — na tomhle bodě je hra poprvé "hratelná jako hra".

> Gameplay loop je teď ověřený → přechod na škálování art pipeline.

### M8 — Character Art Pipeline (stylizovaný)
Base humanoidní tělo (male/female), Mixamo auto-rig, Humanoid avatar v Unity. První skutečný obličej kamaráda: FaceBuilder → decimace → stylizovaný materiál/shader (ne photoreal). Sdílená sada animací z Mixama (idle/walk/run/jump/shoot/reload/death) retargetovaná na Humanoid rig.
**DoD:** první "skutečná" postava (poznatelný kamarád) nahradí grey-box hrdinu vizuálně ve hře, animace fungují i po síti.

### M9 — Scale Content
Zopakovat M8 pipeline pro zbytek kamarádů/hrdinů. Zopakovat M6 proces pro další mapy. Přidat zbývající zbraně/abilities jako nové ScriptableObject assety. LOD setup, performance pass na cílový min-spec.

### M10 — Polish & Release-to-friends
UI polish, settings menu, audio mix, bugfixing, sestavené buildy pro LAN párty.

## Poznámka k postupu
Nepokračuj na M8+ (art), dokud M7 (vertical slice) není hratelné a zábavné. Riziko projektu je v gameplay loopu, ne v grafice.
