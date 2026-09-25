"""Wizard War economy. Only a trusted match server may award currency.

Run locally: python Server/economy.py --db Server/data/economy.sqlite3
Production: TLS reverse proxy -> waitress-serve --call economy:create_app
"""
import argparse
import hashlib
import hmac
import json
import os
from pathlib import Path
import re
import secrets
import sqlite3
import time
import uuid
from contextlib import contextmanager, closing

CATALOG_PATH = Path(__file__).resolve().parents[1] / "Assets/Resources/ShopCatalog.json"
CATALOG = {x["id"]: x for x in json.loads(CATALOG_PATH.read_text(encoding="utf-8"))["items"]}


class APIError(Exception):
    def __init__(self, status, message):
        self.status, self.message = status, message


def require(condition, message="Invalid request", status=400):
    if not condition:
        raise APIError(status, message)


def integer(value, lo, hi):
    require(type(value) is int and lo <= value <= hi)
    return value


def digest(token):
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def reward(kills, seconds, players, winner):
    # Integer arithmetic: no rounding errors and multiplier applies ONLY to kills.
    return kills * (10 + players if winner else 10) + seconds // 60 * 5


class Economy:
    def __init__(self, database, server_key=""):
        self.database, self.server_key = str(database), server_key
        Path(self.database).parent.mkdir(parents=True, exist_ok=True)
        with closing(self.connect()) as db:
            db.executescript("""
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS players (
                    id TEXT PRIMARY KEY, credential TEXT UNIQUE NOT NULL,
                    coins INTEGER NOT NULL DEFAULT 100 CHECK(coins >= 0),
                    hat TEXT NOT NULL DEFAULT '', staff TEXT NOT NULL DEFAULT '', body TEXT NOT NULL DEFAULT '');
                CREATE TABLE IF NOT EXISTS inventory (
                    player TEXT NOT NULL REFERENCES players(id), item TEXT NOT NULL,
                    PRIMARY KEY(player,item));
                CREATE TABLE IF NOT EXISTS tickets (
                    hash TEXT PRIMARY KEY, player TEXT NOT NULL REFERENCES players(id),
                    nonce TEXT NOT NULL, expires REAL NOT NULL);
                CREATE TABLE IF NOT EXISTS matches (
                    id TEXT PRIMARY KEY, payload TEXT NOT NULL, created REAL NOT NULL);
                CREATE TABLE IF NOT EXISTS ledger (
                    id INTEGER PRIMARY KEY, player TEXT NOT NULL REFERENCES players(id),
                    amount INTEGER NOT NULL, reason TEXT NOT NULL, match TEXT,
                    UNIQUE(player,match));
            """)

    def connect(self):
        db = sqlite3.connect(self.database, timeout=15)
        db.row_factory = sqlite3.Row
        db.execute("PRAGMA foreign_keys=ON")
        return db

    @contextmanager
    def transaction(self):
        db = self.connect()
        try:
            db.execute("BEGIN IMMEDIATE")
            yield db
            db.commit()
        except Exception:
            db.rollback()
            raise
        finally:
            db.close()

    def player(self, db, token):
        require(isinstance(token, str) and re.fullmatch(r"[a-f0-9]{64}", token), "Sign in required", 401)
        row = db.execute("SELECT * FROM players WHERE credential=?", (digest(token),)).fetchone()
        require(row is not None, "Unknown account", 401)
        return row

    def profile(self, db, player_id):
        row = db.execute("SELECT * FROM players WHERE id=?", (player_id,)).fetchone()
        last = db.execute("SELECT match,amount FROM ledger WHERE player=? AND match IS NOT NULL ORDER BY id DESC LIMIT 1", (player_id,)).fetchone()
        return dict(playerId=player_id, coins=row["coins"], hat=row["hat"], staff=row["staff"], body=row["body"],
                    owned=[r[0] for r in db.execute("SELECT item FROM inventory WHERE player=? ORDER BY item", (player_id,))],
                    lastMatch=last["match"] if last else "", lastReward=last["amount"] if last else 0)

    def dispatch(self, path, body, token="", server_key=""):
        require(isinstance(body, dict))
        with self.transaction() as db:
            if path == "/v1/account":
                # Client creates and saves a random credential BEFORE this call. A lost reply
                # or a retry therefore cannot grant a second starter balance to this account.
                require(isinstance(token, str) and re.fullmatch(r"[a-f0-9]{64}", token), "Invalid credential", 401)
                db.execute("INSERT OR IGNORE INTO players(id,credential) VALUES (?,?)", (uuid.uuid4().hex, digest(token)))
                return self.profile(db, self.player(db, token)["id"])
            if path == "/v1/tickets/redeem":
                ticket, nonce = body.get("ticket"), body.get("nonce")
                require(isinstance(ticket, str) and len(ticket) <= 128 and isinstance(nonce, str))
                row = db.execute("SELECT * FROM tickets WHERE hash=?", (digest(ticket),)).fetchone()
                require(row is not None and row["expires"] >= time.time() and row["nonce"] == nonce, "Invalid or expired ticket", 401)
                db.execute("DELETE FROM tickets WHERE hash=?", (digest(ticket),))
                profile = self.profile(db, row["player"])
                # A room gets entitlements, never the account credential or wallet access.
                return {k: profile[k] for k in ("playerId", "owned", "hat", "staff", "body")}
            if path == "/v1/matches/complete":
                require(bool(self.server_key) and hmac.compare_digest(self.server_key, server_key), "Trusted server required", 403)
                return self.complete(db, body)
            player = self.player(db, token)
            pid = player["id"]
            if path == "/v1/profile":
                return self.profile(db, pid)
            if path == "/v1/tickets":
                nonce = body.get("nonce")
                require(isinstance(nonce, str) and re.fullmatch(r"[a-f0-9]{32}", nonce))
                db.execute("DELETE FROM tickets WHERE expires<?", (time.time(),))
                ticket = secrets.token_hex(32)
                db.execute("INSERT INTO tickets VALUES (?,?,?,?)", (digest(ticket), pid, nonce, time.time() + 30))
                return {"ticket": ticket}
            if path == "/v1/purchase":
                item_id = body.get("itemId")
                require(isinstance(item_id, str) and item_id in CATALOG, "Unknown item")
                item = CATALOG[item_id]
                if not db.execute("SELECT 1 FROM inventory WHERE player=? AND item=?", (pid, item_id)).fetchone():
                    require(player["coins"] >= item["price"], "Not enough coins", 409)
                    db.execute("UPDATE players SET coins=coins-? WHERE id=?", (item["price"], pid))
                    db.execute("INSERT INTO inventory VALUES (?,?)", (pid, item_id))
                    db.execute("INSERT INTO ledger(player,amount,reason) VALUES (?,?,?)", (pid, -item["price"], item_id))
                return self.profile(db, pid)
            if path == "/v1/equip":
                category, item_id = body.get("category"), body.get("itemId")
                require(category in ("hat", "staff", "body") and isinstance(item_id, str))
                if item_id:
                    require(item_id in CATALOG and CATALOG[item_id]["category"] == category)
                    require(db.execute("SELECT 1 FROM inventory WHERE player=? AND item=?", (pid, item_id)).fetchone() is not None, "Item not owned", 403)
                # category is from the closed allowlist above, never arbitrary SQL.
                db.execute(f"UPDATE players SET {category}=? WHERE id=?", (item_id, pid))
                return self.profile(db, pid)
            raise APIError(404, "Unknown endpoint")

    def complete(self, db, body):
        mid = body.get("matchId")
        require(isinstance(mid, str) and re.fullmatch(r"[a-f0-9]{32}", mid))
        rows, participants = body.get("players"), body.get("participants")
        require(isinstance(rows, list) and isinstance(participants, list))
        count = len(participants)
        require(2 <= count <= 100 and 1 <= len(rows) <= count, "Multiplayer match required")
        require(all(isinstance(x, str) for x in participants) and len(set(participants)) == count)
        duration = integer(body.get("durationSeconds"), 0, 1800)
        player_count = integer(body.get("playerCount"), 2, count)
        payload = json.dumps(body, sort_keys=True, separators=(",", ":"))
        existing = db.execute("SELECT payload FROM matches WHERE id=?", (mid,)).fetchone()
        if existing:
            require(existing[0] == payload, "Match already settled with different results", 409)
            return {"matchId": mid, "settled": True}
        for pid in participants:
            require(db.execute("SELECT 1 FROM players WHERE id=?", (pid,)).fetchone() is not None, "Unknown participant")
        seen = set()
        for row in rows:
            require(isinstance(row, dict))
            pid = row.get("playerId")
            require(isinstance(pid, str) and pid in participants and pid not in seen)
            seen.add(pid)
            integer(row.get("kills"), 0, 33)
            integer(row.get("deaths"), 0, 3300)
            integer(row.get("seconds"), 0, duration)
            require(type(row.get("completed")) is bool)
        # Same deterministic ranking as Mirror, including ties. No client winner flag.
        ordered = sorted(rows, key=lambda r: (-r["kills"], r["deaths"], r["playerId"]))
        db.execute("INSERT INTO matches VALUES (?,?,?)", (mid, payload, time.time()))
        for i, row in enumerate(ordered):
            if not row["completed"]:
                continue
            amount = reward(row["kills"], row["seconds"], player_count, i == 0)
            db.execute("UPDATE players SET coins=coins+? WHERE id=?", (amount, row["playerId"]))
            db.execute("INSERT INTO ledger(player,amount,reason,match) VALUES (?,?,?,?)", (row["playerId"], amount, "completed_match", mid))
        return {"matchId": mid, "settled": True}

    def __call__(self, environ, start_response):
        try:
            require(environ.get("REQUEST_METHOD") == "POST", "POST required", 405)
            size = int(environ.get("CONTENT_LENGTH") or 0)
            require(0 <= size <= 65536, "Request too large", 413)
            body = json.loads(environ["wsgi.input"].read(size) or b"{}")
            auth = environ.get("HTTP_AUTHORIZATION", "")
            result = self.dispatch(environ.get("PATH_INFO", ""), body,
                                   auth[7:] if auth.startswith("Bearer ") else "",
                                   environ.get("HTTP_X_WW_SERVER_KEY", ""))
            status = 200
        except APIError as error:
            status, result = error.status, {"error": error.message}
        except (ValueError, TypeError, KeyError):
            status, result = 400, {"error": "Invalid request"}
        except Exception:
            # Never return SQL, credentials or request bodies in errors.
            status, result = 503, {"error": "Service unavailable"}
        data = json.dumps(result, ensure_ascii=False).encode("utf-8")
        from http import HTTPStatus
        start_response(f"{status} {HTTPStatus(status).phrase}", [("Content-Type", "application/json; charset=utf-8"),
                        ("Content-Length", str(len(data))), ("Cache-Control", "no-store")])
        return [data]


def create_app():
    key = os.environ.get("WW_ECONOMY_SERVER_KEY", "")
    require(len(key) >= 32, "Set a random WW_ECONOMY_SERVER_KEY (at least 32 characters)")
    return Economy(os.environ.get("WW_ECONOMY_DB", "data/economy.sqlite3"), key)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--db", default="Server/data/economy.sqlite3")
    parser.add_argument("--port", type=int, default=8787)
    args = parser.parse_args()
    from wsgiref.simple_server import make_server, WSGIRequestHandler
    class QuietHandler(WSGIRequestHandler):
        def log_message(self, *args):
            pass
    app = Economy(args.db, os.environ.get("WW_ECONOMY_SERVER_KEY", ""))
    print(f"Local economy: http://127.0.0.1:{args.port} (loopback only)", flush=True)
    with make_server("127.0.0.1", args.port, app, handler_class=QuietHandler) as server:
        server.serve_forever()
