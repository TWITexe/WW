using UnityEngine;
using UnityEngine.UI;
using TMPro;

// обновляет текст здоровья над персонажем по событиям сетевого компонента Health.
public class HealthUI : MonoBehaviour
{
    [SerializeField] private TMP_Text healthText;
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

    // показываем текущее здоровье либо обозначение смерти.
    private void UpdateHealthUI(int current, int max)
    {
        if (current > 0)
            healthText.text = "" + current;
        else
            healthText.text = "💀 Is Dead 💀";
    }
}
