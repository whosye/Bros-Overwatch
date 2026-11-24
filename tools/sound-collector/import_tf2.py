"""Copy verified TF2 candidates into the ignored local library; keep originals."""
import json
from pathlib import Path
import shutil

from audio_paths import APP, LOCAL_LIBRARIES, inside
from restore_candidates import digest


def import_candidates(wanted=None):
    manifest = json.loads((APP / 'research/TF2_CANDIDATES.json').read_text(encoding='utf-8'))
    root = LOCAL_LIBRARIES['tf2'].resolve()
    source_root = Path(manifest['source_root']).resolve()
    pending = []
    # Preflight every selected input before writing anything; never overwrite changed audio.
    for row in manifest['files']:
        if wanted is not None and 'tf2:' + row['file'] not in wanted:
            continue
        source, target = inside(source_root, row['path']), inside(root, row['file'])
        if target.exists():
            if not target.is_file() or digest(target) != row['sha256']:
                raise ValueError('Locally changed candidate; not overwriting: ' + str(target))
            continue
        if not source.is_file() or digest(source) != row['sha256']:
            raise ValueError('Original missing or changed: ' + str(source))
        pending.append((source, target, row['sha256']))
    root.mkdir(parents=True, exist_ok=True)
    if shutil.disk_usage(root).free < sum(p.stat().st_size for p, _, _ in pending) + 16 * 1024**2:
        raise RuntimeError('Not enough disk space')
    for source, target, checksum in pending:
        target.parent.mkdir(parents=True, exist_ok=True)
        temporary = target.with_name(target.name + '.importing')
        created = False
        try:
            with source.open('rb') as src, temporary.open('xb') as dst:
                created = True
                shutil.copyfileobj(src, dst)
            if digest(temporary) != checksum:
                raise ValueError('Copy checksum failed: ' + str(source))
            temporary.replace(target)
        finally:
            if created and temporary.is_file():
                temporary.unlink()
        shutil.copystat(source, target)
    manifest['imported'] = True
    temporary = root / 'KANDIDATI.tmp'
    temporary.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    temporary.replace(root / 'KANDIDATI.json')
    return len(pending)


if __name__ == '__main__':
    print('TF2: copied and SHA-256 verified:', import_candidates())
