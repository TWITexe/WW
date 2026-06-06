using Mirror;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Trap : NetworkBehaviour
{
    [SerializeField] private int damage = 10;
    [SerializeField] private float damageCooldown = 1f;

    // Храним для каждого Health запущенную корутину
    private Dictionary<Health, Coroutine> damageCoroutines = new Dictionary<Health, Coroutine>();

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"ENTER {other.name}");
        if (!isServer) return; // обработка только на сервере

        Health health = other.GetComponentInParent<Health>();
        if (health != null)
        {
            Debug.Log($"FOUND HEALTH {health.name}");
            Coroutine coroutine = StartCoroutine(DamageOverTime(health));
            damageCoroutines.Add(health, coroutine);
        }
    }
    private void OnTriggerExit(Collider other)
    {
        if (!isServer) return;

        Health health = other.GetComponent<Health>();
        if (health != null && damageCoroutines.ContainsKey(health))
        {
            {
                StopCoroutine(damageCoroutines[health]);
                damageCoroutines.Remove(health);
            }
        }
    }
    private IEnumerator DamageOverTime(Health health)
    {
        while (true)
        {
            Debug.Log($"DAMAGE TICK {health.name}");
            if (health == null)
                yield break;

            health.TakeDamage(damage);
            yield return new WaitForSeconds(damageCooldown);
        }
    }

}
