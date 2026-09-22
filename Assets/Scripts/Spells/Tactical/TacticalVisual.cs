using Mirror;
using UnityEngine;

// показывает готовность огненной печати и анимирует ядро гравитационного узла.
public class TacticalVisual : MonoBehaviour
{
    public LineRenderer ring;
    public Transform core;
    private TacticalEffect effect;
    private MaterialPropertyBlock block;
    // получаем описание эффекта и создаём блок для индивидуальной настройки материала.
    private void Awake(){effect=GetComponent<TacticalEffect>();block=new MaterialPropertyBlock();}
    // обновляем только графику на клиентах; состояние взведения берём из сетевого эффекта.
    private void Update()
    {
        if(effect==null||effect.definition==null)return;
        if(!NetworkClient.active&&NetworkServer.active)return;
        if(ring!=null&&effect.definition.kind==TacticalKind.FireSeal)
        {
            ring.GetPropertyBlock(block);block.SetColor("_BaseColor",effect.definition.tint*(effect.Armed?1.8f:.35f));ring.SetPropertyBlock(block);
        }
        if(core!=null&&effect.definition.kind==TacticalKind.GravityWell)
        {core.Rotate(Vector3.up,80*Time.deltaTime,Space.Self);core.localScale=Vector3.one*(.35f+Mathf.Sin(Time.time*4)*.06f);}
    }
}
