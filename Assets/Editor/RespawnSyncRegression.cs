using System;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEngine;

// Проверяет реальные сериализаторы Mirror и порядок доставки телепорта, без запуска матча.
public static class RespawnSyncRegression
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<GameObject> objects = new List<GameObject>();
    static int checks;

    static PlayerServerTransform Create(string name, Vector3 position)
    {
        var go = new GameObject(name);
        go.hideFlags = HideFlags.HideAndDontSave;
        objects.Add(go);
        go.AddComponent<NetworkIdentity>();
        go.AddComponent<CharacterController>();
        var sync = go.AddComponent<PlayerServerTransform>();
        typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours", Private).Invoke(go.GetComponent<NetworkIdentity>(), null);
        sync.target = go.transform;
        sync.transform.position = position;
        sync.syncScale = true;
        return sync;
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Respawn sync: " + message);
        checks++;
    }

    static void Teleport(PlayerServerTransform sync, Vector3 position, bool positionOnly = false)
    {
        Type[] signature = positionOnly ? new[] { typeof(Vector3) } : new[] { typeof(Vector3), typeof(Quaternion) };
        object[] args = positionOnly ? new object[] { position } : new object[] { position, Quaternion.Euler(0, 70, 0) };
        typeof(PlayerServerTransform).GetMethod("OnTeleport", Private, null, signature, null).Invoke(sync, args);
    }

    static byte[] Serialize(PlayerServerTransform sync, bool initial = false)
    {
        using (var writer = NetworkWriterPool.Get())
        {
            sync.OnSerialize(writer, initial);
            return writer.ToArray();
        }
    }

    static void Receive(PlayerServerTransform sync, byte[] packet, bool initial = false)
    {
        using (var reader = NetworkReaderPool.Get(new ArraySegment<byte>(packet)))
            sync.OnDeserialize(reader, initial);
    }

    static Vector3 Decoded(PlayerServerTransform sync, string field, float precision) =>
        Compression.ScaleToFloat((Vector3Long)typeof(NetworkTransformReliable).GetField(field, Private).GetValue(sync), precision);

    static void CheckPose(PlayerServerTransform server, params PlayerServerTransform[] clients)
    {
        foreach (var client in clients)
        {
            Check(Vector3.Distance(Decoded(client, "lastDeserializedPosition", client.positionPrecision), server.transform.position) < .02f,
                client.name + " decodes the server position, including height");
            Check(Vector3.Distance(Decoded(client, "lastDeserializedScale", client.scalePrecision), server.transform.localScale) < .02f,
                client.name + " decodes the server scale");
        }
    }

    [MenuItem("Tools/Wizard War/Validate respawn synchronization")]
    public static void Run()
    {
        if (NetworkServer.active || NetworkClient.active)
            throw new InvalidOperationException("Stop the match before running this regression.");
        checks = 0;
        try
        {
            var server = Create("Server", new Vector3(12, -8, 35));
            var observer = Create("Observer", Vector3.zero);
            var owner = Create("Owner", Vector3.zero);
            typeof(NetworkIdentity).GetProperty("isClient").SetValue(owner.netIdentity, true);
            typeof(NetworkIdentity).GetProperty("isOwned").SetValue(owner.netIdentity, true);
            var initial = Serialize(server, true);
            Receive(observer, initial, true);
            Receive(owner, initial, true);

            // Хост сначала переносит сервер, затем получает собственный RPC.
            // Между этими вызовами уже может быть сериализовано новое состояние.
            for (int life = 0; life < 4; life++)
            {
                var spawn = new Vector3(-20 + life * 9, 3 + life * 2, -15);
                var old = new TransformSnapshot(1, 1, server.transform.position, Quaternion.identity, Vector3.one);
                server.serverSnapshots[1] = old;
                observer.clientSnapshots[1] = old;
                Teleport(server, spawn);
                byte[] first = Serialize(server);
                Teleport(observer, spawn);
                Teleport(owner, spawn);
                Check(observer.transform.position == spawn, "observer snaps to spawn immediately");
                Check(server.serverSnapshots.Count == 0 && observer.clientSnapshots.Count == 0, "old interpolation history cleared");
                Receive(observer, first);
                Receive(owner, first);

                Teleport(server, spawn); // отложенная локальная доставка RPC на хосте
                server.transform.position += new Vector3(.35f, -.12f, .6f);
                server.transform.localScale = Vector3.one * (1 + life * .1f);
                byte[] next = Serialize(server);
                Receive(observer, next);
                Receive(owner, next);
                CheckPose(server, observer, owner);
            }

            var newcomer = Create("Late joiner", Vector3.zero);
            Receive(newcomer, Serialize(server, true), true);
            server.transform.position += Vector3.forward * .4f;
            byte[] update = Serialize(server);
            Receive(observer, update);
            Receive(owner, update);
            Receive(newcomer, update);
            CheckPose(server, observer, owner, newcomer);

            var ownerPosition = owner.transform.position;
            Teleport(owner, Vector3.one * 100);
            Check(owner.transform.position == ownerPosition, "owner pose remains under epoch-checked movement reconciliation");
            Teleport(owner, Vector3.one * 200, true);
            Check(owner.transform.position == ownerPosition, "position-only teleport also respects owner reconciliation");

            var controller = server.GetComponent<CharacterController>();
            foreach (bool enabled in new[] { true, false })
            {
                controller.enabled = enabled;
                Teleport(server, new Vector3(4, 6, 8), true);
                Check(controller.enabled == enabled, "teleport preserves CharacterController enabled state");
                Check(server.transform.position == new Vector3(4, 6, 8), "position-only teleport updates position");
            }

            // Настоящий сброс между сессиями по-прежнему должен очищать базы сжатия.
            server.ResetState();
            observer.ResetState();
            Check(Decoded(server, "lastSerializedPosition", server.positionPrecision) == Vector3.zero,
                "session reset clears serialization baseline");
            Check(Decoded(observer, "lastDeserializedPosition", observer.positionPrecision) == Vector3.zero,
                "session reset clears deserialization baseline");
            Debug.Log("RESPAWN_SYNC_PASSED: " + checks + " checks");
        }
        finally
        {
            foreach (var go in objects) UnityEngine.Object.DestroyImmediate(go);
            objects.Clear();
        }
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
