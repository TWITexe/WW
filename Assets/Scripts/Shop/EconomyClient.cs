using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Local encrypted wallet. Mirror hosts report results; no external service is used.
public class EconomyClient : MonoBehaviour
{
    public static EconomyClient Instance { get; private set; }
    public ShopProfile Profile => store?.Profile;
    public bool Busy { get; private set; }
    public bool Connected { get; private set; }
    public string Status { get; private set; } = "Загрузка кошелька W…";
    public event Action Changed;
    LocalEconomyStore store;
    readonly Dictionary<string, EconomyMatchMessage> pendingRewards = new();
    public string SavePath { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return;
        var root = new GameObject("Local W wallet");
        DontDestroyOnLoad(root); root.AddComponent<EconomyClient>();
    }
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        string directory = Application.persistentDataPath;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--shop-test");
        if (index >= 0 && index + 1 < args.Length) directory = args[index + 1];
#endif
        SavePath = Path.Combine(directory, "wallet-w.dat");
        Refresh();
    }
    public void Refresh()
    {
        if (Busy) return;
        Execute(() =>
        {
            if (store == null) store = new LocalEconomyStore(SavePath, SystemInfo.deviceUniqueIdentifier);
            foreach (var reward in new List<EconomyMatchMessage>(pendingRewards.Values))
            { store.Award(reward.matchId, reward.reward); pendingRewards.Remove(reward.matchId); }
        });
    }
    public void Purchase(string id, Action<bool> completed = null)
    {
        bool success = Execute(() => { RequireStore(); store.Purchase(id); });
        completed?.Invoke(success);
    }
    public void Equip(string category, string id) => Execute(() => { RequireStore(); store.Equip(category, id); });
    public void Award(EconomyMatchMessage result)
    {
        if (!result.eligible || !result.settled || Profile == null || result.playerId != Profile.playerId) return;
        if (Profile.lastMatch == result.matchId && !pendingRewards.ContainsKey(result.matchId)) return;
        pendingRewards[result.matchId] = result;
        Refresh();
    }
    void RequireStore() { if (store == null) throw new IOException("Wallet unavailable"); }
    bool Execute(Action operation)
    {
        if (Busy) return false;
        Busy = true;
        bool success = false;
        try { operation(); Connected = store != null; Status = "W и покупки сохранены на этом устройстве"; success = true; }
        catch (InvalidOperationException error) { Status = error.Message; }
        catch (Exception error)
        {
            Connected = false;
            Status = "Не удалось прочитать или сохранить кошелёк W. Проверьте доступ к папке сохранений.";
            Debug.LogWarning("Local W wallet: " + error.GetType().Name);
        }
        finally { Busy = false; Changed?.Invoke(); }
        return success;
    }
    void OnDestroy() { if (Instance == this) Instance = null; }
}

