using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class PlayerNetworkCaster : NetworkBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private Transform firePoint;
    [SyncVar] private ElementLoadout loadout;
    [SyncVar] private bool loadoutReady;
    private SpellManager spells;
    private Health health;
    private readonly List<MagicElement> serverInput = new List<MagicElement>(3);
    private readonly Dictionary<Spell, double> serverCooldowns = new Dictionary<Spell, double>();
    private readonly Dictionary<Spell, double> clientCooldowns = new Dictionary<Spell, double>();
    private double lastInputTime;
    private bool resolvingCommand;
    private Ray commandAimRay;
    private Vector3 commandMoveDirection;
    public ElementLoadout Loadout => loadout;
    public bool LoadoutReady => loadoutReady;
    private void Awake()
    {
        spells = GetComponent<SpellManager>();
        health = GetComponent<Health>();
    }
    public override void OnStartLocalPlayer()
    {
        CmdSetLoadout(LocalPlayerSettings.Instance != null ?
            LocalPlayerSettings.Instance.Loadout : ElementLoadout.Default);
    }
    [Command]
    private void CmdSetLoadout(ElementLoadout requested)
    {
        // Lock the selection for the lifetime of this player, including respawns.
        if (loadoutReady) return;
        loadout = requested.IsValid ? requested : ElementLoadout.Default;
        loadoutReady = true;
    }
    public void SubmitElement(int slot)
    {
        if (!isLocalPlayer || !loadoutReady || PlayerGameUI.InputBlocked) return;
        if (playerCamera == null) return;
        Ray ray=playerCamera.ViewportPointToRay(new Vector3(.5f,.5f));
        var movement = GetComponent<RelativeMovement>();
        CmdSubmitElement(slot, ray.origin, ray.direction, movement != null ? movement.PlanarInputDirection : Vector3.zero);
    }
    public Vector3 AimDirection()
    {
        if (playerCamera == null || firePoint == null) return transform.forward;
        return ResolveAimDirection(playerCamera.ViewportPointToRay(new Vector3(.5f,.5f)));
    }
    private bool FirstAimHit(Ray ray,float range,out RaycastHit hit)
    {
        var hits=Physics.RaycastAll(ray,range,~(1<<2),QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
        foreach(var candidate in hits)
        {
            var collider=candidate.collider;
            if(collider.transform.IsChildOf(transform.root)||collider.GetComponentInParent<ElementalEffect>()!=null||
                collider.GetComponentInParent<FireballProjectile>()!=null||collider.GetComponentInParent<WindFlowProjectile>()!=null)continue;
            var target=collider.GetComponentInParent<Health>();if(target!=null&&target.IsDead)continue;
            hit=candidate;return true;
        }
        hit=default;return false;
    }
    public Vector3 ShotOrigin()
    {
        if(firePoint==null)return transform.position;
        Vector3 anchor=transform.position+Vector3.up*.6f;
        Vector3 delta=firePoint.position-anchor;
        if(delta.sqrMagnitude>.0001f&&FirstAimHit(new Ray(anchor,delta.normalized),delta.magnitude,out var wall))
            return wall.point-delta.normalized*.02f;
        return firePoint.position;
    }
    public Vector3 ResolveAimDirection(Ray ray)
    {
        Vector3 point=FirstAimHit(ray,100,out var hit)?hit.point:ray.GetPoint(100);
        return (point-ShotOrigin()).normalized;
    }
    public bool TryGroundTarget(Ray ray,out Vector3 point)
    {
        point=default;
        if(!FirstAimHit(ray,100,out var hit)||hit.normal.y<.6f||hit.collider.GetComponentInParent<Health>()!=null)return false;
        Vector3 origin=ShotOrigin();Vector3 delta=hit.point-origin;
        if(delta.sqrMagnitude>18*18)return false;
        if(delta.sqrMagnitude>.0001f&&FirstAimHit(new Ray(origin,delta.normalized),delta.magnitude+.01f,out var blocker)&&Vector3.Distance(blocker.point,hit.point)>.05f)return false;
        point=hit.point;return true;
    }
    [Command]
    private void CmdSubmitElement(int slot, Vector3 viewOrigin, Vector3 viewDirection, Vector3 moveDirection)
    {
        if (!loadoutReady || spells == null || slot < 0 || slot > 2 ||
            (health != null && health.IsDead) || !ValidDirection(viewDirection) ||
            !Finite(viewOrigin) || !Finite(moveDirection) || moveDirection.sqrMagnitude > 1.1f || (viewOrigin-transform.position).sqrMagnitude>100)
        {
            serverInput.Clear();
            return;
        }
        double now = NetworkTime.time;
        if (now - lastInputTime >= InputComboTracker.InputTimeout) serverInput.Clear();
        lastInputTime = now;
        if (serverInput.Count == 3) serverInput.RemoveAt(0);
        serverInput.Add(loadout.Get(slot));
        int index = spells.FindSpell(serverInput, loadout);
        if (index < 0) return;
        serverInput.Clear();
        Spell spell = spells.GetSpell(index);
        if (serverCooldowns.TryGetValue(spell, out double readyAt) && now < readyAt) return;
        commandAimRay=new Ray(viewOrigin,viewDirection.normalized);
        commandMoveDirection=Vector3.ProjectOnPlane(moveDirection,Vector3.up).normalized;
        resolvingCommand=true;
        bool activated;
        try { activated=spell.ActivateServer(this,ResolveAimDirection(commandAimRay)); }
        finally { resolvingCommand=false; }
        if (!activated) return;
        readyAt = now + spell.Cooldown;
        serverCooldowns[spell] = readyAt;
        TargetSetCooldown(index, readyAt);
    }
    private void Update()
    {
        if (isServer && ((health != null && health.IsDead) || NetworkTime.time - lastInputTime >= InputComboTracker.InputTimeout)) serverInput.Clear();
    }
    private static bool ValidDirection(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z) &&
            value.sqrMagnitude > 0.001f && value.sqrMagnitude < 100f;
    }
    private static bool Finite(Vector3 value)=>!float.IsNaN(value.x)&&!float.IsNaN(value.y)&&!float.IsNaN(value.z)&&!float.IsInfinity(value.x)&&!float.IsInfinity(value.y)&&!float.IsInfinity(value.z);
    [TargetRpc]
    private void TargetSetCooldown(int index, double readyAt)
    {
        Spell spell = spells.GetSpell(index);
        if (spell != null) clientCooldowns[spell] = readyAt;
    }
    public double RemainingCooldown(Spell spell) => clientCooldowns.TryGetValue(spell, out double readyAt) ?
        System.Math.Max(0, readyAt - NetworkTime.time) : 0;
    [Server]
    public bool CastTactical(TacticalSpell spell, Vector3 direction)
    {
        if (spell == null || spell.effectPrefab == null || !ValidDirection(direction) || health == null || health.IsDead) return false;
        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (flat.sqrMagnitude < .01f) flat = transform.forward;
        var cc = GetComponent<CharacterController>();
        Vector3 feet = cc != null ? transform.TransformPoint(cc.center) - Vector3.up * cc.height * transform.lossyScale.y * .5f : transform.position - Vector3.up;
        Vector3 position = feet + Vector3.up * .05f;
        if (spell.kind == TacticalKind.StoneWall || spell.kind == TacticalKind.FireSeal || spell.kind == TacticalKind.GravityWell)
        {
            if (!TryGroundTarget(resolvingCommand ? commandAimRay : new Ray(ShotOrigin(),direction), out var ground)) return false;
            position = ground + Vector3.up * .05f;
            if (spell.kind == TacticalKind.StoneWall)
            {
                // Do not materialize a solid wall inside a player or existing geometry.
                if (Physics.OverlapBox(position + Vector3.up * 1.35f, new Vector3(1.9f,1.35f,.275f), Quaternion.LookRotation(flat), ~(1<<2), QueryTriggerInteraction.Ignore).Length > 0) return false;
            }
        }
        else if (spell.kind == TacticalKind.IceMirror) position = transform.position + flat * 1.5f;
        if (spell.kind == TacticalKind.SteamDash)
        {
            var movement = GetComponent<RelativeMovement>();
            if (movement == null) return false;
            movement.ServerDash(resolvingCommand && commandMoveDirection.sqrMagnitude > .01f ? commandMoveDirection : flat);
        }
        var effect = Instantiate(spell.effectPrefab,position,Quaternion.LookRotation(flat));
        effect.GetComponent<TacticalEffect>().ownerId = netId;
        NetworkServer.Spawn(effect);
        return true;
    }
    [Server]
    public bool CastElemental(ElementalSpell spell, Vector3 direction)
    {
        if (spell == null || !ValidDirection(direction)) return false;
        if (spell.mode == ElementalCastMode.Shield)
        {
            if (health == null) return false;
            health.GrantShield(spell.shieldAmount, spell.duration);
            RpcShieldFlash(spell.tint);
            return true;
        }
        if (spell.effectPrefab == null || firePoint == null) return false;
        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (flat.sqrMagnitude < 0.01f) flat = transform.forward;
        Vector3 position = ShotOrigin();
        if (spell.mode == ElementalCastMode.GroundZone)
        {
            Ray ray=resolvingCommand?commandAimRay:new Ray(ShotOrigin(),direction.normalized);
            if(!TryGroundTarget(ray,out var ground))return false;
            position=ground+Vector3.up*.25f;
        }
        if (spell.mode == ElementalCastMode.SelfBurst) position = transform.position;
        var effect = Instantiate(spell.effectPrefab, position, Quaternion.LookRotation(spell.mode==ElementalCastMode.Bolt?direction.normalized:flat));
        effect.GetComponent<ElementalEffect>().ownerId = netId;
        var body = effect.GetComponent<Rigidbody>();
        body.useGravity=false;body.linearDamping=0;
        body.linearVelocity = spell.mode == ElementalCastMode.Bolt || spell.mode == ElementalCastMode.Tornado ? direction.normalized * spell.speed : Vector3.zero;
        foreach (Collider source in GetComponentsInChildren<Collider>())
            foreach (Collider target in effect.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(source, target);
        NetworkServer.Spawn(effect);
        return true;
    }
    [ClientRpc]
    private void RpcShieldFlash(Color tint) => SpellVfx.Burst(transform.position, tint, 2);
    [Server]
    public bool SpawnProjectile(GameObject prefab, float speed, Vector3 direction)
    {
        if (prefab == null || firePoint == null || !ValidDirection(direction) ||
            prefab.GetComponent<Rigidbody>() == null || prefab.GetComponent<NetworkIdentity>() == null) return false;
        direction.Normalize();
        GameObject projectile = Instantiate(prefab, ShotOrigin(), Quaternion.LookRotation(direction));
        if (projectile.TryGetComponent<FireballProjectile>(out var fire)) fire.ownerId = netId;
        if (projectile.TryGetComponent<WindFlowProjectile>(out var wind)) wind.ownerId = netId;
        // Ignore every collider of the caster, including child colliders.
        foreach (Collider source in GetComponentsInChildren<Collider>())
            foreach (Collider target in projectile.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(source, target);
        var body=projectile.GetComponent<Rigidbody>();body.useGravity=false;body.linearDamping=0;body.linearVelocity = direction * speed;
        NetworkServer.Spawn(projectile);
        return true;
    }
}

