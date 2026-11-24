"""Explicit local audio roots shared by the desk and Unity export."""
from pathlib import Path

APP = Path(__file__).resolve().parent
PROJECT = APP.parent.parent
INBOX = PROJECT / 'OverwatchBros/Sound/sounds_dir'
LOCAL_LIBRARIES = {'tf2': PROJECT / '_MIMO_GIT_K_PRESUNU/TF2_kandidati',
                   'honza': PROJECT / '_MIMO_GIT_K_PRESUNU/Honza_kandidati'}


def inside(root, relative):
    if not relative or '\\' in relative or ':' in relative or Path(relative).is_absolute():
        raise ValueError('Invalid audio path')
    root = root.resolve()
    path = (root / relative).resolve()
    if not path.is_relative_to(root) or path == root:
        raise ValueError('Audio path outside library')
    return path


def resolve_input(file_id):
    prefix, separator, relative = file_id.partition(':')
    roots = {'inbox': INBOX, **LOCAL_LIBRARIES}
    if not separator or prefix not in roots:
        raise ValueError('Unsupported source ' + file_id)
    return inside(roots[prefix], relative)
