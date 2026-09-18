using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Mirror;
using UnityEngine.EventSystems;

// проверяет движение, ловушки, возрождение и интерфейс на хосте; запускать в отдельном редакторе.
[InitializeOnLoad]
public static class GameplayPolishRegression
{
    const string Flag="Wizard.PolishRegression";
    static int stage,checks,baseline,afterExit,deaths;
    static double next,started;
    static PlayerNetworkCaster caster;
    static RelativeMovement movement;
    static Health health;
    static PlayerGameUI ui;
    static GameObject trapObject;
    static PlayerStats attacker;
    static readonly List<string> errors=new List<string>();
    static BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    // регистрируем обработчик поэтапного сценария редактора.
    static GameplayPolishRegression(){EditorApplication.update+=Tick;}
    // подготавливаем ассеты и включаем сценарий проверки через флаг сеанса.
    public static void Run()
    {
        GameplayPolishBuilder.Apply();
        SceneUIBuilder.Install();
        WizardExpansionValidation.Run();SpellSystemValidation.Run();
        SessionState.SetBool(Flag,true);EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity");EditorApplication.isPlaying=true;
    }
    // останавливаем сценарий при первом невыполненном условии.
    static void Check(bool value,string message){if(!value)throw new Exception("Polish regression: "+message);checks++;}
    // выполняем этапы проверки с ожиданием игровых кадров и завершаем процесс с кодом результата.
    static void Tick()
    {
        if(!SessionState.GetBool(Flag,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            double now=EditorApplication.timeSinceStartup;
            if(started==0){started=now;next=now+2;Application.logMessageReceived+=OnLog;}
            if(now-started>100)throw new Exception("Timeout at stage "+stage);
            if(now<next)return;
            if(stage==0)
            {
                var settings=LocalPlayerSettings.Instance;
                typeof(LocalPlayerSettings).GetProperty("Loadout").SetValue(settings,ElementLoadout.Default);
                var menu=UnityEngine.Object.FindFirstObjectByType<ElementLoadoutUI>();
                int savedObjects=menu.GetComponentsInChildren<Transform>(true).Length;
                Click((Button)typeof(ElementLoadoutUI).GetField("opener",Private).GetValue(menu));
                Canvas.ForceUpdateCanvases();
                Check(menu.GetComponentsInChildren<Transform>(true).Length==savedObjects,"opening spellbook creates no UI objects");
                Check(((RectTransform)((Transform)typeof(ElementLoadoutUI).GetField("cards",Private).GetValue(menu)).GetChild(0)).rect.width>700,"serialized spell cards fill the catalog width");
                ((GameObject)typeof(ElementLoadoutUI).GetField("screen",Private).GetValue(menu)).SetActive(true);
                typeof(ElementLoadoutUI).GetMethod("Refresh",Private).Invoke(menu,null);
                Canvas.ForceUpdateCanvases();
                Capture((Canvas)typeof(ElementLoadoutUI).GetField("canvas",Private).GetValue(menu),"spellbook-polished.png");
                var firstIcon=menu.GetComponentInChildren<SpellIconGraphic>();var iconMesh=firstIcon.canvasRenderer.GetMesh();
                Check(iconMesh!=null&&iconMesh.vertexCount>0&&!firstIcon.canvasRenderer.cull,"visible serialized book icon has rendered geometry");
                ((GameObject)typeof(ElementLoadoutUI).GetField("screen",Private).GetValue(menu)).SetActive(false);
                var manager=NetworkManager.singleton;if(manager.transport is PortTransport port)port.Port=17984;
                manager.StartHost();next=now+2;stage++;
            }
            else if(stage==1)
            {
                if(NetworkClient.localPlayer==null){next=now+.2;return;}
                caster=NetworkClient.localPlayer.GetComponentInChildren<PlayerNetworkCaster>();
                if(!caster.LoadoutReady){next=now+.2;return;}
                movement=caster.GetComponent<RelativeMovement>();health=caster.GetComponent<Health>();ui=UnityEngine.Object.FindFirstObjectByType<PlayerGameUI>();
                if(health.IsDead){next=now+.2;return;}
                movement.enabled=false;
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(215,0,200);floor.transform.localScale=new Vector3(100,1,20);
                var spawn=new GameObject("Safe regression spawn");spawn.transform.position=new Vector3(230,1.5f,200);
                typeof(SpawnManager).GetField("points",Private).SetValue(SpawnManager.Instance,new[]{spawn.transform});
                Teleport(new Vector3(200,1.5f,200));Check(movement.ProbeGround(),"ground detected at feet");
                Teleport(new Vector3(200,2.3f,200));Check(!movement.ProbeGround(),"0.8m gap is not grounded");
                var own=new GameObject("Self collider test");own.transform.SetParent(caster.transform,false);own.transform.localPosition=Vector3.down;
                var ownCollider=own.AddComponent<SphereCollider>();ownCollider.radius=.3f;
                Physics.SyncTransforms();Check(!movement.ProbeGround(),"own collider ignored");ownCollider.enabled=false;UnityEngine.Object.Destroy(own);
                var trigger=GameObject.CreatePrimitive(PrimitiveType.Cube);trigger.transform.position=new Vector3(200,1,200);trigger.transform.localScale=new Vector3(5,.6f,5);trigger.GetComponent<Collider>().isTrigger=true;
                Physics.SyncTransforms();Check(!movement.ProbeGround(),"trigger surface ignored");trigger.GetComponent<Collider>().enabled=false;UnityEngine.Object.Destroy(trigger);
                Teleport(new Vector3(200,8,200));ui.SetPause(true);movement.enabled=true;
                Check(PlayerGameUI.InputBlocked&&Time.timeScale==1,"Esc blocks input without pausing world");
                next=now+.35;stage++;
            }
            else if(stage==2)
            {
                Check(caster.transform.position.y<7.5f,"gravity continues while Esc menu is open");
                Check(caster.transform.position.y>5f,"descent accelerates gradually rather than dropping abruptly");
                movement.enabled=false;
                Check(EventSystem.current!=null&&EventSystem.current.currentInputModule!=null,"arena has a live UI input module");
                Click((Button)typeof(PlayerGameUI).GetField("resumeButton",Private).GetValue(ui));
                // редактор без графического окна не может захватить системный курсор.
                Check(!PlayerGameUI.InputBlocked&&(Application.isBatchMode||Cursor.lockState==CursorLockMode.Locked),"resume restores input");
                StandOnArenaTrap("Trap (1)");next=now+1.2;stage=20;
            }
            else if(stage==20)
            {
                Check(health.CurrentHealth<baseline&&!health.IsDead,"first original red floor trap damages standing player");
                StandOnArenaTrap("Trap (2)");next=now+1.2;stage=21;
            }
            else if(stage==21)
            {
                Check(health.CurrentHealth<baseline&&!health.IsDead,"second original red floor trap damages standing player");
                afterExit=health.CurrentHealth;Teleport(new Vector3(220,8,200));next=now+1.2;stage=22;
            }
            else if(stage==22)
            {
                Check(health.CurrentHealth==afterExit,"original red traps stop damaging after exit");
                Teleport(new Vector3(200,8,200));health.Heal(1000);baseline=health.CurrentHealth;
                trapObject=GameObject.CreatePrimitive(PrimitiveType.Cube);trapObject.transform.position=caster.transform.position;trapObject.transform.localScale=Vector3.one*3;
                trapObject.SetActive(false);
                trapObject.GetComponent<Collider>().isTrigger=true;trapObject.AddComponent<NetworkIdentity>();var trap=trapObject.AddComponent<Trap>();
                var so=new SerializedObject(trap);so.FindProperty("damageCooldown").floatValue=.2f;so.ApplyModifiedPropertiesWithoutUndo();
                trapObject.SetActive(true);NetworkServer.Spawn(trapObject,987001u);Physics.SyncTransforms();next=now+.55;stage=3;
            }
            else if(stage==3)
            {
                Check(health.CurrentHealth<baseline&&!health.IsDead,"trap damages overlapping player");
                afterExit=health.CurrentHealth;Teleport(new Vector3(220,8,200));next=now+.7;stage++;
            }
            else if(stage==4)
            {
                Check(health.CurrentHealth==afterExit,"teleport away stops trap damage without trigger exit");
                health.Heal(1000);deaths=caster.GetComponent<PlayerStats>().Deaths;
                var killer=new GameObject("Test attacker");killer.SetActive(false);killer.AddComponent<NetworkIdentity>();attacker=killer.AddComponent<PlayerStats>();
                killer.SetActive(true);NetworkServer.Spawn(killer,987002u);
                health.RecordAttacker(attacker.netId);
                var so=new SerializedObject(trapObject.GetComponent<Trap>());so.FindProperty("damage").intValue=1000;so.ApplyModifiedPropertiesWithoutUndo();
                Teleport(trapObject.transform.position);next=now+.3;stage++;
            }
            else if(stage==5)
            {
                Check(health.IsDead,"trap kills player");
                Check(caster.GetComponent<PlayerStats>().Deaths==deaths+1,"death counted once");
                Check(attacker.Kills==1,"recent attacker credited for trap death");
                next=now+3.3;stage++;
            }
            else if(stage==6)
            {
                Check(!health.IsDead&&health.CurrentHealth==health.MaxHealth,"safe respawn restores full health");
                Check(Vector3.Distance(caster.transform.position,new Vector3(230,1.5f,200))<.5f,"respawn on known safe surface");
                next=now+.7;stage++;
            }
            else if(stage==7)
            {
                Check(health.CurrentHealth==health.MaxHealth,"no lingering trap damage after respawn");
                var wizard=caster.GetComponent<WizardAppearance>();wizard.Tint(Color.magenta);
                Renderer robe=null;foreach(var r in wizard.visualRoot.GetComponentsInChildren<Renderer>())if(r.name=="Body_Robe")robe=r;
                var block=new MaterialPropertyBlock();robe.GetPropertyBlock(block);
                Check(block.GetColor("_BaseColor")==Color.magenta,"robe receives selected color");
                var camera=caster.transform.root.GetComponentInChildren<OrbitCamera>();
                Check(camera.transform.position.y>caster.transform.position.y+.8f,"camera raised above player");
                Check(Mathf.Abs(caster.AimDirection().magnitude-1)<.001f,"aim direction normalized");
                ui.RefreshScoreboard();
                var kills=(Text)typeof(PlayerGameUI).GetField("killColumn",Private).GetValue(ui);
                Check(kills.text.Contains("1"),"scoreboard displays server kills");
                var cards=(System.Collections.ICollection)typeof(PlayerGameUI).GetField("cards",Private).GetValue(ui);
                Check(cards.Count==14,"HUD has fourteen serialized cards without runtime spawning");
                Check(ui.GetComponentsInChildren<SpellIconGraphic>().Length==5,"only five available default spells are visible");
                foreach(var image in ui.GetComponentsInChildren<Image>(true))if(image.name=="Cooldown")Check(image.sprite!=null&&image.type==Image.Type.Filled,"cooldown mask supports fill");
                for(int i=0;i<3;i++)caster.SubmitElement(0);
                next=now+.2;stage++;
            }
            else if(stage==8)
            {
                Check(caster.RemainingCooldown(caster.GetComponent<SpellManager>().Spells[0])>0,"HUD receives server cooldown");
                var canvas=(Canvas)typeof(PlayerGameUI).GetField("canvas",Private).GetValue(ui);
                Capture(canvas,"hud-polished.png");
                ((GameObject)typeof(PlayerGameUI).GetField("scoreboard",Private).GetValue(ui)).SetActive(true);ui.RefreshScoreboard();Capture(canvas,"scoreboard-polished.png");
                ((GameObject)typeof(PlayerGameUI).GetField("scoreboard",Private).GetValue(ui)).SetActive(false);ui.SetPause(true);Capture(canvas,"pause-polished.png");
                Click((Button)typeof(PlayerGameUI).GetField("menuButton",Private).GetValue(ui));
                stage=9;next=now+2;
            }
            else if(stage==9)
            {
                if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="Menu"){next=now+.2;return;}
                Check(!NetworkClient.active&&!NetworkServer.active,"menu button disconnects host");
                Check(!PlayerGameUI.InputBlocked,"return to menu releases input");
                var book=UnityEngine.Object.FindFirstObjectByType<ElementLoadoutUI>();
                Check(book!=null&&UnityEngine.Object.FindObjectsByType<ElementLoadoutUI>(FindObjectsSortMode.None).Length==1,"one serialized spellbook after returning to menu");
                Click((Button)typeof(ElementLoadoutUI).GetField("opener",Private).GetValue(book));
                Check(((GameObject)typeof(ElementLoadoutUI).GetField("screen",Private).GetValue(book)).activeSelf,"spellbook clickable after match exit");
                Check(errors.Count==0,"runtime errors: "+string.Join("; ",errors));
                SessionState.SetBool(Flag,false);Debug.Log("POLISH_REGRESSION_PASSED: "+checks+" checks");EditorApplication.Exit(0);
            }
        }
        catch(Exception ex){SessionState.SetBool(Flag,false);Debug.LogException(ex);EditorApplication.Exit(1);}
    }
    // перемещаем тестового персонажа с временным отключением контроллера и сбросом движения.
    static void Teleport(Vector3 position)
    {
        var controller=caster.GetComponent<CharacterController>();controller.enabled=false;caster.transform.position=position;movement.ResetVerticalVelocity();controller.enabled=true;Physics.SyncTransforms();
    }
    // размещаем персонажа на указанной ловушке для проверки серверного урона.
    static void StandOnArenaTrap(string name)
    {
        var trap=GameObject.Find(name).GetComponent<Trap>();
        var bounds=trap.GetComponent<BoxCollider>().bounds;
        Vector3 probe=new Vector3(bounds.center.x,bounds.max.y+.1f,bounds.center.z);
        Check(Physics.Raycast(probe,Vector3.down,out var hit,3,~0,QueryTriggerInteraction.Ignore),"floor below "+name);
        float feetOffset=caster.GetComponent<CharacterController>().height*.5f-caster.GetComponent<CharacterController>().center.y;
        Teleport(new Vector3(probe.x,hit.point.y+feetOffset+.02f,probe.z));health.Heal(1000);baseline=health.CurrentHealth;
        Debug.Log($"ARENA_TRAP {name}: floor={hit.point.y}, triggerTop={bounds.max.y}, player={caster.transform.position}, server={trap.isServer}");
    }
    // имитируем нажатие на кнопку интерфейса средствами системы событий.
    static void Click(Button button)
    {
        Canvas.ForceUpdateCanvases();
        var events=EventSystem.current;Check(events!=null,"EventSystem available for "+button.name);
        var rect=button.GetComponent<RectTransform>();
        var pointer=new PointerEventData(events){position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center)),button=PointerEventData.InputButton.Left};
        var hits=new List<RaycastResult>();events.RaycastAll(pointer,hits);
        Check(hits.Count>0&&ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject)==button.gameObject,"button receives UI raycast: "+button.name);
        ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
    }
    // собираем ошибки и исключения Unity, чтобы учитывать их в результате проверки.
    static void OnLog(string text,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(text);}
    // сохраняем изображение интерфейса, временно перенастроив холст и камеру.
    static void Capture(Canvas canvas,string file)
    {
        Canvas.ForceUpdateCanvases();
        var camera=new GameObject("UI capture").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.02f,.03f,.05f);camera.transform.position=new Vector3(0,0,-10);camera.cullingMask=1<<5;
        foreach(Transform child in canvas.GetComponentsInChildren<Transform>(true))child.gameObject.layer=5;
        var render=new RenderTexture(1280,720,24);camera.targetTexture=render;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;Canvas.ForceUpdateCanvases();camera.Render();
        var old=RenderTexture.active;RenderTexture.active=render;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
        System.IO.Directory.CreateDirectory("Logs/GameplayPolish");
        System.IO.File.WriteAllBytes("Logs/GameplayPolish/"+file,image.EncodeToPNG());RenderTexture.active=old;canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;camera.targetTexture=null;
        UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(render);UnityEngine.Object.Destroy(camera.gameObject);
    }
}
