using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Mirror;

// поэтапная проверка прицеливания на локальном хосте; предназначена для отдельного пакетного запуска редактора.
[InitializeOnLoad]
public static class AimRegression
{
    const string Flag="Wizard.AimRegression";
    static int stage,checks;
    static double next,started;
    static PlayerNetworkCaster caster;
    static Camera camera;
    static ElementalSpell steam;
    static Health closeTarget;
    static Vector3 groundPoint;
    static readonly BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    // подключаем шаг проверки к обновлению редактора; выполнение разрешается отдельным флагом сеанса.
    static AimRegression(){EditorApplication.update+=Tick;}
    // применяем настройки персонажа, открываем меню и запускаем игровой режим для проверки.
    public static void Run(){GameplayPolishBuilder.Apply();SessionState.SetBool(Flag,true);EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity");EditorApplication.isPlaying=true;}
    // прерываем сценарий при нарушении условия и считаем успешно выполненные проверки.
    static void Check(bool value,string message){if(!value)throw new Exception("Aim regression: "+message);checks++;}
    // последовательно проверяем направление снарядов, близкие цели и наземный каст с ожиданием между этапами.
    static void Tick()
    {
        if(!SessionState.GetBool(Flag,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            double now=EditorApplication.timeSinceStartup;
            if(started==0){started=now;next=now+2;}
            if(now-started>80)throw new Exception("Aim regression timeout "+stage);
            if(now<next)return;
            if(stage==0)
            {
                typeof(LocalPlayerSettings).GetProperty("Loadout").SetValue(LocalPlayerSettings.Instance,new ElementLoadout{q=MagicElement.Fire,e=MagicElement.Water,r=MagicElement.Air});
                var manager=NetworkManager.singleton;if(manager.transport is PortTransport port)port.Port=17986;
                manager.StartHost();stage=1;next=now+2;
            }
            else if(stage==1)
            {
                if(NetworkClient.localPlayer==null){next=now+.2;return;}
                caster=NetworkClient.localPlayer.GetComponentInChildren<PlayerNetworkCaster>();if(!caster.LoadoutReady){next=now+.2;return;}
                caster.GetComponent<RelativeMovement>().enabled=false;
                var cc=caster.GetComponent<CharacterController>();cc.enabled=false;caster.transform.SetPositionAndRotation(new Vector3(200,5,200),Quaternion.identity);cc.enabled=true;
                camera=(Camera)typeof(PlayerNetworkCaster).GetField("playerCamera",Fields).GetValue(caster);camera.GetComponent<OrbitCamera>().enabled=false;
                camera.transform.position=caster.transform.position+new Vector3(1.6f,1.6f,-6.5f);
                Check(typeof(OrbitCamera).GetField("zoomSpeed",Fields)==null&&typeof(OrbitCamera).GetField("currentZoom",Fields)==null,"camera has no player zoom control");
                var floor=Cube("Aim floor",new Vector3(200,0,210),new Vector3(80,1,80));
                var wall=Cube("Aim elevated target",new Vector3(200,15,216),new Vector3(8,8,.2f));Physics.SyncTransforms();
                Ray ray=new Ray(camera.transform.position,(wall.transform.position-camera.transform.position).normalized);
                Check(Physics.Raycast(ray,out var hit,100,~0,QueryTriggerInteraction.Ignore),"aim ray hits raised target");
                Vector3 direction=caster.ResolveAimDirection(ray),origin=caster.ShotOrigin();
                Check(Vector3.Angle(direction,hit.point-origin)<.01f,"muzzle direction converges on camera target");
                foreach(var spell in caster.GetComponent<SpellManager>().Spells)
                {
                    if(spell is ElementalSpell elemental&&elemental.mode!=ElementalCastMode.Bolt&&elemental.mode!=ElementalCastMode.Tornado)continue;
                    var before=NetworkServer.spawned.Keys.ToHashSet();
                    Check(spell.ActivateServer(caster,direction),"spawn travelling spell "+spell.name);
                    var spawned=NetworkServer.spawned.Where(pair=>!before.Contains(pair.Key)).Select(pair=>pair.Value).Single();
                    var body=spawned.GetComponent<Rigidbody>();
                    Check(Vector3.Distance(body.position,origin)<.001f,"no forward spawn offset: "+spell.name);
                    Check(Vector3.Angle(body.linearVelocity,direction)<.01f&&!body.useGravity&&body.linearDamping==0,"straight aimed velocity: "+spell.name);
                    NetworkServer.Destroy(spawned.gameObject);
                }
                wall.SetActive(false);UnityEngine.Object.Destroy(wall);
                Check(!caster.TryGroundTarget(new Ray(camera.transform.position,Vector3.up),out _),"sky cannot place ground zones");
                groundPoint=new Vector3(204,.5f,208);
                Check(caster.TryGroundTarget(new Ray(camera.transform.position,(groundPoint-camera.transform.position).normalized),out var point)&&Vector3.Distance(point,groundPoint)<.01f,"ground directly under reticle accepted");
                Check(!caster.TryGroundTarget(new Ray(camera.transform.position,(new Vector3(225,.5f,235)-camera.transform.position).normalized),out _),"out-of-range ground rejected");
                var blocker=Cube("Aim wall",origin+Vector3.forward*2,new Vector3(4,8,.2f));Physics.SyncTransforms();
                Check(!caster.TryGroundTarget(new Ray(origin,Vector3.forward),out _),"wall cannot place ground zones");
                blocker.SetActive(false);UnityEngine.Object.Destroy(blocker);
                var ceiling=Cube("Ceiling",origin+Vector3.up*2,new Vector3(8,.2f,8));Physics.SyncTransforms();
                Check(!caster.TryGroundTarget(new Ray(origin,Vector3.up),out _),"ceiling cannot place ground zones");ceiling.SetActive(false);UnityEngine.Object.Destroy(ceiling);
                var target=Cube("Close damage target",origin+Vector3.forward*.45f,Vector3.one*.2f);target.SetActive(false);target.AddComponent<NetworkIdentity>();closeTarget=target.AddComponent<Health>();target.SetActive(true);NetworkServer.Spawn(target,987010u);Physics.SyncTransforms();
                Check(!caster.TryGroundTarget(new Ray(target.transform.position+Vector3.up*2,Vector3.down),out _),"player body cannot place ground zones");
                Check(caster.GetComponent<SpellManager>().Spells[0].ActivateServer(caster,Vector3.forward),"close fireball fired");
                var appearance=caster.GetComponent<WizardAppearance>();appearance.Tint(Color.magenta);
                foreach(string name in new[]{"Body_Robe","Hat_Separate"})
                {
                    var renderer=appearance.visualRoot.GetComponentsInChildren<Renderer>().Single(r=>r.name==name);var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                    Check(block.GetColor("_BaseColor")==Color.magenta,"selected color applied to "+name);
                }
                steam=caster.GetComponent<SpellManager>().Spells.OfType<ElementalSpell>().First(s=>s.name=="SteamCloud");
                camera.transform.rotation=Quaternion.LookRotation(Vector3.up,Vector3.forward);
                CastSteam();stage=2;next=EditorApplication.timeSinceStartup+.3;
            }
            else if(stage==2)
            {
                Check(closeTarget.CurrentHealth<closeTarget.MaxHealth,"close target receives damage without projectile skipping it");
                Check(caster.RemainingCooldown(steam)==0,"invalid ground cast does not consume cooldown");
                Check(!UnityEngine.Object.FindObjectsByType<ElementalEffect>(FindObjectsSortMode.None).Any(e=>e.definition==steam),"invalid ground cast spawns no zone");
                NetworkServer.Destroy(closeTarget.gameObject);
                camera.transform.LookAt(groundPoint);CastSteam();stage=3;next=now+.2;
            }
            else
            {
                Check(caster.RemainingCooldown(steam)>0,"valid ground cast starts cooldown");
                var effect=UnityEngine.Object.FindObjectsByType<ElementalEffect>(FindObjectsSortMode.None).Single(e=>e.definition==steam);
                Check(Vector3.Distance(effect.transform.position,groundPoint+Vector3.up*.25f)<.05f,"zone placed exactly at aimed ground point");
                Debug.Log("AIM_REGRESSION_PASSED: "+checks+" checks");SessionState.SetBool(Flag,false);NetworkManager.singleton.StopHost();EditorApplication.Exit(0);
            }
        }
        catch(Exception e){SessionState.SetBool(Flag,false);Debug.LogException(e);EditorApplication.Exit(1);}
    }
    // отправляем три нажатия для рецепта пара в наборе, выбранном тестом.
    static void CastSteam(){caster.SubmitElement(0);caster.SubmitElement(0);caster.SubmitElement(1);}
    // создаём простое препятствие нужного размера для проверки прицела и перекрытия цели.
    static GameObject Cube(string name,Vector3 position,Vector3 scale){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.position=position;go.transform.localScale=scale;return go;}
}
