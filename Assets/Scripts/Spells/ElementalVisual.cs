using UnityEngine;

// Cosmetic effects are local; damage and area timing belong to ElementalEffect on the server.
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
            // Scale only graphics: network movement and hitboxes keep their size.
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
        main.startLifetime = bolt ? .35f : 1.3f;
        main.startSpeed = tornado ? 5f : bolt ? .15f : .6f;
        main.startSize = bolt ? .18f * projectileVisualScale : .16f;
        main.startColor = new Color(tint.r, tint.g, tint.b, .85f);
        main.simulationSpace = bolt ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        main.maxParticles = 160;
        var emission = particles.emission; emission.rateOverTime = 45;
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.radius = bolt ? .08f : spell.radius * .85f; shape.angle = bolt ? 0 : 15;
        particles.transform.localRotation = Quaternion.Euler(-90, 0, 0);
        particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = particleMaterial;
        particles.Play();
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

public class SpellImpactFlash : MonoBehaviour
{
    public float radius = 1;
    public Material ownedMaterial;
    private float age;
    private void Update()
    {
        age += Time.deltaTime;
        transform.localScale = Vector3.one * Mathf.Lerp(radius * 0.7f, 0, age / 0.25f);
        if (age >= 0.25f) Destroy(gameObject);
    }
    private void OnDestroy() { if (ownedMaterial != null) Destroy(ownedMaterial); }
}
