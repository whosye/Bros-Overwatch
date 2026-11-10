"""Explicit collection catalog based on AUDIO_CHECKLIST.md; no runtime Unity edits."""
import json
from pathlib import Path

items = []


def add(id, title, group, description, filename, *, section="", priority=2, optional=False,
        search="", existing=None):
    items.append(dict(id=id, title=title, group=group, section=section,
                      description=description, filename=filename, priority=priority,
                      optional=optional, search=search or filename.replace("_", " "),
                      existing=existing or []))


surfaces = [
    ("wood", "Dřevěná podlaha", "Chaty, schody, rozhledna, lávky, terasy a dojo.", 1),
    ("solid_wood", "Pevné dřevo", "Bedny, trámy a nábytek. Můžeš sdílet s dřevěnou podlahou.", 2),
    ("stone", "Beton a kámen", "Tunely, rampy, skály a kamenné terasy.", 1),
    ("tile", "Dlažba a keramika", "Obývák, podesta krbu a dlážděné město Hanamury.", 1),
    ("gravel", "Štěrk", "Cesty kolem chat a trasa Yarisu.", 1),
    ("grass", "Tráva", "Louka, zahrada a trávníky obou map.", 1),
    ("dirt", "Hlína", "Zemina a měkké plochy zahrady.", 2),
    ("sand", "Písek", "Cesty a nádvoří Hanamury.", 2),
    ("carpet", "Koberec", "Tlumené kroky na kobercích v chatách.", 1),
    ("rubber", "Guma", "Podlaha Pavoukova workshopu, případně pásy rolby.", 2),
    ("metal", "Kovový plech", "Rolba, kovové plošinky a prahy.", 2),
    ("roof", "Střešní tašky", "Střechy chat a Hanamury. Šindel může sdílet dřevo.", 2),
    ("hay", "Seno", "Balíky sena. Stačí 3–4 kroky a 1–2 dopady.", 2),
    ("sawdust", "Piliny a pytle", "Okolí kotle. Stačí menší sada; pytle mohou sdílet textil.", 2),
    ("water", "Mělká voda", "Brodění, šplouchavé kroky a výstup z vody.", 2),
    ("slide", "Povrch skluzavky", "Kontakt a sklouznutí. Lze sdílet s hladkým tvrdým povrchem.", 3),
]
for key, title, description, priority in surfaces:
    add("steps_" + key, title, "Pohyb a povrchy",
        description + " Pro hlavní povrchy 6–8 jednotlivých kroků a 2–3 dopady po skoku. "
        "Přiřaď sem všechny varianty; kroky ani dopady nemusíš řešit po jednom řádku.",
        f"footstep_{key}_01.wav / land_{key}_01.wav", section="Kroky a dopady", priority=priority,
        optional=key == "slide", search=f"footsteps {key.replace('_', ' ')} single landing")

for key, title, text, query in [
    ("jump", "Odraz a dvojskok", "Krátký odraz; druhý skok Mirka a Mošina může mít vzdušnou vrstvu.", "jump takeoff double jump"),
    ("ladder", "Lezení po žebříku", "Kontakty rukou a bot s příčkami rozhledny, 4–6 variant.", "ladder climbing footsteps"),
    ("ledge", "Vytažení na hranu", "Mirek: úchop a přitažení těla.", "ledge grab climb cloth"),
    ("wallride", "Jízda po zdi", "Mošino: nástup, smyčka tření a odraz/ukončení.", "wall slide scrape loop"),
    ("clothes", "Pohyb oblečení", "Jemné šustění výstroje při prudkém pohybu. Volitelná společná sada.", "cloth movement foley"),
    ("body_wall", "Náraz hráče do stěny", "Těžký tělesný úder po odhození, také pro Mošinův basový úder.", "body impact wall thud"),
]:
    add("move_" + key, title, "Pohyb a povrchy", text, key + "_01.wav",
        section="Další pohyb", optional=key == "clothes", search=query)

