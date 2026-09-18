using UnityEditor;
using UnityEngine;
// выводит структуру импортированной модели мага для настройки частей и размеров.
public static class WizardModelInspection
{
    // перечисляем трансформации и параметры мешей модели в журнал редактора.
    public static void Run()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Wizard/LowPoly_Wizard_White_Faceless.fbx");
        foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
        {
            var mesh = part.GetComponent<MeshFilter>();
            Debug.Log($"MODEL_PART {part.name} pos={part.localPosition} rot={part.localEulerAngles} scale={part.localScale}" +
                (mesh != null ? $" vertices={mesh.sharedMesh.vertexCount} bounds={mesh.sharedMesh.bounds}" : ""));
        }
    }
}
