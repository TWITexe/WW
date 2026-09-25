using System.Collections;
using Mirror;
using UnityEngine;

// подтверждение области и направление струи проверяются сервером так же, как обычное нажатие стихии.
public partial class PlayerNetworkCaster
{
    private Spell pendingServerSpell, pendingLocalSpell;
    private uint pendingServerToken, pendingLocalToken;
    private double pendingServerUntil, pendingLocalUntil;
    private LineRenderer areaAim;
    private Coroutine channelRoutine;
    private bool emittingChannelDrop;
    private Ray channelRay;
    private float channelLocalUntil, nextChannelAim;
    private double nextServerChannelAim;
    public string AreaAimCaption => pendingLocalSpell == null ? null :
        pendingLocalSpell.Name + " · ЛКМ — применить · " + Mathf.CeilToInt((float)(pendingLocalUntil-NetworkTime.time)) + " с";

    public void CancelForUltimate()
    {
        if (pendingLocalSpell != null) CmdCancelArea(pendingLocalToken);
        CancelLocalAreaAim();
        GetComponent<InputComboTracker>()?.Clear();
    }
    [Server] public void ServerCancelForUltimate()
    {
        pendingServerSpell = null; serverInput.Clear();
        if (channelRoutine != null) StopCoroutine(channelRoutine);
        channelRoutine = null; channelLocalUntil = 0;
    }

    public static bool RequiresAreaConfirmation(Spell spell)
    {
        if (spell is ElementalSpell elemental) return elemental.mode == ElementalCastMode.GroundZone && elemental.name != "SmokeCloud";
        if (spell is TacticalSpell tactical) return tactical.kind == TacticalKind.FireSeal || tactical.kind == TacticalKind.GravityWell || tactical.kind == TacticalKind.StoneWall;
        return spell is AdvancedSpell advanced && !advanced.IsProjectile && advanced.kind != AdvancedSpellKind.SteamLens && advanced.kind != AdvancedSpellKind.IceBridge;
    }

