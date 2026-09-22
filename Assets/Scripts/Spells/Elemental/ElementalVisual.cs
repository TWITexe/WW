using UnityEngine;

// графика создаётся локально; урон и время действия области рассчитывает серверный ElementalEffect.
// создаёт локальные частицы, границы областей и спираль торнадо без расчёта урона.
public class ElementalVisual : MonoBehaviour
{
    public Material particleMaterial;
    public Material lineMaterial;
    public Color fallbackTint = new Color(1, .3f, .04f);
    public bool projectile;
    [Min(1)] public float projectileVisualScale = 1;
    public float tornadoHeight = 7;
    private ElementalSpell spell;
    private LineRenderer vortex;
    // подбираем визуальное оформление по типу эффекта и создаём дочерние графические объекты.
    private void Start()
    {
        if (Application.isBatchMode && SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var elemental = GetComponent<ElementalEffect>();
        spell = elemental != null ? elemental.definition : null;
        bool bolt = projectile || (spell != null && spell.mode == ElementalCastMode.Bolt);
        bool tornado = spell != null && spell.mode == ElementalCastMode.Tornado;
        Color tint = spell != null ? spell.tint : fallbackTint;
        if (bolt && projectileVisualScale > 1)
        {
            // размер ядра согласован с коллайдером в префабе; здесь создаём его локальную графику.
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!renderer.enabled || filter == null || filter.sharedMesh == null) continue;
                var core = new GameObject("Scaled projectile visual", typeof(MeshFilter), typeof(MeshRenderer));
                core.transform.SetParent(renderer.transform, false);
                core.transform.localScale = Vector3.one * projectileVisualScale;
                core.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                core.GetComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                renderer.enabled = false;
            }
        }
        if (!bolt && !tornado && spell != null)
        {
            var ring = new GameObject("Area boundary").AddComponent<LineRenderer>();
            ring.transform.SetParent(transform, false);
            ring.useWorldSpace = false; ring.loop = true; ring.positionCount = 96;
            ring.widthMultiplier = .065f; ring.sharedMaterial = lineMaterial;
            ring.startColor = ring.endColor = tint;
            for (int i = 0; i < 96; i++)
            {
                float angle = i * Mathf.PI * 2 / 96;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * spell.radius, -.18f, Mathf.Sin(angle) * spell.radius));
            }
        }
        var particles = new GameObject("Element particles").AddComponent<ParticleSystem>();
        particles.transform.SetParent(transform, false);
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.startLifetime = bolt ? .5f : 1.3f;
        main.startSpeed = tornado ? 5f : bolt ? .15f : .6f;
        main.startSize = bolt ? .28f * projectileVisualScale : .32f;
        main.startColor = new Color(tint.r, tint.g, tint.b, 1f);
        main.simulationSpace = bolt ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        main.maxParticles = 160;
        var emission = particles.emission; emission.rateOverTime = bolt ? 80 : 45;
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.radius = bolt ? .08f : spell.radius * .85f; shape.angle = bolt ? 0 : 15;
        particles.transform.localRotation = Quaternion.Euler(-90, 0, 0);
        particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = particleMaterial;
        particles.Play();
        if (bolt)
        {
            // сразу показываем частицы и непрерывный след, даже если граф ещё не успел их выпустить.
            particles.Emit(8);
            var trail = new GameObject("Projectile trail").AddComponent<TrailRenderer>();
            trail.gameObject.layer = 2;
            trail.transform.SetParent(transform, false);
            trail.sharedMaterial = lineMaterial;
            trail.time = .22f;
            trail.minVertexDistance = .06f;
            trail.startWidth = .28f * transform.lossyScale.x;
            trail.endWidth = .025f;
            trail.startColor = Color.Lerp(tint, Color.white, .4f);
            trail.endColor = new Color(tint.r, tint.g, tint.b, 0);
            trail.numCapVertices = 3;
        }
        if (tornado)
        {
            vortex = new GameObject("Fire spiral").AddComponent<LineRenderer>();
            vortex.transform.SetParent(transform, false);
            vortex.useWorldSpace = false;
            vortex.positionCount = 90;
            vortex.widthMultiplier = 0.15f;
            vortex.sharedMaterial = lineMaterial;
        }
    }
    // перестраиваем вращающуюся спираль торнадо по высоте и радиусу заклинания.
    private void Update()
    {
        if (vortex == null) return;
        for (int i = 0; i < vortex.positionCount; i++)
        {
            float t = i / (float)(vortex.positionCount - 1);
            float angle = t * Mathf.PI * 8 + Time.time * 7;
            float radius = Mathf.Lerp(0.2f, spell.radius * 0.8f, t);
            vortex.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, t * tornadoHeight, Mathf.Sin(angle) * radius));
        }
    }
    // увеличиваем только частицы импортированной области, сохраняя радиус действия и геометрию круга.
    public static void EnlargeAreaParticles(GameObject root)
    {
        if (root == null) return;
        foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = particles.main;
            if (main.startSize3D)
            {
                main.startSizeXMultiplier *= 2;
                main.startSizeYMultiplier *= 2;
                main.startSizeZMultiplier *= 2;
            }
            else main.startSizeMultiplier *= 2;
        }
    }
    // создаём короткую декоративную вспышку без коллайдера.
    public static void Burst(Vector3 position, Color tint, float radius)
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Spell impact";
        sphere.transform.position = position;
        Destroy(sphere.GetComponent<Collider>());
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.color = tint;
        sphere.GetComponent<Renderer>().sharedMaterial = material;
        var flash = sphere.AddComponent<SpellImpactFlash>();
        flash.radius = radius;
        flash.ownedMaterial = material;
    }
}

// уменьшает сферу вспышки и освобождает созданный специально для неё материал.
public class SpellImpactFlash : MonoBehaviour
{
    public float radius = 1;
    public Material ownedMaterial;
    private float age;
    // сжимаем вспышку до нуля и удаляем её через четверть секунды.
    private void Update()
    {
        age += Time.deltaTime;
        transform.localScale = Vector3.one * Mathf.Lerp(radius * 0.7f, 0, age / 0.25f);
        if (age >= 0.25f) Destroy(gameObject);
    }
    // освобождаем принадлежащий вспышке материал вместе с объектом.
    private void OnDestroy() { if (ownedMaterial != null) Destroy(ownedMaterial); }
}
