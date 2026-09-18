using UnityEngine;

// добавляет покачивание камеры при движении через смещение в OrbitCamera.
public class CameraShake : MonoBehaviour
{
    public float shakeAmount = 0.1f;
    public float shakeSpeed = 20f;
    public bool isShaking = false;

    private float shakeTimer;
    private OrbitCamera orbitCamera;

    // получаем контроллер камеры, которому будем передавать смещение.
    void Start()
    {
        orbitCamera = GetComponent<OrbitCamera>();
    }

    // рассчитываем плавное покачивание; после остановки сбрасываем смещение и фазу.
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

    // включаем покачивание и подбираем его частоту по переданному значению скорости.
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