for key, title, description, query in [
    ("empty", "Prázdná zbraň", "Krátké cvaknutí. Společný základ pro střelné zbraně.", "gun dry fire empty click"),
    ("bullet_wood", "Kulka → dřevo", "3–5 variant.", "bullet impact wood"),
    ("bullet_stone", "Kulka → kámen, beton, omítka", "3–5 variant; materiály mohou sdílet sadu.", "bullet impact concrete stone"),
    ("bullet_metal", "Kulka → kov", "3–5 krátkých zvonivých variant.", "bullet impact metal"),
    ("bullet_glass", "Kulka → sklo", "2–3 tvrdá cinknutí, bez požadavku na rozbití celého okna.", "bullet impact glass"),
    ("bullet_ground", "Kulka → hlína, písek, štěrk", "3–5 variant; lze vrstvit kamínky.", "bullet impact dirt sand gravel"),
    ("bullet_leaves", "Kulka → tráva a listí", "Tlumené kontakty s vegetací; doplněk materiálového rozlišení.", "bullet impact foliage leaves"),
    ("bullet_soft", "Kulka → měkký povrch", "Seno, textil, pytle a guma; 2–3 varianty mohou být sdílené.", "bullet impact fabric soft"),
    ("bullet_water", "Kulka → voda", "2–3 malé šplouchance; doplněk podle kolize vodní plochy.", "bullet water splash small"),
    ("slash_wood", "Sekera / kosa → dřevo", "3 varianty; sdílené pro Povu a Maxe.", "axe wood hit impact"),
    ("slash_stone", "Sekera / kosa → tvrdý povrch", "Kámen, omítka a keramika; 3 varianty.", "sword stone impact"),
    ("slash_metal", "Sekera / kosa → kov", "Kovový sečný kontakt, 3 varianty.", "sword metal impact"),
    ("slash_soft", "Sekera / kosa → měkký povrch", "Tlumený kontakt; lze sdílet textil a seno.", "blade soft impact"),
    ("arrow_impact", "Dopady šípů", "Dřevo, tvrdý a měkký povrch, po 2–3 variantách.", "arrow impact wood stone"),
    ("grenade_bounce", "Odraz granátu", "Kovové odrazy od stěny a tvrdé země, 3–5 variant.", "grenade bounce metal"),
    ("explosion_small", "Malý výbuch", "Granát, nálož a granáty po smrti, asi 3 varianty.", "grenade explosion small"),
    ("explosion_large", "Velký výbuch / úder", "Balvan a výrazné ultimátky, 2–3 varianty.", "explosion large heavy impact"),
]:
    add("combat_" + key, title, "Zbraně a zásahy", description, key + "_01.wav",
        section="Společná knihovna", priority=1 if key in ("empty", "bullet_wood", "bullet_stone", "bullet_metal", "explosion_small") else 2,
        optional=key in ("bullet_leaves", "bullet_water", "arrow_impact"), search=query)

add("combat_melee_body", "Sekera / kosa → hráč (společná sada)", "Zbraně a zásahy",
    "Společná náhodná sada sečných zásahů do hráčů pro všechny postavy. Pouze kontakt s živým cílem; švih ani zásah zdi sem nepatří.",
    "melee_body_01.wav", section="Společná knihovna", priority=1, search="knife flesh melee player hit stab")

