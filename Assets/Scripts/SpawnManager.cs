using UnityEngine;

// хранит заданные в сцене точки возрождения персонажей.
public class SpawnManager : MonoBehaviour
{
    [SerializeField] private Transform[] points;
    public static SpawnManager Instance { get; private set; }

    // делаем менеджер текущей сцены доступным для серверного возрождения.
    private void Awake()
    {
        Instance = this;
    }

    // выбираем случайную точку; массив должен быть заполнен в инспекторе.
    public Transform GetSpawnPoint()
    {
        return points[Random.Range(0, points.Length)];
    }
}