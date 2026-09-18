using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.Video;

// воспроизводит заставку и заранее загружает следующую сцену, откладывая переход до конца видео.
public class SplashImage : MonoBehaviour
{
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private int nextSceneIndex;

    private bool videoFinished = false;

    // проверяем видеоплеер, подписываемся на окончание ролика и запускаем подготовку сцены.
    private void Start()
    {
        if (videoPlayer == null)
        {
            Debug.LogError("VideoPlayer не назначен!");
            return;
        }

        videoPlayer.loopPointReached += OnVideoFinished;

        StartCoroutine(PlayAndLoad());
    }

    // загружаем сцену в фоне и разрешаем её активацию после сигнала об окончании видео.
    private IEnumerator LoadNextSceneAsync()
    {
        AsyncOperation operation = SceneManager.LoadSceneAsync(nextSceneIndex);
        operation.allowSceneActivation = false;

        // ждём пока видео закончится
        while (!videoFinished)
        {
            yield return null;
        }

        // разрешаем переход
        operation.allowSceneActivation = true;
    }

    // отмечаем окончание ролика, чтобы ожидающая корутина могла разрешить переход.
    private void OnVideoFinished(VideoPlayer vp)
    {
        videoFinished = true;
    }
    // ждём готовности видео, запускаем его и параллельно загружаем следующую сцену.
    private IEnumerator PlayAndLoad()
    {
        // подготавливаем видео
        videoPlayer.Prepare();

        // ждём готовность
        yield return new WaitUntil(() => videoPlayer.isPrepared);
        videoPlayer.Play();

        // начинаем загрузку сцены
        AsyncOperation operation = SceneManager.LoadSceneAsync(nextSceneIndex);
        operation.allowSceneActivation = false;

        // ждём окончания видео
        while (!videoFinished)
        {
            yield return null;
        }

        // переход
        operation.allowSceneActivation = true;
    }
    // снимаем подписку с видеоплеера при уничтожении заставки.
    private void OnDestroy()
    {
        // отписываемся от завершения видео, чтобы не обращаться к уничтоженной заставке.
        if (videoPlayer != null)
            videoPlayer.loopPointReached -= OnVideoFinished;
    }
}
