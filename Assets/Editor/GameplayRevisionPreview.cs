using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// подгоняет особый коллайдер копья и сохраняет изображения настоящего интерфейса на временной сцене.
public static class GameplayRevisionPreview
{
    public static void PolishAndRender()
    {
        if(Application.isPlaying)throw new Exception("нужно остановить матч");
        FitIce();
        FitDescriptions();
        RenderShelf();
        var original=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/MatchUI.prefab"));
            var ui=root.GetComponentInChildren<PlayerGameUI>(true);var data=new SerializedObject(ui);
            var canvas=(Canvas)data.FindProperty("canvas").objectReferenceValue;
            canvas.gameObject.SetActive(true);canvas.enabled=true;canvas.renderMode=RenderMode.WorldSpace;
            var scaler=canvas.GetComponent<UnityEngine.UI.CanvasScaler>();if(scaler!=null)scaler.enabled=false;
            var rect=canvas.GetComponent<RectTransform>();rect.position=Vector3.zero;rect.rotation=Quaternion.identity;rect.localScale=Vector3.one*.01f;rect.sizeDelta=new Vector2(1280,720);
            ((GameObject)data.FindProperty("pause").objectReferenceValue).SetActive(false);
            ((GameObject)data.FindProperty("scoreboard").objectReferenceValue).SetActive(false);
            var cards=data.FindProperty("cards");
            var sorted=Enumerable.Range(0,cards.arraySize).Select(i=>cards.GetArrayElementAtIndex(i).Copy()).ToList();
            sorted.Sort((a,b)=>Spell.CompareSimplicity((Spell)a.FindPropertyRelative("spell").objectReferenceValue,(Spell)b.FindPropertyRelative("spell").objectReferenceValue));
            int index=0;
            foreach(var card in sorted)
            {
                var spell=(Spell)card.FindPropertyRelative("spell").objectReferenceValue;
                var cardRoot=(GameObject)card.FindPropertyRelative("root").objectReferenceValue;
                cardRoot.transform.SetAsLastSibling();cardRoot.SetActive(spell.IsAvailable(ElementLoadout.Default));
                if(!cardRoot.activeSelf)continue;
                ((UnityEngine.UI.Text)card.FindPropertyRelative("keys").objectReferenceValue).text=ElementLoadout.Default.KeysFor(spell.Recipe).Replace(" → ","");
                ((UnityEngine.UI.Image)card.FindPropertyRelative("cover").objectReferenceValue).fillAmount=index%2==0?.65f:0;
                ((UnityEngine.UI.Text)card.FindPropertyRelative("seconds").objectReferenceValue).text=index%2==0?(12-index).ToString():"";
                index++;
            }
            ((UnityEngine.UI.Text)data.FindProperty("status").objectReferenceValue).text="Здоровье  76 / 100";
            ((UnityEngine.UI.Text)data.FindProperty("combo").objectReferenceValue).text="Осколочная воронка · ЛКМ — применить · 5 с";
            ((UnityEngine.UI.Text)data.FindProperty("killFeed").objectReferenceValue).text=MatchKillFeed.ColoredName("Арканист",new Color(1,.3f,.1f))+"  →  "+MatchKillFeed.ColoredName("Ледяной маг",Color.cyan)+"\n"+MatchKillFeed.ColoredName("Ледяной маг",Color.cyan)+"  →  "+MatchKillFeed.ColoredName("Чародей",Color.magenta);
            Canvas.ForceUpdateCanvases();UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rect);Canvas.ForceUpdateCanvases();
            Render(scene,"Logs/gameplay-hud-preview.png",3.6f);
            root.SetActive(false);
            var player=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            var healthUI=player.GetComponentInChildren<HealthUI>(true);
            var bar=(UnityEngine.UI.Image)new SerializedObject(healthUI).FindProperty("healthBar").objectReferenceValue;
            var overhead=Object.Instantiate(bar.GetComponentInParent<Canvas>(true).gameObject);
            var overheadRect=overhead.GetComponent<RectTransform>();overheadRect.position=Vector3.zero;overheadRect.rotation=Quaternion.identity;overheadRect.localScale=Vector3.one;
            overhead.GetComponent<Canvas>().enabled=true;
            foreach(var text in overhead.GetComponentsInChildren<TMPro.TMP_Text>())if(text.enabled)text.text="Маг противника";
            foreach(var fill in overhead.GetComponentsInChildren<UnityEngine.UI.Image>())if(fill.name=="Health fill")fill.rectTransform.anchorMax=new Vector2(.65f,1);
            Canvas.ForceUpdateCanvases();Render(scene,"Logs/gameplay-health-preview.png",1.1f);
        }
        finally{EditorSceneManager.CloseScene(scene,true);if(original.IsValid())SceneManager.SetActiveScene(original);}
        File.WriteAllText("Logs/gameplay-polish-done.txt",DateTime.Now.ToString("O"));
    }

    // капсула ориентирована вдоль кристалла: учитываем размер частицы, поворот и масштаб вложенного эффекта.
    static void FitIce()
    {
        const string path="Assets/Other Asstets/GeneratedWizard/IceShard.prefab";
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var core=root.GetComponentsInChildren<ParticleSystem>().Single(p=>p.name=="Ice spear - Crystal effect blue");
            var mesh=core.GetComponent<ParticleSystemRenderer>().mesh.bounds;
            float size=core.main.startSize.constant;Bounds bounds=default;bool first=true;
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
            {
                Vector3 corner=mesh.center+Vector3.Scale(mesh.extents,new Vector3(x,y,z));
                corner=root.transform.InverseTransformPoint(core.transform.TransformPoint(corner*size));
                if(first){bounds=new Bounds(corner,Vector3.zero);first=false;}else bounds.Encapsulate(corner);
            }
            var sphere=root.GetComponent<SphereCollider>();if(sphere!=null)Object.DestroyImmediate(sphere);
            var capsule=root.GetComponent<CapsuleCollider>();
            if(capsule==null)capsule=root.AddComponent<CapsuleCollider>();
            capsule.direction=2;capsule.isTrigger=true;capsule.center=bounds.center;
            capsule.radius=Mathf.Max(bounds.extents.x,bounds.extents.y);capsule.height=Mathf.Max(bounds.size.z,capsule.radius*2);
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    static void Render(Scene scene,string path,float size)
    {
        foreach(var root in scene.GetRootGameObjects())foreach(var child in root.GetComponentsInChildren<Transform>(true))child.gameObject.layer=31;
        var obj=new GameObject("камера проверки",typeof(Camera));var camera=obj.GetComponent<Camera>();
        camera.transform.position=new Vector3(0,0,-10);camera.orthographic=true;camera.orthographicSize=size;camera.aspect=16f/9;camera.cullingMask=1<<31;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.065f,.06f);
        var target=new RenderTexture(1600,900,24);var image=new Texture2D(1600,900,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
        finally{camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(target);Object.DestroyImmediate(image);Object.DestroyImmediate(obj);}
    }
    // сохраняем читаемый кегль длинных описаний: карточка растёт по тексту, а порядок соответствует сложности рецепта.
    static void FitDescriptions()
    {
        void Fit(GameObject root)
        {
            foreach(var ui in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                string field=ui is ElementLoadoutUI?"spellCards":ui is ShelfSpellCatalogUI?"cards":null;
                if(field==null)continue;
                var array=new SerializedObject(ui).FindProperty(field);
                var cards=Enumerable.Range(0,array.arraySize).Select(i=>array.GetArrayElementAtIndex(i).Copy()).ToList();
                cards.Sort((a,b)=>Spell.CompareSimplicity((Spell)a.FindPropertyRelative("spell").objectReferenceValue,(Spell)b.FindPropertyRelative("spell").objectReferenceValue));
                foreach(var card in cards)
                {
                    var spell=(Spell)card.FindPropertyRelative("spell").objectReferenceValue;
                    var value=card.FindPropertyRelative(field=="cards"?"root":"background").objectReferenceValue;
                    var cardRoot=value is GameObject go?go:((Component)value).gameObject;
                    cardRoot.transform.SetAsLastSibling();
                    var labels=cardRoot.GetComponentsInChildren<UnityEngine.UI.Text>(true);
                    var text=labels.FirstOrDefault(t=>t.name=="Description" || Mathf.Abs(Top(t)+80)<.1f);
                    if(text==null)throw new Exception("описание не найдено: "+spell.name+" / "+ui.name+"\n"+string.Join("\n",labels.Select(t=>t.name+" "+t.rectTransform.anchoredPosition+" "+t.text)));
                    text.text=spell.Description;
                    var title=labels.FirstOrDefault(t=>t.name=="Spell name" || Mathf.Abs(Top(t)+8)<.1f);
                    if(title!=null)title.text=spell.Name;
                    float height=Mathf.Max(60,text.preferredHeight+4);
                    float top=Top(text);
                    text.rectTransform.pivot=new Vector2(text.rectTransform.pivot.x,1);
                    text.rectTransform.anchoredPosition=new Vector2(text.rectTransform.anchoredPosition.x,top);
                    text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,height);
                    var layout=cardRoot.GetComponent<UnityEngine.UI.LayoutElement>();
                    if(layout!=null)layout.preferredHeight=Mathf.Abs(top)+height+10;
                }
            }
        }
        const string prefab="Assets/Prefabs/UI/SpellbookUI.prefab";
        var contents=PrefabUtility.LoadPrefabContents(prefab);
        try{Fit(contents);PrefabUtility.SaveAsPrefabAsset(contents,prefab);}finally{PrefabUtility.UnloadPrefabContents(contents);}
        var original=SceneManager.GetActiveScene();
        foreach(string path in new[]{"Assets/Scenes/Menu.unity","Assets/Scenes/SampleScene.unity"})
        {
            var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try{foreach(var root in scene.GetRootGameObjects())Fit(root);EditorSceneManager.SaveScene(scene);}
            finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
        }
        if(original.IsValid())SceneManager.SetActiveScene(original);
    }
    static float Top(UnityEngine.UI.Text text)=>text.rectTransform.anchoredPosition.y+(1-text.rectTransform.pivot.y)*text.rectTransform.rect.height;

    // изображение каталога получаем на копии холста, сохраняя выбранные стихии и камеру живого меню.
    static void RenderShelf()
    {
        var original=SceneManager.GetActiveScene();
        var menu=SceneManager.GetSceneByPath("Assets/Scenes/Menu.unity");bool opened=!menu.isLoaded;
        if(opened)menu=EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity",OpenSceneMode.Additive);
        var source=menu.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<ShelfSpellCatalogUI>(true)).Single();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            var ui=Object.Instantiate(source);var rect=ui.GetComponent<RectTransform>();
            rect.position=Vector3.zero;rect.rotation=Quaternion.identity;rect.localScale=Vector3.one*.01f;
            ui.gameObject.SetActive(true);ui.GetComponent<Canvas>().enabled=true;
            ui.Refresh(ElementLoadout.Default,0);
            Canvas.ForceUpdateCanvases();UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rect);Canvas.ForceUpdateCanvases();
            Render(scene,"Logs/gameplay-shelf-preview.png",4.2f);
        }
        finally{EditorSceneManager.CloseScene(scene,true);if(opened)EditorSceneManager.CloseScene(menu,true);if(original.IsValid())SceneManager.SetActiveScene(original);}
    }
}
