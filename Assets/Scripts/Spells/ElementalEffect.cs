using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class ElementalEffect : NetworkBehaviour
{
    public ElementalSpell definition;
    [SyncVar] public uint ownerId;
    private double expiresAt;
    private double nextTick;
    private Vector3 previousPosition;
    private bool consumed;
    private readonly HashSet<Health> affected = new HashSet<Health>();

    public override void OnStartServer()
    {
        previousPosition = transform.position;
        expiresAt = NetworkTime.time + definition.duration;
        nextTick = NetworkTime.time;
    }
    public override void OnStartClient()
    {
        if (!isServer) GetComponent<Rigidbody>().isKinematic = true;
    }
    private void Update()
    {
        if (!isServer || consumed || definition == null) return;
        if (NetworkTime.time >= expiresAt) { Finish(); return; }
        if (definition.mode == ElementalCastMode.Bolt)
        {
            if(ProjectileContact.Sweep(transform,ownerId,previousPosition,out var hit))Impact(hit);
            previousPosition = transform.position;
        }
        else if (NetworkTime.time >= nextTick)
        {
            nextTick = NetworkTime.time + Mathf.Max(0.1f, definition.tickInterval);
            ApplyArea();
            if (definition.mode == ElementalCastMode.SelfBurst) Finish();
        }
    }
    private bool Ignore(Collider other)
    {
        return !ProjectileContact.CanHit(other,transform,ownerId);
    }
    private void OnTriggerEnter(Collider other)
    {
        if (!isServer || consumed || definition.mode != ElementalCastMode.Bolt || Ignore(other)) return;
        Impact(other);
    }
    private void Impact(Collider other)
    {
        if (TacticalEffect.TryReflect(other,transform,ownerId,out uint reflected)) { ownerId=reflected;previousPosition=transform.position;return; }
        other.GetComponentInParent<TacticalEffect>()?.ProjectileHit(ownerId);
        var direct = other.GetComponentInParent<Health>();
        bool headshot = direct != null && SpellDamage.IsHeadshot(direct, other.ClosestPoint(transform.position));
        if (definition.radius > 0.5f) ApplyArea(direct, headshot);
        else ApplyTarget(direct, headshot);
        Finish(true);
    }
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
    [Server]
    private void Finish(bool impact = false)
    {
        if (consumed) return;
        consumed = true;
        if (definition.mode == ElementalCastMode.Bolt)
        {
            if (impact) RpcProjectileImpact(transform.position);
        }
        else RpcBurst(transform.position);
        NetworkServer.Destroy(gameObject);
    }
    [ClientRpc]
    private void RpcProjectileImpact(Vector3 position) => SpellVfx.Impact(position, definition.tint, definition.radius);
    [ClientRpc]
    private void RpcBurst(Vector3 position)
    {
        // A self burst deals damage once; only its local visual lingers.
        if (definition.mode == ElementalCastMode.GroundZone || definition.mode == ElementalCastMode.Tornado) return;
        SpellVfx.Burst(position, definition.tint, Mathf.Max(0.4f, definition.radius),
            definition.mode == ElementalCastMode.SelfBurst ? 3f : .5f);
    }
}

