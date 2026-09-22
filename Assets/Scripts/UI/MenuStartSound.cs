using UnityEngine;

public class MenuStartSound : MonoBehaviour
{
    [SerializeField] private AudioClip[] startSound;
    [SerializeField] private AudioSource audioSource;

    private static bool hasPlayed;

    private void Start()
    {
        if (hasPlayed)
            return;

        PlayRandomSound();
        hasPlayed = true;
    }

    void PlayRandomSound()
    {
        if (startSound.Length == 0)
            return;

        int randomIndex = Random.Range(0, startSound.Length);
        audioSource.PlayOneShot(startSound[randomIndex]);
    }
}
