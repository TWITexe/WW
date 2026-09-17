using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.Video;

public class SplashImage : MonoBehaviour
{
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private int nextSceneIndex;

    private bool videoFinished = false;

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

    private void OnVideoFinished(VideoPlayer vp)
    {
        videoFinished = true;
    }
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
    private void OnDestroy()
    {
        // отписОчка
        if (videoPlayer != null)
            videoPlayer.loopPointReached -= OnVideoFinished;
    }
}
