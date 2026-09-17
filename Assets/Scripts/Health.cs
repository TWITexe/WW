using Mirror;
using System.Collections;
using UnityEngine;

public class Health : NetworkBehaviour
{
    private static readonly System.Collections.Generic.HashSet<Health> serverInstances = new System.Collections.Generic.HashSet<Health>();
    public static System.Collections.Generic.IReadOnlyCollection<Health> ServerInstances => serverInstances;
    //private Animator animator;

    [SerializeField] private int maxHealth = 100;

    [SyncVar(hook = nameof(OnHealthChanged))]
    private int currentHealth;

    // ===================== STATE =====================
    // главный флаг смерти (синхронизируется по сети)
    [SyncVar]
    private bool isDead;

    public bool IsDead => isDead;
    [SyncVar] private int shield;
    private double shieldUntil;
    private uint lastAttacker;
    private double lastAttackTime;
    public int Shield => shield;

    [Server]
    public void GrantShield(int amount, float duration)
    {
        if (isDead) return;
        shield = Mathf.Max(shield, amount);
        shieldUntil = NetworkTime.time + duration;
    }

    private void Update()
    {
        if (isServer && shield > 0 && NetworkTime.time >= shieldUntil) shield = 0;
    }

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
        serverInstances.Add(this);

        currentHealth = maxHealth;
        isDead = false;
    }
    public override void OnStopServer()
    {
        serverInstances.Remove(this);
        base.OnStopServer();
    }

    // ===================== DAMAGE =====================
    [Server]
    public void TakeDamage(int damage, uint attackerId = 0, bool headshot = false)
    {
        if (currentHealth <= 0 || isDead)
            return;

        damage = Mathf.Max(0, damage);
        if (damage > 0) RecordAttacker(attackerId);
        if (NetworkTime.time >= shieldUntil) shield = 0;
        int absorbed = Mathf.Min(shield, damage);
        shield -= absorbed;
        int actualDamage = Mathf.Min(currentHealth, damage - absorbed);
        currentHealth -= actualDamage;
        if (actualDamage > 0) RpcDamageNumber(actualDamage, headshot, transform.position + Vector3.up * 2.1f);

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    [Server]
    public void RecordAttacker(uint attackerId)
    {
        if (attackerId == 0 || attackerId == netId || isDead) return;
        lastAttacker = attackerId;
        lastAttackTime = NetworkTime.time;
    }

    [Server]
    public void TakeSpellDamage(int baseDamage,uint attackerId,bool headshot=false)
    {
        TakeDamage(SpellDamage.Roll(baseDamage,headshot,Random.value),attackerId,headshot);
    }
    [ClientRpc]
    private void RpcDamageNumber(int amount,bool headshot,Vector3 position)
    {
        if(isLocalPlayer)return;
        var prefab=Resources.Load<FloatingDamageNumber>("DamageNumber");
        if(prefab==null)return;
        var number=Instantiate(prefab,position+Vector3.right*Random.Range(-.2f,.2f),Quaternion.identity);
        number.Initialize(amount,headshot);
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
        GetComponent<PlayerStats>()?.AddDeath();
        if (lastAttacker != 0 && NetworkTime.time - lastAttackTime <= 8 &&
            NetworkServer.spawned.TryGetValue(lastAttacker, out var attacker))
            attacker.GetComponentInChildren<PlayerStats>()?.AddKill();
        lastAttacker = 0;
        shield = 0;
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

        // 1. Телепортируем серверную копию игрока
        ServerTeleport(spawn.position, spawn.rotation);

        // 2. Телепортируем клиента-владельца
        // Это важно, потому что именно клиент двигает своего персонажа
        TargetTeleport(connectionToClient, spawn.position, spawn.rotation);
    }
    [Server]
    private void ServerTeleport(Vector3 position, Quaternion rotation)
    {
        CharacterController cc = GetComponent<CharacterController>();

        if (cc != null)
            cc.enabled = false;

        transform.position = position;
        transform.rotation = rotation;

        ResetMovementState();

        if (cc != null)
            cc.enabled = true;
    }

    [TargetRpc]
    private void TargetTeleport(NetworkConnectionToClient target, Vector3 position, Quaternion rotation)
    {
        CharacterController cc = GetComponent<CharacterController>();

        if (cc != null)
            cc.enabled = false;

        transform.position = position;
        transform.rotation = rotation;

        ResetMovementState();

        if (cc != null)
            cc.enabled = true;
    }

    private void ResetMovementState()
    {
        RelativeMovement movement = GetComponent<RelativeMovement>();

        if (movement != null)
        {
            movement.ResetVerticalVelocity();
            movement.ForceGroundReset();
        }
    }

    // ===================== SYNC HEALTH =====================
    private void OnHealthChanged(int oldHealth, int newHealth)
    {
        OnHealthChangedEvent?.Invoke(newHealth, maxHealth);
    }

}

