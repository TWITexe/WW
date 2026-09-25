using UnityEngine;

// The animated rig lives below the smoothed model; hitboxes stay on the simulation root.
[DefaultExecutionOrder(260)]
public class UltimateSpiritAnimator : MonoBehaviour
{
    public Animation flightAnimation;
    public Transform leanRoot, heart;
    private Vector3 previous;
    private float speed, attackUntil;
    private bool moving;
    public string CurrentMotion { get; private set; }
    void OnEnable()
    {
        previous = transform.position; speed = 0; moving = false; attackUntil = 0;
        CurrentMotion = "Hover"; flightAnimation?.Play(CurrentMotion);
    }
    public void Strike(bool rightToLeft, double startedAt)
    {
        if (flightAnimation == null) return;
        string clip = rightToLeft ? "SwingRight" : "SwingLeft";
        var state = flightAnimation[clip];
        if (state == null) return;
        float elapsed = Mathf.Max(0, (float)(Mirror.NetworkTime.time - startedAt));
        if (elapsed >= state.length) return;
        CurrentMotion = clip; attackUntil = Time.time + state.length - elapsed;
        flightAnimation.CrossFade(clip, .06f); state.time = elapsed;
    }
    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0) return;
        Vector3 velocity = (transform.position - previous) / dt; previous = transform.position;
        speed = Mathf.Lerp(speed, Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude, 1 - Mathf.Exp(-9 * dt));
        bool glide = speed > .5f;
        moving = glide;
        if (Time.time >= attackUntil)
        {
            string clip = moving ? "Walk" : "Hover";
            if (CurrentMotion != clip) { CurrentMotion = clip; flightAnimation?.CrossFade(clip, .16f); }
            if (moving && flightAnimation != null && flightAnimation["Walk"] != null)
                flightAnimation["Walk"].speed = Mathf.Clamp(speed / 3.5f, .65f, 1.8f);
        }
        if (leanRoot != null) leanRoot.localRotation = Quaternion.Slerp(leanRoot.localRotation,
            Quaternion.Euler(Mathf.Clamp(speed, 0, 7), 0, 0), 1 - Mathf.Exp(-10 * dt));
        if (heart != null) heart.localScale = Vector3.one * (1 + Mathf.Sin(Time.time * 3) * .06f);
    }
}
