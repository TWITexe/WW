using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// проверяем точные тики, серверный снимок движения и скольжение на временной сцене без запуска матча.
public static class AdvancedSpellRegression
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Wizard War/Check advanced mechanics")]
    public static void Run()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Exit Play Mode.");
        AdvancedSpellBuilder.Validate();
        CheckTicks("ScaldingMist",6,30);
        CheckTicks("Meteor",3,15);
        CheckTicks("BoilingIce",5,35);
        CheckTicks("ThermalSpring",5,0);
        var spring=AssetDatabase.LoadAssetAtPath<AdvancedSpell>("Assets/Spells/Advanced/ThermalSpring.asset");
        Require(spring.healPerTick*AdvancedSpellEffect.DueTicks(spring,5)==50&&spring.Cooldown==17,"Healing budget.");
        Scene original=SceneManager.GetActiveScene();
        Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            Vector3 origin=new Vector3(18000,18000,18000);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position=origin-Vector3.up*.25f;floor.transform.localScale=new Vector3(20,.5f,20);floor.AddComponent<IceBridgeSurface>();
            var actor=new GameObject("Movement test",typeof(Mirror.NetworkIdentity),typeof(CharacterController),typeof(RelativeMovement));
            actor.transform.position=origin+Vector3.up;
            var controller=actor.GetComponent<CharacterController>();controller.height=2;controller.radius=.4f;controller.center=Vector3.zero;
            var movement=actor.GetComponent<RelativeMovement>();movement.enabled=false;
            Set(movement,"controller",controller);Set(movement,"vertSpeed",-1.5f);Set(movement,"stamina",100f);
            Physics.SyncTransforms();
            Require((bool)Call(movement,"OnIceBridge"),"Ice surface not detected.");
            var input=new RelativeMovement.MoveInput{movement=Vector3.right,forward=Vector3.forward};
            for(int i=0;i<12;i++)Call(movement,"Simulate",input,.02f,1d+i*.02);
            float before=actor.transform.position.x;
            var stopped=new RelativeMovement.MoveInput{forward=Vector3.forward};
            for(int i=0;i<6;i++)Call(movement,"Simulate",stopped,.02f,2d+i*.02);
            Require(actor.transform.position.x>before+.01f,"Ice did not retain momentum.");
            Set(movement,"stunUntil",10d);
            before=actor.transform.position.x;
            input.jump=true;input.sprint=true;
            for(int i=0;i<6;i++)Call(movement,"Simulate",input,.02f,3d+i*.02);
            Require(Mathf.Abs(actor.transform.position.x-before)<.002f&&!movement.IsSprinting,"Stun allowed movement.");
            var state=(RelativeMovement.MotorState)Call(movement,"CaptureState");
            Require(state.stunUntil==10&&state.iceVelocity.sqrMagnitude==0,"Prediction snapshot omitted control state.");
            state.epoch=7;state.tick=1;state.stunUntil=11;
            Call(movement,"Reconcile",state);
            Require((double)Get(movement,"stunUntil")==11,"Reconciliation lost stun.");
            CheckServerEffects(origin+Vector3.forward*30);
            CheckLens(origin+Vector3.forward*60);
            File.WriteAllText("Logs/advanced-mechanics-validation.txt","PASS: exact final ticks (30/15/35 damage, 50 healing); solid ice surface and sliding; stun blocks movement/sprint/jump input; movement snapshot and reconciliation preserve stun; server area damage/slow/stun, owner exclusion, cover, healing interruption after shield damage.\n"+DateTime.Now.ToString("O"));
        }
        finally{EditorSceneManager.CloseScene(scene,true);if(original.IsValid())SceneManager.SetActiveScene(original);}
    }

    // проверяем быстрый пролёт, край проёма, принадлежность, срок жизни и однократное расходование.
    static void CheckLens(Vector3 origin)
    {
        var root=new GameObject("Lens test",typeof(Mirror.NetworkIdentity),typeof(AdvancedSpellEffect),typeof(SteamLens));
        root.transform.position=origin;
        var effect=root.GetComponent<AdvancedSpellEffect>();
        effect.definition=AssetDatabase.LoadAssetAtPath<AdvancedSpell>("Assets/Spells/Advanced/SteamLens.asset");
        effect.ownerId=123;effect.phase=1;effect.phaseStarted=Mirror.NetworkTime.time;
        var lens=root.GetComponent<SteamLens>();lens.OnStartServer();
        var shot=new GameObject("Lens projectile test",typeof(Rigidbody));
        var body=shot.GetComponent<Rigidbody>();body.useGravity=false;body.linearVelocity=Vector3.forward*24;
        Vector3 from=origin-Vector3.forward*5,to=origin+Vector3.forward*5;
        try
        {
            Require(SteamLens.Crosses(root.transform,from,to,out float fraction)&&Mathf.Abs(fraction-.5f)<.001f,"Fast lens crossing missed.");
            Require(!SteamLens.Crosses(root.transform,from+Vector3.right*1.2f,to+Vector3.right*1.2f,out _),"Lens edge too large.");
            Require(!SteamLens.Crosses(root.transform,from,origin-Vector3.forward,out _),"Lens behind wall consumed.");
            Require(!SteamLens.TryAmplify(124,from,to,body)&&effect.phase==1,"Foreign shot consumed lens.");
            effect.phaseStarted=Mirror.NetworkTime.time-5;
            Require(!SteamLens.TryAmplify(123,from,to,body),"Expired lens consumed.");
            effect.phaseStarted=Mirror.NetworkTime.time;
            Require(SteamLens.TryAmplify(123,from,to,body)&&effect.phase==2,"Lens not consumed.");
            Require(Mathf.Abs(body.linearVelocity.magnitude-32.4f)<.001f,"Wrong speed bonus.");
            Require(!SteamLens.TryAmplify(123,from,to,body),"Lens consumed twice.");
            Require(SteamLens.BonusDamage==6,"Wrong damage bonus.");
            File.WriteAllText("Logs/lens-validation.txt","PASS: fast crossing, aperture edge, truncated path, owner filter, expiry, one use, +35% speed, +6 damage.\n"+DateTime.Now.ToString("O"));
        }
        finally{lens.OnStopServer();}
    }

    // изолированная серверная модель не открывает соединений и обязательно возвращает глобальное состояние mirror.
    static void CheckServerEffects(Vector3 origin)
    {
        if(Mirror.NetworkServer.active)throw new Exception("A server is already running.");
        var serverFlag=typeof(Mirror.NetworkServer).GetProperty("active",BindingFlags.Static|BindingFlags.Public);
        var healths=new System.Collections.Generic.List<Health>();
        serverFlag.SetValue(null,true);
        try
        {
            Health Actor(uint id,Vector3 position)
            {
                var root=new GameObject("Server test actor",typeof(Mirror.NetworkIdentity));
                root.transform.position=position;
                // повторяем настоящую иерархию: здоровье и контроллер находятся в entity, а не на сетевом корне.
                var entity=new GameObject("Entity",typeof(CharacterController));
                entity.transform.SetParent(root.transform,false);
                entity.AddComponent<RelativeMovement>();entity.AddComponent<Health>();
                var identity=root.GetComponent<Mirror.NetworkIdentity>();
                typeof(Mirror.NetworkIdentity).GetMethod("InitializeNetworkBehaviours",Private).Invoke(identity,null);
                typeof(Mirror.NetworkIdentity).GetProperty("isServer").SetValue(identity,true);
                typeof(Mirror.NetworkIdentity).GetProperty("netId").SetValue(identity,id);
                var health=entity.GetComponent<Health>();health.OnStartServer();healths.Add(health);
                return health;
            }
            var owner=Actor(900001,origin+Vector3.left+Vector3.up);
            var enemy=Actor(900002,origin+Vector3.right+Vector3.up);
            var root=new GameObject("Server test effect",typeof(Mirror.NetworkIdentity),typeof(AdvancedSpellEffect));root.transform.position=origin;
            var identity=root.GetComponent<Mirror.NetworkIdentity>();
            typeof(Mirror.NetworkIdentity).GetMethod("InitializeNetworkBehaviours",Private).Invoke(identity,null);
            typeof(Mirror.NetworkIdentity).GetProperty("isServer").SetValue(identity,true);
            var effect=root.GetComponent<AdvancedSpellEffect>();effect.ownerId=owner.netId;
            effect.definition=AssetDatabase.LoadAssetAtPath<AdvancedSpell>("Assets/Spells/Advanced/ShardVortex.asset");
            Physics.SyncTransforms();
            Call(effect,"AffectTargets",0,true);
            Require(enemy.CurrentHealth==100&&(float)Get(enemy.GetComponent<RelativeMovement>(),"slowMultiplier")==.65f,"Vortex warning damaged or did not slow.");
            effect.phase=1;Call(effect,"AffectTargets",20,true);
            Require(enemy.CurrentHealth>=78&&enemy.CurrentHealth<=82&&owner.CurrentHealth==100,"Impact damage or owner exclusion failed.");
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=origin+new Vector3(.5f,1,0);wall.transform.localScale=new Vector3(.1f,4,6);Physics.SyncTransforms();
            int before=enemy.CurrentHealth;Call(effect,"AffectTargets",20,true);Require(enemy.CurrentHealth==before,"Wall leaked damage.");
            Object.DestroyImmediate(wall);Physics.SyncTransforms();
            effect.definition=AssetDatabase.LoadAssetAtPath<AdvancedSpell>("Assets/Spells/Advanced/CrystalCrash.asset");
            Call(effect,"AffectTargets",0,true);
            Require(enemy.GetComponent<RelativeMovement>().IsStunned,"Crystal did not stun.");
            // поиск владельца при сетевом старте должен работать с дочерним здоровьем.
            Mirror.NetworkServer.spawned.Add(owner.netId,owner.netIdentity);
            effect.definition=AssetDatabase.LoadAssetAtPath<AdvancedSpell>("Assets/Spells/Advanced/Meteor.asset");
            effect.phase=0;effect.OnStartServer();
            Require(ReferenceEquals(Get(effect,"ownerHealth"),owner),"Nested owner was not resolved.");
            effect.phaseStarted=Mirror.NetworkTime.time-2.55;
            before=enemy.CurrentHealth;Call(effect,"Update");
            Require(effect.phase==1&&enemy.CurrentHealth<before,"Meteor failed to activate or deal damage.");
            effect.definition=AssetDatabase.LoadAssetAtPath<AdvancedSpell>("Assets/Spells/Advanced/ThermalSpring.asset");
            effect.SetOwner(owner);Set(effect,"initialDamageVersion",owner.DamageVersion);Set(owner,"currentHealth",40);
            effect.phaseStarted=Mirror.NetworkTime.time-1.05;Call(effect,"Update");Require(owner.CurrentHealth==50,"Spring did not heal 10.");
            owner.GrantShield(50,5);owner.TakeDamage(1,enemy.netId);Require(owner.CurrentHealth==50,"Shield setup failed.");
            effect.phaseStarted=Mirror.NetworkTime.time-2.05;Call(effect,"Update");Require(owner.CurrentHealth==50,"Shield damage did not stop healing.");
            effect.definition=AssetDatabase.LoadAssetAtPath<AdvancedSpell>("Assets/Spells/Advanced/BoilingIce.asset");
            effect.phase=3;effect.phaseStarted=Mirror.NetworkTime.time;
            Call(effect,"Update");Require(effect.phase==3,"Ice exploded without delay.");
            effect.phaseStarted=Mirror.NetworkTime.time-effect.definition.impactDelay-.05;
            Call(effect,"Update");Require(effect.phase==1,"Ice did not activate after delay.");
            typeof(Mirror.NetworkIdentity).GetProperty("isServer").SetValue(identity,false);
        }
        finally
        {
            foreach(var health in healths){Mirror.NetworkServer.spawned.Remove(health.netId);health.OnStopServer();typeof(Mirror.NetworkIdentity).GetProperty("isServer").SetValue(health.netIdentity,false);}
            serverFlag.SetValue(null,false);
        }
    }

    static void CheckTicks(string id,int count,int damage)
    {
        var spell=AssetDatabase.LoadAssetAtPath<AdvancedSpell>("Assets/Spells/Advanced/"+id+".asset");
        Require(AdvancedSpellEffect.DueTicks(spell,0)==0,"Immediate extra tick: "+id);
        Require(AdvancedSpellEffect.DueTicks(spell,spell.duration-.01)==count-1,"Early last tick: "+id);
        Require(AdvancedSpellEffect.DueTicks(spell,spell.duration)==count,"Missing last tick: "+id);
        Require(AdvancedSpellEffect.DueTicks(spell,spell.duration+10)==count,"Extra late ticks: "+id);
        Require(count*spell.tickDamage==damage,"Damage budget: "+id);
    }
    static object Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private).Invoke(target,args);
    static object Get(object target,string name)=>target.GetType().GetField(name,Private).GetValue(target);
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
