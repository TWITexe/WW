using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class LocalEconomyRegression
{
    static void Check(bool value, string message) { if (!value) throw new Exception("LOCAL_W: " + message); }
    static void Rejected(Action action, string message)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("LOCAL_W accepted " + message);
    }
    [MenuItem("Tools/Wizard War/Validate local W wallet")]
    public static void Run()
    {
        string folder = "Logs/Shop/LocalTests/" + Guid.NewGuid().ToString("N");
        string path = Path.Combine(folder, "wallet.dat");
        var wallet = new LocalEconomyStore(path, "regression-device");
        Check(wallet.Profile.coins == 100, "starter balance");
        string player = wallet.Profile.playerId;
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(player), "encrypted payload");
        Rejected(() => wallet.Purchase("book_air"), "insufficient funds");
        Rejected(() => wallet.Equip("hat", "hat_ember"), "unowned equipment");
        wallet.Purchase("color_yellow"); wallet.Purchase("color_yellow");
        Check(wallet.Profile.coins == 0 && wallet.Profile.Owns("color_yellow"), "100 W color, idempotent purchase");
        wallet = new LocalEconomyStore(path, "regression-device");
        Check(wallet.Profile.playerId == player && wallet.Profile.coins == 0, "reopen without starter reset");
        Check(wallet.Profile.Owns("color_yellow"), "persistent purchase");
        string match = Guid.NewGuid().ToString("N");
        wallet.Award(match, ShopCatalog.Reward(10,60,5,true));
        wallet.Award(match, 155);
        Check(wallet.Profile.coins == 155, "five-player payout exactly once");
        wallet = new LocalEconomyStore(path, "regression-device");
        wallet.Award(match, 155);
        Check(wallet.Profile.coins == 155, "replayed reward after restart");
        string nextMatch = Guid.NewGuid().ToString("N"); wallet.Award(nextMatch, ShopCatalog.Reward(10,60,9,true));
        Check(wallet.Profile.coins == 350, "nine-player payout");
        wallet.Purchase("color_purple");
        Check(wallet.Profile.coins == 150, "200 W lower row color");
        wallet.Purchase("hat_ember"); wallet.Equip("hat","hat_ember");
        wallet = new LocalEconomyStore(path, "regression-device");
        Check(wallet.Profile.coins == 50 && wallet.Profile.hat == "hat_ember", "persistent equipped skin");
        Rejected(() => wallet.Equip("body", "hat_ember"), "wrong equipment category");
        wallet.Award(Guid.NewGuid().ToString("N"), 2500);
        wallet.Purchase("book_air"); wallet.Purchase("book_ice");
        Check(wallet.Profile.coins == 50 && ShopCatalog.Allows(wallet.Profile, MagicElement.Air) && ShopCatalog.Allows(wallet.Profile, MagicElement.Ice), "both paid books");
        Check(ShopCatalog.Allows(null,PlayerColorId.Red)&&ShopCatalog.Allows(null,PlayerColorId.Blue)&&ShopCatalog.Allows(null,PlayerColorId.Green),"free RGB");
        Check(!ShopCatalog.Allows(null,PlayerColorId.Yellow)&&!ShopCatalog.Allows(null,PlayerColorId.White),"paid colors locked");
        byte[] damaged = File.ReadAllBytes(path); damaged[24] ^= 1; File.WriteAllBytes(path, damaged);
        var recovered = new LocalEconomyStore(path, "regression-device");
        Check(recovered.RecoveredBackup && recovered.Profile.Owns("book_air"), "recover authenticated backup");
        bool rejected = false;
        try { _ = new LocalEconomyStore(path, "wrong-device"); }
        catch (CryptographicException) { rejected = true; }
        Check(rejected, "wrong device rejected");
        // Simulate a failed disk replacement: no in-memory free purchase may escape.
        string failure = Path.Combine(folder, "failed.dat");
        var blocked = new LocalEconomyStore(failure, "regression-device");
        Directory.CreateDirectory(failure + ".tmp");
        try { blocked.Purchase("hat_ember"); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        Check(blocked.Profile.coins == 100 && !blocked.Profile.Owns("hat_ember"), "failed write is atomic");
        File.WriteAllText("Logs/Shop/local-validation.txt", "PASS: encrypted wallet, initial 100 W, atomic purchases, 100/200 W colors, free RGB, equipment, both books, 5/9-player rewards, replay after restart, tamper backup, wrong key, failed write.\n" + DateTime.UtcNow.ToString("O"));
        Debug.Log("LOCAL_W_REGRESSION_PASS");
    }
}
