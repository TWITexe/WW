using TMPro;
using UnityEngine;

public class UltimateBookEntry : MonoBehaviour
{
    public UltimateCatalog catalog;
    public TMP_Text title, description;
    public UltimateIconGraphic icon;
    private int previousMask = -1;
    public void Refresh(ElementLoadout loadout)
    {
        int mask = UltimateCatalog.Mask(loadout);
        if (mask == previousMask) return;
        previousMask = mask;
        var definition = catalog != null ? catalog.For(loadout) : null;
        if (definition == null) return;
        title.text = definition.title + " · УЛЬТИМЕЙТ";
        description.text = definition.description + "\nF — при 100% · +1% за 30 урона · +1% каждые 3 с. Смерть сохраняет заряд и накопление.";
        icon.kind = definition.kind; icon.color = definition.color; icon.SetVerticesDirty();
    }
}
