using Mirror;
using UnityEngine;

public class Health : NetworkBehaviour
{
    //private Animator animator;

    [SerializeField] private int maxHealth = 100;  

    [SyncVar(hook = nameof(OnHealthChanged))]
    private int currentHealth;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;

    public event System.Action<int, int> OnHealthChangedEvent;
    private void Awake()
    {
        currentHealth = maxHealth;
    }
    private void Start()
    {
        //animator = GetComponent<Animator>();
    }
    public override void OnStartServer()
    {
        base.OnStartServer();
        currentHealth = maxHealth;
    }

    [Server]
    public void TakeDamage(int damage)
    {
        if (currentHealth <= 0)
            return;

        currentHealth -= damage;
        Debug.Log($"{gameObject.name} ������� ����! ������� ��������: {currentHealth}");

        //if (animator != null)
        //    animator.SetTrigger("Hurt");

        if (currentHealth <= 0)
            Die();
    }

    [Server]
    public void Heal(int amount)
    {
        if (currentHealth <= 0)
            return;

        currentHealth += amount;
        if (currentHealth > maxHealth)
            currentHealth = maxHealth;

        Debug.Log($"{gameObject.name} ����������� ��������! ������� ��������: {currentHealth}");
    }

    [Server]
    private void Die()
    {
        Debug.Log($"{gameObject.name} ���� �� �������!");
        RpcDie();
        // ����� ������� �������� ������� � ���������
        NetworkServer.Destroy(transform.parent.gameObject);
    }

    [ClientRpc]
    private void RpcDie()
    {
        Debug.Log($"{gameObject.name} ���� �� �������!");
        //if (animator != null)
        //    animator.SetTrigger("Die");
    }
    private void OnHealthChanged(int oldHealth, int newHealth)
    {
        // �������� UI ��� �������� ����� �� ��������

        //if (animator != null && newHealth < oldHealth)
        //    animator.SetTrigger("Hurt");

        //if (newHealth <= 0)
        //    DieClientSide();

        OnHealthChangedEvent?.Invoke(newHealth, maxHealth);
    }

}