states = [
    ("hit", "Potvrzení zásahu", "Krátký čistý hitmarker, odlišný od prostorového dopadu.", False),
    ("headshot_kill", "Headshot a zabití", "Dvě odlišitelné krátké varianty potvrzení.", True),
    ("body_hit", "Zásahy do těla", "Kulka a sečný zásah, po 3–5 variantách.", True),
    ("hurt", "Přijetí poškození", "Společná reakce tam, kde ji nenahradí hlas postavy.", False),
    ("death", "Smrt a pád těla", "2–3 varianty; hlášky smrti jsou oddělené.", False),
    ("respawn", "Oživení", "Krátký efekt příchodu zpátky do hry.", False),
    ("stun", "Omráčení", "Nástup a krátký dozvuk.", False),
    ("stun_confirm", "Potvrzení omráčení / uspání", "Krátké potvrzení pro útočníka.", False),
    ("sleep", "Uspání a probuzení", "Doplněk k Davidově šipce, nástup a konec.", True),
    ("slow_root", "Zpomalení a uvolnění", "Jemný signál znehybnění/zpomalení; sklapnutí pasti je zvlášť.", True),
    ("silence_antiheal", "Umlčení a blokace léčení", "Dvě jemné rozlišitelné značky pro Šindela a Davida.", True),
    ("shield", "Štít", "Získání, zásah a rozbití. Společná sada pro více zdrojů štítu.", True),
    ("heal_buff", "Léčení a zesílení", "Přijetí léčení/buffu a jeho konec. Nemusí hrát každý léčivý tik.", True),
    ("denied", "Neplatná akce / žádný cíl", "Společné krátké zamítnutí.", False),
    ("ready", "Ultimátka / náboj připravený", "Dosažení 100 %, obnovení náboje a nedostupná schopnost.", True),
    ("ult_start", "Společný nástup ultimátky", "Záložní efekt pro přípravu ultimátky; vlastní nástupy mají přednost.", False),
]
for key, title, description, optional in states:
    add("state_" + key, title, "Zbraně a zásahy", description, key + ".wav",
        section="Zpětná vazba a stavy", optional=optional, priority=3 if optional else 2)

