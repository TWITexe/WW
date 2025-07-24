using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HealthUI : MonoBehaviour
{
    [SerializeField] private TMP_Text healthText;
    private Health health;

    private void Start()
    {
        health = GetComponentInParent<Health>();
        if (health != null)
        {
            health.OnHealthChangedEvent += UpdateHealthUI;
            UpdateHealthUI(health.CurrentHealth, health.MaxHealth); // начальная установка
        }
    }

    private void OnDestroy()
    {
        if (health != null)
            health.OnHealthChangedEvent -= UpdateHealthUI;
    }

    private void UpdateHealthUI(int current, int max)
    {
        healthText.text = "" + current;
    }
}
