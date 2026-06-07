using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    [SerializeField] private Transform[] points;
    public static SpawnManager Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    public Transform GetSpawnPoint()
    {
        return points[Random.Range(0, points.Length)];
    }
}