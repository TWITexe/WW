using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// читает фактические настройки перед правками большого набора механик.
public static class GameplayRevisionBuilder
{
    const string Marker="Logs/gameplay-revision-applied.txt";
    const string Backup="Logs/GameplayRevisionBefore/";
    static readonly System.Collections.Generic.Dictionary<string,string> replacements = new System.Collections.Generic.Dictionary<string,string>();

    // один запуск меняет численные параметры; повторные запуски не умножают скорости и размеры ещё раз.
    public static void Apply()
    {
        if(Application.isPlaying)throw new InvalidOperationException("нужно остановить play mode");
        if(File.Exists(Marker))return;
        Directory.CreateDirectory(Backup);
        var catalog=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab").GetComponentInChildren<SpellManager>().Spells;
        foreach(var spell in catalog)
        {
            SaveBackup(AssetDatabase.GetAssetPath(spell));
            string oldDescription=spell.Description, oldName=spell.Name;
            if(spell is ElementalSpell e)
            {
                if(e.speed>0)e.speed=FasterSpeed(e.speed);
                if(e.name=="FireTornado"){e.damage=5;e.tickInterval=.5f;e.knockback=-12;e.lift=9;}
                if(e.name=="Geyser")e.lift=36;
                if(e.name=="BoilingJet"){e.damage=5;e.radius=.35f;e.knockback=.4f;}
            }
            else if(spell is AdvancedSpell a)
            {
                if(a.IsProjectile)a.projectileSpeed=FasterSpeed(a.projectileSpeed);
                if(a.kind==AdvancedSpellKind.ShardVortex){a.impactDamage=25;a.impactOffsets=new[]{.2f,1f};a.duration=1.2f;}
                if(a.kind==AdvancedSpellKind.CrystalCrash){a.impactOffsets=new[]{0f,.15f};a.duration=.6f;}
            }
            else if(spell is TacticalSpell t && t.kind==TacticalKind.SnowDecoy)t.damage=15;
            var data=new SerializedObject(spell);
            if(spell is FireBall || spell is WindFlow)
            {
                var speed=data.FindProperty("speed");speed.floatValue=FasterSpeed(speed.floatValue);
            }
            if(spell.name=="ScaldingMist")data.FindProperty("displayName").stringValue="Огненный туман";
            data.ApplyModifiedPropertiesWithoutUndo();
            data.Update();data.FindProperty("description").stringValue=Description(spell);data.ApplyModifiedPropertiesWithoutUndo();
            replacements[oldDescription]=spell.Description;
            if(oldName!=spell.Name)replacements[oldName]=spell.Name;
            EditorUtility.SetDirty(spell);
        }
        AssetDatabase.SaveAssets();
        foreach(var spell in catalog)
        {
            GameObject prefab=spell is ElementalSpell e?e.effectPrefab:spell is AdvancedSpell a?a.effectPrefab:spell is TacticalSpell t?t.effectPrefab:spell is FireBall f?f.PreviewPrefab:((WindFlow)spell).PreviewPrefab;
            if(prefab==null)continue;
            EditPrefab(AssetDatabase.GetAssetPath(prefab),root=>ConfigureEffect(root,spell));
        }
        MakeMaterial("AreaTarget","Universal Render Pipeline/Particles/Unlit",new Color(.2f,1,.75f));
        EditPrefab("Assets/Prefabs/Player.prefab",ConfigureOverhead);
        foreach(string path in new[]{"Assets/Prefabs/UI/MatchUI.prefab","Assets/Prefabs/UI/SpellbookUI.prefab"})EditPrefab(path,UpdateInterface);
        var original=SceneManager.GetActiveScene();
        foreach(string path in new[]{"Assets/Scenes/Menu.unity","Assets/Scenes/SampleScene.unity"})
        {
            SaveBackup(path);
            var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            foreach(var root in scene.GetRootGameObjects())
            {
                UpdateInterface(root);
                if(path.EndsWith("Menu.unity"))foreach(var audio in root.GetComponentsInChildren<AudioSource>(true))
                    if(audio.clip!=null && audio.clip.name=="rpg-the-graveyard")
                    {
                        audio.loop=true;audio.playOnAwake=false;
                        if(audio.GetComponent<MenuMusicFade>()==null)audio.gameObject.AddComponent<MenuMusicFade>();
                    }
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            if(opened)EditorSceneManager.CloseScene(scene,true);
        }
        if(original.IsValid())SceneManager.SetActiveScene(original);
        AssetDatabase.SaveAssets();
        File.WriteAllText(Marker,DateTime.Now.ToString("O"));
        Inspect();
    }

    // на очень быстрых снарядах оставляем минимум 0,1 секунды полёта, избегая бесконечной скорости.
    public static float FasterSpeed(float speed)=>15f/Mathf.Max(.1f,15f/speed-.5f);
    static void SaveBackup(string path)
    {
        string target=Backup+path;
        if(File.Exists(target))return;
        Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(path,target);
    }
    static void EditPrefab(string path,Action<GameObject> edit)
    {
        SaveBackup(path);
        var root=PrefabUtility.LoadPrefabContents(path);
        try{edit(root);PrefabUtility.SaveAsPrefabAsset(root,path);}
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    static Material MakeMaterial(string name,string shader,Color color)
    {
        string path="Assets/Resources/"+name+".mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null){mat=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(mat,path);}
        mat.SetColor("_BaseColor",color);EditorUtility.SetDirty(mat);return mat;
    }

    static void ConfigureEffect(GameObject root,Spell spell)
    {
        if(spell is WindFlow)
        {
            var data=new SerializedObject(root.GetComponent<WindFlowProjectile>());
            data.FindProperty("windFlowForce").intValue=90;data.ApplyModifiedPropertiesWithoutUndo();
        }
        if(spell is TacticalSpell tactical && tactical.kind==TacticalKind.StoneWall)
        {
            var scale=root.transform.localScale;scale.y*=1.5f;root.transform.localScale=scale;
        }
        var visual=root.GetComponent<ElementalVisual>();
        bool bolt=spell is FireBall || spell is WindFlow || spell is ElementalSpell elemental && elemental.mode==ElementalCastMode.Bolt;
        if(bolt && visual!=null && visual.enabled)
        {
            float enlargement=visual.projectileVisualScale;
            if(spell.name=="BoilingJet")
            {
                visual.projectileVisualScale=1;
                root.transform.localScale*=.7f;
                enlargement=1;
            }
            // учитываем увеличение ядра, которое графический компонент выполняет при старте.
            if(root.TryGetComponent<SphereCollider>(out var collider))collider.radius*=enlargement;
        }
        if(spell.name=="SmokeCloud")
        {
            var flight=root.GetComponent<ArcSmokeProjectile>()??root.AddComponent<ArcSmokeProjectile>();
            if(root.GetComponent<Mirror.NetworkTransformReliable>()==null)root.AddComponent<Mirror.NetworkTransformReliable>();
            var seed=GameObject.CreatePrimitive(PrimitiveType.Sphere);seed.name="дымовой заряд";seed.layer=2;
            UnityEngine.Object.DestroyImmediate(seed.GetComponent<Collider>());
            seed.transform.SetParent(root.transform,false);seed.transform.localScale=Vector3.one*.45f;
            seed.GetComponent<MeshRenderer>().sharedMaterial=MakeMaterial("SmokeSeed","Universal Render Pipeline/Lit",new Color(.35f,.4f,.43f));
            flight.projectileVisual=seed;
        }
        if(spell.name=="ScaldingMist")
        {
            var effect=root.GetComponent<AdvancedSpellEffect>();var av=root.GetComponent<AdvancedSpellVisual>();
            var fade=root.GetComponent<AdvancedMistFade>()??root.AddComponent<AdvancedMistFade>();fade.effect=effect;fade.area=av.area;
            foreach(var particles in av.area.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=particles.main;main.startSizeMultiplier*=1.8f;
                var lifetime=particles.colorOverLifetime;lifetime.enabled=true;
                var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.15f),new GradientAlphaKey(1,.7f),new GradientAlphaKey(0,1)});lifetime.color=gradient;
            }
        }
    }

    static void ConfigureOverhead(GameObject root)
    {
        foreach(var healthUI in root.GetComponentsInChildren<HealthUI>(true))
        {
            var data=new SerializedObject(healthUI);
            var text=data.FindProperty("healthText").objectReferenceValue as TMPro.TMP_Text;
            if(text==null)throw new Exception("у HealthUI не назначен прежний текст");
            text.enabled=false;
            if(data.FindProperty("healthBar").objectReferenceValue!=null)continue;
            var background=new GameObject("HP Bar",typeof(RectTransform),typeof(UnityEngine.UI.Image));
            background.transform.SetParent(text.transform.parent,false);
            var rect=background.GetComponent<RectTransform>();rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.sizeDelta=new Vector2(1.8f,.16f);rect.anchoredPosition=text.rectTransform.anchoredPosition;
            var image=background.GetComponent<UnityEngine.UI.Image>();image.color=new Color(.025f,.025f,.035f,.9f);image.raycastTarget=false;
            var fill=new GameObject("Health fill",typeof(RectTransform),typeof(UnityEngine.UI.Image));fill.transform.SetParent(rect,false);
            var fillRect=fill.GetComponent<RectTransform>();fillRect.anchorMin=Vector2.zero;fillRect.anchorMax=Vector2.one;fillRect.offsetMin=new Vector2(.02f,.02f);fillRect.offsetMax=new Vector2(-.02f,-.02f);
            var fillImage=fill.GetComponent<UnityEngine.UI.Image>();fillImage.color=new Color(.3f,.95f,.48f);fillImage.raycastTarget=false;
            data.FindProperty("healthBar").objectReferenceValue=fillImage;data.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static void UpdateInterface(GameObject root)
    {
        foreach(var text in root.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            if(replacements.TryGetValue(text.text,out string replacement))text.text=replacement;
        foreach(var ui in root.GetComponentsInChildren<PlayerGameUI>(true))
        {
            var data=new SerializedObject(ui);var cards=data.FindProperty("cards");
            for(int i=0;i<cards.arraySize;i++)
            {
                var card=cards.GetArrayElementAtIndex(i);
                var seconds=card.FindPropertyRelative("seconds").objectReferenceValue as UnityEngine.UI.Text;
                var cardRoot=card.FindPropertyRelative("root").objectReferenceValue as GameObject;
                seconds.transform.SetParent(cardRoot.transform,false);
                var rect=seconds.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=new Vector2(1,14);rect.offsetMax=new Vector2(-1,-1);
                seconds.fontSize=38;seconds.resizeTextForBestFit=true;seconds.resizeTextMinSize=24;seconds.resizeTextMaxSize=42;seconds.alignment=TextAnchor.MiddleCenter;seconds.raycastTarget=false;
            }
            var canvas=data.FindProperty("canvas").objectReferenceValue as Canvas;
            if(data.FindProperty("killFeed").objectReferenceValue!=null)continue;
            var obj=new GameObject("Kill Bar",typeof(RectTransform),typeof(UnityEngine.UI.Text));obj.transform.SetParent(canvas.transform,false);
            var r=obj.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(24,-100);r.sizeDelta=new Vector2(570,150);
            var label=obj.GetComponent<UnityEngine.UI.Text>();label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.fontSize=20;label.color=Color.white;label.raycastTarget=false;label.supportRichText=true;label.lineSpacing=1.15f;
            data.FindProperty("killFeed").objectReferenceValue=label;data.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static string Number(float value)=>value.ToString("0.##",System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    static string Description(Spell spell)
    {
        if(spell is FireBall)return "Огненный снаряд наносит 24 базового урона первой цели. Взрыв при столкновении — визуальный эффект.";
        if(spell is WindFlow)return "Воздушный снаряд сильно отбрасывает первую цель. Не наносит урон персонажам; разбивает лёгкие предметы.";
        if(spell is ElementalSpell e)
        {
            string slow=e.slow<1?" Замедляет на "+Number((1-e.slow)*100)+"% на "+Number(e.slowDuration)+" с.":"";
            if(e.name=="SmokeCloud")return "Дымовой заряд летит по дуге к прицелу. При столкновении раскрывает завесу радиусом 6 м на 10 с. Закрывает обзор, не наносит урон.";
            if(e.name=="BoilingJet")return "Выпускает 12 маленьких капель за 3 с в направлении прицела. Каждая наносит 5 базового урона первой цели. Можно вести струю за противником.";
            if(e.mode==ElementalCastMode.Shield)return "Поглощает до "+e.shieldAmount+" урона в течение "+Number(e.duration)+" с. Повторное применение не складывает щиты.";
            if(e.mode==ElementalCastMode.Bolt)return "Снаряд наносит "+e.damage+" базового урона"+(e.radius>.5f?" в радиусе "+Number(e.radius)+" м.":" первой цели.")+slow+(e.knockback>0?" Отталкивает.":"");
            if(e.mode==ElementalCastMode.SelfBurst)return "Мгновенная волна вокруг мага: "+e.damage+" базового урона в радиусе "+Number(e.radius)+" м. Отталкивает."+slow;
            return (e.mode==ElementalCastMode.Tornado?"Движущийся огненный торнадо":"Область")+" радиусом "+Number(e.radius)+" м на "+Number(e.duration)+" с. Наносит "+Number(e.damage/e.tickInterval)+" базового урона в секунду."+slow+(e.name=="FireTornado"?" Затягивает и подбрасывает врагов. Останавливается у стен.":e.name=="Geyser"?" Сильно подбрасывает врагов.":"")+" ЛКМ — подтвердить область.".Replace(e.mode==ElementalCastMode.Tornado?" ЛКМ — подтвердить область.":"~","");
        }
        if(spell is TacticalSpell t)
        {
            switch(t.kind)
            {
                case TacticalKind.SteamDash:return "Рывок в направлении движения; без движения — вперёд. Помогает быстро сменить позицию.";
                case TacticalKind.IceMirror:return "Зеркало перед магом на "+Number(t.duration)+" с. Отражает один чужой снаряд к его владельцу и исчезает.";
                case TacticalKind.StoneWall:return "Каменная стена высотой 4,05 м на "+Number(t.duration)+" с. Блокирует проход и снаряды. Выберите место и подтвердите ЛКМ.";
                case TacticalKind.FireSeal:return "Ловушка взводится за 1 с и становится невидимой. При приближении врага взрывается: "+t.damage+" базового урона в радиусе "+Number(t.radius)+" м. Существует "+Number(t.duration)+" с.";
                case TacticalKind.GravityWell:return "Сильно притягивает видимых противников к центру области радиусом "+Number(t.radius)+" м в течение "+Number(t.duration)+" с. Не наносит урон.";
                case TacticalKind.SnowDecoy:return "Движущийся снежный двойник на "+Number(t.duration)+" с. Разбивший его противник получает 15 базового урона и замедление 50% на 2 с.";
            }
        }
        if(spell is AdvancedSpell a)
        {
            switch(a.kind)
            {
                case AdvancedSpellKind.ShardVortex:return "Замедляющая воронка радиусом 3 м. После подготовки 1,5 с два взрыва наносят по 25 базового урона: через 0,2 и 1 с. Каждый замедляет на 35% на 2 с.";
                case AdvancedSpellKind.ScaldingMist:return "Снаряд создаёт плотный огненный туман радиусом 5,75 м на 3 с. Закрывает обзор и наносит 10 базового урона в секунду. Плавно появляется и рассеивается.";
                case AdvancedSpellKind.Meteor:return "Через 2,5 с после ЛКМ метеорит наносит 45 базового урона в радиусе 3 м. Затем земля горит 3 с: 5 урона в секунду. Подготовка замедляет мага на 50%.";
                case AdvancedSpellKind.ThermalSpring:return "Источник радиусом 2,5 м на 5 с. Лечит только заклинателя на 10 здоровья в секунду. Полученный урон, включая урон щиту, прекращает лечение.";
                case AdvancedSpellKind.BoilingIce:return "Снаряд задерживается на 0,8 с после столкновения, затем создаёт область радиусом 6,25 м. Наносит 7 базового урона в секунду в течение 5 с.";
                case AdvancedSpellKind.IceBridge:return "Скользкая платформа 3 × 10 м перед магом на 10 с. Может висеть в воздухе. Требует свободного места; доступна всем игрокам.";
                case AdvancedSpellKind.CrystalCrash:return "Через 2,5 с два взрыва кристалла с интервалом 0,15 с наносят по 30 базового урона в радиусе 2,5 м. Каждый оглушает врагов на 1,5 с.";
                case AdvancedSpellKind.SteamLens:return "Линза перед магом на 4 с усиливает один его фаерболл или ледяное копьё: +6 базового урона и +35% скорости. После усиления разбивается.";
            }
        }
        return spell.Description;
    }
    public static void Inspect()
    {
        var report = new StringBuilder();
        report.AppendLine("playing="+Application.isPlaying);
        var player=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        foreach(var spell in player.GetComponentInChildren<SpellManager>().Spells)
            report.AppendLine(AssetDatabase.GetAssetPath(spell)+" "+JsonUtility.ToJson(spell));
        foreach(var ui in player.GetComponentsInChildren<HealthUI>(true)) report.AppendLine("health UI "+JsonUtility.ToJson(ui)+" parent="+ui.transform.parent.name);
        foreach(var collider in player.GetComponentsInChildren<Collider>(true))report.AppendLine("player collider "+collider.name+" "+collider.GetType().Name);
        for(int i=0;i<SceneManager.sceneCount;i++)report.AppendLine("scene "+SceneManager.GetSceneAt(i).path+" dirty="+SceneManager.GetSceneAt(i).isDirty);
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/Menu.unity");bool opened=!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity",OpenSceneMode.Additive);
        foreach(var root in scene.GetRootGameObjects())foreach(var audio in root.GetComponentsInChildren<AudioSource>(true))
            report.AppendLine("audio "+audio.name+" clip="+(audio.clip!=null?audio.clip.name:"none")+" volume="+audio.volume+" loop="+audio.loop+" play="+audio.playOnAwake);
        foreach(var root in scene.GetRootGameObjects())report.AppendLine("menu root "+root.name);
        if(opened)EditorSceneManager.CloseScene(scene,true);
        File.WriteAllText("Logs/gameplay-revision-inspection.txt",report.ToString());
    }
    public static void InspectGraphics()
    {
        var report=new StringBuilder();
        foreach(string path in new[]{"Assets/Spells/Advanced/ShardVortexImpactVisual.prefab","Assets/Spells/Advanced/CrystalCrashImpactVisual.prefab","Assets/Prefabs/Player.prefab","Assets/Prefabs/FireBall.prefab","Assets/Other Asstets/GeneratedWizard/IceShard.prefab","Assets/Other Asstets/GeneratedWizard/BoilingJet.prefab"})
        {
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                report.AppendLine(path);
                foreach(var particles in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var emission=particles.emission;
                    report.Append(particles.name+" delay="+particles.main.startDelay.constant+" lifetime="+particles.main.startLifetime.constant+" bursts:");
                    for(int i=0;i<emission.burstCount;i++)report.Append(emission.GetBurst(i).time+",");
                    report.AppendLine();
                }
                foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>(true))report.AppendLine("mesh "+renderer.name+" "+renderer.bounds);
                foreach(var collider in root.GetComponentsInChildren<Collider>(true))report.AppendLine("collider "+collider.name+" "+collider.bounds);
                foreach(var particles in root.GetComponentsInChildren<ParticleSystem>())
                {
                    var renderer=particles.GetComponent<ParticleSystemRenderer>();
                    if(renderer.renderMode!=ParticleSystemRenderMode.Mesh || renderer.mesh==null)continue;
                    report.AppendLine("particle mesh "+particles.name+" mesh="+renderer.mesh.bounds+" scale="+particles.transform.lossyScale+" size="+particles.main.startSize.constant+" size3d="+particles.main.startSize3D+" xyz="+particles.main.startSizeX.constant+","+particles.main.startSizeY.constant+","+particles.main.startSizeZ.constant);
                }
                foreach(var ui in root.GetComponentsInChildren<HealthUI>(true))
                {
                    var text=new SerializedObject(ui).FindProperty("healthText").objectReferenceValue as TMPro.TMP_Text;
                    report.AppendLine("health text "+text.name+" rect="+text.rectTransform.rect+" pos="+text.rectTransform.anchoredPosition+" parent="+text.transform.parent.name);
                }
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        File.WriteAllText("Logs/gameplay-graphics-inspection.txt",report.ToString());
    }
}
