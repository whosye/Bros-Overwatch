import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import cleanup_unused_audio as cleanup
import restore_candidates as restore


class AudioCleanupTests(unittest.TestCase):
    def test_used_guid_is_retained_and_only_unused_generated_clip_is_selected(self):
        with tempfile.TemporaryDirectory() as directory:
            assets = Path(directory) / 'Assets'
            generated = assets / 'Audio/Collected'
            generated.mkdir(parents=True)
            used, unused = generated/'used.wav', generated/'unused.wav'
            for path, guid in ((used, 'a'*32), (unused, 'b'*32)):
                path.write_bytes(b'fixture')
                Path(str(path)+'.meta').write_text('guid: '+guid+'\n')
            (assets/'bank.asset').write_text('clip: {fileID: 8300000, guid: '+'a'*32+', type: 3}')
            with patch.object(cleanup, 'ASSETS', assets), patch.object(cleanup, 'GENERATED', generated):
                self.assertEqual(cleanup.unused_generated(), [unused])
            self.assertTrue(used.is_file())

    def test_deletion_rejects_an_absolute_path_outside_the_audio_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            allowed = root/'Allowed'
            allowed.mkdir()
            outside = root/'original.wav'
            outside.write_bytes(b'original')
            with self.assertRaises(ValueError): cleanup.checked_unlink(outside, allowed)
            self.assertEqual(outside.read_bytes(), b'original')

    def test_restore_copies_only_selected_input_and_verifies_original_checksum(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            app, inbox, source = root/'app', root/'cache', root/'originals'
            app.mkdir(); inbox.mkdir(); source.mkdir()
            (app/'collection.json').write_text(json.dumps({'items':{'steps_wood':{'files':['inbox:CS2_kandidati/keep.wav']}}}))
            (app/'integration_overrides.json').write_text('{}')
            rows=[]
            for name in ('keep.wav','other.wav'):
                data = name.encode()
                (source/name).write_bytes(data)
                rows.append({'file':name,'path':name,'sha256':hashlib.sha256(data).hexdigest()})
            for name, records in (('CS2_kandidati',rows), ('GDC2026_kandidati',[])):
                (inbox/name).mkdir()
                (inbox/name/'KANDIDATI.json').write_text(json.dumps({'source_root':str(source),'files':records}))
            with patch.object(restore,'APP',app), patch.object(restore,'INBOX',inbox):
                self.assertEqual(restore.restore(),1)
                self.assertFalse((inbox/'CS2_kandidati/other.wav').exists())
                self.assertEqual(restore.restore(),0)
                (inbox/'CS2_kandidati/keep.wav').unlink()
                (source/'keep.wav').write_bytes(b'changed')
                with self.assertRaises(ValueError): restore.restore()


if __name__ == '__main__': unittest.main()