heroes = {
    "pova": ("Pova", [
        ("weapon", "Sekyrky", "Lehký švih vlevo/vpravo a silnější třetí úder komba. Zásahy sdílej se společnou knihovnou.", "axe swing whoosh", 1),
        ("rush", "Modrý plamen · Shift", "Rozhoření, smyčka plamene, kontakt a uhasnutí. Hlasy už máme.", "fire burst flamethrower loop", 2),
        ("hook", "Sekera na řetězu · E", "Vyhození, rozvinutí řetězu, zachycení, přitažení a návrat při minutí.", "chain throw hook pull", 2),
        ("block", "Zkřížené sekyry · RMB", "Nastavení bloku, kovový zásah a proražení/vyčerpání.", "metal parry block break", 2),
        ("ult", "Ohnivý dopad · Q", "Výrazný nástup, stoupání/let, prudký sestup a ohnivý dopad. Hlasy už máme.", "fire ultimate meteor impact", 1),
    ]),
    "honza": ("Honza", [
        ("reload", "Přebití granátometu", "Otevření, vložení munice a zavření podle animace. Výstřel už máme.", "grenade launcher reload", 1),
        ("mine", "Nálož · Shift / RMB", "Hod, dopad/přichycení a odpálení. Výbuch může být společný.", "mine throw stick detonate", 2),
        ("trap", "Past na medvědy · E", "Natažení, hod, kovový dopad, sklapnutí a zničení.", "bear trap set snap", 2),
        ("ult", "Balvan · Q", "Příprava, vypuštění, valení ve smyčce, náraz a koncový výbuch/zničení.", "boulder roll stone impact", 1),
        ("death_grenades", "Granáty po smrti", "Rozsypání a kovové odrazy čtyř granátů; exploze lze sdílet.", "grenade scatter bounce", 2),
    ]),
    "viktor": ("Viktor", [
        ("reload", "Přebití zbraně", "Vyjmutí/vložení zásobníku a závěr. Výstřel už máme.", "rifle pistol reload magazine", 1),
        ("dash", "Úskok · Shift", "Krátký rychlý pohyb a došlap.", "dash whoosh", 2),
        ("heal", "Léčivé pole · E", "Položení zařízení, spuštění, jemná smyčka a vypnutí.", "healing device deploy loop", 2),
        ("grenade", "Pulzní granát · RMB", "Vypuštění, let a elektrický/omračující výbuch.", "electric grenade pulse explosion", 2),
        ("ult", "Taktický zaměřovač · Q", "Zapnutí elektroniky, skenování/zaměření a konec.", "visor activation target lock", 1),
    ]),
    "mirek": ("Mirek", [
        ("weapon", "Luk a šíp", "Založení šípu, napnutí tětivy, puštění, průlet a dopad. Bez přebíjení R.", "bow draw release arrow flight", 1),
        ("jump", "Výskok · Shift", "Prudký odraz a krátké prosvištění.", "jump leap whoosh", 2),
        ("scout", "Průzkumný šíp · E", "Odlišené vypuštění, dopad a aktivace skenu; sonarové pulsy volitelně.", "sonar arrow scan pulse", 2),
        ("rapid", "Rychlopalba · RMB", "Aktivace rychlé série. Jednotlivé šípy sdílejí základní luk.", "bow rapid fire activation", 2),
        ("ult", "Smršť · Q", "Výrazný nástup, vypuštění víru, smyčka letu, kontakt a rozplynutí.", "wind vortex tornado magic", 1),
    ]),
    "david": ("David", [
        ("reload", "Přebití biotické pušky", "Přebití; případně přepnutí optiky. Výstřel už máme.", "rifle reload bolt", 1),
        ("impact", "Biotická střela · kontakt", "Odlišit léčivý kontakt spojence a útočný zásah nepřítele.", "healing dart impact", 2),
        ("sleep", "Uspávací šipka · Shift", "Vystřelení malé šipky, kontakt a uspání. Probuzení může být společné.", "dart gun shoot sleep", 2),
        ("grenade", "Biotický granát · E", "Hod, rozstřik kapaliny, léčivá a negativní vrstva.", "bottle liquid splash magic", 2),
        ("ult", "Posílení · Q", "Hod/dopad plechovky Monsteru, energetický nástup a konec. Hudbu boostu máme.", "can throw energy power up", 1),
    ]),
    "kondulic": ("Kondulič", [
        ("reload", "Přebití odstřelovačky", "Zásobník/mechanika zbraně. Oba režimy střelby už máme.", "sniper rifle reload", 1),
        ("scope", "Optika a nabití rány", "Přepnutí do/z optiky, nabíhání síly rány a plné nabití.", "scope zoom charge ready", 2),
        ("hook", "Hák · Shift", "Vystřelení, kontakt, napnutí lana, navíjení a uvolnění.", "grappling hook cable reel", 2),
        ("kick", "Odkopnutí · E", "Švih nohou a pevný tělesný zásah.", "kick swing body impact", 2),
        ("ult", "Infravize · Q", "Elektronický nástup, jemný aktivní stav a vypnutí.", "infrared vision scan activation", 1),
    ]),
    "mosino": ("Mošino", [
        ("fire", "Zvukomet · výstřel", "Krátký hlubší energetický puls / zvuková vlna. Kandidáty nejdřív porovnej poslechem; část může sloužit jen jako basová nebo elektrická vrstva.", "sonic energy pulse bass wave shot", 1),
        ("reload", "Přebití zvukometu", "Obnovení zdroje; výstřel už máme.", "sci fi gun reload", 1),
        ("impact", "Dopad zvukového projektilu", "Krátký zvukový/energetický kontakt.", "sonic projectile impact", 2),
        ("crossfade", "Přepnutí rytmu · Shift", "Dvě krátká přepnutí: léčivá a rychlostní aura. Obě hudební smyčky máme.", "music switch transition", 2),
        ("amp", "Zesílení · E", "Rytmický nástup a návrat; aktivní vrstvu lze postavit na současné hudbě.", "bass music amp up", 2),
        ("wave", "Basový úder · RMB", "Silný krátký basový tlak a odraz; náraz oběti může být společný.", "bass blast sonic boom", 2),
        ("ult", "Drop the Beat · Q", "Výskok, velký basový dopad, štít, taneční vrstva a konec.", "bass drop ultimate impact", 1),
        ("beat", "Střelba do taktu", "Krátké pozitivní potvrzení rytmického bonusu.", "rhythm beat success", 2),
    ]),
    "sindel": ("Šindel", [
        ("reload", "Přebití Devítky", "Přebití pistole: zásobník a závěr. Výstřel máme.", "pistol reload magazine", 1),
        ("jump", "Spravedlivý skok · Shift", "Odraz, vznášení a doznění/očištění.", "holy leap hover whoosh", 2),
        ("grenade", "Svatý granát · E", "Hod, tvrdý kontakt a výrazná exploze se svatou vrstvou. Předvýbuchovou nahrávku máme.", "holy grenade choir explosion", 2),
        ("wave", "Say what again?! · RMB · efekt", "Tlaková/zastrašující vlna. Hlasový výkřik je zvlášť v Hláškách.", "fear shout sonic wave", 2),
        ("ult", "Ezechiel 25:17 · Q", "Vzlet, napětí během kázání a mohutný světelný úder. Kázání už máme.", "holy light strike thunder", 1),
    ]),
    "max": ("Max", [
        ("weapon", "Kosa", "Běžný švih a těžší třetí úder. Zásahy sdílej se společnou sadou.", "scythe sword swing", 1),
        ("reap", "Rozpolcení · Shift", "Výpad, rotační švih kosy a zásah. Chybějící hlas je zvlášť.", "scythe spin dash slash", 2),
        ("shadow", "Stínová forma · E", "Přechod do stínu, jemná smyčka průchodu a zhmotnění.", "shadow ghost phase loop", 2),
        ("slash", "Dlouhý řez · RMB", "Těžký švih, rozlet stínové vlny a zásah.", "dark magic slash wave", 2),
        ("ult", "Vpád do stínu · Q", "Nástup, vstup do oběti, vnitřní stínový stav a vyražení s úderem.", "shadow enter emerge impact", 1),
    ]),
}
for hero, (name, rows) in heroes.items():
    for key, title, description, search, priority in rows:
        add(hero + "_" + key, title, "Postavy", description, hero + "_" + key + ".wav",
            section=name, search=search, priority=priority)

