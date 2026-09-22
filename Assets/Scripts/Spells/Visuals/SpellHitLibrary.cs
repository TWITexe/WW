using Mirror;
using UnityEngine;

// тип попадания задаётся в ассете заклинания, независимо от цвета его частиц.
public enum SpellHitKind { Holy, Fire, Snow, Stone }

// общие префабы загружаются один раз; каждый удар создаёт только локальную графику.
public class SpellHitLibrary : ScriptableObject
{
    public GameObject[] prefabs;
    public float[] lifetimes;
    // увеличиваем только графику попаданий, сохраняя радиус урона и количество частиц.
    private const float ImpactScaleMultiplier = 6f;
    private static SpellHitLibrary cached;

    // запускаем одноразовый эффект и удаляем его после исчезновения последней частицы.
    public static void Play(SpellHitKind kind, Vector3 position, Color tint, float radius, bool area = false)
    {
        if (!NetworkClient.active && NetworkServer.active) return;
        SpellSurfaceMark.Place(position, kind, radius);
        if (cached == null) cached = Resources.Load<SpellHitLibrary>("SpellHitLibrary");
        int index = (int)kind;
        if (cached == null || index >= cached.prefabs.Length || cached.prefabs[index] == null) return;
        GameObject instance = Instantiate(cached.prefabs[index], position, Quaternion.identity);
        if (area) ElementalVisual.EnlargeAreaParticles(instance);
        instance.transform.localScale *= Mathf.Clamp(radius, .6f, 3.5f) * ImpactScaleMultiplier;
        // магическая вспышка принимает оттенок воды или воздуха; снег, камни и пламя сохраняют свои цвета.
        if (kind == SpellHitKind.Holy)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", Color.Lerp(tint, Color.white, .25f));
            foreach (var renderer in instance.GetComponentsInChildren<ParticleSystemRenderer>())
                renderer.SetPropertyBlock(block);
        }
        Destroy(instance, cached.lifetimes[index]);
    }
}
