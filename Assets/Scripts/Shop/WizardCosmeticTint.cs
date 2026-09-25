using UnityEngine;

// Shared by equipped models and shop previews. Never edits shared materials.
public static class WizardCosmeticTint
{
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    public static Color Shade(Color color, float brightness) =>
        new Color(color.r * brightness, color.g * brightness, color.b * brightness, color.a);

    public static void Apply(GameObject model, Color color)
    {
        var block = new MaterialPropertyBlock();
        bool staff = model.name.StartsWith("staff_", System.StringComparison.Ordinal);
        bool hat = model.name.StartsWith("hat_", System.StringComparison.Ordinal);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            Color tint;
            switch (renderer.name)
            {
                case "Robe": case "Brim": case "Crown": case "Crown base":
                    tint = color; break;
                case "Core":
                    if (!staff) continue;
                    tint = color; break;
                case "TeamTrim":
                    if (hat || staff) continue;
                    tint = Color.Lerp(color, Color.white, .18f); break;
                case "Front panel -1": case "Front panel 1":
                    tint = Shade(color, .4f); break;
                default:
                    continue; // Metal, gems, clasps and piping retain their accent colours.
            }
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColor, tint);
            if (staff && renderer.name == "Core") block.SetColor("_EmissionColor", color * .4f);
            renderer.SetPropertyBlock(block);
            block.Clear();
        }
    }
}
