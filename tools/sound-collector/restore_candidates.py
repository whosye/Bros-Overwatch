"""Restore selected audio inputs (or --all candidates) from verified external originals."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil

APP = Path(__file__).resolve().parent
INBOX = APP.parent.parent / 'OverwatchBros/Sound/sounds_dir'


def digest(path):
    result = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''): result.update(block)
    return result.hexdigest()


def selected_inputs():
    state = json.loads((APP / 'collection.json').read_text(encoding='utf-8'))
    overrides = json.loads((APP / 'integration_overrides.json').read_text(encoding='utf-8'))
    result = {f for item in state['items'].values() for f in item.get('files', [])}
    for key in ('defaults', 'reload_sequences'):
        result.update(f for files in overrides.get(key, {}).values() for f in files)
    result.update(r['source'] for r in overrides.get('reload_shells', {}).values())
    return result


def restore(all_candidates=False):
    wanted = selected_inputs()
    design_needed = all_candidates or any(f.startswith('inbox:Mosino_navrhy/') for f in wanted)
    if design_needed:
        wanted.update('inbox:CS2_kandidati/' + f for f in (
            'weapons/c4/c4_thump_01.mp3', 'weapons/c4/c4_shockwave_01.mp3',
            'ui/chickens/photobooth_fx_beam_01.wav'))
    restored = 0
    for folder in ('GDC2026_kandidati', 'CS2_kandidati'):
        manifest = INBOX / folder / 'KANDIDATI.json'
        data = json.loads(manifest.read_text(encoding='utf-8'))
        source_root = Path(data['source_root']).resolve()
        for row in data['files']:
            if not all_candidates and 'inbox:' + folder + '/' + row['file'] not in wanted: continue
            target = (INBOX / folder / row['file']).resolve()
            source = (source_root / row['path']).resolve()
            if not target.is_relative_to((INBOX / folder).resolve()) or not source.is_relative_to(source_root):
                raise ValueError('Unsafe manifest path')
            if target.is_file():
                if digest(target) != row['sha256']: raise ValueError('Locally changed candidate; not overwriting: ' + str(target))
                continue
            if not source.is_file() or digest(source) != row['sha256']:
                raise ValueError('Original missing or changed: ' + str(source))
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
            if digest(target) != row['sha256']: raise ValueError('Copy checksum failed')
            restored += 1
    if design_needed:
        from design_mosino_pulse import main
        main()
    return restored


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--all', action='store_true', help='Restore all audition candidates, not only selected game inputs.')
    args = parser.parse_args()
    print('Restored candidate files:', restore(args.all))
