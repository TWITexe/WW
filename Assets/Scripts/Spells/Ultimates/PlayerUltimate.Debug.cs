using Mirror;

// Temporary host-only testing control. Release players cannot use it.
public partial class PlayerUltimate
{
    public bool CanDebugCharge
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return isServer && isLocalPlayer && connectionToClient == NetworkServer.localConnection &&
                NetManager.CombatAllowed && Definition != null;
#else
            return false;
#endif
        }
    }

    public void DebugChargeUltimate()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!CanDebugCharge) return;
        chargePoints = UltimateCatalog.FullChargePoints;
#endif
    }
}
