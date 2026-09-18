using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Mirror;

// проверяет базовый бой, щит, смерть и возрождение на хосте; предназначен для отдельного пакетного запуска.
[InitializeOnLoad]
public static class WizardPlaySmoke
{
    private const string Flag="WizardExpansion.Smoke";
    private static int stage;
    private static double next, started;
    private static PlayerNetworkCaster caster;
    private static Health health;
    private static int checks;
    private static readonly List<string> errors=new List<string>();
    // регистрируем шаг сценария в обновлении редактора.
    static WizardPlaySmoke() { EditorApplication.update+=Tick; }
    // включаем сценарий и запускаем меню в игровом режиме.
    public static void Run()
    {
        WizardExpansionValidation.FinalizeAndValidate();
        SessionState.SetBool(Flag,true);
        EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity");
        EditorApplication.isPlaying=true;
    }
    // считаем успешные условия и останавливаем сценарий при ошибке.
    static void Check(bool value,string message)
    {
        if(!value)throw new Exception("Smoke: "+message);
        checks++;
    }
    // выполняем этапы сетевого каста и жизненного цикла игрока с ожиданием игровых событий.
    static void Tick()
    {
        if(!SessionState.GetBool(Flag,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            double now=EditorApplication.timeSinceStartup;
            if(started==0) { started=now; next=now+2; Application.logMessageReceived+=OnLog; }
            if(now-started>70)throw new Exception("Smoke timed out at stage "+stage);
            if(now<next)return;
            if(stage==0)
            {
                var settings=LocalPlayerSettings.Instance;
                Check(settings!=null,"menu settings initialized");
                typeof(LocalPlayerSettings).GetProperty("Loadout").SetValue(settings,ElementLoadout.Default);
                var ui=UnityEngine.Object.FindFirstObjectByType<ElementLoadoutUI>();
                var screen=(GameObject)typeof(ElementLoadoutUI).GetField("screen",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
                screen.SetActive(true);
                typeof(ElementLoadoutUI).GetMethod("Refresh",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,null);
                var cards=(Transform)typeof(ElementLoadoutUI).GetField("cards",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
                Check(cards.childCount==14,"all 14 spell cards rendered");
                next=now+1;stage++;
            }
            else if(stage==1)
            {
                CaptureMenu();
                next=now+1;stage++;
            }
            else if(stage==2)
            {
                var manager=NetworkManager.singleton;
                if(manager.transport is PortTransport ownPort)ownPort.Port=17983;
                manager.StartHost();stage++;next=now+3;
            }
            else if(stage==3)
            {
                if(NetworkClient.localPlayer==null) { next=now+.25;return; }
                caster=NetworkClient.localPlayer.GetComponentInChildren<PlayerNetworkCaster>();
                health=caster.GetComponent<Health>();
                if(!caster.LoadoutReady) { next=now+.25;return; }
                caster.GetComponent<RelativeMovement>().enabled=false;
                Check(caster.GetComponent<WizardAppearance>().pieces.Length==4,"wizard spawned with 4 pieces");
                for(int i=0;i<3;i++)caster.SubmitElement(0);
                next=now+.25;stage++;
            }
            else if(stage==4)
            {
                var catalog=caster.GetComponent<SpellManager>();
                Check(caster.RemainingCooldown(catalog.Spells[0])>0,"server command accepted fire recipe");
                double cooldown=caster.RemainingCooldown(catalog.Spells[0]);
                for(int i=0;i<3;i++)caster.SubmitElement(0);
                Check(caster.RemainingCooldown(catalog.Spells[0])<=cooldown,"cooldown blocks repeated cast");
                foreach(var spell in catalog.Spells)
                    if(spell is ElementalSpell elemental)Check(spell.ActivateServer(caster,elemental.mode==ElementalCastMode.GroundZone?Vector3.down:Vector3.forward),"activate "+spell.Name);
                Check(health.Shield==50,"stone skin grants shield");
                int hp=health.CurrentHealth;health.TakeDamage(20);
                Check(health.CurrentHealth==hp&&health.Shield==30,"shield absorbs damage");
                health.TakeDamage(10000);
                Check(health.IsDead,"server death");
                for(int i=0;i<3;i++)caster.SubmitElement(1);
                Check(caster.RemainingCooldown(catalog.Spells[1])==0,"dead player cannot cast wind");
                next=now+.3;stage++;
            }
            else if(stage==5)
            {
                var appearance=caster.GetComponent<WizardAppearance>();
                Check(!appearance.visualRoot.gameObject.activeSelf,"dead wizard hidden");
                Check(!caster.GetComponent<CharacterController>().enabled,"no invisible body collider after death");
                int fragments=0;
                foreach(var body in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
                    if(body.name.StartsWith("Wizard debris"))fragments++;
                Check(fragments==4,"four physical death fragments");
                next=now+3.3;stage++;
            }
            else if(stage==6)
            {
                Check(!health.IsDead&&health.CurrentHealth==health.MaxHealth,"respawn restores health");
                Check(caster.GetComponent<WizardAppearance>().visualRoot.gameObject.activeSelf,"respawn restores model");
                Check(caster.GetComponent<CharacterController>().enabled,"respawn restores character collision");
                next=now+6;stage++;
            }
            else
            {
                Check(UnityEngine.Object.FindObjectsByType<ElementalEffect>(FindObjectsSortMode.None).Length==0,"effects expire");
                int fragments=0;
                foreach(var body in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
                    if(body.name.StartsWith("Wizard debris"))fragments++;
                Check(fragments==0,"debris expires");
                Check(errors.Count==0,"runtime errors: "+string.Join("; ",errors));
                SessionState.SetBool(Flag,false);
                Debug.Log("PLAY_SMOKE_PASSED: "+checks+" checks");
                NetworkManager.singleton.StopHost();
                EditorApplication.Exit(0);
            }
        }
        catch(Exception ex)
        {
            SessionState.SetBool(Flag,false);
            Debug.LogException(ex);EditorApplication.Exit(1);
        }
    }
    // собираем ошибки и исключения, возникающие во время сценария.
    static void OnLog(string message,string stack,LogType type)
    {
        if(type==LogType.Error||type==LogType.Exception)errors.Add(message);
    }
    // сохраняем снимок книги заклинаний для проверки расположения интерфейса.
    static void CaptureMenu()
    {
        var canvas=LocalPlayerSettings.Instance.GetComponentInChildren<Canvas>();
        var camera=new GameObject("Menu preview camera").AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        camera.transform.position=new Vector3(0,0,-10);camera.cullingMask=1<<5;
        foreach(Transform child in canvas.GetComponentsInChildren<Transform>(true))child.gameObject.layer=5;
        var render=new RenderTexture(1280,720,24);
        camera.targetTexture=render;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
        Canvas.ForceUpdateCanvases();camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=render;
        var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);
        texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();
        System.IO.File.WriteAllBytes("../spellbook-preview.png",texture.EncodeToPNG());
        RenderTexture.active=previous;canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;
        camera.targetTexture=null;
        UnityEngine.Object.Destroy(texture);UnityEngine.Object.Destroy(render);UnityEngine.Object.Destroy(camera.gameObject);
    }
    // сохраняем изображение мага из игровой камеры для визуальной проверки.
    static void CaptureWizard()
    {
        var root=caster.GetComponent<WizardAppearance>().visualRoot;
        var nodes=root.GetComponentsInChildren<Transform>();
        var layers=new int[nodes.Length];
        for(int i=0;i<nodes.Length;i++){layers[i]=nodes[i].gameObject.layer;nodes[i].gameObject.layer=31;}
        var renderers=root.GetComponentsInChildren<Renderer>();
        Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
        var camera=new GameObject("Wizard test camera").AddComponent<Camera>();
        camera.fieldOfView=35;camera.nearClipPlane=.01f;camera.farClipPlane=100;
        camera.transform.position=bounds.center+new Vector3(1.5f,1,-5).normalized*bounds.size.y*2.3f;
        camera.transform.LookAt(bounds.center);camera.cullingMask=1<<31;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.1f,.16f);
        var render=new RenderTexture(640,720,24);camera.targetTexture=render;camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=render;
        var texture=new Texture2D(640,720,TextureFormat.RGB24,false);
        texture.ReadPixels(new Rect(0,0,640,720),0,0);texture.Apply();
        System.IO.File.WriteAllBytes("../wizard-preview.png",texture.EncodeToPNG());
        RenderTexture.active=previous;camera.targetTexture=null;
        for(int i=0;i<nodes.Length;i++)nodes[i].gameObject.layer=layers[i];
        UnityEngine.Object.Destroy(texture);UnityEngine.Object.Destroy(render);UnityEngine.Object.Destroy(camera.gameObject);
    }
}
