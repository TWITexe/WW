using Mirror;
using System.Collections;
using UnityEngine;

// хранит сетевое здоровье, щит и смерть; урон и возрождение рассчитывает сервер.
public class Health : NetworkBehaviour
{
    private static readonly System.Collections.Generic.HashSet<Health> serverInstances = new System.Collections.Generic.HashSet<Health>();
    public static System.Collections.Generic.IReadOnlyCollection<Health> ServerInstances => serverInstances;

    [SerializeField] private int maxHealth = 100;

    [SyncVar(hook = nameof(OnHealthChanged))]
    private int currentHealth;

    // состояние персонажа, синхронизируемое сервером.
    // главный флаг смерти (синхронизируется по сети)
    [SyncVar]
    private bool isDead;

    public bool IsDead => isDead;
    [SyncVar(hook = nameof(OnShieldChanged))] private int shield;
    private double shieldUntil;
    private uint lastAttacker;
    private double lastAttackTime;
    public int Shield => shield;
    public event System.Action<int> ShieldChanged;

    // уведомляем клиентское оформление о появлении и исчезновении синхронизированного щита.
    private void OnShieldChanged(int previous, int current) => ShieldChanged?.Invoke(current);

    // сервер оставляет больший из текущего и нового щитов и задаёт срок его действия.
    [Server]
    public void GrantShield(int amount, float duration)
    {
        if (isDead) return;
        shield = Mathf.Max(shield, amount);
        shieldUntil = NetworkTime.time + duration;
    }

    // сервер снимает оставшийся щит, когда заканчивается его время.
    private void Update()
    {
        if (isServer && shield > 0 && NetworkTime.time >= shieldUntil) shield = 0;
    }

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    // учитываем поглощённые щитом попадания, чтобы источник не лечил под обстрелом.
    public uint DamageVersion { get; private set; }

    public event System.Action<int, int> OnHealthChangedEvent;

    // задаём начальное здоровье до запуска сетевых обратных вызовов.
    private void Awake()
    {
        currentHealth = maxHealth;
    }

    // регистрируем живую серверную сущность для ловушек и тактических эффектов.
    public override void OnStartServer()
    {
        base.OnStartServer();
        serverInstances.Add(this);

        currentHealth = maxHealth;
        isDead = false;
    }
    // удаляем сущность из серверного списка при завершении её сетевой жизни.
    public override void OnStopServer()
    {
        serverInstances.Remove(this);
        base.OnStopServer();
    }

    // получение урона и запоминание атакующего.
    // сначала поглощаем урон щитом, затем уменьшаем здоровье и при необходимости запускаем смерть.
    [Server]
    public void TakeDamage(int damage, uint attackerId = 0, bool headshot = false)
    {
        if (currentHealth <= 0 || isDead)
            return;

        damage = Mathf.Max(0, damage);
        if (damage > 0) DamageVersion++;
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

    // запоминаем последнего противника для зачёта убийства, в том числе после попадания в ловушку.
    [Server]
    public void RecordAttacker(uint attackerId)
    {
        if (attackerId == 0 || attackerId == netId || isDead) return;
        lastAttacker = attackerId;
        lastAttackTime = NetworkTime.time;
    }

    // сервер применяет разброс урона и множитель попадания в голову перед списанием здоровья.
    [Server]
    public void TakeSpellDamage(int baseDamage,uint attackerId,bool headshot=false)
    {
        TakeDamage(SpellDamage.Roll(baseDamage,headshot,Random.value),attackerId,headshot);
    }
    // показываем цифры урона по другим персонажам, скрывая входящий урон от самого пострадавшего.
    [ClientRpc]
    private void RpcDamageNumber(int amount,bool headshot,Vector3 position)
    {
        if (isLocalPlayer) return;
        var prefab=Resources.Load<FloatingDamageNumber>("DamageNumber");
        if(prefab==null)return;
        var number=Instantiate(prefab,position+Vector3.right*Random.Range(-.2f,.2f),Quaternion.identity);
        number.Initialize(amount,headshot);
    }
    // восстановление здоровья живого персонажа.
    // восстанавливаем здоровье живому персонажу, не превышая его максимум.
    [Server]
    public void Heal(int amount)
    {
        if (currentHealth <= 0)
            return;

        currentHealth += amount;

        if (currentHealth > maxHealth)
            currentHealth = maxHealth;
    }

    // обработка смерти и статистики.
    // один раз отмечаем смерть и засчитываем убийство, если противник воздействовал за последние восемь секунд.
    [Server]
    private void Die()
    {
        if (isDead)
            return;

        isDead = true;
        GetComponent<RelativeMovement>()?.ResetVerticalVelocity();
        GetComponent<PlayerStats>()?.AddDeath();
        var victimStats = GetComponent<PlayerStats>();
        PlayerStats killerStats = null;
        if (lastAttacker != 0 && NetworkTime.time - lastAttackTime <= 8 &&
            NetworkServer.spawned.TryGetValue(lastAttacker, out var attacker))
        {
            killerStats = attacker.GetComponentInChildren<PlayerStats>();
            killerStats?.AddKill();
        }
        RpcKillFeed(killerStats != null ? killerStats.DisplayName : "Окружение", victimStats != null ? victimStats.DisplayName : "Player",
            killerStats != null ? killerStats.DisplayColor : Color.gray, victimStats != null ? victimStats.DisplayColor : Color.white);
        lastAttacker = 0;
        shield = 0;
        currentHealth = 0;

        // запускаем респавн только на сервере
        StartCoroutine(RespawnRoutine());
    }

    // запись отправляет сервер один раз при смерти, поэтому у всех клиентов совпадает журнал убийств.
    [ClientRpc] private void RpcKillFeed(string killer, string victim, Color killerColor, Color victimColor)
        => MatchKillFeed.Add(killer, victim, killerColor, victimColor);

    // отложенное возрождение и перенос обеих копий персонажа.
    // через три секунды восстанавливаем здоровье и выполняем серверный перенос.
    [Server]
    private IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(3f);

        Transform spawn = SpawnManager.Instance.GetSpawnPoint();

        currentHealth = maxHealth;
        isDead = false;

        // движение отвечает за серверный перенос, сброс предсказания и интерполяции наблюдателей.
        var movement = GetComponent<RelativeMovement>();
        if (movement != null) movement.ServerTeleport(spawn.position, spawn.rotation);
        else
        {
            transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            GetComponent<NetworkTransformBase>()?.ServerTeleport(spawn.position, spawn.rotation);
        }
    }
    // уведомление интерфейса об изменении сетевого здоровья.
    // уведомляем подписанный интерфейс после получения нового здоровья через SyncVar.
    private void OnHealthChanged(int oldHealth, int newHealth)
    {
        OnHealthChangedEvent?.Invoke(newHealth, maxHealth);
    }

}

