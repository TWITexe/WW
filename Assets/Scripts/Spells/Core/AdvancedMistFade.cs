using Mirror;
using UnityEngine;

// общая прозрачность мягко раскрывает и убирает туман, включая уже выпущенные частицы.
public class AdvancedMistFade : MonoBehaviour
{
    public AdvancedSpellEffect effect;
    public GameObject area;
    private ParticleSystemRenderer[] renderers;
    private Color[] colors;
    private MaterialPropertyBlock block;
    private void Awake()
    {
        renderers=area.GetComponentsInChildren<ParticleSystemRenderer>(true);
        colors=new Color[renderers.Length];
        block=new MaterialPropertyBlock();
        for(int i=0;i<renderers.Length;i++)
            colors[i]=renderers[i].sharedMaterial.HasProperty("_BaseColor")?renderers[i].sharedMaterial.GetColor("_BaseColor"):Color.white;
    }
    private void LateUpdate()
    {
        if(effect==null || effect.phase!=1)return;
        float age=(float)(NetworkTime.time-effect.phaseStarted);
        float opacity=Mathf.SmoothStep(0,1,age/.45f)*Mathf.SmoothStep(0,1,(effect.definition.duration-age)/.8f);
        for(int i=0;i<renderers.Length;i++)
        {
            Color color=colors[i];color.a*=opacity;
            renderers[i].GetPropertyBlock(block);
            block.SetColor("_BaseColor",color);
            block.SetColor("_Color",color);
            renderers[i].SetPropertyBlock(block);
        }
    }
}