world = [
    ("stream", "Potok", "Klidné tekoucí zurčení ve smyčce.", "stream water loop", False),
    ("water_ride", "Jízda proudem", "Silnější šplouchání ve smyčce, nástup a výstup. Hudbu toboganu máme.", "water rush splash loop", False),
    ("splash", "Malý a velký splash", "Vstup do vody a velký dopad do jezírka, několik variant.", "water splash jump big", False),
    ("slide", "Klouzání po skluzavce", "Kontakt těla s povrchem a skluz.", "slide body sliding", True),
    ("boiler_lever", "Kotel · páka", "Kovové zatažení a zacvaknutí.", "metal lever switch", False),
    ("boiler_fire", "Kotel · oheň a přehřátí", "Rozhoření, hoření/hučení ve smyčce, přehřátí a dohasnutí.", "furnace fire roar loop", False),
    ("boiler_warning", "Kotel · varování v patře", "Krátký varovný signál nebo syčení horka.", "steam heat hiss warning", True),
    ("fireplace", "Krb a gril", "Praskající dřevo ve smyčce.", "fireplace wood crackle loop", True),
    ("groomer_engine", "Rolba · motor", "Start, volnoběh, záběr pod zátěží a vypnutí. Dnes generovaný motor.", "diesel engine start idle loop stop", False),
    ("groomer_tracks", "Rolba · pásy a náraz", "Pohyb pásů, zastavení a tělesný náraz/otřes karoserie.", "tank tracks vehicle body impact", False),
    ("groomer_enter", "Rolba · nasednutí a sesednutí", "Krátká mechanika/kontakt s karoserií, lze sdílet jiné ruchy.", "vehicle enter exit foley", True),
    ("yaris", "Yaris · motor a pneumatiky", "Motor/průjezd a pneumatiky na štěrku. Klakson a rádio máme.", "car engine pass gravel tires", True),
    ("yaris_hit", "Yaris · fyzická vrstva sražení", "Náraz pod komickou hlášku, kterou máme.", "car hit body impact", True),
    ("joker", "Respin Joker · výhra", "Výherní automatová znělka místo generované.", "slot machine jackpot win", False),
    ("workshop_pc", "Workshop · počítač", "Větrák, jemné psaní a klikání. Hudbu už máme.", "computer fan keyboard mouse", True),
    ("tunnel", "Tunel · atmosféra", "Jemný hukot a elektrické bzučení. Ozvěna se řeší v Unity.", "tunnel room tone electrical hum", True),
    ("hanamura", "Hanamura · atmosféra", "Vítr, zahradní atmosféra a jemná voda u jezírka.", "garden wind ambience water", True),
    ("outdoor_layers", "Chaty · další ambientové vrstvy", "Vítr, stromy, ptáci/hmyz. Základní vesnický ambient už máme.", "forest wind birds ambience", True),
]
for key, title, description, query, optional in world:
    add("world_" + key, title, "Prostředí", description, key + "_loop.wav",
        search=query, optional=optional, priority=3 if optional else 2)

