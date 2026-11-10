"""Local sound collection desk. Python standard library only; never imports into Unity."""

from __future__ import annotations

import argparse
import copy
import json
import os
from pathlib import Path
import secrets
import shutil
import threading
import time
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit
from urllib.request import urlopen


APP = Path(__file__).resolve().parent
PROJECT = APP.parent.parent
DEFAULT_INBOX = PROJECT / "OverwatchBros" / "Sound" / "sounds_dir"
ASSETS = PROJECT / "OverwatchBros" / "Assets"
EXTENSIONS = {".wav", ".ogg", ".mp3", ".flac", ".aif", ".aiff", ".m4a", ".opus"}
MIME = {".wav": "audio/wav", ".ogg": "audio/ogg", ".mp3": "audio/mpeg",
        ".flac": "audio/flac", ".aif": "audio/aiff", ".aiff": "audio/aiff",
        ".m4a": "audio/mp4", ".opus": "audio/ogg"}


class Invalid(ValueError):
    pass


def contained(root: Path, relative: str) -> Path:
    if not relative or "\\" in relative or Path(relative).is_absolute():
        raise Invalid("Neplatná cesta k souboru.")
    result = (root / relative).resolve()
    if not result.is_relative_to(root.resolve()) or result == root.resolve():
        raise Invalid("Soubor musí být uvnitř zvukové složky.")
    return result


