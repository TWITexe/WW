using Mirror;
using UnityEngine;

[CreateAssetMenu(menuName = "Spells/Fireball")]
public class FireBall : Spell
{
    public override KeyCode[] Combo => new KeyCode[] { KeyCode.E, KeyCode.R, KeyCode.T};

    [SerializeField] private GameObject fireballPrefab;
    [SerializeField] float speed = 10f;

    public override void Activate(PlayerNetworkCaster caster)
    {

        caster.CastFireball(speed);
    }
}
