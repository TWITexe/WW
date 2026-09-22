using UnityEngine;
using UnityEngine.UI;
using TMPro;

// обновляет полоску здоровья над персонажем по событиям сетевого компонента Health.
public class HealthUI : MonoBehaviour
{
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private UnityEngine.UI.Image healthBar;
    private Health health;

    // подписываемся на изменения и сразу показываем здоровье, не ожидая первого попадания.
    private void Start()
    {
        health = GetComponentInParent<Health>();
        if (health != null)
        {
            health.OnHealthChangedEvent += UpdateHealthUI;
            UpdateHealthUI(health.CurrentHealth, health.MaxHealth); // начальная установка
        }
    }

    // снимаем подписку, чтобы уничтоженный интерфейс больше не получал события.
    private void OnDestroy()
    {
        if (health != null)
            health.OnHealthChangedEvent -= UpdateHealthUI;
    }

    // меняем длину и цвет полоски; при нуле скрываем заполнение целиком.
    private void UpdateHealthUI(int current, int max)
    {
        if (healthText != null) healthText.enabled = false;
        if (healthBar == null) return;
        float fraction = Mathf.Clamp01((float)current / Mathf.Max(1,max));
        healthBar.enabled = fraction > 0;
        healthBar.rectTransform.anchorMax = new Vector2(fraction,1);
        healthBar.color = Color.Lerp(new Color(1,.22f,.12f),new Color(.3f,.95f,.48f),fraction);
    }
}
