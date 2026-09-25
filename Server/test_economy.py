import io
import json
from pathlib import Path
import unittest
import uuid
from concurrent.futures import ThreadPoolExecutor
from economy import APIError, CATALOG, Economy, reward


class EconomyTests(unittest.TestCase):
    def setUp(self):
        self.temp = Path(__file__).resolve().parents[1] / "Logs/ShopTests" / uuid.uuid4().hex
        self.temp.mkdir(parents=True)
        self.key = "test-server-key-" + "a" * 40
        self.service = Economy(self.temp / "test.sqlite3", self.key)
        self.tokens = [(str(i) * 64) for i in range(1, 10)]
        self.players = [self.call("account", token=t)["playerId"] for t in self.tokens]

    def tearDown(self):
        for file in self.temp.iterdir():
            file.unlink()
        self.temp.rmdir()

    def call(self, path, body=None, token=None, server_key=""):
        return self.service.dispatch("/v1/" + path, body or {}, token or self.tokens[0], server_key)

    def report(self, count=5, seconds=125):
        return dict(matchId=uuid.uuid4().hex, durationSeconds=seconds, playerCount=count,
                    participants=self.players[:count], players=[dict(playerId=p, kills=10-i, deaths=i, seconds=seconds, completed=True) for i, p in enumerate(self.players[:count])])

    def settle(self, report):
        return self.call("matches/complete", report, server_key=self.key)

    def test_initial_balance_is_once_and_persists(self):
        self.assertEqual(self.call("account")["coins"], 100)
        self.call("purchase", {"itemId": "hat_ember"})
        self.assertEqual(self.call("account")["coins"], 0)
        restarted = Economy(self.service.database, self.key)
        self.assertEqual(restarted.dispatch("/v1/profile", {}, self.tokens[0])["owned"], ["hat_ember"])

    def test_examples_and_full_minutes(self):
        self.assertEqual(reward(10, 60, 5, True), 155)
        self.assertEqual(reward(10, 60, 9, True), 195)
        self.assertEqual(reward(10, 119, 5, False), 105)
        self.assertEqual(reward(0, 59, 5, True), 0)
        self.assertEqual(reward(1, 120, 5, True), 25)

    def test_reward_atomic_idempotent_and_rejects_different_replay(self):
        report = self.report()
        self.settle(report)
        self.settle(report)
        profile = self.call("profile")
        self.assertEqual(profile["coins"], 260)
        self.assertEqual(profile["lastReward"], 160)
        report["players"][0]["kills"] = 20
        with self.assertRaises(APIError):
            self.settle(report)
        self.assertEqual(self.call("profile")["coins"], 260)

    def test_untrusted_host_or_client_cannot_award(self):
        for key in ("", self.tokens[0], "wrong-server"):
            with self.assertRaises(APIError) as error:
                self.call("matches/complete", self.report(), server_key=key)
            self.assertEqual(error.exception.status, 403)

    def test_purchase_uses_server_price_and_double_click_is_once(self):
        body = {"itemId": "hat_ember", "price": -10000, "coins": 999999}
        with ThreadPoolExecutor(max_workers=8) as pool:
            list(pool.map(lambda _: self.call("purchase", body), range(8)))
        self.assertEqual(self.call("profile")["coins"], 0)
        self.assertEqual(self.call("profile")["owned"], ["hat_ember"])
        with self.assertRaises(APIError):
            self.call("purchase", {"itemId": "book_air"})

    def test_parallel_distinct_purchases_do_not_overdraw(self):
        report = self.report()
        self.settle(report)  # 260 coins: enough for either hat, not both.
        def buy(item):
            try:
                self.call("purchase", {"itemId": item})
                return True
            except APIError:
                return False
        with ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(buy, ["hat_ember", "hat_frost"]))
        self.assertEqual(sum(results), 1)
        self.assertGreaterEqual(self.call("profile")["coins"], 0)

    def test_paid_books_and_equip_enforcement(self):
        self.assertEqual(CATALOG["book_air"]["price"], 1000)
        self.assertEqual(CATALOG["book_ice"]["price"], 1500)
        for category in ("hat", "staff", "body"):
            with self.assertRaises(APIError):
                self.call("equip", {"category": category, "itemId": category + "_astral"})
        self.call("purchase", {"itemId": "hat_ember"})
        self.assertEqual(self.call("equip", {"category": "hat", "itemId": "hat_ember"})["hat"], "hat_ember")
        with self.assertRaises(APIError):
            self.call("equip", {"category": "body", "itemId": "hat_ember"})
        self.assertEqual(self.call("equip", {"category": "hat", "itemId": ""})["hat"], "")

    def test_colour_prices_payment_and_room_entitlement(self):
        for colour in ("red", "blue", "green"):
            self.assertEqual(CATALOG["color_" + colour]["price"], 0)
        for colour in ("yellow", "cyan"):
            self.assertEqual(CATALOG["color_" + colour]["price"], 100)
        for colour in ("magenta", "purple", "lime", "orange", "white"):
            self.assertEqual(CATALOG["color_" + colour]["price"], 200)
        with self.assertRaises(APIError):
            self.call("purchase", {"itemId": "color_purple", "price": 0})
        profile = self.call("purchase", {"itemId": "color_yellow", "price": 0})
        self.assertEqual(profile["coins"], 0)
        self.assertEqual(self.call("purchase", {"itemId": "color_yellow"})["coins"], 0)
        self.assertEqual(self.call("account")["owned"], ["color_yellow"])
        nonce = uuid.uuid4().hex
        ticket = self.call("tickets", {"nonce": nonce})["ticket"]
        self.assertEqual(self.call("tickets/redeem", {"ticket": ticket, "nonce": nonce})["owned"], ["color_yellow"])
        self.settle(self.report(seconds=1800))
        before = self.call("profile")["coins"]
        profile = self.call("purchase", {"itemId": "color_purple"})
        self.assertEqual(profile["coins"], before - 200)
        restarted = Economy(self.service.database, self.key)
        self.assertIn("color_purple", restarted.dispatch("/v1/profile", {}, self.tokens[0])["owned"])

    def test_both_books_unlock_only_after_payment(self):
        for _ in range(4):
            report = self.report(seconds=1800)
            report["players"][0]["kills"] = 33
            self.settle(report)
        before = self.call("profile")["coins"]
        self.call("purchase", {"itemId": "book_air"})
        profile = self.call("purchase", {"itemId": "book_ice"})
        self.assertEqual(profile["coins"], before - 2500)
        self.assertIn("book_air", profile["owned"])
        self.assertIn("book_ice", profile["owned"])
        nonce = uuid.uuid4().hex
        ticket = self.call("tickets", {"nonce": nonce})["ticket"]
        verified = self.call("tickets/redeem", {"ticket": ticket, "nonce": nonce})
        self.assertEqual(verified["owned"], profile["owned"])

    def test_ticket_is_single_use_scoped_and_returns_no_wallet(self):
        nonce = uuid.uuid4().hex
        ticket = self.call("tickets", {"nonce": nonce})["ticket"]
        with self.assertRaises(APIError):
            self.call("tickets/redeem", {"ticket": ticket, "nonce": uuid.uuid4().hex})
        verified = self.call("tickets/redeem", {"ticket": ticket, "nonce": nonce})
        self.assertEqual(verified["playerId"], self.players[0])
        self.assertNotIn("coins", verified)
        self.assertNotIn("credential", verified)
        with self.assertRaises(APIError):
            self.call("tickets/redeem", {"ticket": ticket, "nonce": nonce})

    def test_invalid_result_rolls_back_all_players(self):
        for change in ("unknown", "negative", "duration", "duplicate", "solo"):
            report = self.report()
            if change == "unknown": report["players"][1]["playerId"] = "missing"
            if change == "negative": report["players"][1]["kills"] = -1
            if change == "duration": report["players"][1]["seconds"] = 9999
            if change == "duplicate": report["players"][1] = report["players"][0].copy()
            if change == "solo": report["playerCount"] = 1
            with self.assertRaises(APIError): self.settle(report)
            self.assertEqual(self.call("profile")["coins"], 100)

    def test_late_join_time_and_peak_count(self):
        report = self.report(9)
        report["playerCount"] = 5
        report["players"][1]["seconds"] = 59
        self.settle(report)
        self.assertEqual(self.call("profile")["lastReward"], 160)
        self.assertEqual(self.call("profile", token=self.tokens[1])["lastReward"], 90)

    def test_no_arbitrary_write_endpoint(self):
        for path in ("coins", "grant", "balance", "inventory"):
            with self.assertRaises(APIError): self.call(path, {"coins": 1000000})
        self.assertEqual(self.call("profile")["coins"], 100)

    def test_disconnection_forfeits_reward_without_promoting_runner_up(self):
        report = self.report()
        report["players"][0]["completed"] = False
        self.settle(report)
        self.assertEqual(self.call("profile")["coins"], 100)
        self.assertEqual(self.call("profile", token=self.tokens[1])["lastReward"], 100)

    def test_http_layer_rejects_oversize_and_wrong_methods(self):
        for method, size, expected in (("GET", 0, "405"), ("POST", 70000, "413")):
            status = []
            self.service({"REQUEST_METHOD": method, "CONTENT_LENGTH": str(size), "wsgi.input": io.BytesIO(b"{}")}, lambda s, h: status.append(s))
            self.assertTrue(status[0].startswith(expected))


if __name__ == "__main__":
    unittest.main()
