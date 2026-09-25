using System.Collections.Generic;
using Mirror;
using UnityEngine;

// серверная логика стихийного снаряда или области: попадания, периодический урон и дополнительные воздействия.
public class ElementalEffect : NetworkBehaviour
{
    public ElementalSpell definition;
    [SyncVar] public uint ownerId;
    [SyncVar] public float damageScale = 1;
    public int ImpactDamage => Mathf.RoundToInt((definition.damage + (lensAmplified ? SteamLens.BonusDamage : 0)) * damageScale);
    // передаём время создания для согласованного появления и затухания графики.
    [SyncVar] public double bornAt;
    private double expiresAt;
    private double nextTick;
    private Vector3 previousPosition;
    private bool consumed;
    private ArcSmokeProjectile smokeFlight;
    private readonly Collider[] tornadoOverlaps = new Collider[32];
    private readonly RaycastHit[] tornadoHits = new RaycastHit[32];
    [SyncVar(hook = nameof(OnLensAmplified))] private bool lensAmplified;
    private readonly HashSet<Health> affected = new HashSet<Health>();

    // запоминаем точку появления и задаём серверные сроки действия и первого срабатывания.
    public override void OnStartServer()
    {
        smokeFlight = GetComponent<ArcSmokeProjectile>();
        BeginAreaLifetime();
    }
    // время действия дымовой завесы начинается после приземления, а не во время броска.
    public void BeginAreaLifetime()
    {
        previousPosition = transform.position;
        bornAt = NetworkTime.time;
        expiresAt = bornAt + definition.duration;
        nextTick = NetworkTime.time;
    }
    // удалённые клиенты получают позицию по сети и не симулируют Rigidbody самостоятельно.
    public override void OnStartClient()
    {
        if (!isServer) GetComponent<Rigidbody>().isKinematic = true;
        if (lensAmplified) LensProjectileGlow.Show(gameObject);
    }
    // сервер проверяет путь снаряда либо выполняет очередное воздействие области до окончания её жизни.
    private void Update()
    {
        if (!isServer || consumed || definition == null) return;
        if (smokeFlight != null && smokeFlight.Flying) return;
        if (NetworkTime.time >= expiresAt) { Finish(); return; }
        if (definition.mode == ElementalCastMode.Tornado)
        {
            // проверяем только твёрдое окружение: враги внутри вихря не должны останавливать его полёт.
            if (TryTornadoWall(out var wallPoint)) { transform.position = wallPoint; Finish(); return; }
            previousPosition = transform.position;
        }
        if (definition.mode == ElementalCastMode.Bolt)
        {
            if (ProjectileContact.Sweep(transform, ownerId, previousPosition, out var hit, out var point)) Impact(hit, point);
            else TryLens(transform.position);
            previousPosition = transform.position;
        }
        else if (NetworkTime.time >= nextTick)
        {
            nextTick = NetworkTime.time + Mathf.Max(0.1f, definition.tickInterval);
            ApplyArea(transform.position + Vector3.up * .15f);
            if (definition.mode == ElementalCastMode.SelfBurst) Finish();
        }
    }
    // проверяем начало пути и весь пройденный отрезок; низкая опора под вихрем не считается стеной.
    private bool TryTornadoWall(out Vector3 point)
    {
        point = previousPosition;
        int overlaps = Physics.OverlapSphereNonAlloc(previousPosition,.45f,tornadoOverlaps,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        for(int i=0;i<overlaps;i++)
            if(TornadoWall(tornadoOverlaps[i]) && tornadoOverlaps[i].bounds.max.y > previousPosition.y+.1f)return true;
        if(overlaps==tornadoOverlaps.Length)return true;
        Vector3 delta=transform.position-previousPosition;
        if(delta.sqrMagnitude<.000001f)return false;
        int count=Physics.SphereCastNonAlloc(previousPosition,.45f,delta.normalized,tornadoHits,delta.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        float nearest=float.PositiveInfinity;
        for(int i=0;i<count;i++)
            if(TornadoWall(tornadoHits[i].collider) && tornadoHits[i].normal.y < .65f && tornadoHits[i].distance < nearest)
            {nearest=tornadoHits[i].distance;point=tornadoHits[i].point+tornadoHits[i].normal*.5f;}
        return nearest<float.PositiveInfinity || count==tornadoHits.Length;
    }
    private bool TornadoWall(Collider collider) => !collider.transform.IsChildOf(transform) &&
        collider.GetComponentInParent<Health>()==null && collider.GetComponentInParent<ElementalEffect>()==null &&
        collider.GetComponentInParent<FireballProjectile>()==null && collider.GetComponentInParent<WindFlowProjectile>()==null;
    // используем общий фильтр попаданий, чтобы все типы снарядов одинаково выбирали цели.
    private bool Ignore(Collider other)
    {
        return !ProjectileContact.CanHit(other,transform,ownerId);
    }
    // обрабатываем физический контакт только у активного серверного снаряда.
    private void OnTriggerEnter(Collider other)
    {
        if (!isServer || consumed || definition.mode != ElementalCastMode.Bolt || Ignore(other)) return;
        Impact(other, ProjectileContact.ImpactPosition(other, transform, previousPosition));
    }
    // сначала проверяем отражение, затем наносим прямой или площадной урон и завершаем снаряд.
    private void Impact(Collider other, Vector3 point)
    {
        TryLens(point);
        if (TacticalEffect.TryReflect(other,transform,ownerId,out uint reflected)) { ownerId=reflected;previousPosition=transform.position;return; }
        other.GetComponentInParent<TacticalEffect>()?.ProjectileHit(ownerId);
        var direct = other.GetComponentInParent<Health>();
        bool headshot = direct != null && SpellDamage.IsHeadshot(direct, other, other.ClosestPoint(transform.position));
        if (definition.radius > 0.5f) ApplyArea(point, direct, headshot);
        else
        {
            ApplyTarget(direct, headshot);
            ArenaDestructible.Hit(other, point, ImpactDamage);
        }
        Finish(true, point);
    }
    // собираем цели в радиусе; несколько коллайдеров одной цели не должны умножать урон за одно срабатывание.
    [Server]
    private void ApplyArea(Vector3 origin, Health direct = null, bool headshot = false)
    {
        affected.Clear();
        // прямой контакт уже подтверждён снарядом; укрытие не должно отменять это попадание.
        if (direct != null) { affected.Add(direct); ApplyTarget(direct, headshot); }
        foreach (Collider collider in Physics.OverlapSphere(origin, definition.radius, Physics.AllLayers, QueryTriggerInteraction.Collide))
        {
            Health health = collider.GetComponentInParent<Health>();
            if (health == null || health.netId == ownerId || health.IsDead || affected.Contains(health)) continue;
            if (!ProjectileContact.IsDamageCollider(collider, health)) continue;
            // проверяем до добавления: другой коллайдер той же цели может быть виден над укрытием.
            if (!SpellAreaVisibility.CanReach(origin, collider, transform)) continue;
            affected.Add(health);
            ApplyTarget(health);
        }
        ArenaDestructible.Blast(origin, definition.radius, definition.damage, transform);
    }
    // исключаем владельца и мёртвых, наносим урон, затем применяем толчок, подброс и замедление.
    [Server]
    private void ApplyTarget(Health health, bool headshot = false)
    {
        if (health == null || health.netId == ownerId || health.IsDead) return;
        health.TakeSpellDamage(ImpactDamage, ownerId, headshot);
        var movement = health.GetComponent<RelativeMovement>();
        if (movement == null || health.IsDead) return;
        Vector3 outward = (health.transform.position - transform.position);
        outward.y = 0;
        if (outward.sqrMagnitude < 0.01f) outward = transform.forward;
        Vector3 force = outward.normalized * definition.knockback + Vector3.up * definition.lift;
        if (force.sqrMagnitude > 0)
        {
            if (definition.mode == ElementalCastMode.GroundZone || definition.mode == ElementalCastMode.Tornado)
                movement.ServerAddPeriodicForce(force);
            else movement.ServerAddExternalForce(force);
        }
        if (definition.slow < 1) movement.ApplySlow(definition.slow, definition.slowDuration);
    }
    // линза работает только с ледяным копьём; другие стихийные снаряды и области проходят без усиления.
    private void TryLens(Vector3 end)
    {
        if (!lensAmplified && definition.mode == ElementalCastMode.Bolt && definition.name == "IceShard")
            lensAmplified = SteamLens.TryAmplify(ownerId, previousPosition, end, GetComponent<Rigidbody>());
    }
    private void OnLensAmplified(bool previousValue, bool value)
    {
        if (value && isClient) LensProjectileGlow.Show(gameObject);
    }

    // защищаемся от повторного завершения, показываем нужный визуальный эффект и удаляем сетевой объект.
    [Server]
    private void Finish(bool impact = false, Vector3 impactPosition = default)
    {
        if (consumed) return;
        consumed = true;
        if (definition.mode == ElementalCastMode.Bolt)
        {
            if (impact)
            {
                // хост удаляет сетевой объект до обработки очереди rpc, поэтому запускаем графику сразу.
                if (NetworkClient.active) ShowProjectileImpact(impactPosition);
                RpcProjectileImpact(impactPosition);
            }
        }
        else
        {
            if (NetworkClient.active) ShowBurst(transform.position);
            RpcBurst(transform.position);
        }
        NetworkServer.Destroy(gameObject);
    }
    // создаём локальные частицы попадания на клиентах.
    [ClientRpc]
    private void RpcProjectileImpact(Vector3 position)
    {
        if (!isServer) ShowProjectileImpact(position);
    }
    // один и тот же эффект для хоста и остальных клиентов.
    private void ShowProjectileImpact(Vector3 position) => SpellVfx.Impact(position, definition.tint, definition.radius, definition.hitEffect);
    // показываем завершающую вспышку там, где она предусмотрена типом заклинания.
    [ClientRpc]
    private void RpcBurst(Vector3 position)
    {
        if (!isServer) ShowBurst(position);
    }
    // длительные области не создают вспышку при завершении.
    private void ShowBurst(Vector3 position)
    {
        // взрыв вокруг мага наносит урон один раз; дольше остаётся только его локальное изображение.
        if (definition.mode == ElementalCastMode.GroundZone || definition.mode == ElementalCastMode.Tornado) return;
        SpellVfx.Burst(position, definition.tint, Mathf.Max(0.4f, definition.radius),
            definition.hitEffect);
    }
}

