using Mirror;
using UnityEngine;
using UnityEngine.VFX;

// Cosmetic only: the parent network prefab owns movement, collision and lifetime.
public class SpellVfx : MonoBehaviour
{
    public VisualEffect effect;
    public Color tint = Color.white;
    [Min(.05f)] public float size = .65f;
    [Min(.05f)] public float radius = 1;
    public bool area;
    public bool impact;
    private void Start()
    {
        if (!NetworkClient.active && NetworkServer.active) { if (effect != null) effect.enabled = false; return; }
        Configure();
        if (effect != null)
        {
            effect.Play();
            // Imported fireballs emit only one particle/second. A fast projectile
            // can hit a wall before its first particle; seed its local simulation.
            if (!area) effect.Simulate(.05f, 30);
        }
    }
    public void Configure()
    {
        if (effect == null) return;
        if (effect.HasVector3("FireballMovingSpeed")) effect.SetVector3("FireballMovingSpeed", Vector3.zero);
        if (effect.HasFloat("FireballSize")) effect.SetFloat("FireballSize", size);
        if (effect.HasFloat("Radius")) effect.SetFloat("Radius", radius);
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.Lerp(tint, Color.white, .5f), 0), new GradientColorKey(tint, .35f), new GradientColorKey(tint * .35f, 1) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
        if (effect.HasGradient("FIreballColorGradient")) effect.SetGradient("FIreballColorGradient", gradient);
        if (effect.HasGradient("FireballTrailsGradient")) effect.SetGradient("FireballTrailsGradient", gradient);
        foreach (string property in new[] { "Color", "Color2", "Color3", "Color4" })
        {
            Color color = property == "Color" ? Color.Lerp(tint, Color.white, .3f) : tint;
            if (effect.HasVector4(property)) effect.SetVector4(property, color * 2);
            else if (effect.HasVector3(property)) effect.SetVector3(property, new Vector3(color.r, color.g, color.b) * 2);
        }
    }
    public static void Burst(Vector3 position, Color color, float radius, float visibleDuration = .5f)
    {
        if (!NetworkClient.active && NetworkServer.active) return;
        var prefab = Resources.Load<GameObject>("SpellVfxImpact");
        if (prefab == null) return;
        var go = Instantiate(prefab, position, Quaternion.identity);
        var visual = go.GetComponent<SpellVfx>();
        visual.tint = color; visual.radius = Mathf.Max(.3f, radius);
        visual.Configure();
        visual.Invoke(nameof(StopEmission), visibleDuration);
        Destroy(go, visibleDuration + 2f);
    }
    private void StopEmission() { if (effect != null) effect.Stop(); }
    public static void Impact(Vector3 position, Color color, float radius)
    {
        if (!NetworkClient.active && NetworkServer.active) return;
        var prefab = Resources.Load<GameObject>("SpellProjectileImpact");
        if (prefab == null) return;
        var particles = Instantiate(prefab, position, Quaternion.identity).GetComponent<ParticleSystem>();
        var main = particles.main;
        main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, .6f));
        float scale = Mathf.Clamp(radius, .6f, 1.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f * scale, 5f * scale);
        particles.Play();
        particles.Emit(120);
        Destroy(particles.gameObject, 1.5f);
    }
}

