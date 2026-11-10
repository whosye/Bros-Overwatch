"""Prune verified, unselected audition copies and unreferenced generated Unity clips.

Dry run by default. --apply deletes only individual files inside the two explicit
audio directories. --untrack removes the audition audio from Git's index only.
"""
import argparse
import json
from pathlib import Path
import re
import subprocess

from restore_candidates import APP, INBOX, digest, selected_inputs

PROJECT = APP.parent.parent
ASSETS = PROJECT / 'OverwatchBros/Assets'
GENERATED = ASSETS / 'Audio/Collected'
AUDIO = {'.wav', '.mp3', '.ogg', '.flac', '.aif', '.aiff', '.m4a', '.opus'}
GIT = ['git', '-c', 'filter.lfs.process=', '-c', 'filter.lfs.required=false']


def referenced_guids():
    result = set()
    for path in ASSETS.rglob('*'):
        if path.is_file() and path.suffix in ('.asset', '.unity', '.prefab', '.mat', '.controller', '.anim', '.playable', '.cs'):
            result.update(re.findall(r'guid:\s*([0-9a-f]{32})', path.read_text(encoding='utf-8-sig', errors='ignore')))
    return result


def unused_generated():
    references = referenced_guids()
    result = []
    for path in GENERATED.rglob('*.wav'):
        meta = Path(str(path) + '.meta')
        match = re.search(r'guid:\s*([0-9a-f]{32})', meta.read_text()) if meta.exists() else None
        if match and match[1] not in references: result.append(path)
    return result


def checked_unlink(path, directory):
    # Validate the final absolute path before deleting; never recursively delete a directory.
    resolved = path.resolve()
    if not resolved.is_relative_to(directory.resolve()) or not path.is_file() or path.is_symlink():
        raise ValueError('Unsafe deletion path: ' + str(path))
    path.unlink()


def run(apply=False, untrack=False):
    keep = selected_inputs()
    candidates = []
    unverified = []
    for manifest in INBOX.glob('*/KANDIDATI.json'):
        data = json.loads(manifest.read_text(encoding='utf-8'))
        for row in data['files']:
            path = (manifest.parent / row['file']).resolve()
            if not path.is_relative_to(manifest.parent.resolve()): raise ValueError('Unsafe candidate path')
            file_id = 'inbox:' + path.relative_to(INBOX).as_posix()
            if file_id in keep or not path.is_file(): continue
            if digest(path) != row.get('sha256'):
                unverified.append(str(path)); continue
            source_root = Path(data['source_root']).resolve() if data.get('source_root') else None
            if source_root:
                source = (source_root / row['path']).resolve()
                if not source.is_relative_to(source_root) or not source.is_file() or digest(source) != row['sha256']:
                    unverified.append(str(path)); continue
            elif manifest.parent.name != 'Mosino_navrhy':
                unverified.append(str(path)); continue
            candidates.append(path)
    generated = unused_generated()
    tracked = subprocess.check_output(GIT + ['ls-files', '-z'], cwd=PROJECT).decode('utf-8').split('\0')
    cached = [p for p in tracked if p.startswith('OverwatchBros/Sound/sounds_dir/')
              and (Path(p).suffix.lower() in AUDIO or Path(p).suffix=='.meta' and Path(p).with_suffix('').suffix.lower() in AUDIO)]
    report = {'candidate_copies_removed': len(candidates), 'unused_unity_clips_removed': len(generated),
        'local_bytes_removed': sum(p.stat().st_size for p in candidates + generated),
        'audio_files_untracked':len(cached), 'untracked_bytes':sum((PROJECT/p).stat().st_size for p in cached if (PROJECT/p).is_file()),
        'kept_unverified':unverified, 'removed':[p.relative_to(PROJECT).as_posix() for p in candidates + generated]}
    if apply:
        for path in candidates: checked_unlink(path, INBOX)
        for path in generated:
            checked_unlink(path, GENERATED)
            meta = Path(str(path) + '.meta')
            if meta.exists(): checked_unlink(meta, GENERATED)
    if untrack and cached:
        process = subprocess.run(GIT + ['rm', '--cached', '--pathspec-from-file=-', '--pathspec-file-nul'],
            input=('\0'.join(cached)+'\0').encode('utf-8'), cwd=PROJECT, capture_output=True, check=True)
    report['applied'] = apply
    report['untracked'] = untrack
    (APP / 'research/AUDIO_CLEANUP_RESULT.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k not in ('removed','kept_unverified')}))
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--untrack', action='store_true')
    args = parser.parse_args()
    run(args.apply, args.untrack)
