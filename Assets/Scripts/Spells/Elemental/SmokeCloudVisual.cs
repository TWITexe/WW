using Mirror;
using UnityEngine;

// создаёт плотную поднимающуюся завесу и плавно меняет прозрачность по времени серверной области.
public class SmokeCloudVisual : MonoBehaviour
{
    [SerializeField] ParticleSystem particles;
    [SerializeField, Min(.01f)] float fadeIn = 1f;
    [SerializeField, Min(.01f)] float fadeOut = 2f;
    [SerializeField, Min(1)] float emissionRate = 140f;
    [SerializeField, Min(.1f)] float minimumSize = 3.5f;
    [SerializeField, Min(.1f)] float maximumSize = 4.5f;
    [SerializeField, Min(0)] float riseSpeed = .9f;

    ElementalEffect effect;
    ArcSmokeProjectile flight;
    ParticleSystemRenderer particleRenderer;
    MaterialPropertyBlock properties;
    bool started;
    float appearedAt;
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int ColorProperty = Shader.PropertyToID("_Color");

    void Awake()
    {
        effect = GetComponent<ElementalEffect>();
        flight = GetComponent<ArcSmokeProjectile>();
        if (particles == null || effect == null || effect.definition == null) return;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (NetworkServer.active && !NetworkClient.active)
        {
            particles.gameObject.SetActive(false);
            enabled = false;
            return;
        }

        // заполняем объём крупными частицами, оставляя исходные текстуру и рисунок Smoke ground.
        var main = particles.main;
        main.playOnAwake = false;
        main.prewarm = false;
        main.loop = true;
        main.duration = effect.definition.duration;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3.2f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(minimumSize * 2, maximumSize * 2);
        main.startSpeed = 0;
        main.gravityModifier = 0;
        main.maxParticles = 700;
        main.startColor = new Color(.38f, .4f, .42f, 1f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // частицы рождаются на площади завесы и поднимаются выше головы персонажа.
        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.rotation = new Vector3(90, 0, 0);
        shape.radius = effect.definition.radius;
        shape.radiusThickness = 1;
        var velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = 0;
        velocity.y = riseSpeed;
        velocity.z = 0;
        // исходный эффект содержит дополнительные силы; обнуляем их, чтобы дым поднимался именно вверх.
        velocity.orbitalX = velocity.orbitalY = velocity.orbitalZ = 0;
        velocity.radial = 0;
        var force = particles.forceOverLifetime; force.enabled = false;
        var noise = particles.noise; noise.enabled = false;
        var inherit = particles.inheritVelocity; inherit.enabled = false;

        var emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0;
        emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());
        var lifetimeColor = particles.colorOverLifetime;
        lifetimeColor.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .12f),
                new GradientAlphaKey(1, .7f), new GradientAlphaKey(0, 1) });
        lifetimeColor.color = gradient;

        particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
        properties = new MaterialPropertyBlock();
        SetOpacity(0);
    }

    void Update()
    {
        if (effect == null || particles == null || particleRenderer == null || !effect.isClient) return;
        if (flight != null && flight.Flying) return;
        if (!started)
        {
            appearedAt = Time.time;
            particles.Play(true);
            started = true;
        }

        // общий срок берём у сервера; поздно подключившийся клиент тоже получает мягкое появление.
        float age = Mathf.Max(0, (float)(NetworkTime.time - effect.bornAt));
        float remaining = Mathf.Max(0, effect.definition.duration - age);
        float opening = Mathf.SmoothStep(0, 1, Mathf.Min(age, Time.time - appearedAt) / fadeIn);
        float closing = Mathf.SmoothStep(0, 1, remaining / fadeOut);
        float opacity = opening * closing;
        var emission = particles.emission;
        emission.rateOverTime = remaining > fadeOut ? emissionRate * opening : 0;
        SetOpacity(opacity);
    }

    // меняем прозрачность всех живых частиц без создания отдельного материала.
    void SetOpacity(float opacity)
    {
        if (particleRenderer == null || properties == null) return;
        particleRenderer.GetPropertyBlock(properties);
        var color = new Color(1, 1, 1, opacity);
        properties.SetColor(BaseColor, color);
        properties.SetColor(ColorProperty, color);
        particleRenderer.SetPropertyBlock(properties);
    }
}
