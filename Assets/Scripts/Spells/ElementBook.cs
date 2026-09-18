using System.Collections.Generic;
using UnityEngine;

// представляет физическую книгу стихии: область нажатия и окраску выбранного состояния.
public class ElementBook : MonoBehaviour
{
    [SerializeField] private MagicElement element;
    [SerializeField] private BoxCollider hitArea;
    [SerializeField] private Renderer[] bookRenderers;
    [SerializeField, Range(0.05f, 0.8f)] private float inactiveBrightness = 0.16f;

    private readonly List<MaterialState> materials = new List<MaterialState>();
    private bool appearanceCaptured;
    private bool active;
    private bool hovered;
    private bool appearanceApplied;

    public MagicElement Element => element;
    public BoxCollider HitArea => hitArea;

    // сохраняет исходные свойства отдельного материала, чтобы не менять общий ассет книги.
    private class MaterialState
    {
        public Renderer renderer;
        public int index;
        public MaterialPropertyBlock original;
        public MaterialPropertyBlock working;
        public Color baseColor;
        public Color emission;
        public bool hasBaseColor;
        public bool hasEmission;
    }

    // запоминаем исходную окраску до первого затемнения.
    private void Awake()
    {
        CaptureAppearance();
    }

    // сохраняем свойства каждого материала, включая уже заданные индивидуальные цвета.
    private void CaptureAppearance()
    {
        if (appearanceCaptured) return;
        appearanceCaptured = true;
        if (bookRenderers == null) return;

        foreach (Renderer renderer in bookRenderers)
        {
            if (renderer == null) continue;
            var globalBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(globalBlock);
            Material[] sourceMaterials = renderer.sharedMaterials;

            for (int index = 0; index < sourceMaterials.Length; index++)
            {
                Material material = sourceMaterials[index];
                if (material == null) continue;
                var original = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(original, index);
                var working = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(working, index);
                var state = new MaterialState
                {
                    renderer = renderer,
                    index = index,
                    original = original,
                    working = working,
                    hasBaseColor = material.HasProperty("_BaseColor"),
                    hasEmission = material.HasProperty("_EmissionColor")
                };
                if (state.hasBaseColor)
                    state.baseColor = ReadColor(material, original, globalBlock, "_BaseColor");
                if (state.hasEmission)
                    state.emission = ReadColor(material, original, globalBlock, "_EmissionColor");
                materials.Add(state);
            }
        }
    }

    // индивидуальные свойства материала имеют приоритет над общими свойствами рендера и ассетом.
    private static Color ReadColor(Material material, MaterialPropertyBlock local,
        MaterialPropertyBlock global, string property)
    {
        if (local.HasColor(property)) return local.GetColor(property);
        if (global.HasColor(property)) return global.GetColor(property);
        return material.GetColor(property);
    }

    // меняем яркость только при изменении выбора или наведения, не создавая новые материалы.
    public void SetAppearance(bool isActive, bool isHovered)
    {
        CaptureAppearance();
        if (appearanceApplied && active == isActive && hovered == isHovered) return;
        appearanceApplied = true;
        active = isActive;
        hovered = isHovered;
        float brightness = active ? 1f : inactiveBrightness;
        if (hovered) brightness = active ? 1.2f : Mathf.Max(0.38f, brightness);

        foreach (MaterialState state in materials)
        {
            if (state.renderer == null) continue;
            // повторно используем блок материала вместо выделения памяти при каждом наведении.
            MaterialPropertyBlock block = state.working;
            if (state.hasBaseColor)
            {
                Color tint = state.baseColor * brightness;
                tint.a = state.baseColor.a;
                block.SetColor("_BaseColor", tint);
            }
            if (state.hasEmission)
                block.SetColor("_EmissionColor", active ? state.emission : Color.black);
            state.renderer.SetPropertyBlock(block, state.index);
        }
    }

    // проверяем именно сохранённую область книги, не реагируя на декорации за ней.
    public bool Raycast(Ray ray, out RaycastHit hit)
    {
        hit = default;
        return hitArea != null && hitArea.enabled && hitArea.gameObject.activeInHierarchy &&
            hitArea.Raycast(ray, out hit, 30f);
    }

    // восстанавливаем исходные свойства при отключении меню или выходе из игрового режима.
    private void OnDisable()
    {
        RestoreAppearance();
    }

    // возвращаем исходное оформление после редакторского предпросмотра или отключения взаимодействия.
    public void RestoreAppearance()
    {
        appearanceApplied = false;
        foreach (MaterialState state in materials)
            if (state.renderer != null)
                state.renderer.SetPropertyBlock(state.original.isEmpty ? null : state.original, state.index);
    }

#if UNITY_EDITOR
    // сохраняем ссылки на настоящую модель и её область нажатия при установке меню в сцену.
    public void Configure(MagicElement value, BoxCollider collider, Renderer[] renderers)
    {
        element = value;
        hitArea = collider;
        bookRenderers = renderers;
    }
#endif
}
