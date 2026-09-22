using UnityEngine;

// хранит префаб и скорость огненного шара.
[CreateAssetMenu(menuName = "Spells/Fireball")]
public class FireBall : Spell
{
    [SerializeField] private GameObject fireballPrefab;
    [SerializeField, Min(0.1f)] private float speed = 24f;
    public GameObject PreviewPrefab => fireballPrefab;
    public float ProjectileSpeed => speed;
    // просим сервер создать огненный шар с параметрами этого ассета.
    public override bool ActivateServer(PlayerNetworkCaster caster, Vector3 direction)
        => caster.SpawnProjectile(fireballPrefab, speed, direction);
}
