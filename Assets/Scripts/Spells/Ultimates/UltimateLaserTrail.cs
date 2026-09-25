using UnityEngine;

public class UltimateLaserTrail : MonoBehaviour
{
    private LineRenderer line;
    private Color tint;
    private float age, lifetime;
    public void Initialize(LineRenderer renderer, Color color, float seconds)
    {
        line = renderer; tint = color; lifetime = seconds;
        line.numCapVertices = 6;
        Apply();
        Destroy(gameObject, lifetime);
    }
    void Update() { age += Time.deltaTime; Apply(); }
    void Apply()
    {
        if (line == null) return;
        float grow = Mathf.SmoothStep(0, 1, age / .16f);
        float shrink = 1 - Mathf.SmoothStep(0, 1, Mathf.Max(0, age - .16f) / Mathf.Max(.01f, lifetime - .16f));
        line.startWidth = Mathf.Lerp(.025f, .45f, grow) * shrink;
        line.endWidth = line.startWidth * .65f;
        var color = tint; color.a = Mathf.Pow(shrink, .55f);
        line.startColor = line.endColor = color;
    }
}