for key, title, text, optional in [
    ("health", "Lékárnička", "Sebrání a léčivé potvrzení.", False),
    ("big_health", "Velká lékárnička", "Výraznější léčivý efekt. Zařadit podle finálního rozmístění.", True),
    ("beer", "Pivo · rychlost", "Otevření plechovky/lahve a rychlostní efekt. Podle finálního rozmístění.", True),
    ("power", "Slivovice · síla", "Sklo/zátka a silový efekt. Podle finálního rozmístění.", True),
    ("shield", "Štít", "Energetický nástup; sdílet společný štít. Podle finálního rozmístění.", True),
    ("invulnerable", "Dědova slivovice", "Sklo/pití a nástup nesmrtelnosti.", False),
    ("tramal", "Tramal", "Obal/blistr a léčivé potvrzení.", False),
    ("carburetor", "Honzův karburátor", "Sebrání kovové součástky a potvrzení.", False),
    ("return", "Obnovení předmětu / konec buffu", "Společné jemné signály, případně poslední varovné tiky.", True),
]:
    add("pickup_" + key, title, "Pickupy", text, "pickup_" + key + ".wav", optional=optional, priority=3)

for key, title, description, optional in [
    ("capture_unlock", "Odemčení bodu", "Náhrada generovaného CaptureUnlock.", False),
    ("capture_tick", "Tik zabírání", "Krátké opakující se potvrzení průběhu zabírání.", False),
    ("capture_won", "Zabrání bodu", "Krátká pozitivní znělka.", False),
    ("capture_contested", "Sporný bod / bod soupeře", "Odlišit oba stavy od úspěšného zabrání.", True),
    ("countdown", "Odpočet a start", "Krátké tiky a finální startovní signál.", True),
    ("match_end", "Časové varování a konec", "Poslední časové varování a ukončení zápasu.", True),
    ("victory", "Výhra a prohra", "Dvě rozlišitelné krátké znělky.", True),
    ("buttons", "Tlačítka a výběr hrdiny", "Klik/potvrzení, návrat, neplatná volba a výběr hrdiny.", True),
    ("lobby", "Lobby · připojení a připravenost", "Krátká společná sada oznámení.", True),
]:
    add("ui_" + key, title, "Zápas a UI", description, "ui_" + key + ".wav", optional=optional, priority=3)

