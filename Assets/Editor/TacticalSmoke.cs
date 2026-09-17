using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class TacticalSmoke
{
    const string Flag="TacticalSmoke";
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static double started,next;static int stage,checks;static Health player,enemy;static RelativeMovement movement;static TacticalEffect seal;static Vector3 arena=new Vector3(200,40,200);
    static TacticalSmoke(){EditorApplication.update+=Tick;}
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static object Field(object obj,string name)=>obj.GetType().GetField(name,Private).GetValue(obj);
    static void Set(object obj,string name,object value)=>obj.GetType().GetField(name,Private).SetValue(obj,value);
    static TacticalSpell Spell(string id)=>AssetDatabase.LoadAssetAtPath<TacticalSpell>("Assets/TacticalSpells/"+id+".asset");
    static TacticalEffect Spawn(string id,uint owner,Vector3 pos){var go=Object.Instantiate(Spell(id).effectPrefab,pos,Quaternion.identity);var effect=go.GetComponent<TacticalEffect>();effect.ownerId=owner;NetworkServer.Spawn(go);return effect;}
    public static void Run()
    {
        var catalog=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab").GetComponentInChildren<SpellManager>(true);
        Check(catalog.Spells.Count==20,"20 spells");Check(catalog.Spells.Select(s=>string.Join(",",s.Recipe.OrderBy(e=>e))).Distinct().Count()==20,"Unique recipes");
        foreach(var id in TacticalSpellBuilder.Ids){var s=Spell(id);Check(s!=null&&s.effectPrefab.GetComponent<NetworkIdentity>().assetId!=0,"Network prefab "+id);}
        var hud=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/MatchUI.prefab").GetComponent<PlayerGameUI>();
        var data=new SerializedObject(hud);Check(data.FindProperty("cards").arraySize==20,"Saved HUD cards");Check(data.FindProperty("healthFill").objectReferenceValue!=null,"Saved health fill");
        Check(InputComboTracker.InputTimeout==2,"Timeout two seconds");
        EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity");SessionState.SetBool(Flag,true);EditorApplication.isPlaying=true;
    }
    static void Tick()
    {
        if(!SessionState.GetBool(Flag,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        try
        {
            double now=EditorApplication.timeSinceStartup;if(started==0){started=now;next=now+2;}if(now-started>90)throw new Exception("Timeout stage "+stage);if(now<next)return;
            if(stage==0){if(NetworkManager.singleton==null)return;if(NetworkManager.singleton.transport is PortTransport port)port.Port=17989;NetworkManager.singleton.StartHost();stage++;next=now+2;return;}
            if(NetworkClient.localPlayer==null)return;
            if(stage==1)
            {
                player=NetworkClient.localPlayer.GetComponentInChildren<Health>();movement=player.GetComponent<RelativeMovement>();movement.enabled=false;
                var cc=player.GetComponent<CharacterController>();cc.enabled=false;player.transform.position=arena+Vector3.up;cc.enabled=true;
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=arena-Vector3.up*.5f;floor.transform.localScale=new Vector3(40,1,40);
                var dummy=new GameObject("Test enemy");dummy.SetActive(false);dummy.AddComponent<NetworkIdentity>();dummy.AddComponent<Health>();dummy.AddComponent<CapsuleCollider>();dummy.SetActive(true);dummy.transform.position=arena+new Vector3(0,1,10);enemy=dummy.GetComponent<Health>();NetworkServer.Spawn(dummy,0x77112233u);
                Physics.SyncTransforms();
                var caster=player.GetComponent<PlayerNetworkCaster>();Check(!caster.CastTactical(Spell("StoneWall"),Vector3.up),"Wall rejected without ground");
                var tracker=player.GetComponent<InputComboTracker>();tracker.AddElement(MagicElement.Fire);Set(tracker,"lastInput",Time.time-2.01f);tracker.Expire();Check(tracker.History.Count==0,"Client input expires");
                var history=(List<MagicElement>)Field(caster,"serverInput");history.Add(MagicElement.Fire);Set(caster,"lastInputTime",NetworkTime.time-2.01);caster.SendMessage("Update");Check(history.Count==0,"Server input expires");
                movement.ResetVerticalVelocity();
                var wall=Spawn("StoneWall",player.netId,arena+Vector3.forward*4);Check(!wall.GetComponent<Collider>().isTrigger,"Wall solid");NetworkServer.Destroy(wall.gameObject);
                foreach(string path in new[]{"Assets/Prefabs/FireBall.prefab","Assets/Prefabs/WindFlow.prefab","Assets/GeneratedWizard/WaterBolt.prefab"})
                {
                    var mirror=Spawn("IceMirror",player.netId,arena+Vector3.forward*2);var shot=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path),arena+new Vector3(0,1,3),Quaternion.identity);
                    var fire=shot.GetComponent<FireballProjectile>();var wind=shot.GetComponent<WindFlowProjectile>();var elemental=shot.GetComponent<ElementalEffect>();
                    Check(enemy!=null,"Enemy still exists");Check(fire!=null||wind!=null||elemental!=null,"Projectile component: "+path);if(fire!=null)fire.ownerId=enemy.netId;else if(wind!=null)wind.ownerId=enemy.netId;else elemental.ownerId=enemy.netId;
                    shot.GetComponent<Rigidbody>().linearVelocity=Vector3.back*10;NetworkServer.Spawn(shot);
                    Check(!mirror.CanBeHit(player.netId)&&mirror.CanBeHit(enemy.netId),"Mirror ignores owner");
                    if(elemental!=null)typeof(ElementalEffect).GetMethod("Impact",Private).Invoke(elemental,new object[]{mirror.GetComponent<Collider>()});else shot.SendMessage("OnTriggerEnter",mirror.GetComponent<Collider>());
                    uint owner=fire!=null?fire.ownerId:wind!=null?wind.ownerId:elemental.ownerId;Check(owner==player.netId,"Reflected ownership "+path);Check(shot.GetComponent<Rigidbody>().linearVelocity.z>0,"Reflected direction");NetworkServer.Destroy(shot);
                }
                var appearance=player.GetComponent<WizardAppearance>();appearance.Tint(Color.magenta);
                var colored=Spawn("SnowDecoy",player.netId,arena+Vector3.left*3);
                colored.SendMessage("RefreshDecoyAppearance");var robe=colored.GetComponentsInChildren<Renderer>().First(r=>r.name=="Body_Robe");var tintBlock=new MaterialPropertyBlock();robe.GetPropertyBlock(tintBlock);Check(tintBlock.GetColor("_BaseColor")==Color.magenta,"Decoy robe tint");robe.GetPropertyBlock(tintBlock,0);Check(tintBlock.isEmpty,"No indexed override masks robe tint");
                var hat=colored.GetComponentsInChildren<Renderer>().First(r=>r.name=="Hat_Separate");hat.GetPropertyBlock(tintBlock);Check(tintBlock.GetColor("_BaseColor")==Color.magenta,"Decoy hat tint");
                foreach(var r in colored.GetComponentsInChildren<Renderer>())for(int slot=0;slot<r.sharedMaterials.Length;slot++)if(r.sharedMaterials[slot].name.Contains("white crystal")){r.GetPropertyBlock(tintBlock,slot);Check(tintBlock.GetColor("_BaseColor")==Color.magenta,"Decoy crystal tint");}
                Set(colored,"expiresAt",NetworkTime.time-1);colored.SendMessage("Update");
                var decoy=Spawn("SnowDecoy",enemy.netId,arena+Vector3.right);Check(decoy.GetComponentsInChildren<Renderer>().Length>0,"Decoy model");decoy.ProjectileHit(player.netId);Check((float)Field(movement,"slowMultiplier")==.5f,"Decoy slow");movement.ResetVerticalVelocity();
                var gravity=Spawn("GravityWell",enemy.netId,arena+Vector3.right*2);gravity.SendMessage("Update");NetworkServer.Destroy(gravity.gameObject);movement.ServerDash(Vector3.right);
                var head=player.GetComponent<WizardAppearance>().pieces.First(t=>t!=null&&t.name=="Head").GetComponentInChildren<Renderer>();Check(SpellDamage.IsHeadshot(player,head.bounds.center),"Head height detected");Check(!SpellDamage.IsHeadshot(player,player.transform.position-Vector3.up*.5f),"Body is not head");player.TakeSpellDamage(20,enemy.netId,true);Check(player.CurrentHealth>=67&&player.CurrentHealth<=73,"Server headshot damage range");player.Heal(100);enemy.TakeSpellDamage(20,player.netId);
                seal=Spawn("FireSeal",enemy.netId,arena);seal.SendMessage("Update");Check(player.CurrentHealth==100,"Seal waits to arm");stage++;next=now+.2;return;
            }
            if(stage==2) { var fragments=Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Where(b=>b.name.StartsWith("Decoy debris - ")).ToArray();Check(fragments.Length==8,"Both hit and expired decoy break into four pieces: "+fragments.Length);var fragmentRobe=fragments.SelectMany(b=>b.GetComponentsInChildren<Renderer>()).Where(r=>r.name=="Body_Robe");Check(fragmentRobe.Any(r=>{var block=new MaterialPropertyBlock();r.GetPropertyBlock(block);return block.GetColor("_BaseColor")==Color.magenta;}),"Debris preserves owner tint"); var visible=Object.FindObjectsByType<FloatingDamageNumber>(FindObjectsSortMode.None);Check(visible.Length==1,"One remote damage number; own damage hidden");Check(int.Parse(visible[0].label.text)>=18&&int.Parse(visible[0].label.text)<=22,"Damage number matches range");stage=3;next=now+1.2;return; }
            if(stage==3)
            {
                Check((float)Field(movement,"dashRemaining")>0,"Dash received by owner");Check(((Vector3)Field(movement,"externalVelocity")).x>0,"Gravity pulls toward center");Check(player.CurrentHealth>=61&&player.CurrentHealth<=68,"Seal damages once after arming");Check(seal==null,"Seal consumed");
                var ui=Object.FindFirstObjectByType<PlayerGameUI>();ui.SendMessage("Update");var fill=(Image)Field(ui,"healthFill");Check(fill!=null&&Mathf.Abs(fill.fillAmount-(float)player.CurrentHealth/player.MaxHealth)<.001f,"Health bar shows current HP");
                var numbers=Object.FindObjectsByType<FloatingDamageNumber>(FindObjectsSortMode.None);Check(numbers.All(n=>int.Parse(n.label.text)>=18&&int.Parse(n.label.text)<=22),"Own incoming damage numbers hidden");
                Debug.Log("TACTICAL_SMOKE_PASSED: "+checks+" checks");SessionState.SetBool(Flag,false);NetworkManager.singleton.StopHost();EditorApplication.Exit(0);
            }
        }
        catch(Exception e){Debug.LogException(e);SessionState.SetBool(Flag,false);EditorApplication.Exit(1);}
    }
}










