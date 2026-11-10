"""Copy a bounded audition selection from the user's supplied CS2 sound folder.

Source recordings and collection.json are never changed. Running again checks
existing copies instead of overwriting them. Source paths are kept in the manifest.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import wave

APP = Path(__file__).resolve().parent
PROJECT = APP.parent.parent
AUDIO = {".wav", ".mp3", ".ogg", ".flac", ".m4a"}


def checksum(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(block)
    return result.hexdigest()


def natural(path):
    return [int(piece) if piece.isdigit() else piece.lower() for piece in re.split(r"(\d+)", path.as_posix())]


def select(root, catalog):
    selected = {}
    unmatched = []

    def family(pattern, ids, note, limit=4, tier="A", category="Zásahy a boj"):
        targets = ids.split()
        assert all(key in catalog for key in targets), targets
        matches = sorted((p for p in root.glob(pattern) if p.is_file() and p.suffix.lower() in AUDIO), key=natural)[:limit]
        if not matches:
            unmatched.append(pattern)
        for path in matches:
            resolved = path.resolve()
            if not resolved.is_relative_to(root):
                raise ValueError("Zdrojový soubor je mimo určenou složku.")
            relative = path.relative_to(root).as_posix()
            record = selected.setdefault(relative, {"path": relative, "file": relative, "item_ids": [],
                        "note": note, "section": tier, "category": category, "status": "candidate_not_auditioned"})
            record["item_ids"] = list(dict.fromkeys(record["item_ids"] + targets))
            if note not in record["note"]:
                record["note"] += " " + note

    surfaces = [
        ("wood", "steps_wood steps_solid_wood", "Dřevo a prkna", None),
        ("concrete_ct", "steps_stone", "Beton / tvrdý kámen", "concrete"),
        ("grass", "steps_grass", "Tráva", "grass"),
        ("gravel", "steps_gravel", "Štěrk", "gravel"),
        ("dirt", "steps_dirt", "Hlína", "dirt"),
        ("sand", "steps_sand", "Písek", "sand"),
        ("carpet", "steps_carpet", "Koberec", "carpet"),
        ("rubber", "steps_rubber", "Guma", "rubber"),
        ("tile", "steps_tile", "Dlažba", "tile"),
        ("metal_solid", "steps_metal", "Pevný kov", "metal_solid"),
    ]
    for prefix, ids, material, landing in surfaces:
        family(f"player/footsteps/{prefix}_[0-9]*.wav", ids, f"{material}: jednotlivé kroky. Porovnat varianty a hlasitost.",
               limit=8, category="Kroky a dopady")
        if landing:
            family(f"player/footsteps/land_{landing}_[0-9]*.wav", ids, f"{material}: dopad po skoku, doplněk ke krokům.",
                   limit=3, category="Kroky a dopady")
    family("player/land*.mp3", "steps_wood steps_solid_wood move_jump", "Obecný dopad hráče; podklad pro dřevo, přesný materiál ověřit poslechem.",
           limit=2, tier="B", category="Kroky a dopady")
    family("player/footsteps/water_wade_*.wav", "steps_water", "Brodění vodou, jednotlivé pohybové kontakty.", limit=6, category="Kroky a dopady")
    family("player/footsteps/puddle_[0-9]*.wav", "steps_water", "Došlap do mělké louže; může doplnit brodění.", category="Kroky a dopady")
    for action in ("entry", "exit"):
        family(f"player/footsteps/water_{action}_*.wav", "steps_water world_splash", "Vstup do vody / výstup z vody; nepovažovat automaticky za velký splash.",
               limit=3, category="Kroky a dopady")
    family("player/footsteps/ladder_wood_*.wav", "move_ladder", "Dřevěný žebřík: kontakty při lezení.", limit=6, category="Pohyb")
    family("player/footsteps/ladder_[0-9]*.wav", "move_ladder", "Alternativa kontaktu příček žebříku; porovnat materiál.", category="Pohyb")
    family("player/footsteps/jump_launch_*.wav", "move_jump", "Odraz do skoku.", category="Pohyb")
    family("player/footsteps/suit_ct_*.wav", "move_clothes move_ledge", "Pohyb výstroje: vrstva oblečení/šplhání, nikoli celá akce.", tier="B", category="Pohyb")

    for pattern, ids, note in [
        ("physics/wood/wood_solid_impact_bullet*.wav", "combat_bullet_wood", "Zásah kulky do pevného dřeva."),
        ("physics/concrete/concrete_impact_bullet*.wav", "combat_bullet_stone", "Zásah kulky do betonu."),
        ("physics/metal/metal_solid_impact_bullet*.wav", "combat_bullet_metal", "Zásah kulky do pevného kovu."),
        ("physics/glass/glass_impact_bullet*.wav", "combat_bullet_glass", "Zásah skla; bez zavádění rozbíjení oken."),
        ("physics/surfaces/dirt_impact_bullet*.wav", "combat_bullet_ground", "Kulka do hlíny."),
        ("physics/surfaces/sand_impact_bullet[0-9]*.wav", "combat_bullet_ground", "Kulka do písku; další materiálová varianta."),
        ("physics/surfaces/grass_impact_bullet*.wav", "combat_bullet_leaves", "Kulka do trávy."),
        ("physics/wood/foliage_impact_bullet*.wav", "combat_bullet_leaves", "Kulka do listí / vegetace."),
        ("physics/surfaces/carpet_impact_bullet*.wav", "combat_bullet_soft", "Kulka do koberce, měkký kontakt."),
        ("physics/rubber/rubber_tire_impact_bullet*.wav", "combat_bullet_soft", "Kulka do pneumatiky, alternativa gumového kontaktu."),
        ("physics/surfaces/underwater_impact_bullet*.wav", "combat_bullet_water", "Zásah pod vodou; pro zásah hladiny posoudit a případně přidat splash."),
    ]:
        family(pattern, ids, note)
    family("physics/wood/wood_plank_impact_hard*.wav", "combat_slash_wood", "Tvrdý kontakt prkna. Pro sekeru/kosu kombinovat se švihem.", limit=3, tier="B")
    family("physics/metal/metal_box_impact_hard*.wav", "combat_slash_metal pova_block", "Tvrdý kontakt kovové bedny; základ zásahu či bloku, charakter dutého kovu posoudit.", limit=3, tier="B")
    family("physics/metal/chain_impact_hard*.wav", "pova_hook honza_trap", "Úder řetězu; detail přitažení nebo pasti, ne celý pohyb řetězu.", limit=2, tier="B")
    family("physics/concrete/rock_impact_hard*.wav", "combat_slash_stone honza_ult", "Náraz kamene: balvan nebo vrstva tvrdého kontaktu. Není to valení.", tier="B")
    family("physics/concrete/boulder_impact_hard*.wav", "honza_ult", "Těžký náraz balvanu, doplnit valení a fáze ultimátky.", tier="B")
    family("physics/body/body_impact_ct_[0-9]*.wav", "move_body_wall state_body_hit state_death", "Tělesný náraz hráče; posoudit pro stěnu nebo dopad těla.")
    family("physics/body/body_impact_fists_*.wav", "kondulic_kick state_body_hit", "Úder do těla; kandidát pro odkopnutí.", limit=3)
    family("weapons/knife/knife_hit_light_flesh_*.mp3", "combat_melee_body state_body_hit", "Společný lehký sečný zásah do hráče. Ne do zdi a ne při švihu vzduchem.", limit=6)
    family("weapons/knife/heavy_hit_flesh_*.mp3", "combat_melee_body state_body_hit", "Společný těžší sečný zásah do hráče. Ne do zdi a ne při švihu vzduchem.", limit=6)
    family("weapons/knife/knife_slash[0-9]*.wav", "pova_weapon max_weapon max_reap", "Švih / seknutí nožem; kandidát pro pohyb sekery a kosy. Posoudit charakter a případně přidat těžší vrstvu.", limit=2, tier="B")
    family("weapons/knife/knife_hit[0-9]*.wav", "pova_weapon max_weapon", "Zásah nožem. Zvuk kontaktu; materiál a vhodnost pro sekeru/kosu ověřit poslechem.", limit=2, tier="B")
    family("weapons/knife/knife_stab.wav", "pova_weapon max_weapon max_reap", "Bodnutí nožem; alternativa či vrstva silného kontaktu, nikoli automaticky zvuk švihu.", limit=1, tier="B")
    for pattern, note, limit in [
        ("knife_hit_[0-9]*.mp3", "Zásah nožem; konkrétní materiál ověřit poslechem.", 4),
        ("knife_hit_light_[0-9]*.mp3", "Lehký zásah nožem; kandidát pro kontakt sekery/kosy, materiál ověřit poslechem.", 4),
        ("knife_hit_heavy_[0-9]*.mp3", "Těžký zásah nožem; kandidát pro silnější kontakt sekery/kosy, materiál ověřit poslechem.", 4),
        ("knife_hit_light_thud_[0-9]*.mp3", "Tupá složka kontaktu nože; možná vrstva úderu sekery/kosy. Není to čistý švih.", 3),
    ]:
        family("weapons/knife/" + pattern, "pova_weapon max_weapon", note, limit=limit, tier="B")
    family("weapons/knife/knife_swish_light_*.mp3", "pova_weapon max_weapon", "Lehký švih nože; pro větší sekeru/kosu může vyžadovat zesílení nebo jinou vrstvu.", tier="B")
    family("weapons/knife/knife_swish_heavy_*.mp3", "pova_weapon max_weapon max_reap", "Těžší švih nože; kandidát pro velkou zbraň po poslechu a případné úpravě.", limit=3, tier="B")
    family("weapons/knife/knife_stab.wav", "combat_melee_body", "Delší bodnutí / zásah hráče; alternativa pro společnou sadu po poslechu.", limit=1)
    family("weapons/m4a1/m4a1_[0-9]*.wav", "existing_gun_viktor", "Alternativa dosavadní generované rány Viktora.", limit=4)
    family("weapons/awp/awp_[0-9]*.wav", "existing_gun_sniper", "Kondulič: výstřel AWP při střelbě přes optiku.", limit=4)
    family("weapons/m4a1/m4a1_silencer_01.wav", "existing_gun_david", "David: tichý tlumený výstřel M4A1-S.", limit=1)
    family("weapons/revolver/revolver-1_01.wav", "existing_gun_sindel", "Šindel: výstřel revolveru R8.", limit=1)
    family("weapons/mp5/mp5_01.wav", "existing_gun_sniper", "Kondulič: MP5 při rychlé střelbě bez optiky.", limit=1)
    family("weapons/nova/nova_insertshell_[0-9]*.mp3", "honza_reload", "Vložení jednoho náboje; Honza přehraje pětkrát za sebou během přebíjení.", limit=3)
    for pattern, note, tier in [
        ("weapons/c4/c4_thump_01.mp3", "Kandidát podle názvu: tlakový/basový úder jako základ pulsu. Poslechem ověřit charakter; případně zkombinovat s krátkou elektrickou vrstvou.", "B"),
        ("weapons/c4/c4_shockwave_01.mp3", "Kandidát podle názvu: tlaková vlna. Pro běžný výstřel může potřebovat krátký výřez a potlačení explozivního charakteru.", "B"),
        ("ui/chickens/photobooth_fx_beam_01.wav", "Kandidát podle názvu: efekt beam. Porovnat jako energetickou složku zvukometu; výšku a ostrost ověřit poslechem.", "B"),
        ("ui/chickens/photobooth_fx_laser_01.wav", "Kandidát podle názvu: laserový efekt. Pro porovnání, pokud nebude příliš vysoký či komický.", "B"),
        ("weapons/taser/taser_shoot.wav", "Výstřel taseru: kandidát elektrického pulsu. Posoudit ostrost; pro Mošina může potřebovat ztišení vysokých frekvencí.", "B"),
        ("weapons/taser/taser_hit.wav", "Elektrický kontakt taseru: možná krátká elektrická vrstva, ne automaticky hotový výstřel.", "B"),
        ("weapons/fx/nearmiss/bulletby_subsonic_01.wav", "Průlet podzvukové střely: možná vzdušná složka zvukové vlny. Samotný nebude nutně tvořit plný výstřel.", "B"),
        ("player/footsteps/bass_01.wav", "Basová vrstva z knihovny kroků: zkusit jen jako spodní úder, nikoli jako hotovou zbraň.", "B"),
        ("ambient/common/energy/zap1.mp3", "Elektrický zap: doplňková vrstva k posouzení. Může být ostrý nebo vyžadovat výřez, proto až jako další možnost.", "C"),
    ]:
        family(pattern, "mosino_fire", note, limit=1, tier=tier, category="Mošino – energetický výstřel")
    family("physics/water/water_splash_*.wav", "world_splash steps_water combat_bullet_water", "Přirozený vodní splash; vybrat velikost podle poslechu.", category="Voda a prostředí")

    for path, ids, note in [
        ("weapons/hegrenade/hegrenade_detonate_*.wav", "combat_explosion_small honza_mine honza_death_grenades sindel_grenade", "Výbuch granátu; pro svatý granát jen fyzická vrstva."),
        ("weapons/hegrenade/he_bounce*.wav", "combat_grenade_bounce honza_death_grenades", "Odraz granátu."),
        ("physics/weapons/weapon_hegrenade_impact_*.wav", "combat_grenade_bounce honza_mine", "Kontakt granátu s povrchem; další varianty odrazu."),
        ("weapons/hegrenade/grenade_throw.wav", "honza_mine sindel_grenade david_grenade", "Hod granátu; sdílená pohybová vrstva."),
        ("weapons/hegrenade/pinpull*.wav", "honza_mine sindel_grenade", "Vytažení pojistky. Použít jen pokud sedí k vizuálu schopnosti."),
        ("weapons/flashbang/flashbang_explode[0-9].wav", "viktor_grenade state_stun", "Výbuch flashbangu, kandidát pro pulzní granát/omráčení."),
        ("weapons/c4/c4_explode1.wav", "combat_explosion_large honza_ult", "Velká exploze; pro balvan jen koncový výbuch."),
        ("weapons/taser/taser_hit.wav", "viktor_grenade state_stun", "Elektrický zásah; jedna vrstva pulzu nebo omráčení."),
        ("weapons/taser/taser_charge_ready.wav", "state_ready kondulic_scope", "Elektronické potvrzení připravenosti / nabití."),
        ("weapons/taser/taser_charging.wav", "kondulic_scope viktor_ult", "Nabíjení elektroniky; posoudit pro zaměřování."),
        ("weapons/clipempty_*.wav", "combat_empty state_denied", "Cvaknutí prázdné zbraně."),
    ]:
        family(path, ids, note, limit=3, category="Výbušniny a elektronika")

    def reload_set(folder, patterns, ids, note, tier="A"):
        for pattern in patterns:
            family(f"weapons/{folder}/{pattern}", ids, note, limit=1, tier=tier, category="Přebíjení a optika")

    reload_set("m4a1", ["m4a1_clipout.mp3", "m4a1_clipin.mp3", "m4a1_boltback.mp3", "m4a1_boltforward.mp3"],
               "viktor_reload", "M4A1: část přebíjení. Z klipů je potřeba sestavit celek podle délky animace.")
    reload_set("awp", ["awp_clipout.mp3", "awp_clipin.mp3", "awp_cliphit.mp3", "awp_boltback.wav", "awp_boltforward.wav"],
               "kondulic_reload david_reload", "AWP: část přebíjení. Pro Davidovu pušku posoudit charakter; nejde o hotový synchronizovaný celek.")
    reload_set("p90", ["p90_cliprelease.wav", "p90_clipout.wav", "p90_clipin.wav", "p90_boltback.wav", "p90_boltforward.wav"],
               "mosino_reload", "P90: mechanická vrstva přebíjení zvukometu; doplnit stylizaci a časování.", tier="B")
    reload_set("glock18", ["glock_clipout.wav", "glock_clipin.wav", "glock_slideback.wav", "glock_sliderelease.wav"],
               "sindel_reload", "Glock: jednotlivá fáze přebití pistole, sestavit podle animace.")
    reload_set("deagle", ["de_clipout.wav", "de_clipin.mp3", "de_slideback.wav", "de_slideforward.wav"],
               "sindel_reload", "Šindel: přebití z Deaglu, vyjmutí/vložení zásobníku a pohyb závěru. Výstřel zůstává R8.")
    reload_set("mag7", ["mag7_insertshell.wav", "mag7_pump_back.mp3", "mag7_pump_forward.wav"],
               "honza_reload", "Brokovnice: nouzový mechanický podklad pro granátomet. Není to skutečné přebití granátometu.", tier="B")
    family("weapons/awp/zoom.wav", "kondulic_scope david_reload", "Krátká optika pušky. Pro Davida samostatný detail k přebití/optice.", category="Přebíjení a optika")
    family("weapons/awp/awp_zoom_out.wav", "kondulic_scope", "Odchod z optiky.", category="Přebíjení a optika")

    for pattern, ids, note in [
        ("player/damage[0-9].wav", "state_hurt", "Obecná mužská reakce na zásah; nenahrazuje osobní českou hlášku."),
        ("player/death[0-9].wav", "state_death", "Obecná reakce smrti, pouze kandidát společného ruchu."),
        ("items/healthshot_success_01.wav", "pickup_health pickup_big_health state_heal_buff", "Léčivé potvrzení / sebrání zdraví."),
        ("items/healthshot_prepare_01.wav", "pickup_tramal viktor_heal", "Příprava léčivého předmětu, mechanický detail."),
        ("items/nvg_on.wav", "kondulic_ult viktor_ult", "Zapnutí elektronického vidění."),
        ("items/pickup_ammo_*.wav", "pickup_carburetor", "Sebrání munice, kandidát pro kovovou součástku po poslechu."),
        ("items/rush_terminal_no_01.mp3", "state_denied ui_buttons", "Zamítnutí v UI."),
        ("items/rush_terminal_yes_01.mp3", "ui_buttons ui_capture_unlock", "Pozitivní potvrzení / odemčení."),
        ("items/rush_terminal_timer_beep.mp3", "ui_capture_tick ui_countdown", "Tik časovače nebo zabírání."),
        ("items/rush_captured_team_01.mp3", "ui_capture_won", "Získání cíle vlastním týmem."),
        ("items/rush_captured_enemy_01.mp3", "ui_capture_contested", "Získání cíle soupeřem, odlišná znělka."),
        ("physics/metal/soda_can_impact_hard*.wav", "david_ult", "Kontakt plechovky Monsteru; pouze fyzická vrstva posílení."),
        ("physics/glass/glass_bottle_impact_hard*.wav", "pickup_beer pickup_power pickup_invulnerable", "Kontakt lahve; ne zvuk otevření nebo pití."),
    ]:
        family(pattern, ids, note, limit=3, category="Stavy, pickupy a UI")

    for pattern, ids, note in [
        ("ambient/common/fire/fire_med_loop1.mp3", "world_boiler_fire world_fireplace pova_rush", "Smyčka ohně; posoudit pro kotel, krb nebo plamen."),
        ("ambient/common/gas/steam_loop1.mp3", "world_boiler_fire world_boiler_warning", "Smyčka páry; syčení přehřátého kotle, ne vlastní hoření."),
        ("ambient/inferno/water_stream_01.wav", "world_stream", "Potok / tekoucí voda."),
        ("ambient/common/water/water_streamloop3.mp3", "world_stream world_water_ride", "Smyčka proudu vody; intenzitu ověřit poslechem."),
        ("ambient/anubis/anubis_riverlap.wav", "world_stream", "Voda u břehu; pro jezírko, ne automaticky tekoucí potok."),
        ("ambient/common/vehicles/car_idle_01.mp3", "world_yaris", "Motor auta na volnoběh; konkrétní charakter Yarisu neposouzen."),
        ("ambient/common/vehicles/csgo_dust_car_driveby_01.mp3", "world_yaris", "Průjezd auta, alternativa pro Yaris."),
        ("ambient/common/backgrounds/engine_room.mp3", "world_groomer_engine", "Motorový prostor, jen možná hlubší vrstva rolby, nikoli celá sada motoru."),
        ("ambient/anubis/wind_low_amb.wav", "world_hanamura world_outdoor_layers", "Jemný vítr jako podkres."),
        ("ambient/survival/outside_forest_low_01.wav", "world_outdoor_layers", "Lesní ambient; porovnat s hotovým vesnickým podkresem."),
        ("ambient/cache/cache_outside_birds_lp_02.mp3", "world_hanamura world_outdoor_layers", "Ptáci venku; doplněk zahrady."),
    ]:
        family(pattern, ids, note, limit=1, tier="C", category="Prostředí")
    return list(selected.values()), unmatched


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=Path(r"C:\myDev_\audio\sounds\cs2\sounds"))
    parser.add_argument("--copy", action="store_true")
    args = parser.parse_args()
    root = args.source.resolve()
    catalog = {row["id"]: row for row in json.loads((APP / "catalog.json").read_text(encoding="utf-8"))["items"]}
    records, unmatched = select(root, catalog)
    destination = PROJECT / "OverwatchBros/Sound/sounds_dir/CS2_kandidati"
    assert destination.resolve().is_relative_to(PROJECT / "OverwatchBros/Sound/sounds_dir")
    total = sum((root / record["path"]).stat().st_size for record in records)
    report = {"candidates": len(records), "size_mib": round(total / 1024**2, 1),
              "mapped_items": len({key for record in records for key in record["item_ids"]}),
              "unmatched_patterns": unmatched, "destination": str(destination)}
    print(json.dumps(report, ensure_ascii=False), flush=True)
    if not args.copy:
        return
    destination.mkdir(parents=True, exist_ok=True)
    if shutil.disk_usage(destination).free < total + 16 * 1024**2:
        raise RuntimeError("Nedostatek místa na disku.")
    summary = ["CS2 – kandidáti k poslechu", "", "Výběr podle názvů, bez poslechového hodnocení.",
               "Původ: uživatelem dodaná složka " + str(root),
               "Licence pro použití mimo zdrojovou hru nebyla dodaná ani ověřená.",
               "Přiřazení a fajfky dělej ve stávající Zvukové sbírce. Tato příprava evidenci výběru nemění.", ""]
    for record in records:
        source = (root / record["path"]).resolve()
        target = (destination / record["file"]).resolve()
        if not source.is_relative_to(root) or not target.is_relative_to(destination.resolve()):
            raise ValueError("Neplatná cesta kandidáta.")
        digest = checksum(source)
        if target.exists():
            if checksum(target) != digest:
                raise RuntimeError("Existující kopie se liší; nebude přepsána: " + str(target))
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            with source.open("rb") as src, target.open("xb") as dst:
                shutil.copyfileobj(src, dst, 1024 * 1024)
            shutil.copystat(source, target)
        if checksum(target) != digest:
            raise RuntimeError("Kopie neprošla ověřením: " + str(target))
        record["sha256"] = digest
        record["bytes"] = source.stat().st_size
        record["seconds"] = None
        if source.suffix.lower() == ".wav":
            try:
                with wave.open(str(source), "rb") as sound:
                    record.update(seconds=round(sound.getnframes() / sound.getframerate(), 3),
                                  channels=sound.getnchannels(), rate=sound.getframerate())
            except (wave.Error, EOFError):
                pass
        record["targets"] = [(catalog[key]["section"] + " / " if catalog[key]["section"] else "") + catalog[key]["title"] for key in record["item_ids"]]
        summary.extend([record["file"], "  Použití: " + "; ".join(record["targets"]), "  " + record["note"], ""])
    manifest = {"version": 1, "source_root": str(root), "candidate_status": "not_auditioned",
                "source": {"id": "cs2", "label": "CS2", "url": "", "author": "CS2 – uživatelem dodaná knihovna",
                           "license": "Licence pro použití mimo zdrojovou hru nebyla dodaná ani ověřená."},
                "files": records}
    temporary = destination / "KANDIDATI.tmp"
    temporary.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(destination / "KANDIDATI.json")
    (destination / "SOUPIS.txt").write_text("\n".join(summary), encoding="utf-8-sig")
    (APP / "research/CS2_CANDIDATES.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Zkopírováno a SHA-256 ověřeno: {len(records)} souborů. Evidence uživatele se nezměnila.")


if __name__ == "__main__":
    main()
