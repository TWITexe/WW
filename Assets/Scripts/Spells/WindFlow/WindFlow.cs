using UnityEngine;

// хранит префаб и скорость снаряда воздушного толчка.
[CreateAssetMenu(menuName = "Spells/WindFlow")]
public class WindFlow : Spell
{
    [SerializeField] private GameObject windFLowPrefab;
    [SerializeField, Min(0.1f)] private float speed = 20f;
    public GameObject PreviewPrefab => windFLowPrefab;
    public float ProjectileSpeed => speed;
    // просим сервер создать воздушный снаряд с параметрами этого ассета.
    public override bool ActivateServer(PlayerNetworkCaster caster, Vector3 direction)
        => caster.SpawnProjectile(windFLowPrefab, speed, direction);
}