class Collection:
    def __init__(self, inbox: Path = DEFAULT_INBOX, state_path: Path | None = None):
        self.inbox = inbox.resolve()
        self.state_path = state_path or APP / "collection.json"
        self.catalog = json.loads((APP / "catalog.json").read_text(encoding="utf-8"))
        self.items = {item["id"]: item for item in self.catalog["items"]}
        self.existing_ids = {file for item in self.items.values() for file in item.get("existing", [])}
        self.lock = threading.RLock()
        self.token = secrets.token_urlsafe(32)
        self.inbox.mkdir(parents=True, exist_ok=True)
        if self.state_path.exists():
            # Never silently discard an unreadable collection.
            self.state = self.validate_state(json.loads(self.state_path.read_text(encoding="utf-8")))
        else:
            self.state = {"version": 1, "revision": 0, "updated": None, "items": {}, "files": {}}
            self.persist()

    def file_path(self, file_id: str) -> Path:
        if file_id.startswith("inbox:"):
            result = contained(self.inbox, file_id[6:])
        elif file_id in self.existing_ids and file_id.startswith("asset:"):
            result = contained(ASSETS, file_id[6:])
        else:
            raise Invalid("Neznámý zvukový soubor.")
        if result.suffix.lower() not in EXTENSIONS:
            raise Invalid("Tento typ souboru není podporovaný.")
        return result

    def entry(self, item_id: str) -> dict:
        item = self.items[item_id]
        return copy.deepcopy(self.state["items"].get(item_id, {
            "files": item.get("existing", []), "done": bool(item.get("existing")),
            "skipped": False, "note": "",
        }))

    def validate_entry(self, value: dict, *, require_present: bool = False) -> dict:
        if not isinstance(value, dict):
            raise Invalid("Neplatný záznam položky.")
        files = value.get("files", [])
        if not isinstance(files, list) or len(files) > 300 or any(not isinstance(f, str) for f in files):
            raise Invalid("Neplatný seznam zvuků.")
        files = list(dict.fromkeys(files))
        for file in files:
            path = self.file_path(file)
            if require_present and not path.is_file():
                raise Invalid("Některý z přiřazených souborů už ve složce není.")
        done, skipped = value.get("done", False), value.get("skipped", False)
        if type(done) is not bool or type(skipped) is not bool:
            raise Invalid("Neplatný stav položky.")
        note = value.get("note", "")
        if not isinstance(note, str) or len(note) > 8000:
            raise Invalid("Poznámka je příliš dlouhá.")
        if done and not files:
            raise Invalid("Nejdřív přiřaď zvuk, pak označ položku jako hotovou.")
        return {"files": files, "done": done, "skipped": skipped and not done, "note": note}

    def validate_meta(self, value: dict) -> dict:
        if not isinstance(value, dict):
            raise Invalid("Neplatné informace o zdroji.")
        result = {}
        for key in ("source", "license", "author"):
            text = value.get(key, "")
            if not isinstance(text, str) or len(text) > 2000:
                raise Invalid("Informace o zdroji jsou příliš dlouhé.")
            result[key] = text.strip()
        if result["source"] and urlsplit(result["source"]).scheme not in ("http", "https"):
            raise Invalid("Zdroj musí být odkaz začínající https:// nebo http://.")
        return result

    def validate_state(self, value: dict) -> dict:
        if not isinstance(value, dict) or value.get("version") != 1:
            raise Invalid("Záloha nemá podporovaný formát.")
        items, files = value.get("items", {}), value.get("files", {})
        if not isinstance(items, dict) or not isinstance(files, dict) or len(files) > 100000:
            raise Invalid("Záloha obsahuje neplatná data.")
        unknown = set(items) - set(self.items)
        if unknown:
            raise Invalid("Záloha obsahuje položky z jiného katalogu.")
        result_items = {key: self.validate_entry(entry) for key, entry in items.items()}
        result_files = {}
        for key, meta in files.items():
            self.file_path(key)
            result_files[key] = self.validate_meta(meta)
        revision = value.get("revision", 0)
        if type(revision) is not int or revision < 0:
            raise Invalid("Neplatná verze uloženého přehledu.")
        return {"version": 1, "revision": revision, "updated": value.get("updated"),
                "items": result_items, "files": result_files}

    def persist(self):
        self.state_path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.state_path.with_suffix(".tmp")
        with temporary.open("w", encoding="utf-8", newline="\n") as stream:
            json.dump(self.state, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        if self.state_path.exists():
            shutil.copy2(self.state_path, self.state_path.with_suffix(".json.bak"))
        os.replace(temporary, self.state_path)

    def recommendations(self) -> tuple[list[dict], list[str]]:
        """Read the audition manifest without treating suggestions as user choices."""
        result, warnings = [], []
        for manifest in sorted(self.inbox.glob("*/KANDIDATI.json")):
            try:
                relative = manifest.relative_to(self.inbox).as_posix()
                path = contained(self.inbox, relative)
                value = json.loads(path.read_text(encoding="utf-8"))
                if not isinstance(value, dict) or not isinstance(value.get("files"), list):
                    raise Invalid("Neplatný seznam kandidátů.")
                legacy_gdc = manifest.parent.name == "GDC2026_kandidati"
                source = value.get("source", {})
                if not isinstance(source, dict):
                    raise Invalid("Neplatný zdroj kandidátů.")
                source_id = str(source.get("id", "gdc2026" if legacy_gdc else manifest.parent.name))[:80]
                source_label = str(source.get("label", "GDC 2026" if legacy_gdc else manifest.parent.name))[:120]
                source_meta = self.validate_meta({
                    "source": source.get("url", "https://gdc.sonniss.com/" if legacy_gdc else ""),
                    "license": source.get("license", "Sonniss GDC 2026 – licence přiložená v balíčku" if legacy_gdc else ""),
                    "author": source.get("author", ""),
                })
                group = []
                seen = set()
                for record in value["files"]:
                    if not isinstance(record, dict) or not isinstance(record.get("file"), str):
                        raise Invalid("Neplatný kandidát.")
                    file_id = "inbox:" + manifest.parent.relative_to(self.inbox).as_posix() + "/" + record["file"]
                    self.file_path(file_id)
                    targets = record.get("item_ids", [])
                    if not isinstance(targets, list) or any(not isinstance(key, str) for key in targets):
                        raise Invalid("Neplatné přiřazení doporučení.")
                    if file_id in seen:
                        continue
                    seen.add(file_id)
                    group.append({"file": file_id, "item_ids": [key for key in targets if key in self.items],
                                  "tier": record.get("section") if record.get("section") in ("A", "B", "C") else "B",
                                  "note": str(record.get("note", ""))[:4000],
                                  "original": str(record.get("path", ""))[:4000],
                                  "seconds": record.get("seconds") if isinstance(record.get("seconds"), (int, float)) else None,
                                  "source_id": source_id, "source_label": source_label,
                                  "source_metadata": source_meta})
                result.extend(group)
            except (OSError, ValueError, TypeError):
                warnings.append(f"Doporučení ze složky {manifest.parent.name} nejde načíst. Ostatní knihovny a tvůj výběr zůstávají dostupné.")
        return result, warnings

    def mutate(self, action: str, value: dict) -> dict:
        with self.lock:
            if value.get("revision") != self.state["revision"]:
                raise Invalid("Přehled se mezitím změnil v jiném okně. Obnov stránku a zopakuj úpravu.")
            original = copy.deepcopy(self.state)
            try:
                if action == "item":
                    key = value.get("id")
                    if key not in self.items:
                        raise Invalid("Tato položka neexistuje.")
                    entry = self.entry(key)
                    changes = value.get("changes", {})
                    if not isinstance(changes, dict) or set(changes) - {"files", "done", "skipped", "note"}:
                        raise Invalid("Neplatná změna položky.")
                    entry.update(changes)
                    self.state["items"][key] = self.validate_entry(entry, require_present=entry.get("done", False))
                elif action == "assign":
                    key, file_id = value.get("id"), value.get("file")
                    if key not in self.items or not isinstance(file_id, str):
                        raise Invalid("Neplatné přiřazení zvuku.")
                    if not self.describe_file(file_id)["exists"]:
                        raise Invalid("Tato nahrávka už není dostupná.")
                    entry = self.entry(key)
                    if file_id not in entry["files"]:
                        entry["files"].append(file_id)
                        entry["done"] = False
                        entry["skipped"] = False
                    self.state["items"][key] = self.validate_entry(entry)
                    recommendations, _ = self.recommendations()
                    recommendation = next((row for row in recommendations if row["file"] == file_id), None)
                    if recommendation:
                        metadata = dict(recommendation["source_metadata"])
                        if not metadata["author"]:
                            metadata["author"] = recommendation["original"].split("/", 1)[0]
                        self.state["files"].setdefault(file_id, metadata)
                elif action == "file":
                    key = value.get("id", "")
                    self.file_path(key)
                    self.state["files"][key] = self.validate_meta(value.get("metadata"))
                elif action == "restore":
                    restored = self.validate_state(value.get("state"))
                    restored["revision"] = self.state["revision"]
                    self.state = restored
                else:
                    raise Invalid("Neznámá akce.")
                self.state["revision"] += 1
                self.state["updated"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
                self.persist()
            except Exception:
                self.state = original
                raise
            return {"state": copy.deepcopy(self.state)}

    def describe_file(self, file_id: str) -> dict:
        path = self.file_path(file_id)
        exists = path.is_file()
        info = path.stat() if exists else None
        pointer = False
        if exists and info.st_size < 1024:
            with path.open("rb") as stream:
                pointer = stream.read(40).startswith(b"version https://git-lfs")
        return {"id": file_id, "name": path.name, "relative": file_id.split(":", 1)[1],
                "inbox": file_id.startswith("inbox:"), "exists": exists and not pointer,
                "pointer": pointer, "size": info.st_size if info else 0,
                "modified": info.st_mtime if info else 0,
                "format": path.suffix[1:].upper(), "mime": MIME.get(path.suffix.lower(), "audio/*")}

    def snapshot(self) -> dict:
        with self.lock:
            ids = set(self.existing_ids)
            recommendations, warnings = self.recommendations()
            ids.update(record["file"] for record in recommendations)
            for path in self.inbox.rglob("*"):
                if path.is_file() and path.suffix.lower() in EXTENSIONS:
                    relative = path.relative_to(self.inbox).as_posix()
                    try:
                        self.file_path("inbox:" + relative)
                        ids.add("inbox:" + relative)
                    except (Invalid, OSError):
                        warnings.append("Soubor mimo zvukovou složku byl vynechán: " + relative)
            for entry in self.state["items"].values():
                ids.update(entry["files"])
            files = []
            for key in sorted(ids):
                try:
                    files.append(self.describe_file(key))
                except OSError:
                    warnings.append("Soubor se právě mění nebo nejde přečíst: " + key)
            return {"catalog": self.catalog, "state": copy.deepcopy(self.state), "files": files,
                    "recommendations": recommendations,
                    "folder": str(self.inbox), "statePath": str(self.state_path),
                    "token": self.token, "warnings": warnings}


class Handler(BaseHTTPRequestHandler):
    server_version = "SoundCollector/1.0"

    @property
    def collection(self) -> Collection:
        return self.server.collection

    def log_message(self, fmt, *args):
        if len(args) > 1 and str(args[1]) not in ("200", "204", "206", "304"):
            super().log_message(fmt, *args)

    def allowed_host(self) -> bool:
        return self.headers.get("Host", "") in {
            f"127.0.0.1:{self.server.server_port}", f"localhost:{self.server.server_port}"}

    def headers_for(self, code, mime, size, *, extra=None):
        self.send_response(code)
        self.send_header("Content-Type", mime)
        self.send_header("Content-Length", str(size))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Referrer-Policy", "no-referrer")
        self.send_header("Content-Security-Policy", "default-src 'self'; script-src 'self'; style-src 'self'; "
                         "img-src 'self' data:; media-src 'self' blob:; connect-src 'self'; frame-ancestors 'none'")
        for key, value in (extra or {}).items():
            self.send_header(key, value)
        self.end_headers()

    def send_json(self, value, code=200, extra=None):
        data = json.dumps(value, ensure_ascii=False).encode("utf-8")
        self.headers_for(code, "application/json; charset=utf-8", len(data), extra=extra)
        if self.command != "HEAD":
            self.wfile.write(data)

    def do_HEAD(self):
        self.do_GET()

    def do_GET(self):
        if not self.allowed_host():
            self.send_json({"error": "Povolený je pouze přístup z tohoto počítače."}, 403)
            return
        url = urlsplit(self.path)
        try:
            if url.path == "/api/identity":
                self.send_json({"application": "bros-sound-collector", "apiVersion": 3, "pid": os.getpid(), "project": str(PROJECT),
                                "folder": str(self.collection.inbox), "statePath": str(self.collection.state_path.resolve())})
            elif url.path == "/api/snapshot":
                self.send_json(self.collection.snapshot())
            elif url.path == "/api/export":
                with self.collection.lock:
                    self.send_json(self.collection.state, extra={
                        "Content-Disposition": 'attachment; filename="bros-zvuky-zaloha.json"'})
            elif url.path == "/api/audio":
                key = parse_qs(url.query).get("id", [""])[0]
                self.send_audio(self.collection.file_path(key))
            elif url.path in ("/", "/index.html", "/app.js", "/style.css", "/integration.css"):
                filename = "index.html" if url.path == "/" else url.path[1:]
                data = (APP / "web" / filename).read_bytes()
                mime = {"index.html": "text/html", "app.js": "text/javascript", "style.css": "text/css", "integration.css": "text/css"}[filename]
                self.headers_for(200, mime + "; charset=utf-8", len(data))
                if self.command != "HEAD":
                    self.wfile.write(data)
            elif url.path == "/favicon.ico":
                self.headers_for(204, "image/x-icon", 0)
            else:
                self.send_json({"error": "Stránka neexistuje."}, 404)
        except (Invalid, FileNotFoundError) as exc:
            self.send_json({"error": str(exc)}, 404)
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            pass
        except OSError:
            self.send_json({"error": "Soubor se právě mění nebo není dostupný. Zkus obnovit přehled."}, 500)

    def send_audio(self, path: Path):
        # HTTP range support allows seeking without loading an entire ambience pack.
        with path.open("rb") as stream:
            size = os.fstat(stream.fileno()).st_size
            if size < 1024 and stream.read(40).startswith(b"version https://git-lfs"):
                raise Invalid("Je dostupný pouze Git LFS odkaz, nikoli zvuková nahrávka.")
            stream.seek(0)
            start, end, status = 0, size - 1, 200
            requested = self.headers.get("Range")
            extra = {"Accept-Ranges": "bytes"}
            if requested:
                try:
                    unit, span = requested.split("=", 1)
                    first, last = span.split("-", 1)
                    if unit != "bytes" or "," in span:
                        raise ValueError
                    if first:
                        start, end = int(first), min(int(last), size - 1) if last else size - 1
                    else:
                        length = int(last)
                        if length <= 0:
                            raise ValueError
                        start = max(0, size - length)
                    if start < 0 or start >= size or end < start:
                        raise ValueError
                    status = 206
                    extra["Content-Range"] = f"bytes {start}-{end}/{size}"
                except ValueError:
                    self.headers_for(416, "text/plain", 0, extra={"Content-Range": f"bytes */{size}"})
                    return
            self.headers_for(status, MIME[path.suffix.lower()], max(0, end - start + 1), extra=extra)
            if self.command == "HEAD":
                return
            stream.seek(start)
            remaining = end - start + 1
            while remaining > 0:
                chunk = stream.read(min(65536, remaining))
                if not chunk:
                    break
                self.wfile.write(chunk)
                remaining -= len(chunk)

    def do_POST(self):
        if not self.allowed_host():
            self.send_json({"error": "Nepovolený přístup."}, 403)
            return
        origin = self.headers.get("Origin")
        if (origin and origin != "http://" + self.headers["Host"]) or not secrets.compare_digest(
                self.headers.get("X-Collector-Token", ""), self.collection.token):
            self.send_json({"error": "Obnov stránku před další úpravou."}, 403)
            return
        if self.headers.get_content_type() != "application/json":
            self.send_json({"error": "Požadavek musí obsahovat JSON."}, 400)
            return
        try:
            length = int(self.headers.get("Content-Length", "0"))
            if not 0 < length <= 8_000_000:
                raise Invalid("Požadavek je příliš velký nebo prázdný.")
            value = json.loads(self.rfile.read(length))
            if not isinstance(value, dict):
                raise Invalid("Neplatný požadavek.")
            action = urlsplit(self.path).path.removeprefix("/api/")
            if action == "open-folder":
                if os.name == "nt":
                    os.startfile(self.collection.inbox)
                self.send_json({"ok": True})
            else:
                self.send_json(self.collection.mutate(action, value))
        except (Invalid, ValueError, TypeError) as exc:
            self.send_json({"error": str(exc)}, 400)
        except OSError:
            self.send_json({"error": "Uložení se nepovedlo. Úprava nebyla potvrzena; zkontroluj přístup do složky."}, 500)


def main():
    parser = argparse.ArgumentParser(description="Bros Overwatch – místní sbírka zvuků")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--open", action="store_true", help="Otevřít ve výchozím prohlížeči")
    parser.add_argument("--inbox", type=Path, default=DEFAULT_INBOX)
    parser.add_argument("--state", type=Path, default=APP / "collection.json")
    args = parser.parse_args()
    # Reopening the launcher reuses the existing desk instead of starting two
    # independent writers for the same collection.json.
    for port in range(args.port, args.port + 10):
        try:
            with urlopen(f"http://127.0.0.1:{port}/api/identity", timeout=0.15) as response:
                identity = json.load(response)
            if (identity.get("application") == "bros-sound-collector"
                    and identity.get("project") == str(PROJECT)
                    and identity.get("folder") == str(args.inbox.resolve())
                    and identity.get("statePath") == str(args.state.resolve())):
                url = f"http://127.0.0.1:{port}"
                print(f"Přehled už běží: {url}", flush=True)
                if args.open:
                    webbrowser.open(url)
                return
        except (OSError, ValueError):
            pass
    collection = Collection(args.inbox, args.state)
    for port in range(args.port, args.port + 10):
        try:
            server = ThreadingHTTPServer(("127.0.0.1", port), Handler)
            break
        except OSError:
            if port == args.port + 9:
                raise SystemExit("Nedaří se otevřít místní přehled: porty jsou obsazené.")
    server.collection = collection
    server.daemon_threads = True
    url = f"http://127.0.0.1:{server.server_port}"
    print(f"Bros Overwatch – zvuková sbírka: {url}", flush=True)
    print(f"Zvuky: {collection.inbox}", flush=True)
    print("Nech toto okno běžet. Přehled ukončíš pomocí Ctrl+C nebo zavřením okna.", flush=True)
    if args.open:
        threading.Timer(0.5, lambda: webbrowser.open(url)).start()
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
