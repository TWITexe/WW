using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

// Original colliders remain unchanged: cosmetics never change the damage hitboxes.
public class WizardCosmetics : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnSkinChanged))] string hatId = "";
    [SyncVar(hook = nameof(OnSkinChanged))] string staffId = "";
    [SyncVar(hook = nameof(OnSkinChanged))] string bodyId = "";
    readonly Dictionary<string, GameObject> visuals = new();
    readonly Dictionary<string, Renderer[]> originals = new();
    readonly Dictionary<string, string> applied = new();
    WizardAppearance wizard;
    void Awake() { wizard = GetComponent<WizardAppearance>(); }
    public override void OnStartServer()
    {
        var profile = NetManager.Room?.RoomAuth.ProfileFor(connectionToClient);
        hatId = Owned(profile, "hat"); staffId = Owned(profile, "staff"); bodyId = Owned(profile, "body");
        Apply();
    }
    static string Owned(ShopProfile profile, string category)
    {
        string id = profile?.Equipped(category);
        return !string.IsNullOrEmpty(id) && profile.Owns(id) && ShopCatalog.Find(id)?.category == category ? id : "";
    }
    public override void OnStartClient() { Apply(); }
    void OnSkinChanged(string previous, string next) { if (wizard != null) Apply(); }
    void Apply()
    {
        Replace("Hat", hatId); Replace("Staff", staffId); Replace("Body", bodyId);
        Tint(GetComponent<PlayerColor>()?.DisplayColor ?? Color.white);
    }
    void Replace(string part, string id)
    {
        id ??= "";
        if (applied.TryGetValue(part, out var existing) && existing == id) return;
        Transform anchor = wizard?.pieces?.FirstOrDefault(x => x != null && x.name == part);
        if (anchor == null) return;
        if (!originals.ContainsKey(part)) originals[part] = anchor.GetComponentsInChildren<Renderer>(true);
        if (visuals.TryGetValue(part, out var old) && old != null) { old.SetActive(false); Destroy(old); }
        visuals.Remove(part); applied[part] = id;
        var prefab = string.IsNullOrEmpty(id) ? null : Resources.Load<GameObject>("Shop/" + id);
        foreach (var renderer in originals[part]) renderer.enabled = prefab == null;
        if (prefab == null) return;
        // The asset installer places each skin in the same local frame as the original part.
        var visual = Instantiate(prefab, anchor, false); visuals[part] = visual;
    }
    public void Tint(Color color)
    {
        foreach (var visual in visuals.Values)
            if (visual != null) WizardCosmeticTint.Apply(visual, color);
    }
}
