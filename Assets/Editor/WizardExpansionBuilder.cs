using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Mirror;

public static class WizardExpansionBuilder
{
    const string Folder = "Assets/GeneratedWizard";
    class Spec
    {
        public string id, title, text;
        public int[] recipe;
        public ElementalCastMode mode;
        public Color color;
        public float cooldown, speed, duration, radius, push, lift, slow;
        public int damage;
        public Spec(string id, string title, string text, int[] recipe, ElementalCastMode mode, Color color,
            float cooldown, int damage, float radius, float speed = 20, float duration = 4, float push = 0, float lift = 0, float slow = 1)
        { this.id=id; this.title=title; this.text=text; this.recipe=recipe; this.mode=mode; this.color=color;
          this.cooldown=cooldown; this.damage=damage; this.radius=radius; this.speed=speed; this.duration=duration;
          this.push=push; this.lift=lift; this.slow=slow; }
    }
    static Spec[] Specs => new[]
    {
        new Spec("WaterBolt","Водяной удар","Быстрый снаряд: 18 урона и лёгкое отталкивание.",new[]{4,4,4},ElementalCastMode.Bolt,Color.cyan,3,18,.4f,25,4,4),
        new Spec("IceShard","Ледяное копьё","22 урона. Замедляет цель на 40% на 2 секунды.",new[]{2,2,2},ElementalCastMode.Bolt,new Color(.6f,.85f,1),4,22,.4f,24,4,0,0,.6f),
        new Spec("Boulder","Каменная глыба","Тяжёлый снаряд: 32 урона и сильный толчок.",new[]{3,3,3},ElementalCastMode.Bolt,new Color(.55f,.4f,.25f),5,32,.5f,13,5,10),
        new Spec("SteamCloud","Паровое облако","Область на 4 секунды: 5 урона каждые 0,5 с; замедление 20%.",new[]{0,0,4},ElementalCastMode.GroundZone,new Color(.8f,.9f,.95f),8,5,3.2f,0,4,0,0,.8f),
        new Spec("BoilingJet","Кипящая струя","Быстрый заряд кипятка: взрыв на 28 урона в радиусе 2 м.",new[]{0,4,4},ElementalCastMode.Bolt,new Color(1,.6f,.3f),6,28,2,28,4,3),
        new Spec("FireTornado","Огненный торнадо","Движущийся вихрь на 5 секунд: 6 урона каждые 0,5 с; затягивает и подбрасывает.",new[]{0,1,1},ElementalCastMode.Tornado,new Color(1,.28f,.04f),10,6,2.3f,4,5,-4,6),
        new Spec("Blizzard","Метель","Область на 5 секунд: 4 урона каждые 0,5 с; замедление 50%.",new[]{1,2,2},ElementalCastMode.GroundZone,new Color(.55f,.75f,1),9,4,3.5f,0,5,0,0,.5f),
        new Spec("Mud","Грязевая трясина","Область на 5 секунд: замедление 65% и 2 урона каждые 0,5 с.",new[]{3,3,4},ElementalCastMode.GroundZone,new Color(.35f,.25f,.12f),8,2,3,0,5,0,0,.35f),
        new Spec("Magma","Магматическая бомба","Медленная бомба: 40 урона в радиусе 3 м и отталкивание.",new[]{0,3,3},ElementalCastMode.Bolt,new Color(1,.12f,.03f),9,40,3,11,5,10,3),
        new Spec("FrostNova","Ледяная волна","Вспышка вокруг мага: 14 урона в радиусе 4 м; замедление 60%.",new[]{2,2,4},ElementalCastMode.SelfBurst,new Color(.4f,.9f,1),8,14,4,0,1,5,0,.4f),
        new Spec("StoneSkin","Каменная кожа","Щит поглощает до 50 урона в течение 5 секунд.",new[]{2,3,3},ElementalCastMode.Shield,new Color(.65f,.65f,.7f),12,0,0,0,5),
        new Spec("Geyser","Гейзер","Область на 1 секунду: 10 урона каждые 0,5 с и сильный подброс.",new[]{1,3,4},ElementalCastMode.GroundZone,new Color(.15f,.65f,1),8,10,2,0,1,2,12),
    };
    [MenuItem("Tools/Wizard War/Build wizard expansion assets")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets","GeneratedWizard");
        var spells = new List<Spell> {
            AssetDatabase.LoadAssetAtPath<Spell>("Assets/Scripts/Spells/FireBall/FireBall.asset"),
            AssetDatabase.LoadAssetAtPath<Spell>("Assets/Scripts/Spells/WindFlow/WindFlow.asset")
        };
        SetString(spells[0],"description","Огненный снаряд: 20 урона при прямом попадании.");
        SetString(spells[1],"description","Порыв воздуха отталкивает противника.");
        var prefabs = new List<GameObject>();
        foreach (Spec spec in Specs)
        {
            string path = Folder + "/" + spec.id + ".asset";
            var spell = AssetDatabase.LoadAssetAtPath<ElementalSpell>(path);
            if (spell == null) { spell = ScriptableObject.CreateInstance<ElementalSpell>(); AssetDatabase.CreateAsset(spell,path); }
            var so = new SerializedObject(spell);
            so.FindProperty("displayName").stringValue = spec.title;
            so.FindProperty("description").stringValue = spec.text;
            so.FindProperty("cooldown").floatValue = spec.cooldown;
            var recipe = so.FindProperty("recipe"); recipe.arraySize = 3;
            for (int i=0;i<3;i++) recipe.GetArrayElementAtIndex(i).enumValueIndex = spec.recipe[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            spell.mode=spec.mode; spell.tint=spec.color; spell.speed=spec.speed; spell.duration=spec.duration;
            spell.radius=spec.radius; spell.damage=spec.damage; spell.knockback=spec.push; spell.lift=spec.lift;
            spell.slow=spec.slow; spell.slowDuration=2; spell.tickInterval=.5f; spell.shieldAmount=50;
            if (spec.mode != ElementalCastMode.Shield)
            {
                var go = new GameObject(spec.id);
                go.AddComponent<NetworkIdentity>();
                var nt = go.AddComponent<NetworkTransformReliable>(); nt.syncDirection=SyncDirection.ServerToClient;
                var body = go.AddComponent<Rigidbody>(); body.useGravity=false;
                body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
                var collider=go.AddComponent<SphereCollider>(); collider.isTrigger=true; collider.radius=.2f;
                go.AddComponent<ElementalEffect>().definition=spell;
                var material = Material(spec.id, spec.color);
                var visual = go.AddComponent<ElementalVisual>();
                visual.particleMaterial = ParticleMaterial(spec.id, spec.color); visual.lineMaterial = material;
                if (spec.mode == ElementalCastMode.Bolt)
                {
                    var orb=GameObject.CreatePrimitive(spec.id=="Boulder" ? PrimitiveType.Cube : PrimitiveType.Sphere);
                    orb.name="Spell core"; orb.transform.SetParent(go.transform,false);
                    orb.transform.localScale=Vector3.one*(spec.id=="Magma" ? .8f : .4f);
                    UnityEngine.Object.DestroyImmediate(orb.GetComponent<Collider>());
                    orb.GetComponent<Renderer>().sharedMaterial=material;
                    var trail=go.AddComponent<TrailRenderer>(); trail.sharedMaterial=material;
                    trail.time=.22f; trail.startWidth=.28f; trail.endWidth=0;
                }
                else
                {
                    var ring=go.AddComponent<LineRenderer>(); ring.sharedMaterial=material;
                    ring.loop=true; ring.useWorldSpace=false; ring.widthMultiplier=.08f; ring.positionCount=64;
                    for(int i=0;i<64;i++) { float a=i*Mathf.PI*2/64; ring.SetPosition(i,new Vector3(Mathf.Cos(a)*spec.radius,0,Mathf.Sin(a)*spec.radius)); }
                }
                spell.effectPrefab=PrefabUtility.SaveAsPrefabAsset(go,Folder+"/"+spec.id+".prefab");
                UnityEngine.Object.DestroyImmediate(go); prefabs.Add(spell.effectPrefab);
            }
            EditorUtility.SetDirty(spell); spells.Add(spell);
        }
        BuildPlayer(spells);
        foreach(string scenePath in new[]{"Assets/Scenes/Menu.unity","Assets/Scenes/SampleScene.unity"})
        {
            var scene=EditorSceneManager.OpenScene(scenePath);
            foreach(var manager in UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                foreach(var prefab in prefabs) if(!manager.spawnPrefabs.Contains(prefab)) manager.spawnPrefabs.Add(prefab);
                EditorUtility.SetDirty(manager);
            }
            EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();
        FinalizeNetworkPrefabs();
        Debug.Log("EXPANSION_BUILT: 14 spells, 5 elements, 4 wizard pieces");
    }
    public static void FinalizeNetworkPrefabs()
    {
        // New identities are initially saved before Mirror knows the prefab asset path.
        // Persist their deterministic IDs explicitly so standalone clients can spawn them.
        foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{Folder}))
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            var identity=prefab.GetComponent<NetworkIdentity>();
            if(identity==null)continue;
            var serialized=new SerializedObject(identity);
            serialized.FindProperty("_assetId").longValue=NetworkIdentity.AssetGuidToUint(new Guid(guid));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(identity);
            PrefabUtility.SavePrefabAsset(prefab);
        }
        AssetDatabase.SaveAssets();
    }
    static Material Material(string id,Color color)
    {
        string path=Folder+"/"+id+".mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        string shader=id.StartsWith("Wizard_")?"Universal Render Pipeline/Lit":"Universal Render Pipeline/Unlit";
        if(mat==null) { mat=new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(mat,path); }
        mat.shader=Shader.Find(shader);
        if(mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness",.15f);
        mat.SetColor("_BaseColor",color); EditorUtility.SetDirty(mat); return mat;
    }
    static Material ParticleMaterial(string id, Color color)
    {
        string texturePath=Folder+"/SoftParticle.asset";
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if(texture==null)
        {
            texture=new Texture2D(32,32,TextureFormat.RGBA32,false);
            for(int y=0;y<32;y++) for(int x=0;x<32;x++)
            {
                float a=Mathf.Clamp01(1-Vector2.Distance(new Vector2(x,y),new Vector2(15.5f,15.5f))/15.5f);
                texture.SetPixel(x,y,new Color(1,1,1,a*a));
            }
            texture.Apply(); AssetDatabase.CreateAsset(texture,texturePath);
        }
        string path=Folder+"/"+id+"Particles.mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null) { mat=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(mat,path); }
        mat.SetTexture("_BaseMap",texture); mat.SetColor("_BaseColor",color);
        mat.SetFloat("_Surface",1); mat.SetFloat("_SrcBlend",5); mat.SetFloat("_DstBlend",10); mat.SetFloat("_ZWrite",0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.renderQueue=3000;
        EditorUtility.SetDirty(mat); return mat;
    }
    static void SetString(UnityEngine.Object obj,string field,string value)
    {
        var so=new SerializedObject(obj); so.FindProperty(field).stringValue=value; so.ApplyModifiedPropertiesWithoutUndo();
    }
    static void BuildPlayer(List<Spell> spells)
    {
        const string path="Assets/Prefabs/Player.prefab";
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var manager=root.GetComponentInChildren<SpellManager>(true);
            var so=new SerializedObject(manager);
            var list=so.FindProperty("spells"); list.arraySize=spells.Count;
            for(int i=0;i<spells.Count;i++) list.GetArrayElementAtIndex(i).objectReferenceValue=spells[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            var entity=manager.transform;
            var previous=entity.Find("WizardVisual");
            if(previous!=null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
            foreach(var renderer in entity.GetComponentsInChildren<MeshRenderer>(true)) renderer.enabled=false;
            var modelAsset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Wizard/LowPoly_Wizard_White_Faceless.fbx");
            var visual=UnityEngine.Object.Instantiate(modelAsset,entity);
            visual.name="WizardVisual";
            if(PrefabUtility.IsPartOfPrefabInstance(visual)) PrefabUtility.UnpackPrefabInstance(visual,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            visual.transform.localPosition=Vector3.zero; visual.transform.localRotation=Quaternion.identity; visual.transform.localScale=Vector3.one;
            var renderers=visual.GetComponentsInChildren<MeshRenderer>();
            Bounds bounds=renderers[0].bounds; foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            visual.transform.localScale=Vector3.one*(2.5f/bounds.size.y);
            bounds=renderers[0].bounds; foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            visual.transform.position+=new Vector3(entity.position.x-bounds.center.x,entity.position.y-1-bounds.min.y,entity.position.z-bounds.center.z);
            var originalParts=visual.transform.Cast<Transform>().ToArray();
            var groups=new[]{new GameObject("Head").transform,new GameObject("Hat").transform,new GameObject("Body").transform,new GameObject("Staff").transform};
            foreach(var group in groups) group.SetParent(visual.transform,false);
            foreach(var part in originalParts)
            {
                int group=part.name.Contains("Head")?0:part.name.Contains("Hat")?1:part.name.Contains("Staff")?3:2;
                part.SetParent(groups[group],true);
            }
            foreach(var renderer in renderers)
            {
                var mats=renderer.sharedMaterials;
                for(int i=0;i<mats.Length;i++)
                {
                    var old=mats[i];
                    Color color=old!=null && old.HasProperty("_Color")?old.color:Color.white;
                    // Preserve the white robe and dark faceless hood/material accents.
                    mats[i]=Material("Wizard_"+(old!=null?old.name:"White").Replace("/","_"),color);
                }
                renderer.sharedMaterials=mats;
            }
            var appearance=entity.GetComponent<WizardAppearance>()??entity.gameObject.AddComponent<WizardAppearance>();
            appearance.visualRoot=visual.transform; appearance.pieces=groups;
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
