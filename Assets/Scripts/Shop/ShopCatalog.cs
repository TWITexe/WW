using System;
using System.Linq;
using UnityEngine;

[Serializable]
public class ShopItem
{
    public string id, name, category;
    public int price, style;
}

[Serializable]
public class ShopProfile
{
    public string playerId, hat, staff, body, lastMatch;
    public int coins, lastReward;
    public string[] owned = Array.Empty<string>();
    public bool Owns(string id) => string.IsNullOrEmpty(id) || (owned != null && Array.IndexOf(owned, id) >= 0);
    public string Equipped(string category) => category == "hat" ? hat : category == "staff" ? staff : body;
}

public static class ShopCatalog
{
    [Serializable] class CatalogData { public ShopItem[] items; }
    static ShopItem[] items;
    public static ShopItem[] Items => items ??= JsonUtility.FromJson<CatalogData>(Resources.Load<TextAsset>("ShopCatalog").text).items;
    public static ShopItem Find(string id) => Items.FirstOrDefault(x => x.id == id);
    public static bool IsCosmetic(ShopItem item) => item.category == "hat" || item.category == "staff" || item.category == "body";
    public static string ColorId(PlayerColorId color) => color == PlayerColorId.None ? "" : "color_" + color.ToString().ToLowerInvariant();
    public static bool Allows(ShopProfile profile, PlayerColorId color) => Enum.IsDefined(typeof(PlayerColorId), color) &&
        (color == PlayerColorId.Red || color == PlayerColorId.Blue || color == PlayerColorId.Green ||
         (color != PlayerColorId.None && profile != null && profile.Owns(ColorId(color))));
    public static string BookId(MagicElement element) => element == MagicElement.Air ? "book_air" : element == MagicElement.Ice ? "book_ice" : "";
    public static bool Allows(ShopProfile profile, MagicElement element) => Enum.IsDefined(typeof(MagicElement), element) &&
        (string.IsNullOrEmpty(BookId(element)) || (profile != null && profile.Owns(BookId(element))));
    public static bool Allows(ShopProfile profile, ElementLoadout loadout) => loadout.IsValid &&
        Allows(profile, loadout.q) && Allows(profile, loadout.e) && Allows(profile, loadout.r);
    public static int Reward(int kills, int seconds, int players, bool winner) =>
        players < 2 ? 0 : Mathf.Max(0, kills) * (10 + (winner ? players : 0)) + Mathf.Max(0, seconds) / 60 * 5;
}
