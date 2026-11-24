"""Read-only TF2 inventory and shortlist; never moves audio or changes user choices."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import wave

APP = Path(__file__).resolve().parent
PROJECT = APP.parent.parent
AUDIO = {".wav", ".mp3", ".ogg", ".flac", ".aiff", ".m4a", ".opus"}


def rules():
    rows = []

    def add(category, tier, ids, patterns, note):
        for pattern in patterns.split():
            rows.append((category, tier, ids.split(), pattern, note))

    for material, ids, label in [
        ("wood", "steps_wood steps_solid_wood", "Dřevo"),
        ("woodpanel", "steps_wood steps_solid_wood", "Dřevěné panely"),
        ("concrete", "steps_stone", "Beton"),
        ("tile", "steps_tile", "Dlažba"),
        ("dirt", "steps_dirt", "Hlína"),
        ("grass", "steps_grass", "Tráva"),
        ("gravel", "steps_gravel", "Štěrk"),
        ("sand", "steps_sand", "Písek"),
        ("metal", "steps_metal", "Kov"),
        ("metalgrate", "steps_metal", "Kovový rošt"),
        ("wade", "steps_water", "Brodění"),
        ("slosh", "steps_water", "Šplouchavé kroky"),
    ]:
        add("Kroky a pohyb", "A", ids, f"player/footsteps/{material}[0-9].wav",
            label + ": jednotlivé kroky, zachovat varianty pro náhodné střídání.")
    add("Kroky a pohyb", "A", "move_ladder", "player/footsteps/ladder*.wav", "Kontakty při lezení po žebříku.")
    add("Kroky a pohyb", "B", "steps_water", "player/footsteps/wet*.wav player/footsteps/mud*.wav", "Mokré kroky / bláto: doplněk vody a břehu.")
    add("Kroky a pohyb", "B", "move_jump mirek_jump", "player/pl_scout_jump*.wav", "Odraz / dvojskok; ověřit, kolik klipu tvoří hlas postavy.")
    add("Kroky a pohyb", "B", "move_jump steps_stone", "player/pl_fallpain.wav weapons/mantreads.wav", "Dopad po pádu; obecná vrstva, ne sada různých materiálů.")
    add("Kroky a pohyb", "B", "move_clothes move_ledge", "player/cyoa_pda_clothes_rustle*.wav player/cyoa_pda_grab.wav player/taunt_clothes_rustle.wav", "Šustění / úchop: pohybová vrstva pro prudký pohyb nebo vytažení na hranu.")
    add("Kroky a pohyb", "B", "viktor_dash mirek_jump max_reap", "player/cyoa_pda_fly_swoosh.wav passtime/projectile_swoosh*.wav", "Krátký průlet / whoosh. Pro úskok, šíp nebo rychlý přesun porovnat délku a charakter.")
    add("Sekery, kosa a kontakty", "A", "pova_weapon max_weapon max_reap", "weapons/demo_sword_swing[0-9].wav weapons/machete_swing.wav weapons/shovel_swing.wav weapons/knife_swing.wav weapons/pickaxe_swing[0-9].wav", "Švih chladné zbraně; porovnat váhu sekery a kosy. Není to zásah cíle.")
    add("Sekery, kosa a kontakty", "A", "combat_melee_body state_body_hit", "weapons/axe_hit_flesh*.wav weapons/blade_hit[0-9].wav weapons/blade_slice_*.wav", "Kontakt ostré zbraně s hráčem; oddělit od švihu vzduchem a zásahu stěny.")
    add("Sekery, kosa a kontakty", "B", "combat_slash_stone pova_block", "weapons/demo_sword_hit_world*.wav weapons/blade_hitworld.wav weapons/cbar_hit[0-9].wav", "Kontakt s objektem: přesný materiál nelze určit jen z názvu; ověřit poslechem.")
    add("Sekery, kosa a kontakty", "B", "pova_weapon max_weapon max_slash", "weapons/samurai/tf_katana_slice_*.wav weapons/halloween_boss/knight_axe_miss.wav", "Alternativní seknutí / těžký švih, vhodnost pro kosu nebo sekeru ověřit.")
    add("Sekery, kosa a kontakty", "B", "combat_melee_body", "weapons/samurai/tf_katana_0*.wav weapons/halloween_boss/knight_axe_hit.wav", "Alternativní zásah chladnou zbraní; charakter kontaktu ověřit.")
    add("Sekery, kosa a kontakty", "B", "combat_slash_metal pova_block", "weapons/pan/pan_impact_world*.wav mvm/melee_impacts/blade_hit_robo*.wav", "Výrazný kovový kontakt; robot/pánev mohou být příliš specifické. Jen alternativa bloku či zásahu kovu.")
    add("Sekery, kosa a kontakty", "A", "move_body_wall state_death world_yaris_hit", "physics/body/body_medium_impact_hard*.wav", "Tvrdý dopad těla nebo fyzická vrstva sražení autem; bez hlášky.")
    add("Sekery, kosa a kontakty", "B", "state_death state_body_hit", "physics/body/body_medium_impact_soft*.wav", "Měkčí dopad těla, porovnat oproti tvrdé sadě.")
    add("Sekery, kosa a kontakty", "B", "kondulic_kick state_body_hit", "weapons/boxing_gloves_hit[0-9].wav player/pl_impact_airblast*.wav", "Tělesný úder nebo odhození; podklad kopnutí, ne samotný pohyb nohy.")
    add("Hák a řetěz", "A", "pova_hook kondulic_hook", "weapons/grappling_hook_*.wav", "Fáze háku: výstřel, kontakt, navíjení a konec. Ke každé fázi vybírat samostatně; není to automaticky zvuk kovového řetězu.")
    add("Mirek – luk", "A", "mirek_weapon", "weapons/bow_arrow_slide.wav weapons/bow_shoot.wav weapons/bow_shoot_pull.wav weapons/bow_shoot_pull_short.wav weapons/bow_shoot_pull_reverse.wav weapons/draw_bow.wav", "Luk: založení šípu, natažení, vypuštění nebo vytažení zbraně podle názvu.")
    add("Mirek – luk", "A", "combat_arrow_impact mirek_weapon", "weapons/fx/rics/arrow_impact_concrete*.wav weapons/fx/rics/arrow_impact_metal*.wav weapons/fx/rics/arrow_impact_wood*.wav weapons/fx/rics/arrow_impact_flesh*.wav", "Dopad šípu; materiál je uvedený v názvu. Zachovat varianty.")
    add("Mirek – luk", "A", "mirek_weapon mirek_scout", "weapons/fx/nearmiss/arrow_nearmiss*.wav", "Průlet šípu kolem posluchače; ne zvuk vypuštění z luku.")
    add("Mirek – luk", "B", "mirek_scout viktor_ult", "weapons/recon_ping.wav weapons/sentry_scan*.wav", "Ping / sken: kandidát pro průzkumný šíp či zaměřovač.")
    add("Honza – granátomet a výbušniny", "A", "honza_reload", "weapons/grenade_launcher_drum_*.wav weapons/grenade_launcher_worldreload.wav", "Skutečný granátomet: otevření, vložení munice, zavření a další fáze přebití. Sestavit podle animace.")
    add("Honza – granátomet a výbušniny", "A", "existing_gun_honza", "weapons/grenade_launcher_shoot.wav weapons/loch_n_load_shoot.wav weapons/loose_cannon_shoot.wav", "Alternativní výstřel granátometu; stávající výstřel nepřepisovat bez výběru.")
    add("Honza – granátomet a výbušniny", "A", "combat_grenade_bounce honza_death_grenades honza_mine", "weapons/grenade_impact*.wav weapons/pipe_bomb[0-9].wav", "Kontakt / odraz granátu nebo bomby. Porovnat jednotlivé varianty.")
    add("Honza – granátomet a výbušniny", "A", "honza_mine", "weapons/stickybomblauncher_det.wav weapons/stickybomblauncher_charge_up.wav weapons/grenade_throw.wav", "Detonace nálože, nabíjení nebo hod: každé je jiná fáze schopnosti.")
    add("Honza – granátomet a výbušniny", "A", "combat_explosion_small honza_mine honza_death_grenades sindel_grenade", "weapons/explode[0-9].wav weapons/rocket_blackbox_explode*.wav", "Výbuch: fyzická vrstva granátu či nálože. Pro svatý granát ponechat samostatnou předvýbuchovou nahrávku.")
    add("Honza – granátomet a výbušniny", "B", "combat_explosion_large honza_ult", "mvm/mvm_bomb_explode.wav mvm/mvm_tank_explode.wav weapons/loose_cannon_explode.wav", "Těžší exploze / koncová vrstva balvanu; nikoli samotné valení kamene.")
    add("Honza – granátomet a výbušniny", "B", "honza_trap", "weapons/breadmonster/sapper/bm_sapper_snap_*.wav misc/halloween/hwn_bomb_attach.wav", "Pouze alternativa sklapnutí/přichycení; sapper nemusí znít jako kovová past. Ověřit poslechem.")
    add("Pova – oheň", "A", "pova_rush", "weapons/flame_thrower_start.wav weapons/flame_thrower_loop.wav weapons/flame_thrower_end.wav weapons/flame_thrower_fire_hit.wav weapons/flame_thrower_fire_hitloop.wav", "Ucelené fáze plamenometu: nástup, smyčka, kontakt a doznění.")
    add("Pova – oheň", "B", "pova_rush pova_ult", "misc/flame_engulf.wav weapons/dragons_fury_shoot.wav weapons/dragons_fury_impact.wav misc/halloween/spell_fireball_cast.wav misc/halloween/spell_fireball_impact.wav misc/halloween/spell_meteor_cast.wav misc/halloween/spell_meteor_impact.wav", "Rozhoření / ohnivý projektil / meteor: stylizovaná vrstva plamene nebo ohnivého dopadu.")
    add("Střelné zbraně a přebíjení", "A", "combat_empty", "weapons/shotgun_empty.wav weapons/sniper_railgun_dry_fire.wav weapons/widow_maker_dry_fire.wav", "Prázdná zbraň: porovnat krátká mechanická cvaknutí.")
    add("Střelné zbraně a přebíjení", "B", "viktor_reload", "weapons/smg_clip_in.wav weapons/smg_clip_out.wav weapons/smg_worldreload.wav", "Fáze přebití SMG: alternativa pro Viktora podle vzhledu a časování zbraně.")
    add("Střelné zbraně a přebíjení", "A", "kondulic_reload david_reload", "weapons/sniper_bolt_back.wav weapons/sniper_bolt_forward.wav weapons/sniper_worldreload.wav", "Odstřelovací puška: závěr / přebití, porovnat pro Konduliče a Davida.")
    add("Střelné zbraně a přebíjení", "B", "sindel_reload", "weapons/pistol_clipin.wav weapons/pistol_reload_scout.wav weapons/pistol_worldreload.wav", "Přebití pistole; vybrat podle animace Devítky.")
    add("Střelné zbraně a přebíjení", "B", "existing_gun_sniper", "weapons/sniper_shoot.wav weapons/sniper_shoot_large.wav weapons/csgo_awp_shoot.wav weapons/smg_shoot.wav", "Alternativa rány odstřelovačky či rychlé SMG. Existující volbu nemění.")
    add("Střelné zbraně a přebíjení", "B", "existing_gun_sindel", "weapons/pistol_shoot.wav weapons/revolver_shoot.wav weapons/ambassador_shoot.wav", "Alternativní pistole/revolver pro porovnání; nepřepisovat vybranou R8 automaticky.")
    add("Střelné zbraně a přebíjení", "B", "combat_bullet_stone", "physics/concrete/concrete_impact_flare*.wav", "Dopad světlice do betonu; jen alternativa tvrdého zásahu, nejde o potvrzený běžný zásah kulky.")
    add("Střelné zbraně a přebíjení", "B", "combat_bullet_metal", "mvm/physics/robo_impact_bullet*.wav", "Zásah kulky do robota: kovová alternativa, charakter mimo robota posoudit.")
    add("Mošino – energie a rytmus", "B", "mosino_fire", "weapons/bison_main_shot.wav weapons/bison_main_shot_0*.wav weapons/pomson_fire_01.wav weapons/cow_mangler_main_shot.wav weapons/capper_shoot.wav", "Energetický výstřel: porovnat jako základ zvukometu, může potřebovat basovou vrstvu a úpravu délky.")
    add("Mošino – energie a rytmus", "B", "mosino_impact mosino_wave viktor_grenade", "weapons/fx/rics/bison_projectile_impact_*.wav weapons/fx/rics/pomson_projectile_impact_*.wav weapons/flame_thrower_airblast.wav weapons/push.wav weapons/push_impact.wav", "Energetický kontakt / tlakový úder. Rozlišit zásah projektilu a odhození basovou vlnou.")
    add("Mošino – energie a rytmus", "B", "mosino_reload", "weapons/bison_reload.wav weapons/cow_mangler_reload*.wav", "Stylizované přebití energetické zbraně, nikoli automaticky synchronizovaná hotová sekvence.")
    add("Mošino – energie a rytmus", "B", "mosino_crossfade mosino_amp", "weapons/vaccinator_toggle.wav weapons/buffed_on.wav weapons/buffed_off.wav", "Přepnutí režimu / zapnutí a vypnutí buffu, porovnat pro rytmus a zesílení.")
    add("Mošino – energie a rytmus", "C", "mosino_beat", "ambient/halloween/mysterious_perc_0[1-4].wav", "Experimentální perkusní akcenty; tóninu, tempo a vhodnost pro střelbu do taktu je nutné ověřit.")
    add("Stavy, léčení a schopnosti", "A", "state_shield pickup_shield", "weapons/medi_shield_deploy.wav weapons/medi_shield_retract.wav player/spy_shield_break.wav", "Nástup, konec nebo rozbití štítu podle názvu.")
    add("Stavy, léčení a schopnosti", "B", "state_shield", "player/resistance_light*.wav player/resistance_medium*.wav", "Zásah ochrany / odporu, alternativa pro zvuk zásahu štítu.")
    add("Stavy, léčení a schopnosti", "A", "viktor_heal state_heal_buff", "weapons/medigun_heal.wav weapons/medigun_heal_detach.wav weapons/dispenser_heal.wav weapons/quick_fix_heal.wav", "Léčení: smyčka nebo odpojení, možné vrstvy léčivého pole.")
    add("Stavy, léčení a schopnosti", "B", "david_impact", "weapons/fx/rics/arrow_impact_crossbow_heal.wav weapons/crusaders_crossbow_shoot.wav", "Léčivý kontakt nebo vypuštění léčivé střely; kontakt a výstřel rozlišit.")
    add("Stavy, léčení a schopnosti", "B", "david_sleep", "weapons/syringegun_shoot.wav", "Vypuštění injekční střely: kandidát pro uspávací šipku, ne samotný stav spánku.")
    add("Stavy, léčení a schopnosti", "A", "state_stun state_stun_confirm", "player/pl_impact_stun.wav player/pl_impact_stun_range.wav mvm/mvm_robo_stun.wav", "Omráčení nebo potvrzení; robotickou variantu porovnat s běžnou.")
    add("Stavy, léčení a schopnosti", "A", "state_denied", "weapons/medigun_no_target.wav weapons/wrench_hit_build_fail.wav", "Žádný cíl / neplatná akce.")
    add("Stavy, léčení a schopnosti", "A", "state_ready kondulic_scope", "player/recharged.wav weapons/medigun_charged.wav weapons/rocket_pack_boosters_ready.wav", "Nabití / připravenost schopnosti; pro optiku jen kandidát elektronického potvrzení.")
    add("Stavy, léčení a schopnosti", "B", "max_shadow max_ult", "player/spy_cloak.wav player/spy_uncloak.wav misc/halloween/spell_stealth.wav misc/halloween/spell_teleport.wav", "Nástup a konec neviditelnosti / teleport: možné vrstvy stínové formy a ultimátky.")
    add("Stavy, léčení a schopnosti", "B", "sindel_jump mirek_jump", "weapons/rocket_pack_boosters_fire.wav weapons/rocket_pack_land.wav misc/halloween/spell_blast_jump.wav", "Zesílený odraz / dopad; porovnat, aby nepůsobil jako raketový batoh.")
    add("Stavy, léčení a schopnosti", "B", "state_ult_start david_ult mosino_ult", "weapons/weapon_crit_charged_on.wav weapons/crit_power.wav items/powerup_pickup_supernova_activate.wav", "Výrazný nástup zesílení/ultimátky, samotný nevytvoří celou schopnost.")
    add("Hitmarkery a UI", "A", "state_hit", "ui/hitsound.wav ui/hitsound_beepo.wav ui/hitsound_electro*.wav ui/hitsound_percussion*.wav ui/hitsound_retro*.wav ui/hitsound_space.wav ui/hitsound_squasher.wav ui/hitsound_vortex*.wav", "Krátké hitmarkery; vhodnost a ostrost rozhodne poslech.")
    add("Hitmarkery a UI", "A", "state_headshot_kill", "ui/killsound*.wav", "Potvrzení zabití; headshot může potřebovat jinou, rozlišitelnou značku.")
    add("Hitmarkery a UI", "B", "state_headshot_kill", "player/crit_hit.wav player/crit_hit[0-9].wav weapons/upgrade_explosive_headshot.wav", "Kritický zásah / headshot: alternativa výraznějšího potvrzení.")
    add("Hitmarkery a UI", "A", "ui_buttons", "ui/buttonclick.wav ui/buttonclickrelease.wav ui/buttonrollover.wav ui/menu_focus.wav ui/panel_open.wav ui/panel_close.wav", "Tlačítka, najetí myší, výběr a otevření/zavření panelu.")
    add("Hitmarkery a UI", "A", "ui_lobby", "ui/mm_join.wav ui/trade_ready.wav ui/vote_yes.wav ui/vote_no.wav", "Připojení / připravenost nebo ano/ne; porovnat pro lobby.")
    add("Hitmarkery a UI", "B", "ui_capture_unlock ui_capture_won", "ui/chime_rd_2base_pos.wav ui/scored.wav ui/quest_turn_in_accepted_light.wav", "Pozitivní krátké potvrzení odemčení či získání bodu; nejde o hotovou herní hlášku.")
    add("Hitmarkery a UI", "B", "ui_capture_contested", "ui/chime_rd_2base_neg.wav ui/rd_2base_alarm.wav", "Negativní značka / alarm: porovnat pro sporný bod nebo soupeře.")
    add("Hitmarkery a UI", "A", "ui_countdown ui_capture_tick", "pl_hoodoo/alarm_clock_ticking_3.wav plats/grandfather_clock_tick.wav misc/rd_finale_beep01.wav", "Jednotlivé tikání / pípnutí; odpočet sestavit časováním, nejde o mluvená čísla.")
    add("Hitmarkery a UI", "B", "ui_match_end", "misc/hud_warning.wav items/cart_warning_single.wav mvm/mvm_warning.wav", "Varovný signál: konec času či důležitá událost.")
    add("Hitmarkery a UI", "C", "ui_victory", "misc/your_team_won.wav misc/your_team_lost.wav misc/your_team_stalemate.wav ui/mm_match_end_win_music_casual.wav ui/mm_match_end_lose_music_casual.wav", "Výsledková znělka: silná identita TF2, případnou řeč a délku ověřit.")
    add("Pickupy a pití", "A", "pickup_health pickup_big_health pickup_tramal", "items/regenerate.wav", "Doplnění zdraví / regenerace, porovnat pro léčivé předměty.")
    add("Pickupy a pití", "A", "pickup_beer pickup_power pickup_invulnerable", "player/pl_scout_dodge_can_open.wav player/pl_scout_dodge_can_drink.wav player/pl_scout_dodge_can_drink_fast.wav player/taunt_bottle_slosh.wav", "Otevření plechovky, pití nebo šplouchání lahve; fyzické vrstvy pickupu, nepřepisují hudbu.")
    add("Pickupy a pití", "A", "david_ult", "player/pl_scout_dodge_can_open.wav player/pl_scout_dodge_can_crush.wav", "Otevření / zmáčknutí plechovky pro Monster; fyzická vrstva posílení.")
    add("Pickupy a pití", "A", "pickup_invulnerable", "player/taunt_demo_bigburp.wav", "Demomanovo velké grknutí (burp) z TF2: uživatelem vyžádaný kandidát po vypití dědovy slivovice. Není to zvuk respawnu pickupu.")
    for name, ids in [("haste", "pickup_beer"), ("strength", "pickup_power"), ("uber", "pickup_invulnerable"), ("resistance", "pickup_shield"), ("regeneration", "pickup_health pickup_tramal")]:
        add("Pickupy a pití", "B", ids, f"items/powerup_pickup_{name}.wav", "Stylizované sebrání odpovídajícího buffu; porovnat oproti běžnému potvrzení.")
    add("Pickupy a pití", "B", "pickup_carburetor", "ui/item_metal_scrap_pickup.wav ui/item_metal_tiny_pickup.wav items/gunpickup2.wav", "Kovový pickup, kandidát karburátoru.")
    add("Pickupy a pití", "A", "pickup_return state_respawn", "items/spawn_item.wav mvm/mvm_revive.wav", "Obnovení předmětu / oživení hráče, rozlišit použití poslechem.")
    add("Pickupy a pití", "B", "pickup_return state_heal_buff", "weapons/buffed_off.wav player/invulnerable_off.wav", "Ukončení buffu, odlišit od sebrání nového předmětu.")
    add("Prostředí – voda a oheň", "A", "world_splash steps_water combat_bullet_water", "ambient/water/water_splash*.wav", "Vodní splash, velikost a vhodnost zásahu hladiny ověřit poslechem.")
    add("Prostředí – voda a oheň", "A", "world_stream world_water_ride", "ambient/pondwater.wav ambient/waterfall.wav ambient/waterfallinside.wav ambient/waterfalloutside.wav", "Rybník / proud / vodopád, porovnat intenzitu pro potok a tobogan.")
    add("Prostředí – voda a oheň", "B", "world_stream world_tunnel", "ambient/drips*.wav ambient/halloween/underground_water_lp_01.wav", "Kapání a podzemní voda: lokální vrstva tunelu či vody, ne automaticky potok.")
    add("Prostředí – voda a oheň", "A", "world_boiler_fire", "ambient/boiler_0*.wav ambient/lair/cap_2_boiler_rough1.wav ambient/lair/cap_2_boiler_smooth1.wav ambient/cp_harbor/cauldron_lp_main.wav ambient/cp_harbor/furnace_1_shot_*.wav", "Kotel / pec: smyčka nebo jednotlivé zvuky provozu, přesný charakter ověřit.")
    add("Prostředí – voda a oheň", "A", "world_fireplace world_boiler_fire", "ambient/medieval_brazier.wav", "Ohniště / hoření, kandidát krbu a grilu.")
    add("Prostředí – voda a oheň", "B", "world_boiler_fire world_boiler_warning", "ambient/steam_drum.wav ambient_mp3/lair/jungle_underground_steam*.mp3", "Pára / syčení: doplněk kotle, nikoli vlastní vrstva hoření.")
    add("Prostředí – voda a oheň", "B", "world_boiler_warning", "ambient/alarms/doomsday_lift_alarm.wav items/bomb_warning.wav", "Technický alarm: porovnat pro přehřátí kotle.")
    add("Prostředí – stroje a vozidla", "B", "world_yaris", "ambient/engine_idle.wav ambient/cp_harbor/car_1shot_*.wav ambient/mvm_warehouse/car_passby_*.wav", "Motor / jednorázový zvuk / průjezd auta; typ motoru a možnost smyčky vyžaduje poslech.")
    add("Prostředí – stroje a vozidla", "B", "world_groomer_engine world_groomer_tracks", "mvm/mvm_tank_start.wav mvm/mvm_tank_loop.wav mvm/mvm_tank_end.wav mvm/mvm_tank_smash.wav player/taunt_tank_idle.wav player/taunt_tank_forward.wav player/taunt_tank_reverse.wav player/taunt_tank_end_engine_stop.wav", "Pásové vozidlo: vrstvy motoru, pohybu a nárazu rolby; nejde o potvrzený zvuk skutečné rolby.")
    add("Prostředí – stroje a vozidla", "C", "world_yaris", "weapons/bumper_car_accelerate.wav weapons/bumper_car_decelerate.wav weapons/bumper_car_go_loop.wav weapons/bumper_car_screech.wav", "Stylizovaná elektrická autíčka: pouze alternativní pohybová vrstva Yarisu, nikoli věrný benzínový motor.")
    add("Prostředí – stroje a vozidla", "B", "world_boiler_lever", "doors/door_metal_rusty_move1.wav", "Rezavý kovový pohyb: pouze kandidát mechanické vrstvy páky; délku a charakter posoudit.")
    add("Prostředí – stroje a vozidla", "B", "world_workshop_pc", "ambient/computer_working.wav ambient/computer_working2.wav ambient/computer_tape.wav ambient/lair/cap_3_inside_computer_bank1.wav", "Počítač / elektronika: podkres workshopu nebo krátké pracovní ruchy.")
    add("Prostředí – venek a tunel", "A", "world_outdoor_layers world_hanamura", "ambient/bird[0-9].wav ambient/forest_bird*.wav ambient/outdoors_quiet_birds.wav ambient/forest_life.wav ambient/pondlife.wav ambient/wind_gust*.wav ambient/lightwindcreaks.wav", "Ptáci, příroda a vítr: lokální vrstvy chat a zahrady, porovnat se stávajícím podkresem.")
    add("Prostředí – venek a tunel", "B", "world_hanamura world_outdoor_layers", "ambient/rottenburg/smallbird_0[1-4].wav ambient/rottenburg/dove_0[1-2].wav ambient_mp3/lair/animal_call_woodpecker[1-4].mp3", "Další ptáci; zahrnout i MP3 bez WAV protějšku, nejde o duplicitní formát.")
    add("Prostředí – venek a tunel", "B", "world_tunnel", "ambient/underground.wav ambient/indoors.wav ambient/halloween/underground_wind_lp_01.wav ambient/hell/hell_rumbles_01.wav", "Tunel / vnitřní prostor: poslední varianta je stylizovaný temný rumble, porovnat intenzitu.")
    add("Prostředí – venek a tunel", "C", "world_joker", "misc/achievement_earned.wav ui/mm_xp_chime.wav", "Krátká odměna / cinknutí: jen kandidát vrstvy výhry automatu, ne hotový zvuk slotu.")
    return rows


def scan(root, output):
    root = root.resolve(strict=True)
    catalog = {r["id"]: r for r in json.loads((APP / "catalog.json").read_text(encoding="utf-8"))["items"]}
    paths = sorted(p for p in root.rglob("*") if p.is_file())
    selected = {}
    missing = []
    for category, tier, ids, pattern, note in rules():
        if set(ids) - catalog.keys():
            raise ValueError(f"Neznámé položky katalogu: {ids}")
        matches = sorted(p for p in root.glob(pattern) if p.is_file() and p.suffix.lower() in AUDIO)
        if not matches:
            missing.append(pattern)
        for path in matches:
            if not path.resolve().is_relative_to(root):
                raise ValueError(f"Odkaz mimo knihovnu: {path}")
            relative = path.relative_to(root).as_posix()
            row = selected.setdefault(relative, dict(path=relative, file=relative, item_ids=[],
                note="", section=tier, category=category, status="candidate_not_auditioned"))
            row["item_ids"] = list(dict.fromkeys(row["item_ids"] + ids))
            row["section"] = min(tier, row["section"])
            if note not in row["note"]:
                row["note"] = (row["note"] + " " + note).strip()
    if missing:
        raise ValueError("Pravidla bez shody: " + ", ".join(missing))

    inventory = []
    for path in paths:
        relative = path.relative_to(root).as_posix()
        reason = "Mimo užší výběr: specifická TF2 varianta nebo bez přesvědčivého použití v aktuálním katalogu."
        disposition = "outside_shortlist"
        if relative in selected:
            disposition, reason = "candidate", selected[relative]["note"]
        elif relative.startswith("ambient_mp3/") and (root / "ambient" / path.relative_to(root / "ambient_mp3")).with_suffix(".wav").exists():
            disposition, reason = "alternate_format", "Existuje WAV stejné relativní cesty v ambient/. Preferován WAV; shodnost obsahu nebyla poslechem ověřena."
        elif path.suffix.lower() not in AUDIO:
            reason = "MIDI: není běžný zvukový klip pro stávající přehrávač."
        elif relative.startswith(("coach/", "commentary/")):
            reason = "Instrukce trenéra / vývojářský komentář, ne herní ruch pro naši aplikaci."
        elif relative.startswith(("music/", "ui/holiday/")) or (path.name.startswith(("gamestartup", "cyoa_music"))):
            reason = "Hotová hudba TF2; stávající hudební volby již máme, pro ruchy zatím vynecháno."
        elif relative.startswith("replay/"):
            reason = "Editor replayů: pro aktuální herní katalog nemá potřebnou roli."
        inventory.append(dict(path=relative, bytes=path.stat().st_size, disposition=disposition, reason=reason))

    records = sorted(selected.values(), key=lambda r: (r["category"], r["section"], r["path"]))
    for row in records:
        path = root / row["path"]
        row["bytes"] = path.stat().st_size
        with path.open("rb") as stream:
            row["sha256"] = hashlib.file_digest(stream, "sha256").hexdigest()
        row["seconds"] = None
        if path.suffix.lower() == ".wav":
            try:
                with wave.open(str(path), "rb") as clip:
                    row.update(seconds=round(clip.getnframes() / clip.getframerate(), 3),
                               channels=clip.getnchannels(), rate=clip.getframerate())
            except (wave.Error, EOFError):
                pass
        row["targets"] = [" / ".join(filter(None, [catalog[key]["section"], catalog[key]["title"]])) for key in row["item_ids"]]

    folders = []
    for folder in [root] + sorted(p for p in root.rglob("*") if p.is_dir()):
        relative = folder.relative_to(root).as_posix()
        direct = [r for r in inventory if Path(r["path"]).parent.as_posix() == relative]
        folders.append(dict(path=relative, files=len(direct), candidates=sum(r["disposition"] == "candidate" for r in direct)))
    mapped = {key for row in records for key in row["item_ids"]}
    summary = dict(total_files=len(paths), audio_files=sum(p.suffix.lower() in AUDIO for p in paths),
        subfolders=len(folders)-1, total_mib=round(sum(r["bytes"] for r in inventory)/1024**2, 2),
        candidates=len(records), candidate_mib=round(sum(r["bytes"] for r in records)/1024**2, 2),
        tiers=dict(sorted(Counter(r["section"] for r in records).items())),
        categories=dict(sorted(Counter(r["category"] for r in records).items())), mapped_items=len(mapped))
    manifest = dict(version=1, source_root=str(root), candidate_status="not_auditioned",
        source=dict(id="tf2", label="TF2", url="", author="TF2 – uživatelem dodaná knihovna", license="Nedoložena"),
        planned_destination=str(PROJECT / "_MIMO_GIT_K_PRESUNU" / "TF2_kandidati"),
        imported=False, summary=summary, files=records)
    output.mkdir(parents=True, exist_ok=True)
    (output / "TF2_CANDIDATES.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    (output / "TF2_INVENTORY.json").write_text(json.dumps(dict(source_root=str(root), summary=summary,
        folders=folders, files=inventory), ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    lines = ["TF2 – kandidáti pro Bros Overwatch", "="*60, "", "Zdroj: " + str(root),
        "Prohlédnuty rekurzivně všechny složky, názvy souborů a metadata kandidátů.",
        "Výběr podle názvů a rolí v catalog.json. Zvuky NEBYLY poslechnuty.",
        "A = přímý kandidát; B = alternativa / vrstva; C = experimentální doplněk.",
        "Samotný skener audio nekopíruje a nemění přiřazení uživatele.",
        "Umístění místní knihovny: " + manifest["planned_destination"],
        "Import / obnova zvuků: python tools/sound-collector/import_tf2.py (ověřuje SHA-256).",
        "Server a export podporují tuto knihovnu přes stabilní odkazy tf2:… .",
        "Poslech a přiřazení: http://127.0.0.1:8765/?view=candidates&source=tf2",
        "Licence zdrojů: nedoložena; původ evidován jako uživatelem dodaná knihovna TF2.", "", "SOUHRN", json.dumps(summary, ensure_ascii=False, indent=2),
        "", "POKRYTÍ SLOŽEK (počty souborů přímo ve složce, podsložky jsou samostatné)"]
    for folder in folders:
        lines.append(f"{folder['path']}: {folder['files']} souborů, {folder['candidates']} kandidátů")
    lines.extend(["", "Přehled všech souborů včetně důvodů vyřazení: TF2_INVENTORY.json", "", "KANDIDÁTI"])
    current = None
    for row in records:
        if row["category"] != current:
            current = row["category"]
            lines.extend(["", current.upper(), "-"*60])
        lines.extend([f"[{row['section']}] {row['path']} ({row['seconds'] if row['seconds'] is not None else '?'} s)",
                      "  Návrh použití: " + "; ".join(row["targets"]), "  " + row["note"]])
    lines.extend(["", "POLOŽKY BEZ PŘESVĚDČIVÉHO KANDIDÁTA Z TÉTO KNIHOVNY"])
    for key, item in catalog.items():
        if key not in mapped and not key.startswith(("existing_", "voice_")):
            lines.append(key + " – " + item["title"])
    (output / "TF2_CANDIDATES.txt").write_text("\n".join(lines)+"\n", encoding="utf-8-sig")
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path(r"C:\myDev_\audio\sounds\tf2\sound"))
    parser.add_argument("--output", type=Path, default=APP / "research")
    args = parser.parse_args()
    scan(args.source, args.output)
