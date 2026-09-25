using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class ShopRegression
{
    public static void Build()
    {
        Directory.CreateDirectory("Logs/Shop/Build");
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{"Assets/Scenes/Menu.unity","Assets/Scenes/SampleScene.unity"},
            locationPathName="Logs/Shop/Build/ShopTest.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development|BuildOptions.CompressWithLz4});
        File.WriteAllText("Logs/Shop/build-result.txt",report.summary.result+" errors="+report.summary.totalErrors);
        Check(report.summary.result==UnityEditor.Build.Reporting.BuildResult.Succeeded,"integration build");
    }
    static void Check(bool condition,string message) { if(!condition)throw new Exception("SHOP: "+message); }
    [MenuItem("Tools/Wizard War/Validate shop")]
    public static void Run()
    {
        Check(ShopCatalog.Reward(10,60,5,true)==155,"five-player winner reward");
        Check(ShopCatalog.Reward(10,60,9,true)==195,"nine-player winner reward");
        Check(ShopCatalog.Reward(10,119,5,false)==105,"only complete minutes");
        Check(ShopCatalog.Reward(10,60,1,true)==0,"no solo farming");
        Check(ShopCatalog.Allows(null,ElementLoadout.Default),"default loadout is free");
        Check(!ShopCatalog.Allows(null,MagicElement.Air)&&!ShopCatalog.Allows(null,MagicElement.Ice),"locked paid elements");
        Check(ShopCatalog.Allows(null,MagicElement.Fire)&&ShopCatalog.Allows(null,MagicElement.Earth)&&ShopCatalog.Allows(null,MagicElement.Water),"three free elements");
        var paid=new ShopProfile{owned=new[]{"book_air"}};
        Check(ShopCatalog.Allows(paid,MagicElement.Air)&&!ShopCatalog.Allows(paid,MagicElement.Ice),"independent book ownership");
        Check(ShopCatalog.Find("book_air").price==1000&&ShopCatalog.Find("book_ice").price==1500,"book prices");
        foreach(string category in new[]{"hat","staff","body"})
        {
            var items=ShopCatalog.Items.Where(x=>x.category==category).ToArray();Check(items.Length==3,"three "+category+" styles");
            foreach(var item in items)
            {
                var prefab=Resources.Load<GameObject>("Shop/"+item.id);
                Check(prefab!=null&&prefab.GetComponentsInChildren<MeshRenderer>().Length>=3,"real mesh cosmetic "+item.id);
                Check(prefab.GetComponentsInChildren<Collider>().Length==0,"cosmetic has no gameplay collider");
                Check(Resources.Load<Texture2D>("Shop/Icons/"+item.id)!=null,"shop preview "+item.id);
            }
        }
        var player=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        Check(player.GetComponentInChildren<WizardCosmetics>(true)!=null,"networked cosmetics saved");
        // Exercise the actual woven server command body, independent of the client menu.
        var go=UnityEngine.Object.Instantiate(player);go.SetActive(false);
        try
        {
            var caster=go.GetComponentInChildren<PlayerNetworkCaster>(true);
            var body=typeof(PlayerNetworkCaster).GetMethods(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)
                .Single(m=>m.Name.StartsWith("UserCode_CmdSetLoadout",StringComparison.Ordinal));
            body.Invoke(caster,new object[]{new ElementLoadout{q=MagicElement.Fire,e=MagicElement.Air,r=MagicElement.Ice}});
            Check(caster.LoadoutReady&&ShopCatalog.Allows(null,caster.Loadout),"server rejects forged paid loadout");
            var color=go.GetComponentInChildren<PlayerColor>(true);
            var colorCommand=typeof(PlayerColor).GetMethods(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)
                .Single(m=>m.Name.StartsWith("UserCode_CmdRequestColor",StringComparison.Ordinal));
            foreach(PlayerColorId id in Enum.GetValues(typeof(PlayerColorId)))
            {
                colorCommand.Invoke(color,new object[]{id});
                bool free=id==PlayerColorId.Red||id==PlayerColorId.Blue||id==PlayerColorId.Green;
                Check(color.DisplayColor==PlayerColorManager.ToUnityColor(free?id:PlayerColorId.Blue),"server color entitlement "+id);
            }
            colorCommand.Invoke(color,new object[]{(PlayerColorId)999});
            Check(color.DisplayColor==Color.blue,"invalid color rejected");
            var ownedColor=new ShopProfile{owned=new[]{"color_purple"}};
            Check(ShopCatalog.Allows(ownedColor,PlayerColorId.Purple)&&!ShopCatalog.Allows(ownedColor,PlayerColorId.Yellow),"independent color ownership");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
        var shop=UnityEngine.Object.FindFirstObjectByType<ShopMenuUI>(FindObjectsInactive.Include);
        Check(shop!=null&&shop.cards.Length==3&&shop.tabs.Length==4,"shop UI saved in menu");
        var colors=UnityEngine.Object.FindObjectsByType<ColorButton>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        Check(colors.Length==10,"ten color choices");
        foreach(var color in colors)
        {
            var item=ShopCatalog.Find(ShopCatalog.ColorId(color.ColorId));
            int expected=(int)color.ColorId<=3?0:(int)color.ColorId<=5?100:200;
            Check(item.price==expected,"color row price "+color.ColorId);
            Check(color.transform.Find("Colour price")!=null&&color.transform.Find("Purchase lock")!=null,"color purchase UI "+color.ColorId);
            foreach(var itemSkin in ShopCatalog.Items.Where(ShopCatalog.IsCosmetic))
                Check(Resources.Load<Texture2D>($"Shop/Icons/{color.ColorId}/{itemSkin.id}")!=null,"colored preview "+itemSkin.id+" "+color.ColorId);
        }
        var locks=UnityEngine.Object.FindObjectsByType<BookPurchaseLock>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        Check(locks.Length==2&&locks.All(x=>x.book.Element==MagicElement.Air||x.book.Element==MagicElement.Ice),"locks on Air and Ice only");
        Directory.CreateDirectory("Logs/Shop");File.WriteAllText("Logs/Shop/validation.txt","PASS: rewards, free defaults, paid entitlements, forged server loadout/color, nine 3D skins, 90 colored previews, shop UI, RGB free, seven paid colors, two book locks.\n"+DateTime.UtcNow.ToString("O"));
        Debug.Log("SHOP_REGRESSION_PASS");
    }
}
