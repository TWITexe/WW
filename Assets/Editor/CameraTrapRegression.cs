using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Mirror;

// проверяет камеру и прохождение ловушек на локальном хосте; в конце завершает процесс редактора.
[InitializeOnLoad]
public static class CameraTrapRegression
{
    const string Flag="Wizard.CameraTrapRegression";
    static double next,started;
    static int stage;
    static Health health;
    static RelativeMovement movement;
    static Trap trap;
    static int baseline;
    static bool walking;
    static readonly BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    // подключаем поэтапную проверку к обновлению редактора.
    static CameraTrapRegression(){EditorApplication.update+=Tick;}
    // применяем настройки камеры и интерфейса, открываем меню и включаем проверку в игровом режиме.
    public static void Run(){CameraHudTuning.Apply();SessionState.SetBool(Flag,true);EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity");EditorApplication.isPlaying=true;}
    // ведём персонажа через ловушки, проверяем здоровье и отображение камеры с ожиданием физических кадров.
    static void Tick()
    {
        if(!SessionState.GetBool(Flag,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            double now=EditorApplication.timeSinceStartup;
            if(started==0){started=now;next=now+2;}
            if(now-started>70)throw new Exception("Walk test timeout "+stage);
            if(walking&&movement!=null)typeof(RelativeMovement).GetField("externalVelocity",Fields).SetValue(movement,Vector3.forward*6);
            if(now<next)return;
            if(stage==0)
            {
                var manager=NetworkManager.singleton;if(manager.transport is PortTransport port)port.Port=17985;
                manager.StartHost();stage=1;next=now+2;
            }
            else if(stage==1)
            {
                if(NetworkClient.localPlayer==null){next=now+.2;return;}
                movement=NetworkClient.localPlayer.GetComponentInChildren<RelativeMovement>();health=movement.GetComponent<Health>();
                BeginWalk("Trap (1)");stage=2;next=now+2;
            }
            else if(stage==2||stage==3)
            {
                var cc=movement.GetComponent<CharacterController>();
                Debug.Log($"WALK_RESULT {trap.name}: health={health.CurrentHealth}/{baseline}, player={movement.transform.position}, controllerBottom={cc.bounds.min.y}, trapTop={trap.GetComponent<Collider>().bounds.max.y}, grounded={movement.ProbeGround()}, server={trap.isServer}");
                if(health.CurrentHealth>=baseline)throw new Exception("No damage while walking across "+trap.name);
                if(stage==2){BeginWalk("Trap (2)");stage=3;next=now+2;}
                else
                {
                    walking=false;movement.ResetVerticalVelocity();movement.enabled=false;
                    cc.enabled=false;movement.transform.position=new Vector3(0,1.08f,-22);cc.enabled=true;Physics.SyncTransforms();
                    var camera=movement.transform.root.GetComponentInChildren<OrbitCamera>();
                    typeof(OrbitCamera).GetField("yaw",Fields).SetValue(camera,0f);
                    typeof(OrbitCamera).GetField("pitch",Fields).SetValue(camera,3f);
                    baseline=health.CurrentHealth;stage=4;next=now+1.2;
                }
            }
            else if(stage==4)
            {
                if(health.CurrentHealth!=baseline)throw new Exception("Trap damage continued after walking away");
                var camera=movement.transform.root.GetComponentInChildren<OrbitCamera>().GetComponent<Camera>();
                var frame=camera.WorldToViewportPoint(movement.transform.position+Vector3.up*.5f);
                
                if(frame.x<.15f||frame.x>.45f)throw new Exception("Player must be left of the reticle: "+frame);
                
                if(Vector3.Distance(camera.transform.position,movement.transform.position)<6)throw new Exception("Camera too close in open space");
                var ui=UnityEngine.Object.FindFirstObjectByType<PlayerGameUI>();
                
                foreach(var icon in ui.GetComponentsInChildren<SpellIconGraphic>())
                    if(Mathf.Abs(icon.rectTransform.rect.width-64f/3)>.01f)throw new Exception("Cooldown icon was not reduced 3x");
                Capture(camera,ui.GetComponentInChildren<Canvas>());
                trap=GameObject.Find("Trap (1)").GetComponent<Trap>();var bounds=trap.GetComponent<BoxCollider>().bounds;
                movement.GetComponent<CharacterController>().enabled=false;
                movement.transform.position=new Vector3(bounds.center.x,1.08f,bounds.center.z);health.Heal(1000);baseline=health.CurrentHealth;
                // рендер снимка может задержать редактор, поэтому ожидание физических кадров начинаем после него.
                stage=5;next=EditorApplication.timeSinceStartup+1.2;
            }
            else if(stage==5)
            {
                if(health.CurrentHealth>=baseline)throw new Exception("Server capsule check depends on active physics collider");
                movement.transform.position=new Vector3(0,1.08f,-22);baseline=health.CurrentHealth;stage=6;next=now+1.2;
            }
            else if(stage==6)
            {
                if(health.CurrentHealth!=baseline)throw new Exception("Damage continued after server transform moved out");
                Debug.Log("WALK_CAMERA_HUD_PASSED: both moving trap crossings, safe exit, server-only capsule, camera framing, compact HUD");
                SessionState.SetBool(Flag,false);NetworkManager.singleton.StopHost();EditorApplication.Exit(0);
            }
        }
        catch(Exception e){SessionState.SetBool(Flag,false);Debug.LogException(e);EditorApplication.Exit(1);}
    }
    // временно направляем вывод камеры и интерфейса в текстуру для сохранения проверочного снимка.
    static void Capture(Camera camera,Canvas canvas)
    {
        var render=new RenderTexture(1280,720,24);camera.targetTexture=render;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;Canvas.ForceUpdateCanvases();camera.Render();
        
        var old=RenderTexture.active;RenderTexture.active=render;var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);
        texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();System.IO.Directory.CreateDirectory("Logs/GameplayPolish");
        System.IO.File.WriteAllBytes("Logs/GameplayPolish/shooter-camera.png",texture.EncodeToPNG());
        RenderTexture.active=old;camera.targetTexture=null;canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;
        UnityEngine.Object.Destroy(texture);UnityEngine.Object.Destroy(render);
    }
    // находим именованную ловушку и подготавливаем персонажа к проходу через неё.
    static void BeginWalk(string name)
    {
        trap=GameObject.Find(name).GetComponent<Trap>();var bounds=trap.GetComponent<BoxCollider>().bounds;
        var cc=movement.GetComponent<CharacterController>();cc.enabled=false;
        movement.transform.position=new Vector3(bounds.center.x,1.1f,bounds.min.z-1);
        movement.ResetVerticalVelocity();cc.enabled=true;movement.enabled=true;health.Heal(1000);baseline=health.CurrentHealth;
        Physics.SyncTransforms();walking=true;
    }
}
