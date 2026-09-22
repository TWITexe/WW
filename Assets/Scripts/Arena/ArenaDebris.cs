using System.Collections.Generic;
using Mirror;
using UnityEngine;

// лёгкие локальные обломки не наносят урон и не загружают сеть синхронизацией каждой щепки.
public class ArenaDebris : MonoBehaviour
{
    private const int Maximum = 48;
    private static readonly Queue<ArenaDebris> fragments = new Queue<ArenaDebris>();
    private static Material material;
    private float expiresAt;
    private Vector3 initialScale;

    public static void Emit(Bounds bounds, Color color, int count, Vector3 origin, float force)
    {
        if (material == null) material = Resources.Load<Material>("ArenaDebris");
        if (material == null) return;
        for (int i = 0; i < count; i++)
        {
            while (fragments.Count >= Maximum)
            {
                var old = fragments.Dequeue();
                if (old != null) Destroy(old.gameObject);
            }
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = "обломок укрытия";
            piece.layer = 2; // снаряды и прицел игнорируют косметические обломки.
            piece.transform.position = bounds.center + Vector3.Scale(Random.insideUnitSphere, bounds.extents * .6f);
            piece.transform.rotation = Random.rotation;
            piece.transform.localScale = new Vector3(Random.Range(.25f,.65f), Random.Range(.2f,.45f), Random.Range(.3f,.7f));
            var renderer = piece.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color * Random.Range(.85f, 1.15f));
            renderer.SetPropertyBlock(block);
            // обломки сталкиваются с ландшафтом, но не мешают персонажам и друг другу.
            var collider = piece.GetComponent<Collider>();
            foreach (var identity in NetworkClient.spawned.Values)
                if (identity != null && identity.GetComponentInChildren<Health>() != null)
                    foreach (var playerCollider in identity.GetComponentsInChildren<Collider>())
                        Physics.IgnoreCollision(collider, playerCollider);
            foreach (var old in fragments)
                if (old != null) Physics.IgnoreCollision(collider, old.GetComponent<Collider>());
            var body = piece.AddComponent<Rigidbody>();
            body.mass = 1;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = ((piece.transform.position - origin).normalized + Vector3.up * .65f).normalized * force;
            body.angularVelocity = Random.insideUnitSphere * 8;
            var lifetime = piece.AddComponent<ArenaDebris>();
            lifetime.expiresAt = Time.time + 5;
            lifetime.initialScale = piece.transform.localScale;
            fragments.Enqueue(lifetime);
        }
    }

    private void Update()
    {
        float remaining = expiresAt - Time.time;
        if (remaining <= 0) { Destroy(gameObject); return; }
        if (remaining < 1) transform.localScale = initialScale * remaining;
    }
}
