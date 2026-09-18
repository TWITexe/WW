using Mirror;
using UnityEngine;

// отключает декоративную область на выделенном сервере; на клиентах она живёт вместе с сетевым заклинанием.
public class ImportedAreaVisual : MonoBehaviour
{
    [SerializeField] private GameObject visualRoot;

    private void Awake()
    {
        if (NetworkServer.active && !NetworkClient.active && visualRoot != null)
            visualRoot.SetActive(false);
    }

    // сохраняем прямую ссылку при подготовке префаба, без поиска объектов во время игры.
    public void Configure(GameObject root) => visualRoot = root;
}
