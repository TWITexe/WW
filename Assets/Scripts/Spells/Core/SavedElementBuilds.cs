using System;
using System.Collections.Generic;
using UnityEngine;

// Именованные наборы хранят копию всех слотов, независимо от текущего выбора игрока.
public sealed class SavedElementBuilds
{
    public const string StorageKey = "Elements.SavedBuilds.v1";
    public const int NameLimit = 32;
    public const int Capacity = 60;

    [Serializable]
    public sealed class Build
    {
        [SerializeField] private string name;
        [SerializeField] private ElementLoadout loadout;
        public string Name => name;
        public ElementLoadout Loadout => loadout;
        public Build(string name, ElementLoadout loadout) { this.name = name; this.loadout = loadout; }
    }

    [Serializable]
    private sealed class Data
    {
        public List<Build> builds = new List<Build>();
    }

    private readonly string key;
    private readonly List<Build> builds = new List<Build>();
    public IReadOnlyList<Build> Builds => builds.AsReadOnly();

    public SavedElementBuilds(string storageKey = StorageKey)
    {
        key = storageKey;
        try
        {
            var data = JsonUtility.FromJson<Data>(PlayerPrefs.GetString(key, ""));
            if (data?.builds == null) return;
            foreach (Build build in data.builds)
            {
                if (build == null || !build.Loadout.IsValid || !ValidName(build.Name)) continue;
                string name = build.Name.Trim();
                if (Find(name) >= 0) continue;
                builds.Add(new Build(name, build.Loadout));
                if (builds.Count == Capacity) break;
            }
        }
        catch (ArgumentException) { Debug.LogWarning("Не удалось прочитать сохранённые билды стихий."); }
    }

    public int Find(string name) => builds.FindIndex(build =>
        string.Equals(build.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool ValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameLimit) return false;
        foreach (char character in name.Trim()) if (char.IsControl(character)) return false;
        return true;
    }

    // Замена одноимённого билда разрешена только после явного подтверждения интерфейсом.
    public bool Save(string name, ElementLoadout loadout, bool replace = false)
    {
        if (!ValidName(name) || !loadout.IsValid) return false;
        name = name.Trim();
        int index = Find(name);
        if (index >= 0 && !replace || index < 0 && builds.Count >= Capacity) return false;
        var build = new Build(name, loadout);
        if (index >= 0) builds[index] = build;
        else builds.Add(build);
        Persist();
        return true;
    }

    public bool Delete(string name)
    {
        int index = Find(name);
        if (index < 0) return false;
        builds.RemoveAt(index);
        Persist();
        return true;
    }

    private void Persist()
    {
        PlayerPrefs.SetString(key, JsonUtility.ToJson(new Data { builds = builds }));
        PlayerPrefs.Save();
    }
}
