"""Build audition variants for a rolling boulder and a spring-loaded metal trap.

Derived from the user's local recordings; no changes to saved choices or Unity.
"""
import argparse
import array
import hashlib
import json
import math
from pathlib import Path
import random
import shutil
import subprocess
import sys
import wave

from audio_paths import APP, LOCAL_LIBRARIES, inside

RATE = 44100
SOURCE = Path(r'C:\myDev_\audio\sounds')
MECH = 'sounds/GameAudioBundle2026/Epic Stock Media - HD Lock And Mechanism Sound Design Kit/'
SOURCES = {
    'latch': MECH + 'MECHLtch_Click Deep Mechanism Latch Button Nearfield Thunk 02_ESM_HDLM.wav',
    'spring': MECH + 'MECHMisc_Tool Tape Measure Pull Retract Spring Slide Spin Long 07_ESM_HDLM.wav',
    'wire': MECH + 'METLTonl_Item Spring Wire Impact Flick Top Clatter Light Tap Roll Handling Short 01_ESM_HDLM.wav',
    'jaw': 'cs2/sounds/doors/door_metal_thin_close2.wav',
    'metal': 'cs2/sounds/physics/metal/metal_solid_impact_soft1.wav',
    'boulder': 'cs2/sounds/physics/concrete/boulder_impact_hard4.wav',
    'collapse': 'cs2/sounds/physics/destruction/smash_rockcollapse1.wav',
    'gravel': 'cs2/sounds/ambient/train/gravel_chute.mp3',
    'blast': 'cs2/sounds/weapons/c4/c4_explode1.wav',
    **{f'rock{i}': f'cs2/sounds/ambient/common/materials/rock{i}.mp3' for i in range(1, 4)},
    **{f'hit{i}': f'cs2/sounds/physics/concrete/rock_impact_hard{i}.wav' for i in range(1, 7)},
}


