using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// Protection against casual save editing, not authority over a modified game client.
public sealed class LocalEconomyStore
{
    [Serializable] class Save
    {
        public int version = 1;
        public ShopProfile profile;
        public string[] rewardedMatches = Array.Empty<string>();
    }
    readonly string path;
    readonly byte[] encryptionKey, authenticationKey;
    Save data;
    public ShopProfile Profile => data.profile;
    public bool RecoveredBackup { get; private set; }

    public LocalEconomyStore(string path, string device)
    {
        this.path = path;
        using var sha = SHA256.Create();
        encryptionKey = sha.ComputeHash(Encoding.UTF8.GetBytes("WizardWar/W/local-v1/encryption/" + device));
        authenticationKey = sha.ComputeHash(Encoding.UTF8.GetBytes("WizardWar/W/local-v1/integrity/" + device));
        if (File.Exists(path) || File.Exists(path + ".bak"))
        {
            try { data = Read(path); }
            catch (Exception error) when (error is IOException || error is CryptographicException || error is ArgumentException)
            {
                data = Read(path + ".bak"); RecoveredBackup = true;
                if (File.Exists(path)) File.Move(path, path + ".damaged-" + Guid.NewGuid().ToString("N"));
                Write(data);
            }
        }
        else
        {
            data = new Save { profile = new ShopProfile { playerId = Guid.NewGuid().ToString("N"), coins = 100 } };
            Write(data);
        }
    }
    public void Purchase(string id)
    {
        var item = ShopCatalog.Find(id);
        if (item == null) throw new InvalidOperationException("Предмет не найден");
        if (data.profile.Owns(id)) return;
        if (data.profile.coins < item.price) throw new InvalidOperationException("Недостаточно W");
        Commit(next =>
        {
            next.profile.coins -= item.price;
            next.profile.owned = next.profile.owned.Concat(new[] { id }).ToArray();
        });
    }
    public void Equip(string category, string id)
    {
        if (category != "hat" && category != "staff" && category != "body") throw new InvalidOperationException("Неизвестная категория");
        if (!string.IsNullOrEmpty(id) && (ShopCatalog.Find(id)?.category != category || !data.profile.Owns(id)))
            throw new InvalidOperationException("Сначала приобретите предмет");
        Commit(next =>
        {
            if (category == "hat") next.profile.hat = id;
            else if (category == "staff") next.profile.staff = id;
            else next.profile.body = id;
        });
    }
    public void Award(string matchId, int reward)
    {
        if (!Guid.TryParseExact(matchId, "N", out _) || reward < 0 || reward > 100000)
            throw new InvalidOperationException("Некорректный результат матча");
        if (Array.IndexOf(data.rewardedMatches, matchId) >= 0) return;
        Commit(next =>
        {
            next.profile.coins = checked(next.profile.coins + reward);
            next.profile.lastMatch = matchId; next.profile.lastReward = reward;
            next.rewardedMatches = next.rewardedMatches.Concat(new[] { matchId }).ToArray();
        });
    }
    void Commit(Action<Save> change)
    {
        var next = JsonUtility.FromJson<Save>(JsonUtility.ToJson(data));
        change(next); Write(next); data = next;
    }
    void Write(Save save)
    {
        byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(save));
        using var aes = Aes.Create(); aes.Key = encryptionKey; aes.GenerateIV();
        aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
        using var encryptor = aes.CreateEncryptor();
        byte[] ciphertext = encryptor.TransformFinalBlock(payload, 0, payload.Length);
        byte[] signed = new byte[4 + 16 + ciphertext.Length];
        Buffer.BlockCopy(Encoding.ASCII.GetBytes("WW01"), 0, signed, 0, 4);
        Buffer.BlockCopy(aes.IV, 0, signed, 4, 16); Buffer.BlockCopy(ciphertext, 0, signed, 20, ciphertext.Length);
        using var hmac = new HMACSHA256(authenticationKey); byte[] tag = hmac.ComputeHash(signed);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + ".tmp";
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { file.Write(signed, 0, signed.Length); file.Write(tag, 0, tag.Length); file.Flush(true); }
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
        else File.Move(temporary, path);
    }
    Save Read(string source)
    {
        byte[] bytes = File.ReadAllBytes(source);
        if (bytes.Length < 68 || Encoding.ASCII.GetString(bytes, 0, 4) != "WW01") throw new CryptographicException("Save header");
        int count = bytes.Length - 32;
        using var hmac = new HMACSHA256(authenticationKey); byte[] expected = hmac.ComputeHash(bytes, 0, count);
        int difference = 0; for (int i = 0; i < 32; i++) difference |= expected[i] ^ bytes[count + i];
        if (difference != 0) throw new CryptographicException("Save integrity");
        using var aes = Aes.Create(); aes.Key = encryptionKey;
        var iv = new byte[16]; Buffer.BlockCopy(bytes, 4, iv, 0, 16); aes.IV = iv;
        aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
        using var decryptor = aes.CreateDecryptor();
        var save = JsonUtility.FromJson<Save>(Encoding.UTF8.GetString(decryptor.TransformFinalBlock(bytes, 20, count - 20)));
        if (save == null || save.version != 1 || save.profile == null || save.profile.coins < 0 ||
            !Guid.TryParseExact(save.profile.playerId, "N", out _) || save.profile.owned == null || save.rewardedMatches == null)
            throw new CryptographicException("Save data");
        return save;
    }
}
