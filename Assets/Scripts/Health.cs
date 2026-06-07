using Mirror;
using System.Collections;
using UnityEngine;

public class Health : NetworkBehaviour
{
    //private Animator animator;

    [SerializeField] private int maxHealth = 100;

    [SyncVar(hook = nameof(OnHealthChanged))]
    private int currentHealth;

    // ===================== STATE =====================
    // главный флаг смерти (синхронизируется по сети)
    [SyncVar]
    private bool isDead;

    public bool IsDead => isDead;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;

    public event System.Action<int, int> OnHealthChangedEvent;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        currentHealth = maxHealth;
        isDead = false;
    }

    // ===================== DAMAGE =====================
    [Server]
    public void TakeDamage(int damage)
    {
        if (currentHealth <= 0 || isDead)
            return;

        currentHealth -= damage;

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    // ===================== HEAL =====================
    [Server]
    public void Heal(int amount)
    {
        if (currentHealth <= 0)
            return;

        currentHealth += amount;

        if (currentHealth > maxHealth)
            currentHealth = maxHealth;
    }

    // ===================== DEATH =====================
    [Server]
    private void Die()
    {
        if (isDead)
            return;

        isDead = true;
        currentHealth = 0;

        // запускаем респавн только на сервере
        StartCoroutine(RespawnRoutine());
    }

    // ===================== RESPAWN =====================
    [Server]
    private IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(3f);

        Transform spawn = SpawnManager.Instance.GetSpawnPoint();

        currentHealth = maxHealth;
        isDead = false;

        CharacterController cc = GetComponent<CharacterController>();

        cc.enabled = false;
        transform.position = spawn.position;
        transform.rotation = spawn.rotation;
        cc.enabled = true;
    }

    // ===================== SYNC HEALTH =====================
    private void OnHealthChanged(int oldHealth, int newHealth)
    {
        OnHealthChangedEvent?.Invoke(newHealth, maxHealth);
    }

}