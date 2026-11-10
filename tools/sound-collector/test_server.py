"""Integration checks for real file assignment, durable saves and safe serving."""
import copy
import json
from pathlib import Path
import tempfile
import threading
import unittest
from urllib.error import HTTPError
from urllib.parse import urlencode
from urllib.request import Request, urlopen
import wave
from unittest.mock import patch

from server import Collection, Handler, Invalid, ThreadingHTTPServer


class DeskIntegration(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix=".test-", dir=Path(__file__).resolve().parent)
        self.root = Path(self.directory.name)
        self.inbox = self.root / "sounds"
        self.state_path = self.root / "state" / "collection.json"
        self.collection = Collection(self.inbox, self.state_path)
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.server.collection = self.collection
        self.server.daemon_threads = True
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.url = f"http://127.0.0.1:{self.server.server_port}"
        self.file = self.inbox / "kroky" / "dřevo #1.wav"
        self.file.parent.mkdir()
        with wave.open(str(self.file), "wb") as sound:
            sound.setnchannels(1)
            sound.setsampwidth(2)
            sound.setframerate(8000)
            sound.writeframes(b"\x00\x00" * 800)
        self.file_id = "inbox:kroky/dřevo #1.wav"

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=3)
        self.assertTrue(self.root.resolve().is_relative_to(Path(__file__).resolve().parent))
        self.directory.cleanup()

    def get(self, path, headers=None):
        return urlopen(Request(self.url + path, headers=headers or {}))

    def post(self, action, payload, **headers):
        request = Request(self.url + "/api/" + action,
                          data=json.dumps(payload).encode(), method="POST", headers={
                              "Content-Type": "application/json", "X-Collector-Token": self.collection.token, **headers})
        return urlopen(request)

    def item(self, id, changes, revision=None):
        with self.post("item", {"id": id, "changes": changes,
                               "revision": self.collection.state["revision"] if revision is None else revision}) as response:
            return json.load(response)

    def test_unicode_subfolders_and_live_discovery(self):
        with self.get("/api/snapshot") as response:
            files = {file["id"]: file for file in json.load(response)["files"]}
        self.assertTrue(files[self.file_id]["exists"])
        second = self.inbox / "new.mp3"
        second.write_bytes(b"new audio")
        with self.get("/api/snapshot") as response:
            self.assertIn("inbox:new.mp3", [file["id"] for file in json.load(response)["files"]])

    def test_assignment_done_notes_and_source_survive_restart(self):
        self.item("steps_wood", {"files": [self.file_id], "done": True, "note": "Použít tři varianty."})
        self.item("steps_solid_wood", {"files": [self.file_id]})
        with self.post("file", {"id": self.file_id, "metadata": {"source": "https://example.com/sound", "license": "CC0", "author": "Autor"}, "revision": 2}):
            pass
        restarted = Collection(self.inbox, self.state_path)
        self.assertTrue(restarted.entry("steps_wood")["done"])
        self.assertEqual(restarted.entry("steps_wood")["note"], "Použít tři varianty.")
        self.assertEqual(restarted.entry("steps_solid_wood")["files"], [self.file_id])
        self.assertEqual(restarted.state["files"][self.file_id]["license"], "CC0")
        previous = json.loads(self.state_path.with_suffix(".json.bak").read_text(encoding="utf-8"))
        self.assertEqual(previous["revision"], 2)

    def test_seekable_audio_and_exact_ranges(self):
        endpoint = "/api/audio?" + urlencode({"id": self.file_id})
        raw = self.file.read_bytes()
        for span, expected in [("bytes=10-29", raw[10:30]), ("bytes=20-", raw[20:]), ("bytes=-10", raw[-10:])]:
            with self.get(endpoint, {"Range": span}) as response:
                self.assertEqual(response.status, 206)
                self.assertEqual(response.read(), expected)
                self.assertEqual(response.headers["Content-Type"], "audio/wav")
        with self.assertRaises(HTTPError) as caught:
            self.get(endpoint, {"Range": "bytes=999999-"})
        self.assertEqual(caught.exception.code, 416)

    def test_stale_write_is_rejected_without_losing_newer_notes(self):
        self.item("steps_wood", {"note": "novější"})
        with self.assertRaises(HTTPError) as caught:
            self.item("steps_wood", {"note": "zastaralé"}, revision=0)
        self.assertEqual(caught.exception.code, 400)
        self.assertEqual(self.collection.entry("steps_wood")["note"], "novější")

    def test_export_restore_and_corrupt_restore_is_atomic(self):
        self.item("steps_wood", {"files": [self.file_id], "done": True})
        with self.get("/api/export") as response:
            exported = json.load(response)
        self.item("steps_wood", {"done": False})
        with self.post("restore", {"state": exported, "revision": 2}):
            pass
        self.assertTrue(self.collection.entry("steps_wood")["done"])
        before = self.state_path.read_bytes()
        broken = copy.deepcopy(exported)
        broken["items"]["steps_wood"]["files"] = ["inbox:../../outside.wav"]
        with self.assertRaises(HTTPError):
            self.post("restore", {"state": broken, "revision": 3})
        self.assertEqual(before, self.state_path.read_bytes())

    def test_cannot_mark_empty_or_missing_file_done(self):
        with self.assertRaises(HTTPError):
            self.item("steps_wood", {"done": True})
        with self.assertRaises(HTTPError):
            self.item("steps_wood", {"files": ["inbox:missing.wav"], "done": True})

    def test_renamed_file_is_reported_missing_and_original_not_modified(self):
        before = self.file.read_bytes()
        self.item("steps_wood", {"files": [self.file_id], "done": True})
        moved = self.file.with_name("renamed.wav")
        self.file.rename(moved)
        snap = self.collection.snapshot()
        self.assertFalse(next(file for file in snap["files"] if file["id"] == self.file_id)["exists"])
        self.assertEqual(moved.read_bytes(), before)

    def test_paths_csrf_and_unexpected_hosts_are_rejected(self):
        for id in ("inbox:../../outside.wav", "inbox:C:/outside.wav", "asset:../outside.wav", "inbox:..\\outside.wav"):
            with self.subTest(id=id), self.assertRaises(Invalid):
                self.collection.file_path(id)
        with self.assertRaises(HTTPError) as caught:
            self.post("item", {"id": "steps_wood", "changes": {}, "revision": 0}, Origin="https://other.example")
        self.assertEqual(caught.exception.code, 403)
        with self.assertRaises(HTTPError) as caught:
            self.get("/api/snapshot", {"Host": "other.example"})
        self.assertEqual(caught.exception.code, 403)
        with self.assertRaises(HTTPError) as caught:
            self.get("/collection.json")
        self.assertEqual(caught.exception.code, 404)

    def test_lfs_pointer_is_not_presented_as_audio(self):
        pointer = self.inbox / "pointer.wav"
        pointer.write_text("version https://git-lfs.github.com/spec/v1\noid sha256:123\nsize 2000\n")
        file = self.collection.describe_file("inbox:pointer.wav")
        self.assertFalse(file["exists"])
        self.assertTrue(file["pointer"])
        with self.assertRaises(HTTPError):
            self.get("/api/audio?" + urlencode({"id": "inbox:pointer.wav"}))

    def test_failed_disk_write_does_not_confirm_or_change_state(self):
        original = copy.deepcopy(self.collection.state)
        with patch("server.os.replace", side_effect=OSError("disk unavailable")):
            with self.assertRaises(HTTPError) as caught:
                self.item("steps_wood", {"note": "will fail"})
        self.assertEqual(caught.exception.code, 500)
        self.assertEqual(self.collection.state, original)
        self.assertEqual(json.loads(self.state_path.read_text(encoding="utf-8")), original)

    def test_corrupt_state_is_not_silently_reset(self):
        self.state_path.write_text("broken", encoding="utf-8")
        with self.assertRaises(ValueError):
            Collection(self.inbox, self.state_path)
        self.assertEqual(self.state_path.read_text(encoding="utf-8"), "broken")

    def candidate_manifest(self):
        folder = self.inbox / "GDC2026_kandidati"
        folder.mkdir()
        (folder / "01_svih.wav").write_bytes(self.file.read_bytes())
        value = {"version": 1, "files": [{"file": "01_svih.wav", "path": "Autor - Pack/old_name.wav",
                 "item_ids": ["pova_weapon", "max_weapon", "not_in_catalog"], "section": "A",
                 "note": "Vyzkoušet jako švih", "seconds": 0.1}]}
        (folder / "KANDIDATI.json").write_text(json.dumps(value), encoding="utf-8")
        return folder, value, "inbox:GDC2026_kandidati/01_svih.wav"

    def test_recommendations_map_to_tasks_without_selecting_them(self):
        folder, _, file_id = self.candidate_manifest()
        before = self.state_path.read_bytes()
        with self.get("/api/snapshot") as response:
            snapshot = json.load(response)
        recommendation = snapshot["recommendations"][0]
        self.assertEqual(recommendation["file"], file_id)
        self.assertEqual(recommendation["item_ids"], ["pova_weapon", "max_weapon"])
        self.assertEqual(recommendation["note"], "Vyzkoušet jako švih")
        self.assertEqual(self.collection.entry("pova_weapon")["files"], [])
        self.assertEqual(before, self.state_path.read_bytes())

    def test_one_click_assignment_preserves_notes_and_adds_origin(self):
        _, _, file_id = self.candidate_manifest()
        self.item("pova_weapon", {"note": "Moje poznámka", "files": [self.file_id]})
        with self.post("assign", {"id": "pova_weapon", "file": file_id, "revision": 1}):
            pass
        restarted = Collection(self.inbox, self.state_path)
        choice = restarted.entry("pova_weapon")
        self.assertEqual(choice["files"], [self.file_id, file_id])
        self.assertEqual(choice["note"], "Moje poznámka")
        self.assertFalse(choice["done"])
        self.assertEqual(restarted.state["files"][file_id]["author"], "Autor - Pack")
        self.item("pova_weapon", {"done": True})
        with self.post("assign", {"id": "pova_weapon", "file": file_id, "revision": 3}):
            pass
        self.assertTrue(self.collection.entry("pova_weapon")["done"])
        self.assertEqual(self.collection.entry("pova_weapon")["files"].count(file_id), 1)

    def test_same_candidate_can_be_used_by_two_tasks_without_overwriting_source(self):
        _, _, file_id = self.candidate_manifest()
        custom = {"source": "https://example.com/original", "license": "Moje poznámka o licenci", "author": "Upravený autor"}
        with self.post("file", {"id": file_id, "metadata": custom, "revision": 0}):
            pass
        for item in ("pova_weapon", "max_weapon"):
            with self.post("assign", {"id": item, "file": file_id, "revision": self.collection.state["revision"]}):
                pass
        self.assertEqual(self.collection.entry("max_weapon")["files"], [file_id])
        self.assertEqual(self.collection.entry("pova_weapon")["files"], [file_id])
        self.assertEqual(self.collection.state["files"][file_id], custom)

    def test_missing_or_unsafe_candidate_does_not_break_collection(self):
        folder, manifest, file_id = self.candidate_manifest()
        (folder / "01_svih.wav").unlink()
        self.assertFalse(next(file for file in self.collection.snapshot()["files"] if file["id"] == file_id)["exists"])
        with self.assertRaises(HTTPError):
            self.post("assign", {"id": "pova_weapon", "file": file_id, "revision": 0})
        manifest["files"][0]["file"] = "../../outside.wav"
        (folder / "KANDIDATI.json").write_text(json.dumps(manifest), encoding="utf-8")
        snapshot = self.collection.snapshot()
        self.assertEqual(snapshot["recommendations"], [])
        self.assertTrue(snapshot["warnings"])
        self.assertTrue(any(file["id"] == self.file_id for file in snapshot["files"]))

    def test_multiple_libraries_keep_distinct_ids_and_provenance(self):
        self.candidate_manifest()
        folder = self.inbox / "CS2_kandidati"
        (folder / "player").mkdir(parents=True)
        (folder / "player/step.wav").write_bytes(self.file.read_bytes())
        manifest = {"source": {"id": "cs2", "label": "CS2", "url": "", "license": "Nedoložená", "author": "CS2 knihovna"},
                    "files": [{"file": "player/step.wav", "path": "player/step.wav", "item_ids": ["steps_wood"], "section": "A"}]}
        (folder / "KANDIDATI.json").write_text(json.dumps(manifest), encoding="utf-8")
        snapshot = self.collection.snapshot()
        self.assertEqual({row["source_id"] for row in snapshot["recommendations"]}, {"cs2", "gdc2026"})
        cs2_file = "inbox:CS2_kandidati/player/step.wav"
        with self.post("assign", {"id": "steps_wood", "file": cs2_file, "revision": 0}):
            pass
        metadata = self.collection.state["files"][cs2_file]
        self.assertEqual(metadata["source"], "")
        self.assertEqual(metadata["license"], "Nedoložená")
        self.assertEqual(metadata["author"], "CS2 knihovna")
        self.assertEqual(self.collection.entry("pova_weapon")["files"], [])
        (folder / "KANDIDATI.json").write_text("invalid", encoding="utf-8")
        snapshot = self.collection.snapshot()
        self.assertEqual({row["source_id"] for row in snapshot["recommendations"]}, {"gdc2026"})
        self.assertTrue(snapshot["warnings"])
        self.assertEqual(self.collection.entry("steps_wood")["files"], [cs2_file])


if __name__ == "__main__":
    unittest.main()