for key, title, text, optional in [
    ("pova_death", "Pova · smrt", "2–3 krátké hlasové reakce; v hero assetu chybí.", False),
    ("sindel_death", "Šindel · smrt", "2–3 krátké hlasové reakce; v hero assetu chybí.", False),
    ("honza_snare", "Honza · znehybnění", "Reakce na past nebo znehybnění, ideálně 2 varianty.", False),
    ("honza_idle", "Honza · idle", "2–3 hlášky při nečinnosti, pokud je chcete používat.", True),
    ("mosino_idle", "Mošino · idle", "2–3 hlášky při nečinnosti, pokud je chcete používat.", True),
    ("max_shift", "Max · Rozpolcení", "Hláška pro Shift, 1–2 varianty. Nejdřív poslechnout neurčenou repliku v nex_hlasky.", False),
    ("sindel_rmb", "Šindel · Say what again?!", "Hlasový výkřik pro RMB, 1–2 varianty.", False),
]:
    add("voice_" + key, title, "Hlášky", text, key + "_01.wav", optional=optional, priority=3)

existing = [
    ("gun_viktor", "Viktor · výstřel", "Audio/Weapons/gun_viktor.wav"),
    ("gun_honza", "Honza · výstřel", "Audio/Weapons/gun_honza.wav"),
    ("gun_david", "David · výstřel", "Audio/Weapons/gun_anna.wav"),
    ("gun_mosino", "Mošino · výstřel", "Audio/Weapons/gun_bard.wav"),
    ("gun_sindel", "Šindel · výstřel", "Audio/Weapons/gun_sindel.wav"),
    ("gun_sniper", "Kondulič · oba režimy střelby", "Audio/Weapons/gun_sniper_smg.wav|Audio/Weapons/gun_sniper_scoped.wav"),
    ("aura", "Mošino · hudba obou aur", "Resources/Audio/bard_heal.ogg|Resources/Audio/bard_speed.ogg"),
    ("monster", "David · hudba posílení", "Resources/Audio/monster_boost.wav"),
    ("holy_grenade", "Šindel · předvýbuchová nahrávka", "Resources/Audio/sindel_granat.wav"),
    ("slide", "Tobogan · hudba jízdy", "Resources/Audio/tobogan.wav"),
    ("potg", "Play of the Game · znělka", "Resources/Audio/potg_intro.mp3"),
    ("menu", "Menu · hudba", "Audio/Music/bros_overwatch_live_improv.ogg"),
    ("workshop", "Workshop · hudba", "Audio/Music/tunnel_vision.mp3"),
    ("radio", "Yaris · rádio", "Audio/Music/why_is_this_dealer_loop.mp3"),
    ("horn", "Yaris · klakson", "Audio/Traffic/YarisHorn.wav"),
    ("car_hit", "Yaris · komická hláška sražení", "Audio/Misc/bohuno_cut.mp3"),
    ("ambient", "Chaty · vesnický ambient", "Audio/Ambient/village_ambient_spatial.ogg"),
    ("toilet", "Toaleta · tři nahrávky", "Resources/Audio/Toilet/toilet_1.wav|Resources/Audio/Toilet/toilet_2.wav|Resources/Audio/Toilet/toilet_3.wav"),
    ("banter", "Rozhovor Max–Honza", "Resources/Banter/max_honza/1_max.wav|Resources/Banter/max_honza/2_honza.wav|Resources/Banter/max_honza/3_max.wav"),
]
for key, title, paths in existing:
    add("existing_" + key, title, "Už ve hře", "Nahrávka už existuje a je připojená. "
        "Není potřeba ji shánět znovu; přehráním můžeš posoudit kvalitu.",
        paths.split("|")[0].split("/")[-1], priority=4,
        existing=["asset:" + path for path in paths.split("|")])

if __name__ == "__main__":
    assert len(items) == len({item["id"] for item in items})
    path = Path(__file__).with_name("catalog.json")
    path.write_text(json.dumps({"version": 1, "items": items}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Catalog: {len(items)} items")
