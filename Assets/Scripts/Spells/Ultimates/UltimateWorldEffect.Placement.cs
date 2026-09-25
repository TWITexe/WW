using Mirror;
using UnityEngine;

public partial class UltimateWorldEffect
{
    public struct MirrorPose { public Vector3 position; public float yaw; }
    public readonly SyncList<MirrorPose> mirrorPoses = new SyncList<MirrorPose>();
    [SyncVar] private Vector3 islandOrigin;
    [SyncVar] private double islandStarted, collapseAt;
    [SyncVar] private float collapseHeight;
    [SyncVar] private float riseHeight;
    private static readonly System.Collections.Generic.HashSet<UltimateWorldEffect> platforms = new System.Collections.Generic.HashSet<UltimateWorldEffect>();
    public bool Collapsing => collapseAt > 0;
    void OnEnable() { if (kind == UltimateKind.EarthDepths) platforms.Add(this); }
    void OnDisable() => platforms.Remove(this);

    public static bool MirrorPlacementClear(Vector3 ground, float yaw)
    {
        var overlaps = Physics.OverlapBox(ground + Vector3.up * 1.64f, new Vector3(1.08f, 1.56f, .16f),
            Quaternion.Euler(0, yaw, 0), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        return overlaps.Length == 0;
    }
    [Server] public bool PlaceMirror(Vector3 ground, float yaw)
    {
        if (finished || kind != UltimateKind.MirrorLabyrinth || mirrorPoses.Count >= 6 || !MirrorPlacementClear(ground, yaw)) return false;
        int index = mirrorPoses.Count;
        mirrorPoses.Add(new MirrorPose { position = ground + Vector3.up * 1.64f, yaw = Mathf.Repeat(yaw, 360) });
        intactMirrors |= 1 << index;
        RefreshMirrorPoses(); ApplyMirrorMask(); Physics.SyncTransforms();
        return true;
    }
    void RefreshMirrorPoses()
    {
        if (mirrors == null) return;
        for (int i = 0; i < mirrorPoses.Count && i < mirrors.Length; i++)
            mirrors[i].transform.SetPositionAndRotation(mirrorPoses[i].position, Quaternion.Euler(0, mirrorPoses[i].yaw, 0));
    }
    [Server] public void SetExpiry(double value) => expiresAt = value;

    public float IslandY(double now)
    {
        if (collapseAt > 0 && now >= collapseAt)
            return islandOrigin.y + Mathf.Max(0, collapseHeight - 4 * (float)(now - collapseAt) - 16 * Mathf.Pow((float)(now - collapseAt), 2));
        float t = Mathf.Clamp01((float)(now - islandStarted) / Mathf.Max(.1f, catalog.islandRiseTime));
        return islandOrigin.y + riseHeight * Mathf.SmoothStep(0, 1, t);
    }
    [Server] public bool RequestCollapse()
    {
        if (finished || kind != UltimateKind.EarthDepths || collapseAt > 0) return false;
        collapseHeight = Mathf.Max(0, IslandY(NetworkTime.time) - islandOrigin.y);
        collapseAt = NetworkTime.time;
        expiresAt = collapseAt + 2;
        return true;
    }
    void StepIsland(double now)
    {
        transform.position = new Vector3(islandOrigin.x, IslandY(now), islandOrigin.z);
        if (isServer && collapseAt > 0 && now > collapseAt && transform.position.y <= islandOrigin.y + .01f) ServerFinish(true);
    }
    // Uses the same analytic platform motion in server simulation and client prediction.
    public static Vector3 PlatformDisplacement(Vector3 feet, float dt, double now)
    {
        foreach (var platform in platforms)
        {
            if (platform == null || platform.finished || platform.catalog == null) continue;
            float before = platform.IslandY(now - dt);
            Vector3 delta = feet - platform.islandOrigin;
            if (Mathf.Abs(feet.y - before) > .3f || delta.x * delta.x + delta.z * delta.z > Mathf.Pow(platform.catalog.islandRadius - .15f, 2)) continue;
            return Vector3.up * (platform.IslandY(now) - before);
        }
        return Vector3.zero;
    }
}
