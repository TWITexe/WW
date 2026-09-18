using System.Collections.Generic;
using Mirror;
using UnityEngine;

// серверная логика стихийного снаряда или области: попадания, периодический урон и дополнительные воздействия.
public class ElementalEffect : NetworkBehaviour
{
    public ElementalSpell definition;
    [SyncVar] public uint ownerId;
    private double expiresAt;
    private double nextTick;
    private Vector3 previousPosition;
    private bool consumed;
    private readonly HashSet<Health> affected = new HashSet<Health>();

    // запоминаем точку появления и задаём серверные сроки действия и первого срабатывания.
    public override void OnStartServer()
    {
        previousPosition = transform.position;
        expiresAt = NetworkTime.time + definition.duration;
        nextTick = NetworkTime.time;
    }
    // удалённые клиенты получают позицию по сети и не симулируют Rigidbody самостоятельно.
    public override void OnStartClient()
    {
        if (!isServer) GetComponent<Rigidbody>().isKinematic = true;
    }
    // сервер проверяет путь снаряда либо выполняет очередное воздействие области до окончания её жизни.
    private void Update()
    {
        if (!isServer || consumed || definition == null) return;
        if (NetworkTime.time >= expiresAt) { Finish(); return; }
        if (definition.mode == ElementalCastMode.Bolt)
        {
            if (ProjectileContact.Sweep(transform, ownerId, previousPosition, out var hit, out var point)) Impact(hit, point);
            previousPosition = transform.position;
        }
        else if (NetworkTime.time >= nextTick)
        {
            nextTick = NetworkTime.time + Mathf.Max(0.1f, definition.tickInterval);
            ApplyArea();
            if (definition.mode == ElementalCastMode.SelfBurst) Finish();
        }
    }
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
        if (TacticalEffect.TryReflect(other,transform,ownerId,out uint reflected)) { ownerId=reflected;previousPosition=transform.position;return; }
        other.GetComponentInParent<TacticalEffect>()?.ProjectileHit(ownerId);
        var direct = other.GetComponentInParent<Health>();
        bool headshot = direct != null && SpellDamage.IsHeadshot(direct, other.ClosestPoint(transform.position));
        if (definition.radius > 0.5f) ApplyArea(direct, headshot);
        else ApplyTarget(direct, headshot);
        Finish(true, point);
    }
    // собираем цели в радиусе; несколько коллайдеров одной цели не должны умножать урон за одно срабатывание.
    [Server]
    private void ApplyArea(Health direct = null, bool headshot = false)
    {
        affected.Clear();
        foreach (Collider collider in Physics.OverlapSphere(transform.position, definition.radius))
        {
            Health health = collider.GetComponentInParent<Health>();
            if (health != null && affected.Add(health)) ApplyTarget(health, health == direct && headshot);
        }
    }
    // исключаем владельца и мёртвых, наносим урон, затем применяем толчок, подброс и замедление.
    [Server]
    private void ApplyTarget(Health health, bool headshot = false)
    {
        if (health == null || health.netId == ownerId || health.IsDead) return;
        health.TakeSpellDamage(definition.damage, ownerId, headshot);
        var movement = health.GetComponent<RelativeMovement>();
        if (movement == null || health.IsDead) return;
        Vector3 outward = (health.transform.position - transform.position);
        outward.y = 0;
        if (outward.sqrMagnitude < 0.01f) outward = transform.forward;
        Vector3 force = outward.normalized * definition.knockback + Vector3.up * definition.lift;
        if (force.sqrMagnitude > 0) movement.ServerAddExternalForce(force);
        if (definition.slow < 1) movement.ApplySlow(definition.slow, definition.slowDuration);
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

