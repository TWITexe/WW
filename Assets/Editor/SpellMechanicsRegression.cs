using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// проверяем правила контроля и реальные геометрические запросы без запуска матча и изменения сцены.
public static class SpellMechanicsRegression
{
    [MenuItem("Tools/Wizard War/Validate spell mechanics")]
    public static void Validate()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play Mode.");
        CheckSlows();
        CheckLift();
        Scene original = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            var targetObject = new GameObject("Test target", typeof(Mirror.NetworkIdentity), typeof(BoxCollider), typeof(Health));
            var wallObject = new GameObject("Test cover", typeof(BoxCollider));
            var effect = new GameObject("Test area");
            SceneManager.MoveGameObjectToScene(targetObject, scene);
            SceneManager.MoveGameObjectToScene(wallObject, scene);
            SceneManager.MoveGameObjectToScene(effect, scene);
            var target = targetObject.GetComponent<BoxCollider>();
            var wall = wallObject.GetComponent<BoxCollider>();
            Vector3 origin = new Vector3(12000, 12000, 12000);
            targetObject.transform.position = origin + Vector3.forward * 4;
            target.size = new Vector3(1, 2, 1);
            effect.transform.position = origin;
            wallObject.transform.position = origin + Vector3.forward * 2;
            wall.size = new Vector3(4, 4, .2f);
            Physics.SyncTransforms();
            Require(!SpellAreaVisibility.CanReach(origin, target, effect.transform), "Solid wall leaked.");
            wall.isTrigger = true;
            Physics.SyncTransforms();
            Require(SpellAreaVisibility.CanReach(origin, target, effect.transform), "Trigger blocked area.");
            wall.isTrigger = false;
            wallObject.transform.position = origin;
            Physics.SyncTransforms();
            Require(!SpellAreaVisibility.CanReach(origin, target, effect.transform), "Origin inside wall leaked.");
            wallObject.transform.position = origin + Vector3.forward * 2 - Vector3.up * 2;
            Physics.SyncTransforms();
            Require(SpellAreaVisibility.CanReach(origin + Vector3.up * .15f, target, effect.transform), "Low cover hid exposed body.");
            // пол между этажами тоже должен закрывать цель.
            wallObject.transform.position = origin + Vector3.up * 2;
            wall.size = new Vector3(8, .2f, 8);
            targetObject.transform.position = origin + Vector3.up * 4;
            Physics.SyncTransforms();
            Require(!SpellAreaVisibility.CanReach(origin, target, effect.transform), "Floor leaked.");
            wall.enabled = false;
            Physics.SyncTransforms();
            Require(SpellAreaVisibility.CanReach(origin, target, effect.transform), "Open target blocked.");
            File.WriteAllText("Logs/spell-mechanics-validation.txt", "PASS: independent slow expiry, refresh, snapshot immutability, invalid input; shared periodic lift limit and horizontal force; wall, trigger, embedded origin, low cover, floor and open target.\n" + DateTime.Now.ToString("O"));
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (original.IsValid()) SceneManager.SetActiveScene(original);
        }
    }

    // слабый эффект не продлевает сильный; повтор той же силы обновляет только собственный срок.
    private static void CheckSlows()
    {
        var strong = SpellControlRules.AddSlow(null, .35f, 2, 10);
        var mixed = SpellControlRules.AddSlow(strong, .8f, 4, 11);
        Require(SpellControlRules.SlowMultiplier(mixed, 11.5) == .35f, "Strong slow lost.");
        Require(SpellControlRules.SlowMultiplier(mixed, 12) == .8f, "Weak slow extended strong slow.");
        Require(SpellControlRules.SlowMultiplier(mixed, 15) == 1, "Slow did not expire.");
        var refreshed = SpellControlRules.AddSlow(mixed, .35f, 2, 11);
        Require(refreshed.Length == 2 && SpellControlRules.SlowMultiplier(refreshed, 12.5) == .35f, "Same-strength refresh failed.");
        Require(SpellControlRules.SlowMultiplier(mixed, 12.5) == .8f, "Snapshot was mutated.");
        Require(SpellControlRules.SlowMultiplier(SpellControlRules.AddSlow(mixed, .2f, -1, 11), 12) == .8f, "Invalid duration accepted.");
        Require(SpellControlRules.AddSlow(mixed, .6f, 2, 20).Length == 1, "Expired effects accumulated.");
    }

    // разные источники используют один срок цели, но не теряют горизонтальный толчок.
    private static void CheckLift()
    {
        double next = 0;
        Vector3 force = new Vector3(2, 12, 0);
        Require(SpellControlRules.LimitPeriodicLift(force, 10, ref next).y == 12, "First lift missing.");
        Vector3 blocked = SpellControlRules.LimitPeriodicLift(force, 10.5, ref next);
        Require(blocked.y == 0 && blocked.x == 2, "Repeat lift or lost horizontal force.");
        Require(SpellControlRules.LimitPeriodicLift(force, 11, ref next).y == 0, "Second source bypassed cooldown.");
        Require(SpellControlRules.LimitPeriodicLift(force, 11.25, ref next).y == 12, "Lift did not recover.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
