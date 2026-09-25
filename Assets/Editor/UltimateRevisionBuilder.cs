using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit, repeatable migration of the existing editable assets (network prefab GUIDs stay intact).
public static class UltimateRevisionBuilder
{
    const string Folder = "Assets/Spells/Ultimates";
    static Material basalt, slate, frost, snow, lava, soil;
    static Mesh rock, crystal;
    static Material beamMaterial;
    [MenuItem("Tools/Wizard War/Revise ultimate mechanics and models")]
    public static void Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        basalt = Material("Spirit basalt", new Color(.075f,.12f,.17f), 0);
        slate = Material("Spirit stone", new Color(.28f,.38f,.43f), 0);
        frost = Material("Spirit ice", new Color(.07f,.48f,.7f), .35f);
        snow = Material("Spirit crystal", new Color(.48f,.88f,1), 1.2f);
        lava = Material("Spirit magma", new Color(1,.055f,.008f), 2);
        soil = Material("Island earth", new Color(.25f,.21f,.15f), 0);
        beamMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/UltimateBeam.mat");
        if(beamMaterial==null)
        {
            beamMaterial=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/AreaTarget.mat"));
            beamMaterial.name="Ultimate beam";AssetDatabase.CreateAsset(beamMaterial,"Assets/Resources/UltimateBeam.mat");
        }
        beamMaterial.color=Color.white;EditorUtility.SetDirty(beamMaterial);
        rock = SaveMesh("Faceted stone", Profile(new[]{.5f,.95f,1f,.7f}, new[]{-.5f,-.28f,.25f,.5f}, 7));
        crystal = SaveMesh("Element crystal", Profile(new[]{.04f,.42f,.38f,.01f},new[]{-.65f,-.22f,.3f,.85f}, 5));
        BuildGolem();
        UpdatePlayer();
        UpdateWorlds();
        UpdateCatalog();
        AssetDatabase.SaveAssets();
        File.WriteAllText("Logs/ultimate-revision-assets.txt", "PASS: saved island, six individually placed mirrors, gravity field, rigged spirit with Hover/Glide clips, red phoenix, aiming guide and four prisms.");
    }
    static Material Material(string name, Color color, float emission)
    {
        string path = Folder + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", emission > 0 ? .68f : .18f);
        if (emission > 0) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * emission); }
        EditorUtility.SetDirty(m); return m;
    }
    static GameObject Child(Transform parent, string name, Vector3 position = default)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position; return go;
    }
    static GameObject Part(Transform parent, string name, Vector3 position, Vector3 scale, Material material, Mesh mesh = null)
    {
        var go = Child(parent, name, position); go.layer = 2; go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh != null ? mesh : rock;
        go.AddComponent<MeshRenderer>().sharedMaterial = material; return go;
    }
    static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--) Object.DestroyImmediate(parent.GetChild(i).gameObject);
    }
    static Mesh SaveMesh(string name, Mesh source)
    {
        string path = Folder + "/" + name + ".asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path); source.name = name;
        if (saved == null) { AssetDatabase.CreateAsset(source, path); return source; }
        EditorUtility.CopySerialized(source, saved); Object.DestroyImmediate(source); return saved;
    }
    static Mesh Profile(float[] radii, float[] heights, int segments)
    {
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        Vector3 Ring(int ring, int i)
        {
            float a = i * Mathf.PI * 2 / segments;
            float rough = 1 + Mathf.Sin(i * 13.4f) * .07f;
            return new Vector3(Mathf.Cos(a) * radii[ring] * rough, heights[ring], Mathf.Sin(a) * radii[ring] * rough);
        }
        void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            int start = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c);
            triangles.Add(start); triangles.Add(start+1); triangles.Add(start+2);
        }
        for (int i = 0; i < segments; i++)
        {
            Triangle(new Vector3(0, heights[0], 0), Ring(0,i), Ring(0,i+1));
            int top = heights.Length - 1;
            Triangle(new Vector3(0,heights[top],0), Ring(top,i+1), Ring(top,i));
            for (int r = 0; r < top; r++)
            {
                Triangle(Ring(r,i), Ring(r+1,i), Ring(r,i+1));
                Triangle(Ring(r,i+1), Ring(r+1,i), Ring(r+1,i+1));
            }
        }
        var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }
    static LineRenderer Line(Transform parent, string name, Color color, float width, params Vector3[] points)
    {
        var go = Child(parent, name); go.layer = 2;
        var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = beamMaterial;
        line.useWorldSpace = false; line.positionCount = points.Length; line.SetPositions(points);
        line.startColor = line.endColor = color; line.widthMultiplier = width;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return line;
    }
    static void BuildGolem()
    {
        var root = new GameObject("Elemental Spirit Model");
        try
        {
            var animation = root.AddComponent<Animation>();
            var actor = root.AddComponent<UltimateSpiritAnimator>(); actor.flightAnimation = animation;
            var lean = Child(root.transform, "Lean"); actor.leanRoot = lean.transform;
            var rig = Child(lean.transform, "Rig");
            Part(rig.transform, "Floating obsidian torso", new Vector3(0,.7f,0), new Vector3(.66f,1.3f,.5f), basalt);
            Part(rig.transform, "Chest mantle", new Vector3(0,1.12f,.1f), new Vector3(.73f,.52f,.5f), slate);
            Part(rig.transform, "Lower armour", new Vector3(0,.05f,0), new Vector3(.45f,.4f,.35f), slate);
            var heart = Child(rig.transform, "Living core", new Vector3(0,.87f,.49f)); actor.heart = heart.transform;
            Part(heart.transform, "Molten heart", Vector3.zero, new Vector3(.24f,.45f,.13f), lava, crystal);
            for (int i=0;i<5;i++)
            {
                float a = i * Mathf.PI * 2 / 5;
                var shard = Part(heart.transform, "Core rim " + i, new Vector3(Mathf.Sin(a)*.3f,Mathf.Cos(a)*.3f,-.015f), new Vector3(.1f,.25f,.12f), slate, crystal);
                shard.transform.localRotation = Quaternion.Euler(0,0,-i*72);
            }
            Part(rig.transform, "Ancient face", new Vector3(0,1.81f,.03f), new Vector3(.38f,.58f,.32f), basalt);
            Part(rig.transform, "Brow", new Vector3(0,1.96f,.27f), new Vector3(.38f,.13f,.13f), slate);
            for (int side=-1;side<=1;side+=2)
            {
                Part(rig.transform, "Luminous eye " + side, new Vector3(side*.15f,1.84f,.32f), new Vector3(.09f,.1f,.045f), snow);
                var crown = Part(rig.transform, "Crown " + side, new Vector3(side*.3f,2.18f,0), new Vector3(.24f,.65f,.22f), snow, crystal);
                crown.transform.localRotation = Quaternion.Euler(0,0,-side*22);
                var arm = Child(rig.transform, side < 0 ? "FrostArm" : "MagmaArm", new Vector3(side*.87f,1.13f,0));
                Part(arm.transform, "Shoulder slab", Vector3.zero, new Vector3(.43f,.64f,.48f), side < 0 ? frost : slate);
                for (int i=0;i<3;i++)
                {
                    var spike = Part(arm.transform, "Elemental crest " + i, new Vector3(side*(.1f+i*.12f),.38f,-.15f+i*.15f), new Vector3(.22f,.55f+i*.08f,.22f), side < 0 ? snow : lava, crystal);
                    spike.transform.localRotation = Quaternion.Euler(-18+i*18,0,-side*(22+i*12));
                }
                Part(arm.transform, "Suspended forearm", new Vector3(side*.12f,-.64f,.08f), new Vector3(.33f,.73f,.35f), basalt);
                Part(arm.transform, "Gauntlet", new Vector3(side*.13f,-1.04f,.2f), new Vector3(.39f,.4f,.38f), side < 0 ? frost : slate);
                Part(arm.transform, "Fist core", new Vector3(side*.13f,-.9f,.5f), new Vector3(.16f,.4f,.08f), side < 0 ? snow : lava, crystal);
                var leg = Child(rig.transform, side < 0 ? "LeftLeg" : "RightLeg", new Vector3(side*.32f,-.39f,0));
                Part(leg.transform, "Suspended shin", Vector3.zero, new Vector3(.25f,.61f,.3f), slate);
                Part(leg.transform, "Hovering foot", new Vector3(0,-.3f,.13f), new Vector3(.29f,.26f,.42f), basalt);
                Line(rig.transform, "Chest fissure " + side, new Color(1,.09f,.015f), .032f,
                    new Vector3(side*.21f,.97f,.48f),new Vector3(side*.45f,.7f,.39f),new Vector3(side*.28f,.36f,.39f));
            }
            Part(rig.transform, "Crown keystone", new Vector3(0,2.25f,-.07f), new Vector3(.24f,.7f,.2f), frost, crystal);
            for(int i=0;i<7;i++)
            {
                float a=i*Mathf.PI*2/7;
                var debris=Part(rig.transform,"Orbiting shard "+i,new Vector3(Mathf.Cos(a)*.77f,-.6f+Mathf.Sin(i)*.12f,Mathf.Sin(a)*.55f),new Vector3(.12f,.22f,.13f),i%2==0?frost:slate,crystal);
                debris.transform.localRotation=Quaternion.Euler(i*17,i*39,18);
            }
            foreach (bool glide in new[]{false,true})
            {
                var clip = FlightClip(glide); animation.AddClip(clip, glide ? "Glide" : "Hover");
                if (!glide) animation.clip = clip;
            }
            animation.playAutomatically = true;
            UltimateMotionBuilder.ConfigureRig(root);
            PrefabUtility.SaveAsPrefabAsset(root, Folder + "/ElementalSpiritModel.prefab");
        }
        finally { Object.DestroyImmediate(root); }
    }
    static AnimationClip FlightClip(bool glide)
    {
        string name=glide?"Glide":"Hover", path=Folder+"/Spirit "+name+".anim";
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(clip==null){clip=new AnimationClip();AssetDatabase.CreateAsset(clip,path);}
        clip.ClearCurves();clip.name=name;clip.legacy=true;clip.wrapMode=WrapMode.Loop;clip.frameRate=30;
        float duration=glide?1.2f:2.4f;
        void Curve(string bone,string property,float a,float b,float c)
        {
            clip.SetCurve("Lean/Rig"+bone,typeof(Transform),property,new AnimationCurve(new Keyframe(0,a),new Keyframe(duration*.5f,b),new Keyframe(duration,c)));
        }
        Curve("","localPosition.y",.03f,glide?.09f:.16f,.03f);
        Curve("/FrostArm","localEulerAnglesRaw.z",glide?15:7,glide?19:12,glide?15:7);
        Curve("/MagmaArm","localEulerAnglesRaw.z",glide?-15:-7,glide?-19:-12,glide?-15:-7);
        Curve("/FrostArm","localEulerAnglesRaw.x",glide?-22:-3,glide?-16:4,glide?-22:-3);
        Curve("/MagmaArm","localEulerAnglesRaw.x",glide?-22:3,glide?-16:-4,glide?-22:3);
        Curve("/LeftLeg","localEulerAnglesRaw.x",glide?28:6,glide?35:12,glide?28:6);
        Curve("/RightLeg","localEulerAnglesRaw.x",glide?28:12,glide?35:6,glide?28:12);
        EditorUtility.SetDirty(clip);return clip;
    }
    static void UpdatePlayer()
    {
        const string path="Assets/Prefabs/Player.prefab";
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var view=root.GetComponentInChildren<UltimatePresentation>(true);
            foreach(var line in view.GetComponentsInChildren<LineRenderer>(true))line.sharedMaterial=beamMaterial;
            foreach(var ps in view.GetComponentsInChildren<ParticleSystemRenderer>(true))ps.sharedMaterial=beamMaterial;
            foreach(var line in view.phoenixAura.GetComponentsInChildren<LineRenderer>(true)) line.startColor=line.endColor=new Color(1,.015f,.035f);
            foreach(var particles in view.phoenixAura.GetComponentsInChildren<ParticleSystem>(true)) { var main=particles.main;main.startColor=new Color(1,.01f,.025f);main.simulationSpace=ParticleSystemSimulationSpace.Local; }
            view.meteorFill.color=new Color(.9f,.025f,.035f);
            view.meteorFill.type=Image.Type.Filled;view.meteorFill.fillMethod=Image.FillMethod.Horizontal;
            foreach(var img in view.meteorBar.GetComponentsInChildren<Image>(true)) {img.sprite=null;img.raycastTarget=false;}
            const string spritePath=Folder+"/Solid health bar.asset";
            var solid=AssetDatabase.LoadAllAssetsAtPath(spritePath).OfType<Sprite>().FirstOrDefault();
            if(solid==null)
            {
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);texture.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white});texture.Apply();texture.name="Solid health bar";
                AssetDatabase.CreateAsset(texture,spritePath);
                solid=Sprite.Create(texture,new Rect(0,0,2,2),new Vector2(.5f,.5f),2,0,SpriteMeshType.FullRect);solid.name="Solid rectangle";AssetDatabase.AddObjectToAsset(solid,texture);
            }
            view.meteorFill.sprite=solid;
            view.meteorBar.sizeDelta=new Vector2(1.9f,.16f);
            if(view.meteorModel==null)
            {
                var model=Child(view.meteor.transform,"Meteor model");
                foreach(var child in view.meteor.transform.Cast<Transform>().Where(t=>t!=model.transform).ToArray()) child.SetParent(model.transform,false);
                view.meteorModel=model.transform;
            }
            ClearChildren(view.spirit.transform);
            var wrapper=Child(view.spirit.transform,"Smoothed spirit");view.spiritModel=wrapper.transform;
            var golem=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/ElementalSpiritModel.prefab"),wrapper.transform);
            golem.transform.localPosition=Vector3.zero;
            ClearChildren(view.prisms.transform);view.crystals=new Transform[UltimateCatalog.VolleyCapacity];
            for(int i=0;i<view.crystals.Length;i++)
            {
                float a=(i*90+45)*Mathf.Deg2Rad;
                view.crystals[i]=Part(view.prisms.transform,"Stored shot "+i,new Vector3(Mathf.Cos(a)*1.1f,1.8f+Mathf.Sin(a)*.6f,0),Vector3.one*.2f,i%2==0?frost:lava,crystal).transform;
            }
            view.beam.widthMultiplier=.24f;
            if(view.polarGuide==null) view.polarGuide=Line(view.transform,"Polar targeting guide",new Color(.25f,.85f,1,.5f),1,Vector3.zero,Vector3.forward*2.5f);
            view.polarGuide.useWorldSpace=true;view.polarGuide.startWidth=.06f;view.polarGuide.endWidth=.28f;view.polarGuide.enabled=false;
            view.smoothRoots=new[]{view.phoenixAura.transform,view.meteorModel,view.spiritModel,view.flight.transform,view.prisms.transform,view.healingRing.transform,view.meteorBar};
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally {PrefabUtility.UnloadPrefabContents(root);}
    }
    static void UpdateWorlds()
    {
        string path=Folder+"/EarthDepths.prefab";var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            ClearChildren(root.transform);
            var mesh=SaveMesh("Floating island",Profile(new[]{.25f,1.5f,2.6f,3f,3f},new[]{-2.7f,-1.8f,-.7f,-.18f,-.025f},13));
            Part(root.transform,"Torn bedrock",Vector3.zero,Vector3.one,basalt,mesh);
            var topMesh=SaveMesh("Island top",Profile(new[]{2.96f,2.96f},new[]{-.14f,0f},13));
            var top=Part(root.transform,"Walkable island",Vector3.zero,Vector3.one,soil,topMesh);top.layer=0;
            var collider=top.AddComponent<MeshCollider>();collider.sharedMesh=topMesh;collider.convex=true;
            for(int i=0;i<12;i++)
            {
                float a=i*Mathf.PI/6;
                var ridge=Part(root.transform,"Broken rim "+i,new Vector3(Mathf.Cos(a)*2.8f,-.02f,Mathf.Sin(a)*2.8f),new Vector3(.28f,.25f,.48f),slate);
                ridge.transform.localRotation=Quaternion.Euler(i*5,i*30,8);
                if(i%2==0)Line(root.transform,"Lava vein "+i,new Color(1,.15f,.02f),.065f,
                    new Vector3(Mathf.Cos(a)*2.94f,-.22f,Mathf.Sin(a)*2.94f),new Vector3(Mathf.Cos(a+.1f)*2.4f,-.8f,Mathf.Sin(a+.1f)*2.4f),new Vector3(Mathf.Cos(a)*1.3f,-1.8f,Mathf.Sin(a)*1.3f));
            }
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        path=Folder+"/MirrorLabyrinth.prefab";root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            foreach(var mirror in root.GetComponent<UltimateWorldEffect>().mirrors) mirror.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        path=Folder+"/GravityInversion.prefab";root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            foreach(var line in root.GetComponentsInChildren<LineRenderer>(true))
            {
                line.sharedMaterial=beamMaterial;
                for(int i=0;i<line.positionCount;i++)
                {var p=line.GetPosition(i);float angle=Mathf.Atan2(p.z,p.x);line.SetPosition(i,new Vector3(Mathf.Cos(angle)*12,p.y>1?12:.1f,Mathf.Sin(angle)*12));}
            }
            foreach(var child in root.transform.Cast<Transform>().Where(t=>t.name.StartsWith("Lift rune")))
            {float a=Mathf.Atan2(child.localPosition.z,child.localPosition.x);child.localPosition=new Vector3(Mathf.Cos(a)*11.8f,6,Mathf.Sin(a)*11.8f);}
            foreach(var particles in root.GetComponentsInChildren<ParticleSystem>(true)){var shape=particles.shape;shape.radius=11;}
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    static void UpdateCatalog()
    {
        var catalog=AssetDatabase.LoadAssetAtPath<UltimateCatalog>(Folder+"/UltimateCatalog.asset");
        catalog.gravityRadius=12;catalog.gravityHeight=12;catalog.islandRadius=3;catalog.islandHeight=5;catalog.islandRiseTime=1.25f;
        catalog.Get(UltimateKind.PrismaticVolley).duration=15;
        catalog.Get(UltimateKind.PrismaticVolley).description="Четыре кристалла на 15 с накапливают копии фаерболлов и ледяных копий с 60% урона. F выпускает накопленный залп.";
        catalog.Get(UltimateKind.SteamFlight).title="Паровой двигатель";
        catalog.Get(UltimateKind.SteamFlight).description="На 15 с включает полёт и сразу подбрасывает мага на высоту его роста. WASD — движение, Пробел — вверх, Shift — вниз, F — завершить. Потолок полёта — 12 м от старта.";
        catalog.Get(UltimateKind.ElementalSpirit).description="Парящий голем на 10 с, 200 HP. WASD — движение, Пробел — прыжок. ЛКМ: разлом на 30 урона. ПКМ: мороз на 20 урона и замедление 50% на 2 с. Гибель голема убивает мага.";
        catalog.Get(UltimateKind.EarthDepths).description="Под магом поднимается каменный остров на высоту 5 м. Враги на нём получают 12 урона/с. F или конец 12 с: остров падает и при ударе взрывается на 30 урона в радиусе 6 м.";
        catalog.Get(UltimateKind.MirrorLabyrinth).description="Поставьте шесть зеркал по отдельности: ЛКМ — установка, колесо — поворот, ПКМ — убрать прицел, F — вернуться к установке. 90 HP у каждого. 30 с на размещение, после шестого — ещё 12 с. Зеркала отражают снаряды.";
        catalog.Get(UltimateKind.GravityInversion).title="Гравитационное поле";
        catalog.Get(UltimateKind.GravityInversion).description="Поле радиусом 12 м поднимает игроков на 12 м. F: предупреждение 0,8 с и обрушение. Удар о землю наносит 40 урона и создаёт hit-эффект.";
        catalog.Get(UltimateKind.GlacierRam).description="Сфера на 8 с с камерой от третьего лица. WASD — движение, мышь — камера. Маг стоит на месте. Таран наносит 15 урона раз в секунду. F / ЛКМ: взрыв на 40 урона в 4 м и оглушение на 3 с.";
        catalog.Get(UltimateKind.HeatDrain).title="Похищение энергии";catalog.Get(UltimateKind.HeatDrain).color=new Color(.8f,.02f,.04f);
        catalog.Get(UltimateKind.PhoenixBirth).color=new Color(1,.02f,.035f);
        EditorUtility.SetDirty(catalog);
        UltimateMotionBuilder.ApplyDurations(catalog);
    }
}