    [TargetRpc] private void TargetArmArea(int index, uint token, double until)
    {
        if (token != localInputNumber) return;
        CancelLocalAreaAim();
        pendingLocalSpell = spells.GetSpell(index);
        pendingLocalToken = token;
        pendingLocalUntil = until;
        GetComponent<InputComboTracker>()?.Clear();
        var obj = new GameObject("прицел области");
        obj.layer = 2;
        areaAim = obj.AddComponent<LineRenderer>();
        areaAim.sharedMaterial = Resources.Load<Material>("AreaTarget");
        areaAim.useWorldSpace = true;
        areaAim.loop = true;
        areaAim.positionCount = 64;
        areaAim.widthMultiplier = .08f;
        areaAim.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void CancelLocalAreaAim()
    {
        pendingLocalSpell = null;
        if (areaAim != null) Destroy(areaAim.gameObject);
        areaAim = null;
    }

    private void UpdateAreaAim()
    {
        if (isServer && pendingServerSpell != null && (NetworkTime.time >= pendingServerUntil || health.IsDead || movementController.IsStunned)) pendingServerSpell = null;
        if (!isLocalPlayer || pendingLocalSpell == null) return;
        // смена комбинации в тот же кадр имеет приоритет над подтверждением мышью.
        if (NetworkTime.time >= pendingLocalUntil || health.IsDead || movementController.IsStunned || PlayerGameUI.InputBlocked || Input.GetMouseButtonDown(1) ||
            Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.R))
        {
            CmdCancelArea(pendingLocalToken);
            CancelLocalAreaAim();
            return;
        }
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(.5f,.5f));
        bool valid = TryAreaTarget(pendingLocalSpell,ray, out var point);
        areaAim.enabled = valid;
        if (!valid) return;
        float radius = pendingLocalSpell is ElementalSpell e ? e.radius : pendingLocalSpell is TacticalSpell t ? Mathf.Max(2,t.radius) : ((AdvancedSpell)pendingLocalSpell).radius;
        areaAim.startColor = areaAim.endColor = new Color(.2f,1,.75f,.9f);
        for(int i=0;i<64;i++)
        {
            float angle=i*Mathf.PI*2/64;
            areaAim.SetPosition(i,point+new Vector3(Mathf.Cos(angle)*radius,.06f,Mathf.Sin(angle)*radius));
        }
        if (Input.GetMouseButtonDown(0)) CmdConfirmArea(pendingLocalToken,ray.origin,ray.direction);
    }

    [Command] private void CmdCancelArea(uint token)
    {
        if (token == pendingServerToken) pendingServerSpell = null;
    }

    [Command] private void CmdConfirmArea(uint token, Vector3 origin, Vector3 direction)
    {
        if (!NetManager.CombatAllowed || UltimateBlocksSpells || pendingServerSpell == null || token != pendingServerToken || NetworkTime.time >= pendingServerUntil ||
            health.IsDead || movementController.IsStunned || !Finite(origin) || !ValidDirection(direction) ||
            (origin-transform.position).sqrMagnitude > 100) return;
        Spell spell = pendingServerSpell;
        if (ServerRemainingCooldown(spell, NetworkTime.time) > 0) return;
        commandAimRay = new Ray(origin,direction.normalized);
        if (!TryAreaTarget(spell,commandAimRay,out _)) return;
        resolvingCommand = true;
        bool activated;
        try { activated = spell.ActivateServer(this,ResolveAimDirection(commandAimRay)); }
        finally { resolvingCommand = false; }
        if (!activated) return;
        pendingServerSpell = null;
        serverCooldowns[spell] = NetworkTime.time + spell.Cooldown;
        for (int i=0;i<spells.Spells.Count;i++)
            if (spells.Spells[i]==spell) { TargetSetCooldown(i,serverCooldowns[spell]); break; }
        TargetFinishArea(token);
    }

    [TargetRpc] private void TargetFinishArea(uint token)
    {
        if (token == pendingLocalToken) CancelLocalAreaAim();
    }

    // стена сохраняет запасную точку перед магом, когда прицел направлен выше пола.
    private bool TryAreaTarget(Spell spell,Ray ray,out Vector3 point)
    {
        if(TryGroundTarget(ray,out point))return true;
        if(!(spell is TacticalSpell tactical) || tactical.kind!=TacticalKind.StoneWall)return false;
        var controller=GetComponent<CharacterController>();
        Vector3 feet=controller!=null ? transform.TransformPoint(controller.center)-Vector3.up*controller.height*transform.lossyScale.y*.5f : transform.position-Vector3.up;
        Vector3 forward=Vector3.ProjectOnPlane(transform.forward,Vector3.up).normalized;
        if(forward.sqrMagnitude<.01f)forward=Vector3.forward;
        Vector3 ahead=feet+forward*2.5f;
        return TryGroundTarget(new Ray(ahead+Vector3.up*2,Vector3.down),out point) && Mathf.Abs(point.y-feet.y)<=2;
    }

    // двенадцать капель за три секунды; каждая получает актуальный проверенный луч прицела.
    private IEnumerator EmitBoilingDrops(ElementalSpell spell)
    {
        for(int i=0;i<12;i++)
        {
            if (!NetManager.CombatAllowed || UltimateBlocksSpells || health.IsDead || movementController.IsStunned) break;
            emittingChannelDrop = true;
            try { CastElemental(spell,ResolveAimDirection(channelRay)); }
            finally { emittingChannelDrop = false; }
            yield return new WaitForSeconds(.25f);
        }
        channelRoutine = null;
    }
    [TargetRpc] private void TargetChannelStarted() => channelLocalUntil = Time.unscaledTime + 3;
    private void UpdateChannelAim()
    {
        if (!isLocalPlayer || Time.unscaledTime >= channelLocalUntil || Time.unscaledTime < nextChannelAim || playerCamera == null) return;
        nextChannelAim = Time.unscaledTime + .1f;
        Ray ray=playerCamera.ViewportPointToRay(new Vector3(.5f,.5f));
        CmdChannelAim(ray.origin,ray.direction);
    }
    [Command] private void CmdChannelAim(Vector3 origin,Vector3 direction)
    {
        if (channelRoutine == null || NetworkTime.time < nextServerChannelAim || !Finite(origin) || !ValidDirection(direction) || (origin-transform.position).sqrMagnitude>100) return;
        nextServerChannelAim = NetworkTime.time+.05;
        channelRay = new Ray(origin,direction.normalized);
    }

    // дуга долетает до точки прицела; дальность броска ограничена 18 метрами.
    private Vector3 ResolveThrowVelocity(Ray ray)
    {
        Vector3 start=ShotOrigin();
        Vector3 target=FirstAimHit(ray,25,out var hit)?hit.point:ray.GetPoint(18);
        Vector3 delta=Vector3.ClampMagnitude(target-start,18);
        const float flight=1.1f;
        return delta/flight+Vector3.up*(9.81f*flight*.5f);
    }
}
