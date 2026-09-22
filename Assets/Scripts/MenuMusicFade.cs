using UnityEngine;

// музыка начинает играть с нуля и мягко выходит на сохранённую громкость; повтор включён на самом источнике.
[RequireComponent(typeof(AudioSource))]
public class MenuMusicFade : MonoBehaviour
{
    [Min(.1f)] public float fadeDuration = 2;
    private AudioSource source;
    private float targetVolume, startedAt;
    private void Awake()
    {
        source = GetComponent<AudioSource>();
        targetVolume = source.volume;
        source.loop = true;
        source.playOnAwake = false;
        source.volume = 0;
    }
    private void OnEnable()
    {
        source.volume = 0;
        startedAt = Time.unscaledTime;
        source.Play();
    }
    private void Update()
    {
        source.volume = targetVolume * Mathf.SmoothStep(0,1,(Time.unscaledTime-startedAt)/fadeDuration);
    }
    private void OnDisable() { if(source!=null)source.Stop(); }
}
