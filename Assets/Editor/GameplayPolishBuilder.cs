using UnityEditor;
using UnityEngine;

// записывает настройки падения, коллайдера, статистики и камеры в префаб игрока.
public static class GameplayPolishBuilder
{
    // редактируем содержимое префаба и сохраняем его, освобождая загруженную копию в finally.
    public static void Apply()
    {
        const string path="Assets/Prefabs/Player.prefab";
        var player=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var movement=player.GetComponentInChildren<RelativeMovement>(true);
            if(movement.GetComponent<PlayerStats>()==null)movement.gameObject.AddComponent<PlayerStats>();
            var movementSettings=new SerializedObject(movement);
            movementSettings.FindProperty("terminalVelocity").floatValue=-22;
            movementSettings.FindProperty("fallGravityMultiplier").floatValue=1.8f;
            movementSettings.ApplyModifiedPropertiesWithoutUndo();
            var controller=movement.GetComponent<CharacterController>();
            controller.height=2.5f;controller.center=new Vector3(0,.25f,0);
            foreach(var collider in movement.GetComponentsInChildren<Collider>(true))
                if(collider!=controller)collider.enabled=false;
            var camera=player.GetComponentInChildren<OrbitCamera>(true);
            var so=new SerializedObject(camera);
            so.FindProperty("target").objectReferenceValue=movement.transform;
            so.FindProperty("defaultDistance").floatValue=6.5f;so.FindProperty("pivotHeight").floatValue=1.6f;
            so.FindProperty("shoulderOffset").floatValue=1.6f;so.FindProperty("initialPitch").floatValue=3;
            so.FindProperty("fieldOfView").floatValue=70;
            so.FindProperty("minVerticalAngle").floatValue=-35;so.FindProperty("maxVerticalAngle").floatValue=65;
            so.ApplyModifiedPropertiesWithoutUndo();
            camera.GetComponent<CameraShake>().shakeAmount=.025f;
            PrefabUtility.SaveAsPrefabAsset(player,path);
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
        AssetDatabase.SaveAssets();
        Debug.Log("GAMEPLAY_POLISH_APPLIED");
    }
}
