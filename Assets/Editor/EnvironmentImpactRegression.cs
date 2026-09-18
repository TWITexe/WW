using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// проверяет попадания без здоровья цели и размещение вспышки снаружи стены, пола и предмета.
public static class EnvironmentImpactRegression
{
    [MenuItem("Tools/Wizard War/Validate environment impacts")]
    public static void Validate()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run outside Play Mode.");
        Scene original = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            var obstacle = new GameObject("Ordinary wall", typeof(BoxCollider));
            var projectile = new GameObject("Test projectile", typeof(SphereCollider));
            SceneManager.MoveGameObjectToScene(obstacle, scene);
            SceneManager.MoveGameObjectToScene(projectile, scene);
            var box = obstacle.GetComponent<BoxCollider>();
            projectile.GetComponent<SphereCollider>().radius = .1f;
            projectile.GetComponent<SphereCollider>().isTrigger = true;
            Vector3 origin = new Vector3(10000, 10000, 10000);
            obstacle.transform.position = origin;
            box.size = new Vector3(6, 6, .2f);
            Check(projectile.transform, box, origin - Vector3.forward * 2, origin + Vector3.forward * 2);
            // тот же контакт через триггер, когда центр снаряда уже оказался внутри препятствия.
            projectile.transform.position = origin;
            Physics.SyncTransforms();
            Vector3 point = ProjectileContact.ImpactPosition(box, projectile.transform, origin - Vector3.forward * 2);
            Require(point.z < origin.z - .1f, "Trigger effect is inside wall.");
            point = ProjectileContact.ImpactPosition(box, projectile.transform, origin);
            Require(point.z < origin.z - .1f, "Initial overlap effect is inside wall.");
            obstacle.transform.rotation = Quaternion.Euler(0, 35, 0);
            Check(projectile.transform, box, origin - Vector3.forward * 2, origin + Vector3.forward * 2);
            obstacle.transform.rotation = Quaternion.identity;
            box.size = new Vector3(6, .2f, 6);
            Check(projectile.transform, box, origin + Vector3.up * 2, origin - Vector3.up * 2);
            UnityEngine.Object.DestroyImmediate(box);
            var sphere = obstacle.AddComponent<SphereCollider>();
            sphere.radius = .5f;
            Check(projectile.transform, sphere, origin - Vector3.forward * 2, origin + Vector3.forward * 2);
            sphere.isTrigger = true;
            Require(!ProjectileContact.CanHit(sphere, projectile.transform, 0), "Decorative trigger blocks projectile.");
            File.WriteAllText("Logs/environment-impact-validation.txt",
                "PASS: ordinary wall, angled wall, floor and sphere collisions; impact stays outside surface; trigger penetration and initial overlap; decorative triggers ignored.");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (original.IsValid()) SceneManager.SetActiveScene(original);
        }
    }

    // быстрый снаряд проходит объект за один шаг, но проверка пути должна найти его ближайшую поверхность.
    private static void Check(Transform source, Collider obstacle, Vector3 previous, Vector3 current)
    {
        source.position = current;
        Physics.SyncTransforms();
        Require(ProjectileContact.Sweep(source, 0, previous, out var hit, out var point), "Missed environment collision.");
        Require(hit == obstacle, "Wrong collider.");
        Require(Vector3.Distance(point, obstacle.ClosestPoint(point)) > .1f, "Impact hidden inside geometry.");
        Require(Vector3.Dot(point - obstacle.ClosestPoint(point), previous - current) > 0, "Impact on back of obstacle.");
    }

    // явная ошибка останавливает проверку до записи успешного отчёта.
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
