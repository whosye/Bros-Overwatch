"""Build a self-contained Unity audio bank from the user's saved choices.

Only Assets/Audio/Collected and Resources/CollectedAudioBank are written.
Original recordings, existing game audio, data assets and collection.json stay intact.
"""
from __future__ import annotations

import array
from collections import defaultdict
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import uuid
import wave

APP = Path(__file__).resolve().parent
PROJECT = APP.parent.parent
UNITY = PROJECT / 'OverwatchBros'
OUT = UNITY / 'Assets/Audio/Collected'
RATE = 44100
SCRIPT_GUID = uuid.uuid5(uuid.NAMESPACE_URL, 'bros-overwatch/CollectedAudioBank.cs').hex


def guid(path):
    return uuid.uuid5(uuid.NAMESPACE_URL, 'bros-overwatch/' + path.relative_to(UNITY).as_posix()).hex


def write_meta(path, audio=False, streaming=False):
    meta = Path(str(path) + '.meta')
    if meta.exists():
        return re.search(r'guid: ([0-9a-f]{32})', meta.read_text())[1]
    value = guid(path)
    text = f'fileFormatVersion: 2\nguid: {value}\n'
    if audio:
        text += f'''AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: {2 if streaming else 0}
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 0.8
    conversionMode: 0
    preloadAudioData: {0 if streaming else 1}
  platformSettingOverrides: {{}}
  forceToMono: 1
  normalize: 0
  loadInBackground: 0
  ambisonic: 0
  3D: 1
  userData: User-selected sound collection
  assetBundleName:
  assetBundleVariant:
'''
    meta.write_text(text, encoding='utf-8')
    return value


def decode(path, seconds=30):
    result = subprocess.run([shutil.which('ffmpeg'), '-v', 'error', '-i', str(path), '-t', str(seconds),
                             '-f', 'f32le', '-ac', '1', '-ar', str(RATE), '-'], capture_output=True, check=True)
    samples = array.array('f')
    samples.frombytes(result.stdout)
    if sys.byteorder != 'little': samples.byteswap()
    if not samples: raise ValueError('Empty audio: ' + str(path))
    return samples


def save_audio(path, samples, loop=False):
    # Trim leading dead air for responsive impacts; preserve loop duration.
    peak = max(abs(v) for v in samples)
    if peak < 0.00001: raise ValueError('Silent recording: ' + str(path))
    if not loop:
        first = next((i for i, v in enumerate(samples) if abs(v) > peak * 0.01), 0)
        samples = samples[max(0, first - int(RATE * 0.003)):]
    scale = min(8.0, 0.7 / peak)
    fade = min(int(RATE * (0.025 if loop else 0.003)), len(samples) // 4)
    if loop and fade:
        # Crossfade the end into the beginning, then remove the overlapping head.
        for i in range(fade):
            t = i / fade
            samples[-fade + i] = samples[-fade + i] * (1 - t) + samples[i] * t
        samples = samples[fade:]
    pcm = array.array('h')
    for i, value in enumerate(samples):
        envelope = 1.0 if loop else min(1.0, i / max(1, fade), (len(samples) - 1 - i) / max(1, fade))
        pcm.append(round(max(-1, min(1, value * scale * envelope)) * 32767))
    if sys.byteorder != 'little': pcm.byteswap()
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), 'wb') as output:
        output.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
        output.writeframes(pcm.tobytes())
    return len(pcm) / RATE


