using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// проверяет серверные переходы на временной сцене, не запуская сетевой матч и не меняя арену.
public static class GameplayRevisionRegression
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<Health> actors = new List<Health>();
    static readonly List<NetworkIdentity> identities = new List<NetworkIdentity>();
    static readonly List<string> checks = new List<string>();

    public static void Run()
    {
        if(Application.isPlaying || NetworkServer.active)throw new Exception("проверка требует остановленного матча");
        checks.Clear();
        AdvancedSpellRegression.Run();
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var serverFlag = typeof(NetworkServer).GetProperty("active",BindingFlags.Public|BindingFlags.Static);
        GameplayProbeSpell probe = null;
        serverFlag.SetValue(null,true);
        try
        {
            var origin = new Vector3(20000,20000,20000);
            var owner = Actor(910001,origin);
            var enemy = Actor(910002,origin+Vector3.right);
            CheckPulses(owner,enemy,origin,"ShardVortex",.2,1,25);
            CheckPulses(owner,enemy,origin,"CrystalCrash",0,.15,30);
            probe = CheckConfirmation(owner,origin);
            CheckDecoy(owner,enemy,origin);
            CheckEnvironment(origin+Vector3.forward*40);
            CheckAssets();
            File.WriteAllLines("Logs/gameplay-revision-validation.txt",checks.Concat(new[]{DateTime.Now.ToString("O")}));
        }
        finally
        {
            foreach(var actor in actors)if(actor!=null){NetworkServer.spawned.Remove(actor.netId);actor.OnStopServer();}
            foreach(var identity in identities)if(identity!=null)typeof(NetworkIdentity).GetProperty("isServer").SetValue(identity,false);
            actors.Clear();identities.Clear();serverFlag.SetValue(null,false);
            if(probe!=null)Object.DestroyImmediate(probe);
            EditorSceneManager.CloseScene(scene,true);
            if(original.IsValid())SceneManager.SetActiveScene(original);
        }
    }

    static void ServerObject(GameObject root,uint id)
    {
        var identity = root.GetComponent<NetworkIdentity>();
        typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours",Private).Invoke(identity,null);
        typeof(NetworkIdentity).GetProperty("isServer").SetValue(identity,true);
        typeof(NetworkIdentity).GetProperty("netId").SetValue(identity,id);
        identities.Add(identity);
    }
    static Health Actor(uint id,Vector3 position)
    {
        var root = new GameObject("серверный игрок проверки",typeof(NetworkIdentity));root.transform.position=position;
        var entity = new GameObject("Entity",typeof(CharacterController),typeof(RelativeMovement),typeof(Health));entity.transform.SetParent(root.transform,false);
        ServerObject(root,id);
        var health=entity.GetComponent<Health>();health.OnStartServer();actors.Add(health);
        NetworkServer.spawned.Add(id,root.GetComponent<NetworkIdentity>());
        return health;
    }

    // каждый визуальный импульс обязан причинить урон ровно один раз, в том числе при пропущенном кадре.
    static void CheckPulses(Health owner,Health enemy,Vector3 origin,string name,double first,double second,int damage)
    {
        var root=new GameObject("взрывы проверки",typeof(NetworkIdentity),typeof(AdvancedSpellEffect));root.transform.position=origin;
        ServerObject(root,0);
        var effect=root.GetComponent<AdvancedSpellEffect>();effect.ownerId=owner.netId;effect.phase=1;
        effect.definition=AssetDatabase.LoadAssetAtPath<AdvancedSpell>("Assets/Spells/Advanced/"+name+".asset");
        Physics.SyncTransforms();Set(enemy,"currentHealth",100);
        Call(effect,"ApplyImpactPulses",first-.01);Require(enemy.CurrentHealth==100,name+": урон раньше взрыва");
        Call(effect,"ApplyImpactPulses",first);
        int afterFirst=enemy.CurrentHealth;
        Require(afterFirst<100 && afterFirst>=100-Mathf.CeilToInt(damage*1.1f),name+": первый взрыв");
        Call(effect,"ApplyImpactPulses",second-.01);Require(enemy.CurrentHealth==afterFirst,name+": повторный ранний урон");
        Call(effect,"ApplyImpactPulses",second);Require(enemy.CurrentHealth<afterFirst,name+": второй взрыв");
        int afterSecond=enemy.CurrentHealth;
        Call(effect,"ApplyImpactPulses",second+10);Require(enemy.CurrentHealth==afterSecond,name+": лишний урон");
        Set(effect,"impactIndex",0);Set(enemy,"currentHealth",100);
        Call(effect,"ApplyImpactPulses",second+1);Require((int)Get(effect,"impactIndex")==2 && enemy.CurrentHealth<100-damage,name+": потерян взрыв при задержке кадра");
        Require(owner.CurrentHealth==100,name+": нанесён урон владельцу");
        checks.Add("PASS: "+name+" — оба взрыва, точное время, без повторов, пропуск кадра, исключение владельца.");
    }

    static GameplayProbeSpell CheckConfirmation(Health owner,Vector3 origin)
    {
        var manager=owner.gameObject.AddComponent<SpellManager>();
        var caster=owner.gameObject.AddComponent<PlayerNetworkCaster>();
        ServerObject(owner.netIdentity.gameObject,owner.netId);
        Call(caster,"Awake");
        var muzzle=new GameObject("точка выпуска проверки");muzzle.transform.SetParent(owner.transform,false);muzzle.transform.localPosition=Vector3.up*.7f;
        Set(caster,"firePoint",muzzle.transform);
        var spell=ScriptableObject.CreateInstance<GameplayProbeSpell>();spell.kind=AdvancedSpellKind.Meteor;
        var data=new SerializedObject(spell);var recipe=data.FindProperty("recipe");recipe.arraySize=3;
        for(int i=0;i<3;i++)recipe.GetArrayElementAtIndex(i).enumValueIndex=i;
        data.ApplyModifiedPropertiesWithoutUndo();
        Set(manager,"spells",new List<Spell>{spell});Set(caster,"loadout",ElementLoadout.Default);Set(caster,"loadoutReady",true);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=origin+new Vector3(0,-.25f,5);floor.transform.localScale=new Vector3(5,.5f,5);
        Physics.SyncTransforms();Vector3 view=origin+Vector3.up;Vector3 direction=(floor.transform.position+Vector3.up*.25f-view).normalized;
        Require(caster.TryGroundTarget(new Ray(view,direction),out _),"не подготовлена видимая поверхность для проверки прицела");
        void Arm(){for(int slot=0;slot<3;slot++)Command(caster,"CmdSubmitElement",slot,view,direction,Vector3.zero);}
        var cooldowns=(Dictionary<Spell,double>)Get(caster,"serverCooldowns");
        Arm();uint token=(uint)Get(caster,"pendingServerToken");
        Require(Get(caster,"pendingServerSpell")==spell && spell.casts==0 && cooldowns.Count==0,"комбинация расходует заклинание до лкм");
        Command(caster,"CmdConfirmArea",token+1,view,direction);Require(spell.casts==0,"принят чужой номер прицела");
        Command(caster,"CmdConfirmArea",token,view,Vector3.up);Require(spell.casts==0 && cooldowns.Count==0,"невалидная цель расходует кулдаун");
        Set(caster,"pendingServerUntil",NetworkTime.time-.01);Command(caster,"CmdConfirmArea",token,view,direction);Require(spell.casts==0,"прицел не истёк");
        Arm();Command(caster,"CmdSubmitElement",0,view,direction,Vector3.zero);Require(Get(caster,"pendingServerSpell")==null,"новая стихия не сбрасывает прицел");
        ((List<MagicElement>)Get(caster,"serverInput")).Clear();Arm();token=(uint)Get(caster,"pendingServerToken");
        Command(caster,"CmdConfirmArea",token,view,direction);Require(spell.casts==1 && cooldowns.ContainsKey(spell) && Get(caster,"pendingServerSpell")==null,"лкм не применяет область один раз");
        Command(caster,"CmdConfirmArea",token,view,direction);Require(spell.casts==1,"повтор команды создаёт вторую область");
        checks.Add("PASS: прицел — комбинация без каста/кулдауна, неверная цель и токен, 5-секундный срок, отмена стихией, лкм и защита от повторной команды.");
        return spell;
    }

    static void CheckDecoy(Health owner,Health enemy,Vector3 origin)
    {
        Set(enemy,"currentHealth",100);
        var bystander=Actor(910003,origin+Vector3.forward*2);
        var root=new GameObject("двойник проверки",typeof(NetworkIdentity),typeof(TacticalEffect));root.transform.position=bystander.transform.position;
        ServerObject(root,0);
        var effect=root.GetComponent<TacticalEffect>();effect.ownerId=owner.netId;
        effect.definition=AssetDatabase.LoadAssetAtPath<TacticalSpell>("Assets/Scripts/Spells/Tactical/SnowDecoy.asset");
        effect.ProjectileHit(enemy.netId);
        Require(enemy.CurrentHealth<100 && bystander.CurrentHealth==100 && owner.CurrentHealth==100,"двойник наказал не стрелявшего игрока");
        Require((float)Get(enemy.GetComponent<RelativeMovement>(),"slowMultiplier")==.5f,"двойник не замедлил стрелявшего");
        checks.Add("PASS: двойник — урон и замедление стрелявшему, сосед и владелец не задеты.");
    }

    static void CheckAssets()
    {
        var player=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        var catalog=player.GetComponentInChildren<SpellManager>().Spells;
        foreach(var ui in player.GetComponentsInChildren<HealthUI>(true))Require(new SerializedObject(ui).FindProperty("healthBar").objectReferenceValue!=null,"не назначен hp bar");
        var smoke=catalog.OfType<ElementalSpell>().Single(s=>s.name=="SmokeCloud");
        Require(smoke.effectPrefab.GetComponent<ArcSmokeProjectile>()?.projectileVisual!=null && smoke.effectPrefab.GetComponent<NetworkTransformReliable>()!=null,"дым не подготовлен к сетевому броску");
        var fire=catalog.OfType<ElementalSpell>().Single(s=>s.name=="FireTornado");Require(fire.damage/fire.tickInterval==10 && fire.knockback==-12,"настройки торнадо");
        var sorted=catalog.ToList();sorted.Sort(Spell.CompareSimplicity);Require(sorted.Take(5).All(s=>s.Recipe.Distinct().Count()==1),"простые рецепты не первые");
        foreach(var path in new[]{"Assets/Prefabs/UI/MatchUI.prefab","Assets/Prefabs/UI/SpellbookUI.prefab"})
            foreach(var ui in AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<PlayerGameUI>(true))
                Require(new SerializedObject(ui).FindProperty("killFeed").objectReferenceValue!=null,"не назначена лента убийств");
        checks.Add("PASS: префабы hp/kill bar, сетевой дым, 10 урона/с торнадо, простой порядок каталога.");
    }
    // проверяем стены и дугу броска настоящими запросами физики, включая очень быстрый пролёт копья.
    static void CheckEnvironment(Vector3 origin)
    {
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=origin+new Vector3(0,1,3);wall.transform.localScale=new Vector3(4,4,.1f);
        var root=new GameObject("вихрь проверки",typeof(NetworkIdentity),typeof(ElementalEffect));
        var tornado=root.GetComponent<ElementalEffect>();
        Vector3 start=origin+Vector3.up;Set(tornado,"previousPosition",start);root.transform.position=start+Vector3.forward*6;
        Physics.SyncTransforms();Require((bool)Call(tornado,"TryTornadoWall",Vector3.zero),"торнадо пролетел тонкую стену");
        Set(tornado,"previousPosition",wall.transform.position);Require((bool)Call(tornado,"TryTornadoWall",Vector3.zero),"торнадо не замечает начальное перекрытие стены");
        wall.SetActive(false);Set(tornado,"previousPosition",start);Physics.SyncTransforms();Require(!(bool)Call(tornado,"TryTornadoWall",Vector3.zero),"торнадо останавливается в пустоте");
        var spear=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Other Asstets/GeneratedWizard/IceShard.prefab"));
        spear.transform.position=start+Vector3.forward*8;wall.SetActive(true);Physics.SyncTransforms();
        Require(ProjectileContact.Sweep(spear.transform,0,start,out var target,out _) && target==wall.GetComponent<Collider>(),"копьё пролетело тонкую стену");
        spear.SetActive(false);wall.SetActive(false);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=origin-Vector3.up*.25f;floor.transform.localScale=new Vector3(40,.5f,40);
        var smokeAsset=AssetDatabase.LoadAssetAtPath<ElementalSpell>("Assets/Scripts/Spells/Elemental/SmokeCloud.asset");
        var smoke=Object.Instantiate(smokeAsset.effectPrefab);smoke.transform.position=origin+Vector3.up*2;
        ServerObject(smoke,0);var flight=smoke.GetComponent<ArcSmokeProjectile>();var area=smoke.GetComponent<ElementalEffect>();
        Call(flight,"Awake");area.OnStartServer();flight.OnStartServer();flight.Launch(Vector3.forward*5+Vector3.up*5);
        double before=NetworkTime.time;float highest=smoke.transform.position.y;
        for(int i=0;i<150 && flight.Flying;i++){Call(flight,"FixedUpdate");Physics.SyncTransforms();highest=Mathf.Max(highest,smoke.transform.position.y);}
        Require(highest>origin.y+2.5f && !flight.Flying && smoke.transform.position.z>origin.z+3 && area.bornAt>=before,"дуга или раскрытие дымовой завесы");
        checks.Add("PASS: торнадо — тонкая стена, начальное перекрытие, свободный путь; быстрое копьё не пропускает стену; дым летит вверх по дуге и раскрывается на земле.");
    }
    static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private).Invoke(target,args);
    static void Command(object target,string name,params object[] args)=>target.GetType().GetMethods(Private).Single(m=>m.Name.StartsWith("UserCode_"+name+"__")).Invoke(target,args);
    static object Get(object target,string name)=>target.GetType().GetField(name,Private).GetValue(target);
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
}

// подставной эффект считает применения: подтверждение тестируется без создания боевого снаряда и открытия соединения.
public class GameplayProbeSpell : AdvancedSpell
{
    public int casts;
    public override bool ActivateServer(PlayerNetworkCaster caster,Vector3 direction){casts++;return true;}
}
