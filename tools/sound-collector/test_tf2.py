"""Local TF2 import, recovery and export path regression checks."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import audio_paths
import import_tf2


class Tf2LibraryTests(unittest.TestCase):
    def test_copy_verify_restore_and_never_overwrite_changed_audio(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).resolve().parent, prefix='.test-') as temp:
            root = Path(temp)
            source, library, app = root / 'originals', root / 'ignored', root / 'app'
            source.mkdir()
            (app / 'research').mkdir(parents=True)
            records = []
            for name in ('first.wav', 'second.wav'):
                raw = (name * 10).encode()
                (source / name).write_bytes(raw)
                records.append({'file': name, 'path': name, 'sha256': hashlib.sha256(raw).hexdigest()})
            (app / 'research/TF2_CANDIDATES.json').write_text(json.dumps({'source_root': str(source), 'files': records}))
            with patch.object(import_tf2, 'APP', app), patch.dict(audio_paths.LOCAL_LIBRARIES, {'tf2': library}):
                self.assertEqual(import_tf2.import_candidates({'tf2:first.wav'}), 1)
                self.assertFalse((library / 'second.wav').exists())
                self.assertEqual(audio_paths.resolve_input('tf2:first.wav'), library / 'first.wav')
                self.assertEqual(import_tf2.import_candidates(), 1)
                self.assertEqual(import_tf2.import_candidates(), 0)
                self.assertTrue((source / 'first.wav').exists())
                (library / 'first.wav').write_bytes(b'local change')
                with self.assertRaises(ValueError):
                    import_tf2.import_candidates()
                self.assertEqual((library / 'first.wav').read_bytes(), b'local change')

    def test_export_source_resolution_rejects_escape_and_unknown_roots(self):
        for file_id in ('tf2:../../outside.wav', 'tf2:C:/outside.wav', 'tf2:..\\outside.wav',
                        'other:outside.wav', 'inbox:../../outside.wav'):
            with self.subTest(file_id=file_id), self.assertRaises(ValueError):
                audio_paths.resolve_input(file_id)
