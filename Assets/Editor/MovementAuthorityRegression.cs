using System;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// запускается в отдельной тестовой копии через -executeMethod MovementAuthorityRegression.Run.
[InitializeOnLoad]
public static class MovementAuthorityRegression
{
    const string Flag = "MovementAuthorityRegression.Active";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks, stage;
    static double started;
    static RelativeMovement movement;
    static Health health;
    static readonly Vector3 Origin = new Vector3(0, 1.1f, 0);

    static MovementAuthorityRegression() { EditorApplication.update += Tick; }
    public static void Run()
    {
        if (!Application.isBatchMode) throw new Exception("Run this regression in a separate batch-mode project copy.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Flag, true);
        EditorApplication.isPlaying = true;
    }
    static object Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, Private).Invoke(target, args);
    static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Movement regression: " + message);
        checks++;
        Debug.Log("MOVEMENT_CHECK: " + message);
    }
    static void Step(RelativeMovement motor, int count)
    {
        for (int i = 0; i < count; i++) Call(motor, "ServerStep");
    }
    static RelativeMovement.MoveInput Input(RelativeMovement motor, uint sequence, Vector3 direction) =>
        new RelativeMovement.MoveInput { epoch = (uint)Get(motor, "serverEpoch"), sequence = sequence, movement = direction, forward = Vector3.forward };
    static bool Receive(RelativeMovement motor, RelativeMovement.MoveInput input) => (bool)Call(motor, "ReceiveInput", input);
    static void Reset(RelativeMovement motor, Vector3 position)
    {
        motor.ServerTeleport(position, Quaternion.identity);
        Set(motor, "inputTokens", 12f);
        Physics.SyncTransforms();
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Flag, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (started == 0) started = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - started > 70) throw new Exception("Movement regression timeout");
            if (stage == 0)
            {
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "Movement test floor";
                floor.transform.position = new Vector3(0, -.5f, 0);
                floor.transform.localScale = new Vector3(150, 1, 150);
                var spawn = new GameObject("Movement test respawn").transform;
                spawn.position = new Vector3(-10, 1.1f, 0);
                var spawns = new GameObject("SpawnManager").AddComponent<SpawnManager>();
                Set(spawns, "points", new[] { spawn });
                var go = new GameObject("Movement test network");
                var transport = go.AddComponent<kcp2k.KcpTransport>();
                transport.Port = 17993;
                var manager = go.AddComponent<NetworkManager>();
                manager.transport = transport;
                manager.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
                manager.StartHost();
                stage = 1;
                return;
            }
            if (stage == 1)
            {
                if (NetworkClient.localPlayer == null) return;
                movement = NetworkClient.localPlayer.GetComponentInChildren<RelativeMovement>();
                health = movement.GetComponent<Health>();
                movement.enabled = false;
                Check(movement.GetComponent<PlayerServerTransform>() != null, "player uses prediction-aware transform");
                Check(movement.GetComponent<NetworkTransformBase>().syncDirection == SyncDirection.ServerToClient, "transform only accepts server authority");
                Reset(movement, Origin);
                Check(movement.ProbeGround(), "ground probe works at character feet");
                var input = Input(movement, 1, Vector3.right);
                Check(Receive(movement, input), "valid input accepted");
                Vector3 before = movement.transform.position;
                Check(movement.transform.position == before, "receiving input does not move player");
                Step(movement, 1);
                Check(Mathf.Abs(movement.transform.position.x - before.x - .12f) < .02f, "walk uses fixed server speed");
                Check(!Receive(movement, input), "duplicate sequence rejected");
                var invalid = Input(movement, 2, Vector3.right * 100);
                Check(!Receive(movement, invalid), "oversized movement rejected");
                invalid.movement = new Vector3(float.NaN, 0, 0);
                Check(!Receive(movement, invalid), "NaN rejected");
                invalid.movement = Vector3.up;
                Check(!Receive(movement, invalid), "client vertical movement rejected");
                invalid.movement = Vector3.zero; invalid.forward = new Vector3(float.PositiveInfinity, 0, 0);
                Check(!Receive(movement, invalid), "infinite facing rejected");
                invalid = Input(movement, 2, Vector3.right); invalid.epoch--;
                Check(!Receive(movement, invalid), "previous life input rejected");

                Reset(movement, Origin);
                input = Input(movement, 1, new Vector3(1, 0, 1).normalized); input.sprint = true;
                Receive(movement, input); before = movement.transform.position; Step(movement, 1);
                Check(Mathf.Abs(Vector3.ProjectOnPlane(movement.transform.position - before, Vector3.up).magnitude - .18f) < .02f, "diagonal sprint does not exceed configured speed");

                Reset(movement, Origin);
                input = Input(movement, 1, Vector3.zero); input.jump = true;
                Receive(movement, input); Step(movement, 1);
                Check((float)Get(movement, "vertSpeed") == 12, "configured jump impulse preserved");
                Step(movement, 150);
                Check(movement.transform.position.y < 1.2f && movement.transform.position.y > .8f, "jump lands on floor");

                Reset(movement, Origin);
                movement.ApplySlow(.5f, 5);
                Receive(movement, Input(movement, 1, Vector3.right)); before = movement.transform.position; Step(movement, 1);
                Check(Mathf.Abs(movement.transform.position.x - before.x - .06f) < .015f, "slow applies on server");

                Reset(movement, Origin);
                movement.ServerDash(Vector3.right); Step(movement, 11);
                Check(Mathf.Abs(movement.transform.position.x - 5.28f) < .04f, "dash moves 5.28m without client execution");

                Reset(movement, Origin);
                movement.ServerAddExternalForce(Vector3.right * 10);
                input = Input(movement, 1, Vector3.zero); input.blocked = true;
                Receive(movement, input); Step(movement, 1);
                Check(movement.transform.position.x > .17f, "blocked input cannot suppress knockback");

                Reset(movement, Origin);
                Receive(movement, Input(movement, 1, Vector3.right)); Step(movement, 1);
                Set(movement, "lastArrival", NetworkTime.localTime - 1);
                before = movement.transform.position; Step(movement, 1);
                Check(Mathf.Abs(movement.transform.position.x - before.x) < .001f, "stale input stops walking");
                movement.ServerAddExternalForce(Vector3.right * 10); Step(movement, 1);
                Check(movement.transform.position.x > before.x + .17f, "missing input cannot suppress knockback");

                Reset(movement, Origin);
                before = movement.transform.position;
                for (uint i = 1; i <= 1000; i++) Receive(movement, Input(movement, i, Vector3.right));
                Check(movement.transform.position == before, "1000 commands cannot move player outside server tick");
                Check(((Queue<RelativeMovement.MoveInput>)Get(movement, "serverInputs")).Count <= 8, "server input queue bounded");
                Step(movement, 1);
                Check(movement.transform.position.x <= .121f, "command flood cannot accelerate server tick");

                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.position = new Vector3(2, 2, 0); wall.transform.localScale = new Vector3(.3f, 4, 10);
                Reset(movement, Origin); movement.ServerDash(Vector3.right); Step(movement, 20);
                Check(movement.transform.position.x < 1.6f, "dash cannot pass through wall");
                wall.GetComponent<Collider>().enabled = false;
                UnityEngine.Object.Destroy(wall);

                var remote = UnityEngine.Object.Instantiate(NetworkManager.singleton.playerPrefab);
                var remoteMovement = remote.GetComponentInChildren<RelativeMovement>(); remoteMovement.enabled = false;
                NetworkServer.Spawn(remote);
                Reset(remoteMovement, new Vector3(10, 1.1f, 0));
                remoteMovement.ServerAddExternalForce(Vector3.right * 10); Step(remoteMovement, 1);
                Check(remoteMovement.transform.position.x > 10.17f, "server moves player even without owner connection");
                NetworkServer.Destroy(remote);

                CheckPrediction();
                Reset(movement, Origin);
                var oldInput = Input(movement, 1, Vector3.right);
                Receive(movement, oldInput);
                health.TakeDamage(1000);
                Check(health.IsDead, "death registered");
                before = movement.transform.position; Step(movement, 10);
                Check(movement.transform.position == before, "dead player cannot move");
                Check(!Receive(movement, oldInput), "death invalidates queued life");
                stage = 2;
                return;
            }
            if (stage == 2 && !health.IsDead)
            {
                Check(Vector3.Distance(movement.transform.position, new Vector3(-10, 1.1f, 0)) < .05f, "respawn teleports server");
                Check(((Queue<RelativeMovement.MoveInput>)Get(movement, "serverInputs")).Count == 0, "respawn clears old input");
                Check(((Vector3)Get(movement, "externalVelocity")) == Vector3.zero, "respawn clears impulses");
                Debug.Log("MOVEMENT_AUTHORITY_PASSED: " + checks + " checks");
                SessionState.SetBool(Flag, false);
                NetworkManager.singleton.StopHost();
                EditorApplication.Exit(0);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SessionState.SetBool(Flag, false);
            EditorApplication.Exit(1);
        }
    }

    static void CheckPrediction()
    {
        // отдельная несетевая копия проверяет восстановление и повтор истории владельца.
        var copy = UnityEngine.Object.Instantiate(NetworkManager.singleton.playerPrefab);
        var motor = copy.GetComponentInChildren<RelativeMovement>(); motor.enabled = false;
        var state = new RelativeMovement.MotorState
        {
            epoch = 7, tick = 10, position = new Vector3(20, 1.1f, 0),
            rotation = Quaternion.identity, verticalSpeed = -1.5f, slowMultiplier = 1,
            simulationTime = NetworkTime.time
        };
        Call(motor, "Reconcile", state);
        var history = (List<RelativeMovement.MoveInput>)Get(motor, "pendingInputs");
        history.Add(new RelativeMovement.MoveInput { epoch = 7, sequence = 1, movement = Vector3.right, forward = Vector3.forward });
        history.Add(new RelativeMovement.MoveInput { epoch = 7, sequence = 2, movement = Vector3.right, forward = Vector3.forward });
        state.tick++; state.acknowledged = 1; state.position.x += .12f;
        Call(motor, "Reconcile", state);
        Check(history.Count == 1, "ack removes only confirmed inputs");
        Check(Mathf.Abs(motor.transform.position.x - 20.24f) < .02f, "unconfirmed input replayed once");
        var before = motor.transform.position;
        state.tick--; state.position = Vector3.zero; Call(motor, "Reconcile", state);
        Check(motor.transform.position == before, "out-of-order snapshot ignored");
        state.epoch++; state.tick++; state.position = new Vector3(-20, 1.1f, 0); state.acknowledged = 0;
        Call(motor, "Reconcile", state);
        Check(history.Count == 0 && motor.transform.position == state.position, "new life discards prediction and snaps to spawn");
        state.epoch--; state.tick += 100; state.position = Vector3.zero; Call(motor, "Reconcile", state);
        Check(motor.transform.position.x == -20, "late previous-life snapshot ignored");
        UnityEngine.Object.Destroy(copy);
    }
}