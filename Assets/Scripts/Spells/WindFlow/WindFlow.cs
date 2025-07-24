using UnityEngine;
[CreateAssetMenu(menuName = "Spells/WindFlow")]
public class WindFlow : Spell
{
    public override KeyCode[] Combo => new KeyCode[] { KeyCode.R, KeyCode.E, KeyCode.Q };
    [SerializeField] private GameObject windFLowPrefab;
    [SerializeField] float speed = 15f;
    public override void Activate(PlayerNetworkCaster caster)
    {
        caster.CmdCastWindFlow(speed);
    }
}