def assemble_shells(part, repeats, duration):
    """Fit complete, separate insertions into the animation; compress long clips instead of cutting tails."""
    if repeats < 1 or duration <= 0.08 or not part:
        raise ValueError('Invalid shell reload sequence')
    mix = array.array('f', [0]) * round(RATE * duration)
    peak = max(abs(value) for value in part)
    if peak <= 0: raise ValueError('Silent shell recording')
    first = next(i for i, value in enumerate(part) if abs(value) > peak * 0.01)
    last = len(part) - next(i for i, value in enumerate(reversed(part)) if abs(value) > peak * 0.01)
    part = part[max(0, first - RATE // 500):min(len(part), last + RATE // 500)]
    slot = int(RATE * (duration - 0.08) / repeats)
    length = min(len(part), int(slot * 0.9))
    if length < 2: raise ValueError('Reload too short for the requested number of insertions')
    # A single faster version is reused for every insertion, retaining the same sound.
    compressed = array.array('f')
    fade = min(RATE // 500, length // 4)
    for i in range(length):
        position = i * (len(part) - 1) / (length - 1)
        a = int(position)
        value = part[a] + (part[min(a + 1, len(part) - 1)] - part[a]) * (position - a)
        value *= min(1.0, i / max(1, fade), (length - i - 1) / max(1, fade))
        compressed.append(value)
    for index in range(repeats):
        start = round(RATE * 0.04) + slot * index
        mix[start:start + length] = compressed
    return mix


def cues_for(item, filename):
    name = filename.lower()
    if item == 'world_splash':
        if 'entry' in name: return ['steps_water.enter']
        if 'exit' in name: return ['steps_water.exit']
        return ['world_splash']
    if item.startswith('steps_'):
        if item == 'steps_water':
            if 'entry' in name: return [item + '.enter']
            if 'exit' in name: return [item + '.exit']
            if 'splash' in name: return [item + '.land', 'world_splash']
        return [item + ('.land' if 'land' in name else '.step')]
    if item in ('pova_weapon', 'max_weapon'):
        if 'flesh' in name or 'stab' in name: return [item + '.impact.body']
        if 'hit' in name: return [item + '.impact.surface']
        return [item + '.swing']
    if item == 'honza_ult': return ['honza_ult.land']
    if item == 'honza_mine':
        return [item + ('.explode' if 'detonate' in name or 'explos' in name else '.impact')]
    if item == 'kondulic_scope':
        return [item + ('.exit' if 'out' in name else '.ready' if 'ready' in name else '.enter')]
    return [item]


def main():
    if not shutil.which('ffmpeg'): raise RuntimeError('ffmpeg is required')
    # Source audio is a local ignored cache; recover only required inputs after a clone/cleanup.
    from restore_candidates import restore
    restore()
    state_path = APP / 'collection.json'
    original = state_path.read_bytes()
    state = json.loads(original)
    choices = dict(state['items'])
    overrides = json.loads((APP / 'integration_overrides.json').read_text(encoding='utf-8'))
    for item, files in overrides.get('defaults', {}).items():
        if not choices.get(item, {}).get('files'):
            choices[item] = {'files': files, 'done': False, 'integration_default': True}
    previous_path = OUT / 'IMPORT_MANIFEST.json'
    previous = {r['source']: r for r in json.loads(previous_path.read_text(encoding='utf-8'))['files']} if previous_path.exists() else {}
    cues = defaultdict(list)
    exported = {}
    report = {'revision': state['revision'], 'items': {}, 'files': [], 'rules': [
        'Assigned files are used even when done is false; skipped items are excluded.',
        'Land and water entry/exit are separate from walking.',
        'Melee swing and contact are separate.',
        'Reload components are assembled in order, not randomly exchanged.',
        'Existing recordings and hero voices take priority and are not modified.'
    ]}
    for item, choice in choices.items():
        if choice.get('skipped') or not choice.get('files'): continue
        records = []
        effective_files = overrides.get('reload_sequences', {}).get(item, choice['files'])
        for file_id in effective_files:
            if not file_id.startswith('inbox:'): raise ValueError('Unsupported source ' + file_id)
            relative = Path(file_id.removeprefix('inbox:'))
            source_root = (UNITY / 'Sound/sounds_dir').resolve()
            source = (source_root / relative).resolve()
            if not source.is_relative_to(source_root) or not source.is_file(): raise ValueError('Missing/unsafe source ' + file_id)
            if file_id not in exported:
                digest = hashlib.sha256(source.read_bytes()).hexdigest()
                library = {'CS2_kandidati': 'CS2', 'GDC2026_kandidati': 'GDC2026'}.get(relative.parts[0], 'Designed/' + relative.parts[0])
                parent = Path(*relative.parts[1:-1]) if len(relative.parts) > 2 else Path('Effects')
                path = OUT / library / parent / (source.stem + '_' + digest[:8] + '.wav')
                loop = item.startswith('world_') and item not in ('world_splash',) or item == 'world_groomer_engine'
                if path.exists() and file_id in previous and previous[file_id]['source_sha256'] == digest and previous[file_id]['loop'] == loop:
                    with wave.open(str(path)) as sound: duration = sound.getnframes() / sound.getframerate()
                else:
                    samples = decode(source, 16 if loop else 8)
                    duration = save_audio(path, samples, loop)
                clip_guid = write_meta(path, audio=True, streaming=loop)
                exported[file_id] = {'source': file_id, 'source_sha256': digest, 'asset': path.relative_to(UNITY).as_posix(),
                                     'guid': clip_guid, 'seconds': round(duration, 4), 'loop': loop,
                                     'provenance': state.get('files', {}).get(file_id, {})}
                report['files'].append(exported[file_id])
            record = exported[file_id]
            records.append(record)
            if not item.endswith('_reload'):
                for cue in cues_for(item, source.name):
                    if record not in cues[cue]: cues[cue].append(record)
        if item.endswith('_reload'):
            # Follow the user's order (magazine out, in, bolt, etc.), with gaps between actions.
            duration = {'honza_reload': 1.8, 'viktor_reload': 1.5, 'kondulic_reload': 1.8,
                        'david_reload': 1.6, 'mosino_reload': 1.5, 'sindel_reload': 1.5}[item]
            mix = array.array('f', [0]) * round(RATE * duration)
            shell = overrides.get('reload_shells', {}).get(item)
            if shell:
                part = decode(UNITY / 'Sound/sounds_dir' / shell['source'].removeprefix('inbox:'))
                mix = assemble_shells(part, shell['repeats'], duration)
            else:
                pieces = [decode(UNITY / record['asset']) for record in records]
                for i, part in enumerate(pieces):
                    start = round((0.05 + (duration - 0.4) * i / max(1, len(pieces) - 1)) * RATE)
                    available = len(mix) - start
                    for j, value in enumerate(part[:available]): mix[start + j] += value
            path = OUT / 'Weapons/Reload' / (item + '.wav')
            seconds = save_audio(path, mix)
            sequence = {'asset': path.relative_to(UNITY).as_posix(), 'guid': write_meta(path, audio=True), 'seconds': seconds}
            cues[item].append(sequence)
        report['items'][item] = {'done': choice.get('done', False), 'note': choice.get('note', ''),
                                  'sources': choice['files'], 'integration_default': choice.get('integration_default', False),
                                  'playback_sources': effective_files,
                                  'cues': [key for key in cues if key == item or key.startswith(item + '.')]}
    # Quiet gravel/stone grains form a provisional rolling texture from already selected recordings.
    if cues.get('steps_gravel.step'):
        grains = [decode(UNITY / record['asset']) for record in cues['steps_gravel.step'][:4]]
        mix = array.array('f', [0]) * (RATE * 3)
        for i in range(24):
            grain = grains[i % len(grains)]
            start = round(i * RATE / 8)
            for j, value in enumerate(grain[:RATE // 3]): mix[(start + j) % len(mix)] += value * 0.35
        path = OUT / 'World/honza_rolling_gravel.wav'
        seconds = save_audio(path, mix, loop=True)
        cues['honza_ult.roll'] = [{'asset':path.relative_to(UNITY).as_posix(), 'guid':write_meta(path,audio=True), 'seconds':seconds}]
    script_meta = UNITY / 'Assets/Scripts/CollectedAudioBank.cs.meta'
    if not script_meta.exists(): script_meta.write_text('fileFormatVersion: 2\nguid: ' + SCRIPT_GUID + '\n')
    actual_script_guid = re.search(r'guid: ([0-9a-f]{32})', script_meta.read_text())[1]
    asset = UNITY / 'Assets/Resources/CollectedAudioBank.asset'
    text = f'''%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {actual_script_guid}, type: 3}}
  m_Name: CollectedAudioBank
  m_EditorClassIdentifier:
  collectionRevision: {state['revision']}
  cues:
'''
    for cue, records in sorted(cues.items()):
        text += f'  - id: {cue}\n    clips:\n'
        for record in records: text += '    - {fileID: 8300000, guid: ' + record['guid'] + ', type: 3}\n'
    asset.write_text(text, encoding='utf-8')
    write_meta(asset)
    for script in ('AudioSurface.cs', 'CollectedWorldAudio.cs'):
        path = UNITY / 'Assets/Scripts' / script
        if path.exists(): write_meta(path)
    report['cue_count'] = len(cues)
    report['unique_source_files'] = len(exported)
    report['reload_overrides'] = overrides.get('reload_shells', {})
    for record in report['reload_overrides'].values():
        source = UNITY / 'Sound/sounds_dir' / record['source'].removeprefix('inbox:')
        record['source_sha256'] = hashlib.sha256(source.read_bytes()).hexdigest()
    (OUT / 'IMPORT_MANIFEST.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    from cleanup_unused_audio import unused_generated, checked_unlink
    pruned = unused_generated()
    for path in pruned:
        checked_unlink(path, OUT)
        meta = Path(str(path) + '.meta')
        if meta.exists(): checked_unlink(meta, OUT)
    assert original == state_path.read_bytes(), 'User choices changed during export; rerun to capture the latest revision.'
    print(json.dumps({'revision':state['revision'], 'items':len(report['items']), 'cues':len(cues), 'source_files':len(exported), 'pruned_unused_clips':len(pruned)}))


if __name__ == '__main__': main()
