using UnityEngine;

// сохраняет настройки вертикальной синхронизации и целевой частоты кадров между сценами.
public class FrameRateLimiter : MonoBehaviour
{
    [SerializeField] private int targetFps = 75;

    // включаем вертикальную синхронизацию; её настройка может иметь приоритет над targetFps.
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);

        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = targetFps;
    }
}