def checksum(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def decode(path, pitch=1.0, lowpass=9000, highpass=45):
    command = [shutil.which('ffmpeg'), '-v', 'error', '-i', str(path), '-t', '14', '-af',
        f'aresample={RATE},asetrate={round(RATE*pitch)},aresample={RATE},highpass=f={highpass},lowpass=f={lowpass}',
        '-ac', '1', '-f', 'f32le', '-']
    data = array.array('f')
    data.frombytes(subprocess.run(command, capture_output=True, check=True).stdout)
    if sys.byteorder != 'little':
        data.byteswap()
    peak = max(map(abs, data), default=0)
    if peak < 0.00001:
        raise ValueError('Empty/silent input: ' + str(path))
    return array.array('f', (v/peak for v in data))


def transient(data, seconds, before=.012):
    """Extract the strongest event instead of keeping a long handling sequence."""
    peak = max(range(len(data)), key=lambda i: abs(data[i]))
    start = max(0, peak-round(before*RATE))
    return data[start:start+round(seconds*RATE)]


def add(mix, data, at=0, gain=1, decay=None, wrap=False):
    start = round(at*RATE)
    fade = min(160, len(data)//4)
    for i, value in enumerate(data):
        index = start+i
        if index >= len(mix) and not wrap:
            break
        envelope = min(1, i/max(1, fade), (len(data)-i-1)/max(1, fade))
        if decay:
            envelope *= math.exp(-i/(RATE*decay))
        mix[index % len(mix)] += value*gain*envelope


def build(source=SOURCE):
    output = LOCAL_LIBRARIES['honza']
    output.mkdir(parents=True, exist_ok=True)
    previous_path = APP / 'research/HONZA_AUDIO_DESIGN.json'
    previous = json.loads(previous_path.read_text(encoding='utf-8')) if previous_path.exists() else {}
    for row in previous.get('files', []):
        path = inside(output, row['file'])
        if path.is_file() and checksum(path) != row['sha256']:
            raise ValueError('Locally changed draft; not overwriting: ' + str(path))
    paths = {key: inside(source, relative) for key, relative in SOURCES.items()}
    hashes = {key: checksum(path) for key, path in paths.items()}
    # Preserve reproducibility and detect changed source material before replacing drafts.
    for key, value in previous.get('source_hashes', {}).items():
        if key in hashes and hashes[key] != value:
            raise ValueError('Original recording changed: ' + key)
    cache = {}
    records = []

    def layer(key, pitch=1, lowpass=9000, highpass=45):
        signature = (key, pitch, lowpass, highpass)
        if signature not in cache:
            cache[signature] = decode(paths[key], pitch, lowpass, highpass)
        return cache[signature]

    def save(name, title, mix, phase, keys, note, loop=False):
        if not loop:
            # No clicks at clip boundaries and no unnecessary pre-roll.
            peak = max(map(abs, mix))
            first = next(i for i, value in enumerate(mix) if abs(value) > peak*.003)
            mix = mix[max(0, first-100):]
            for i in range(min(440, len(mix))):
                mix[-i-1] *= i/440
        peak = max(map(abs, mix))
        if not math.isfinite(peak) or peak <= 0:
            raise ValueError('Invalid mix')
        gain = .79/peak
        pcm = array.array('h', (round(max(-1, min(1, v*gain))*32767) for v in mix))
        if sys.byteorder != 'little': pcm.byteswap()
        path = inside(output, name)
        with wave.open(str(path), 'wb') as clip:
            clip.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
            clip.writeframes(pcm.tobytes())
        records.append(dict(file=name, path='Složený návrh: ' + ' + '.join(keys), title=title,
            phase=phase, loop=loop, section='A', item_ids=['honza_trap' if phase=='trap_snap' else 'honza_ult'],
            note=note + ' Složený návrh z nahrávek; finální vhodnost posuď poslechem.',
            seconds=round(len(mix)/RATE, 3), bytes=path.stat().st_size, sha256=checksum(path),
            status='candidate_not_auditioned', sources=[dict(path=SOURCES[k], sha256=hashes[k]) for k in keys]))

    # A short trigger, rushing spring and two close-spaced metal jaws, then a short rattle.
    for number, pitch, seconds, jaw_gain, wire_gain in [(1, 1.12, .38, .8, .12), (2, .83, .55, 1.0, .2), (3, 1, .44, .65, .08)]:
        mix = array.array('f', [0])*round(seconds*RATE)
        add(mix, transient(layer('latch', pitch, 8500), .22), gain=.8, decay=.09)
        add(mix, transient(layer('spring', 1.3, 6800, 500), .12), .002, .35, decay=.04)
        add(mix, transient(layer('jaw', pitch, 7200), .35), .011, jaw_gain, decay=.10)
        add(mix, transient(layer('metal', pitch, 5500), .22), .023, .35, decay=.09)
        add(mix, transient(layer('wire', 1.3, 4500, 600), .23), .028, wire_gain, decay=.055)
        save(f'honza_trap_snap_{number:02}.wav', ['Past · ostré sklapnutí čelistí', 'Past · těžší ocelové sklapnutí', 'Past · suché rychlé sklapnutí'][number-1],
             mix, 'trap_snap', ['latch','spring','jaw','metal','wire'],
             'SKLAPNUTÍ PASTI: mechanická západka + prudká pružina + kovové čelisti. Krátký kontakt, bez hlasu či výbuchu.')

    for number, pitch in [(1, .72), (2, .88)]:
        mix = array.array('f', [0])*round(1.25*RATE)
        add(mix, layer('boulder', pitch, 5500), gain=.9)
        add(mix, transient(layer('collapse', 1, 6500), .6), .03, .32, decay=.25)
        save(f'honza_boulder_launch_{number:02}.wav', f'Balvan · vypuštění / první kontakt {number}', mix, 'launch',
             ['boulder','collapse'], 'VYPUŠTĚNÍ: těžký kámen dosedne a začne se pohybovat. Jednorázový nástup, nikoli valicí smyčka.')

    for number, speed, gravel_gain in [(1, .68, .18), (2, .84, .32), (3, .58, .08)]:
        length=RATE*5
        mix=array.array('f', [0])*length
        rng=random.Random(8200+number)
        # Periodic overlap-add makes the texture wrap without an unrelated hard seam.
        bed=layer('gravel', .8, 2400, 70)
        grain=bed[RATE:RATE*2]
        for n in range(10):
            add(mix, grain, n*.5, gravel_gain, wrap=True)
        for n in range(17):
            key='rock'+str(n%3+1)
            body=layer(key, speed+rng.uniform(-.06,.06), 3100, 40)
            body=transient(body, .65)
            add(mix, body, n*5/17+rng.uniform(-.025,.025)+.03, .38+rng.random()*.15, decay=.28, wrap=True)
        for n in range(10):
            body=transient(layer('hit'+str(n%6+1), .65, 2800), .4)
            add(mix, body, n*.5+.11, .14, decay=.17, wrap=True)
        # A tiny circular smoothing pass guarantees matching boundary samples without fading the loop to silence.
        for n in range(220):
            t=n/219
            value=mix[-220+n]*(1-t)+mix[n]*t
            mix[-220+n]=value
        mix[-1]=mix[0]
        save(f'honza_boulder_roll_{number:02}.wav', ['Balvan · těžké valení po zemi', 'Balvan · valení se štěrkem', 'Balvan · hluboké pomalé drcení'][number-1],
             mix, 'roll', ['gravel','rock1','rock2','rock3',*[f'hit{i}' for i in range(1,7)]],
             'VALENÍ: pětisekundová smyčka kamenného tření, nerovnoměrných těžkých kontaktů a sypkého podkladu. Přehrávač opakuje smyčku.', True)

    for number, pitch in [(1, .75), (2, .95)]:
        mix=array.array('f', [0])*round(1.4*RATE)
        add(mix, layer('boulder', pitch, 6200), gain=.9)
        add(mix, transient(layer('hit'+str(number), .8, 5500), .6), .018, .5)
        add(mix, layer('rock'+str(number), 1, 4500)[:RATE], .045, .22, decay=.4)
        save(f'honza_boulder_impact_{number:02}.wav', f'Balvan · náraz / odskok {number}', mix, 'impact',
             ['boulder','hit'+str(number),'rock'+str(number)], 'NÁRAZ: těžký kontakt skály s podkladem nebo překážkou, krátké drobení. Bez exploze.')

    for number, pitch in [(1, .85), (2, 1.05)]:
        mix=array.array('f', [0])*round(2.8*RATE)
        add(mix, layer('blast', pitch, 6000, 35), gain=.85)
        add(mix, layer('collapse', .85, 7000), .025, .75)
        add(mix, layer('rock3', .9, 5500), .07, .35)
        save(f'honza_boulder_explode_{number:02}.wav', f'Balvan · závěrečný výbuch a kamenná suť {number}', mix, 'explode',
             ['blast','collapse','rock3'], 'ZÁVĚREČNÝ VÝBUCH: tlaková exploze + zhroucení a rozlet kamenných úlomků. Jednorázový konec ultimátky, ne náhodná součást valení.')

    manifest=dict(version=1, source_root=str(source), source_hashes=hashes,
        source=dict(id='honza_design', label='Honza – balvan a past', url='',
                    author='Složené návrhy z lokálních knihoven CS2 a Sonniss GDC 2026',
                    license='Odvozeno ze zdrojů uvedených u jednotlivých klipů; původní podmínky zůstávají platné.'), files=records)
    text=json.dumps(manifest, ensure_ascii=False, indent=2)+'\n'
    previous_path.write_text(text, encoding='utf-8')
    temporary=output/'KANDIDATI.tmp'
    temporary.write_text(text, encoding='utf-8')
    temporary.replace(output/'KANDIDATI.json')
    print(json.dumps({'candidates':len(records), 'folder':str(output)}, ensure_ascii=False))
    return manifest


def restore(wanted=None):
    manifest=json.loads((APP/'research/HONZA_AUDIO_DESIGN.json').read_text(encoding='utf-8'))
    missing=0
    for row in manifest['files']:
        if wanted is not None and 'honza:'+row['file'] not in wanted:
            continue
        path=inside(LOCAL_LIBRARIES['honza'], row['file'])
        if not path.is_file():
            missing+=1
        elif checksum(path)!=row['sha256']:
            raise ValueError('Locally changed draft: '+str(path))
    if missing:
        build(Path(manifest['source_root']))
    return missing


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=SOURCE)
    build(parser.parse_args().source)
