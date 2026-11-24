"""Install only the explicitly requested pickup respawn cue; preserve other exports."""
import hashlib
import json
import re

from audio_paths import resolve_input
from export_to_unity import APP, OUT, UNITY, decode, save_audio, write_meta


def main():
    source_id = 'tf2:items/spawn_item.wav'
    source = resolve_input(source_id)
    digest = hashlib.sha256(source.read_bytes()).hexdigest()
    asset = OUT / 'TF2/items' / ('spawn_item_' + digest[:8] + '.wav')
    state_path = APP / 'collection.json'
    original_state = state_path.read_bytes()
    seconds = save_audio(asset, decode(source))
    clip_guid = write_meta(asset, audio=True)
    bank_path = UNITY / 'Assets/Resources/CollectedAudioBank.asset'
    bank = bank_path.read_text(encoding='utf-8')
    block = '  - id: pickup_respawn\n    clips:\n    - {fileID: 8300000, guid: ' + clip_guid + ', type: 3}\n'
    pattern = r'^  - id: pickup_respawn\n.*?(?=^  - id:|\Z)'
    if re.search(pattern, bank, re.M | re.S):
        bank = re.sub(pattern, lambda _: block, bank, flags=re.M | re.S)
    else:
        bank = bank.rstrip() + '\n' + block
    manifest_path = OUT / 'IMPORT_MANIFEST.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    record = dict(source=source_id, source_sha256=digest, asset=asset.relative_to(UNITY).as_posix(),
                  guid=clip_guid, seconds=round(seconds, 4), loop=False,
                  provenance=dict(author='TF2 – uživatelem dodaná knihovna', license='Nedoložena', source=''))
    manifest['files'] = [r for r in manifest['files'] if r['source'] != source_id] + [record]
    manifest['items']['pickup_respawn'] = dict(done=False, note='Explicitně vyžádaný spawn_item při respawnu pickupů.',
        sources=[source_id], integration_default=True, playback_sources=[source_id], cues=['pickup_respawn'])
    manifest['cue_count'] = len(re.findall(r'^  - id:', bank, re.M))
    manifest['unique_source_files'] = len(manifest['files'])
    bank_path.write_text(bank, encoding='utf-8')
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    assert original_state == state_path.read_bytes(), 'User choices changed during import.'
    print(json.dumps({'cue': 'pickup_respawn', 'seconds': seconds, 'asset': str(asset)}))


if __name__ == '__main__':
    main()
