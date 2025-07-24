using UnityEngine;

public class CameraShake : MonoBehaviour
{
    public float shakeAmount = 0.1f;
    public float shakeSpeed = 20f;
    public bool isShaking = false;

    private float shakeTimer;
    private OrbitCamera orbitCamera;

    void Start()
    {
        orbitCamera = GetComponent<OrbitCamera>();
    }

    void LateUpdate()
    {
        if (isShaking)
        {
            shakeTimer += Time.deltaTime * shakeSpeed;
            float offsetX = Mathf.Sin(shakeTimer) * shakeAmount * 0.7f;
            float offsetY = Mathf.Cos(shakeTimer) * shakeAmount;
            Vector3 shakeOffset = new Vector3(offsetX, offsetY, 0);
            orbitCamera.SetShakeOffset(shakeOffset);
        }
        else
        {
            orbitCamera.SetShakeOffset(Vector3.zero);
            shakeTimer = 0;
        }
    }

    public void SetShaking(bool value, float speed)
    {
        isShaking = value;
        if (speed < 50)
        {
            shakeSpeed = speed / 8;
        }
        else
        {
            shakeSpeed = speed / 6;
        }
        
    }
}
