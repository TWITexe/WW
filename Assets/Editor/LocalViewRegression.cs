using System;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class LocalViewRegression
{
    const string Flag = "LocalViewRegression.Active";
    static double started, next;
    static int stage, checks;
    static GameObject remote;
    static Camera localCamera;
    static LocalViewRegression() { EditorApplication.update += Tick; }
    public static void Run()
    {
        SessionState.SetBool(Flag, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity");
        EditorApplication.isPlaying = true;
    }
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception("Local view: " + message);
        checks++;
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Flag, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            double now = EditorApplication.timeSinceStartup;
            if (started == 0) { started = now; next = now + 2; }
            if (now - started > 70) throw new Exception("Local view timeout");
            if (now < next) return;
            if (stage == 0)
            {
                var manager = NetworkManager.singleton;
                if (manager.transport is PortTransport port) port.Port = 17987;
                manager.StartHost(); stage = 1; next = now + 2;
            }
            else if (stage == 1)
            {
                if (NetworkClient.localPlayer == null) return;
                localCamera = NetworkClient.localPlayer.GetComponentInChildren<RelativeMovement>().ViewCamera;
                Check(localCamera.isActiveAndEnabled, "local camera active");
                var ownLabels = NetworkClient.localPlayer.GetComponentsInChildren<BillboardToCamera>(true);
                Check(ownLabels.Length == 2, "both local overhead labels found");
                Check(ownLabels.All(b => !b.GetComponent<Canvas>().enabled), "own name and HP hidden");
                remote = UnityEngine.Object.Instantiate(NetworkManager.singleton.playerPrefab);
                Check(!remote.GetComponentInChildren<RelativeMovement>().ViewCamera.gameObject.activeSelf, "remote camera disabled immediately on creation");
                NetworkServer.Spawn(remote);
                stage = 2; next = now + 1;
            }
            else
            {
                Check(remote.GetComponent<NetworkIdentity>().isClient, "remote replica started on host client");
                Check(!remote.GetComponentInChildren<RelativeMovement>().ViewCamera.isActiveAndEnabled, "remote camera stays disabled after spawn");
                Check(localCamera.isActiveAndEnabled && Camera.main == localCamera, "local camera retained after second spawn");
                Check(UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(a => a.isActiveAndEnabled) == 1, "one active audio listener");
                var labels = remote.GetComponentsInChildren<BillboardToCamera>(true);
                Check(labels.Length == 2 && labels.All(b => b.GetComponent<Canvas>().enabled), "remote name and HP visible");
                Debug.Log("LOCAL_VIEW_REGRESSION_PASSED: " + checks + " checks");
                SessionState.SetBool(Flag, false); NetworkManager.singleton.StopHost(); EditorApplication.Exit(0);
            }
        }
        catch (Exception e) { SessionState.SetBool(Flag, false); Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
