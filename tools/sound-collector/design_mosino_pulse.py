"""Audition user-requested sonic pulses, including shortened Photo Booth beam mixes."""
import array
import hashlib
import json
import math
from pathlib import Path
import shutil
import subprocess
import wave

APP = Path(__file__).resolve().parent
INBOX = APP.parent.parent / 'OverwatchBros/Sound/sounds_dir'
SOURCE = INBOX / 'CS2_kandidati/weapons/c4'
OUT = INBOX / 'Mosino_navrhy'
RATE = 44100


def layer(name, factor, highpass=100):
    result = subprocess.run([shutil.which('ffmpeg'), '-v', 'error', '-i', str(SOURCE / name),
        '-af', f'aresample={RATE},asetrate={round(RATE * factor)},aresample={RATE},highpass=f={highpass},lowpass=f=6500',
        '-f', 'f32le', '-ac', '1', '-'], capture_output=True, check=True)
    data = array.array('f')
    data.frombytes(result.stdout)
    peak = max(abs(v) for v in data)
    if peak < 0.00001: raise ValueError('Silent source')
    first = next(i for i, v in enumerate(data) if abs(v) >= peak * 0.008)
    data = data[max(0, first - RATE // 1000):]
    return array.array('f', (v / peak for v in data))


def main():
    OUT.mkdir(exist_ok=True)
    sources = [{"file": 'CS2_kandidati/weapons/c4/' + name,
                "sha256": hashlib.sha256((SOURCE / name).read_bytes()).hexdigest()}
               for name in ('c4_thump_01.mp3', 'c4_shockwave_01.mp3')]
    beam_path = INBOX / 'CS2_kandidati/ui/chickens/photobooth_fx_beam_01.wav'
    beam_source = {"file": beam_path.relative_to(INBOX).as_posix(),
                   "sha256": hashlib.sha256(beam_path.read_bytes()).hexdigest()}
    records = []
    presets = [
        (1, 'Hutnější puls', 2.5, 6.0, 0.13, 0.46),
        (2, 'Krátký energetický puls', 4.0, 9.0, 0.10, 0.34),
        (3, 'Lehký rychlý puls', 5.0, 12.0, 0.07, 0.25),
        (4, 'Beam puls – kratší a méně dutý', 8.0, 16.0, 0.04, 0.30),
        (5, 'Beam puls – extra vysoký a rychlý', 12.0, 20.0, 0.025, 0.23),
    ]
    for number, title, thump_pitch, wave_pitch, wave_gain, seconds in presets:
        with_beam = number >= 4
        thump_gain = 0.18 if with_beam else 0.68
        beam_pitch = 1.0 if number == 4 else 1.4
        beam = layer(beam_path, beam_pitch, highpass=180) if with_beam else array.array('f')
        thump = layer('c4_thump_01.mp3', thump_pitch, highpass=600 if with_beam else 100)
        shock = layer('c4_shockwave_01.mp3', wave_pitch, highpass=350 if with_beam else 100)
        count = round(RATE * seconds)
        mix = []
        delay = round(RATE * 0.006)
        for i in range(count):
            t = i / max(1, count - 1)
            attack = min(1, i / (RATE * 0.002))
            tail = (1 - t) ** (1.1 if with_beam else 1.5)
            a = thump[i] if i < len(thump) else 0
            b = shock[i - delay] if 0 <= i - delay < len(shock) else 0
            c = beam[i] if i < len(beam) else 0
            mix.append((a * thump_gain + b * wave_gain + c * 0.72) * attack * tail)
        peak = max(abs(v) for v in mix)
        gain = 0.65 / max(peak, 0.00001) if with_beam else min(1, 0.8 / max(peak, 0.00001))
        pcm = array.array('h', (round(v * gain * 32767) for v in mix))
        name = f'mosino_pulse_{number:02}.wav'
        with wave.open(str(OUT / name), 'wb') as output:
            output.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
            output.writeframes(pcm.tobytes())
        records.append({"file": name, "path": 'c4_thump_01.mp3 + c4_shockwave_01.mp3' + (' + photobooth_fx_beam_01.wav' if with_beam else ''),
            "item_ids": ['mosino_fire'], "section": 'B', "seconds": seconds,
            "note": f'{title}. Thump pitch {thump_pitch:g}×, shockwave pitch {wave_pitch:g}×; '
                    f'shockwave {20 * math.log10(wave_gain / thump_gain):.1f} dB pod úderem. '
                    + (f'Hlavní vrstva je krátký Photo Booth FX Beam (pitch {beam_pitch:g}×); dutý spodek úderu je odfiltrovaný. ' if with_beam else '')
                    + 'Krátká obálka a potlačené ostré výšky. Smíchaný návrh k poslechu.',
            "sources": sources + ([beam_source] if with_beam else []), "status": 'candidate_not_auditioned',
            "sha256": hashlib.sha256((OUT / name).read_bytes()).hexdigest(),
            "mix": {"thump_pitch_factor": thump_pitch, "shockwave_pitch_factor": wave_pitch,
                    "shockwave_gain": wave_gain, "thump_gain": thump_gain, "duration": seconds,
                    "beam_pitch_factor": beam_pitch if with_beam else None, "beam_gain": 0.72 if with_beam else 0,
                    "peak": peak * gain}})
    manifest = {"version": 1, "source": {"id": 'mosino_design', "label": 'Mošino – pulsy',
        "url": '', "author": 'Úpravy nahrávek z uživatelem dodané knihovny CS2',
        "license": 'Zdroj CS2; licence pro použití mimo zdrojovou hru nebyla dodaná ani ověřená.'},
        "files": records}
    (OUT / 'KANDIDATI.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (APP / 'research/MOSINO_PULSE_DESIGN.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({"files": len(records), "folder": str(OUT), "peaks": [round(r['mix']['peak'], 3) for r in records]}))


if __name__ == '__main__': main()
