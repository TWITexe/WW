using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit installer: saved, editable UI and real 3D assets, no scene YAML editing.
public static class ShopBuilder
{
    const string Folder = "Assets/Resources/Shop";
    static TMP_FontAsset Font => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/UI/SavedBuildNamesFont.asset") ?? TMP_Settings.defaultFontAsset;
    static readonly Color Gold = new Color(.91f,.70f,.35f), Ink = new Color(.96f,.91f,.81f);
    static Material gold, dark, gem, cloth, trim, band;
    [MenuItem("Tools/Wizard War/Install shop and cosmetics")]
    public static void Install()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        Directory.CreateDirectory(Folder + "/Icons"); Directory.CreateDirectory(Folder + "/Meshes");
        Directory.CreateDirectory("Logs/Shop"); AssetDatabase.Refresh();
        BuildSkins(); InstallMenu(); InstallPlayer(); InstallRewardUI(); UpgradeColorShop();
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Debug.Log("SHOP_INSTALLED: nine cosmetics, two locked books, shop UI, reward UI.");
    }
    static Material Material(string name, Color color, float emission = 0)
    {
        string path = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .35f);
        if (emission > 0) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * emission); }
        EditorUtility.SetDirty(material); return material;
    }
    static void BuildSkins()
    {
        gold = Material("Antique gold", new Color(.63f,.37f,.10f));
        dark = Material("Obsidian wood", new Color(.06f,.035f,.023f));
        trim = Material("Team cloth", Color.white);
        var wizard = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab").GetComponentInChildren<WizardAppearance>(true);
        foreach (var item in ShopCatalog.Items.Where(ShopCatalog.IsCosmetic))
        {
            int style = item.style;
            cloth = Material("Cloth " + style, style == 1 ? new Color(.22f,.042f,.025f) : style == 2 ? new Color(.07f,.23f,.34f) : new Color(.12f,.055f,.26f));
            gem = Material("Crystal " + style, style == 1 ? new Color(1,.21f,.025f) : style == 2 ? new Color(.18f,.82f,1) : new Color(.66f,.32f,1), .4f);
            band = Material("Band " + style, style == 1 ? new Color(.16f,.035f,.018f) : style == 2 ? new Color(.08f,.65f,.90f) : new Color(.21f,.07f,.34f));
            var root = new GameObject(item.id);
            try
            {
                if (item.category == "hat") Hat(root.transform, style);
                else if (item.category == "staff") Staff(root.transform, style);
                else Body(root.transform, style);
                // Fit each authored skin to the original visual part, preserving its animated pivot.
                var target = wizard.pieces.First(x => x.name.Equals(item.category == "body" ? "Body" : item.category == "hat" ? "Hat" : "Staff"));
                Bounds from = BoundsIn(root.transform), to = BoundsIn(target);
                var fit = new GameObject("Skin geometry").transform; fit.SetParent(root.transform, false);
                foreach (var child in root.transform.Cast<Transform>().Where(t => t != fit).ToArray()) child.SetParent(fit, false);
                fit.localScale = new Vector3(to.size.x/from.size.x, to.size.y/from.size.y, to.size.z/from.size.z);
                fit.localPosition = to.center - Vector3.Scale(from.center, fit.localScale);
                foreach (var f in root.GetComponentsInChildren<MeshFilter>())
                    if (!AssetDatabase.Contains(f.sharedMesh))
                    {
                        string path = Folder + "/Meshes/" + item.id + "_" + f.name + ".asset";
                        f.sharedMesh.name=Path.GetFileNameWithoutExtension(path);
                        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                        if (existing == null) AssetDatabase.CreateAsset(f.sharedMesh, path);
                        else { var source = f.sharedMesh; EditorUtility.CopySerialized(source, existing); f.sharedMesh = existing; Object.DestroyImmediate(source); }
                    }
                PrefabUtility.SaveAsPrefabAsset(root, Folder + "/" + item.id + ".prefab");
                RenderIcon(root, item.id);
                foreach (PlayerColorId color in Enum.GetValues(typeof(PlayerColorId)))
                    if (color != PlayerColorId.None) RenderIcon(root, item.id, color);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
    [MenuItem("Tools/Wizard War/Rebuild cosmetic models and previews")]
    public static void RebuildCosmetics()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play Mode first.");
        BuildSkins();AssetDatabase.SaveAssets();AssetDatabase.Refresh();
    }
    static Bounds BoundsIn(Transform root)
    {
        Bounds result = default; bool first = true;
        foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Bounds b = f.sharedMesh.bounds;
            for (int i=0;i<8;i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                var point = root.InverseTransformPoint(f.transform.TransformPoint(corner));
                if (first) { result = new Bounds(point, Vector3.zero); first=false; } else result.Encapsulate(point);
            }
        }
        return result;
    }
    static void Hat(Transform parent, int style)
    {
        if (style == 2)
        {
            Shape(parent,"Crown base",new[]{.37f,.40f,.38f},new[]{-.40f,-.24f,-.12f},10,cloth);
            for(int i=0;i<5;i++)
            {
                float a=i*Mathf.PI*2/5;
                Crystal(parent,"Crown shard "+i,new Vector3(Mathf.Cos(a)*.3f,.06f,Mathf.Sin(a)*.3f),new Vector3(.12f,.54f,.12f),gem);
            }
            Ring(parent,"Gold circlet",-.20f,.395f,.035f,gold);
            Shape(parent,"Crown band",new[]{.401f,.397f},new[]{-.32f,-.18f},10,band);
        }
        else
        {
            Shape(parent,"Brim",new[]{.52f,.58f,.55f},new[]{-.46f,-.41f,-.36f},16,cloth);
            Shape(parent,"Crown",style==1?new[]{.31f,.27f,.14f,.015f}:new[]{.29f,.31f,.22f,.01f},
                style==1?new[]{-.37f,-.1f,.27f,.6f}:new[]{-.35f,.0f,.28f,.49f},style==1?8:12,cloth);
            Ring(parent,"Gold brim",-.40f,.55f,.025f,gold);
            Shape(parent,"TeamTrim",new[]{.309f,.294f},new[]{-.31f,-.18f},12,band);
            Crystal(parent,"Front jewel",new Vector3(0,-.2f,-.31f),new Vector3(.13f,.18f,.07f),gem);
            if(style==3)
            {
                for(int i=0;i<3;i++) Crystal(parent,"Star "+i,new Vector3(-.17f+i*.17f,.02f+i*.08f,-.23f),Vector3.one*.07f,gold);
                Crystal(parent,"Tip diamond",new Vector3(0,.565f,0),new Vector3(.13f,.22f,.13f),gem);
            }
        }
    }
    static void Staff(Transform parent,int style)
    {
        Shape(parent,"Shaft",new[]{.045f,.036f,.045f},new[]{-.9f,0f,.72f},8,dark);
        Shape(parent,"TeamTrim",new[]{.057f,.057f},new[]{-.28f,.08f},8,band);
        Ring(parent,"Grip lower",-.28f,.061f,.02f,gold);Ring(parent,"Grip upper",.08f,.061f,.02f,gold);
        Crystal(parent,"Core",new Vector3(0,.7f,0),style==1?new Vector3(.23f,.32f,.23f):style==2?new Vector3(.18f,.48f,.18f):Vector3.one*.23f,gem);
        if(style==3)
        {
            var orbit=Ring(parent,"Orbit",.71f,.27f,.018f,gold); orbit.localRotation=Quaternion.Euler(64,0,25);
            var orbit2=Ring(parent,"Orbit crossing",.71f,.23f,.018f,gold);orbit2.localRotation=Quaternion.Euler(20,0,-35);
            Crystal(parent,"Satellite",new Vector3(.25f,.77f,0),Vector3.one*.065f,gem);
        }
        else
            for(int i=0;i<3;i++)
            {
                float a=i*Mathf.PI*2/3;
                Crystal(parent,"Prong "+i,new Vector3(Mathf.Cos(a)*.15f,.67f,Mathf.Sin(a)*.15f),new Vector3(.06f,style==1?.42f:.25f,.06f),style==1?gold:gem);
            }
        Ring(parent,"Ferrule",-.85f,.047f,.025f,gold);
    }
    static void Body(Transform parent,int style)
    {
        Shape(parent,"Robe",style==1?new[]{.50f,.46f,.27f,.39f,.22f}:style==2?new[]{.43f,.49f,.28f,.43f,.20f}:new[]{.49f,.42f,.24f,.32f,.19f},
            new[]{-.65f,-.48f,.08f,.47f,.61f},style==2?10:12,cloth);
        Shape(parent,"Collar",new[]{.22f,.26f},new[]{.52f,.69f},12,style==2?gem:gold);
        Shape(parent,"TeamTrim",new[]{.282f,.278f},new[]{.04f,.17f},12,trim);
        Ring(parent,"Hem piping",-.57f,style==2?.456f:.476f,.018f,gold);
        Crystal(parent,"Belt clasp",new Vector3(0,.10f,-.285f),new Vector3(.10f,.13f,.055f),gem);
        for(int side=-1;side<=1;side+=2)
        {
            Crystal(parent,"Shoulder "+side,new Vector3(side*.32f,.44f,0),new Vector3(.22f,.18f,.36f),style==2?gem:gold);
            var panel=Cube(parent,"Front panel "+side,new Vector3(side*.10f,-.18f,-.30f),new Vector3(.10f,.55f,.035f),style==3?gold:dark);
            panel.localRotation=Quaternion.Euler(-15,0,side*8);
        }
        if(style==3)
            for(int i=0;i<4;i++) Crystal(parent,"Astral clasp "+i,new Vector3(0,.35f-i*.16f,-.30f),new Vector3(.07f,.08f,.04f),gem);
    }
    static Transform Cube(Transform parent,string name,Vector3 pos,Vector3 scale,Material material)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=pos;go.transform.localScale=scale;
        go.GetComponent<Renderer>().sharedMaterial=material;return go.transform;
    }
    static Transform Crystal(Transform parent,string name,Vector3 pos,Vector3 scale,Material material)
    {
        var go=Shape(parent,name,new[]{0f,.5f,.35f,0f},new[]{-.5f,-.18f,.21f,.5f},5,material);
        go.localPosition=pos;go.localScale=scale;return go;
    }
    static Transform Shape(Transform parent,string name,float[] radii,float[] heights,int segments,Material material)
    {
        var vertices=new List<Vector3>();var triangles=new List<int>();
        Vector3 Point(int r,int s) { float a=s*Mathf.PI*2/segments;return new Vector3(Mathf.Cos(a)*radii[r],heights[r],Mathf.Sin(a)*radii[r]); }
        for(int ring=0;ring<radii.Length-1;ring++) for(int i=0;i<segments;i++)
        {
            int n=vertices.Count;vertices.Add(Point(ring,i));vertices.Add(Point(ring+1,i));vertices.Add(Point(ring+1,i+1));vertices.Add(Point(ring,i+1));
            triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
        }
        for(int end=0;end<2;end++) for(int i=0;i<segments;i++)
        {
            int ring=end==0?0:radii.Length-1,n=vertices.Count;
            vertices.Add(new Vector3(0,heights[ring],0));vertices.Add(Point(ring,i));vertices.Add(Point(ring,i+1));
            triangles.AddRange(end==0?new[]{n,n+1,n+2}:new[]{n,n+2,n+1});
        }
        return MeshObject(parent,name,vertices,triangles,material);
    }
    static Transform Ring(Transform parent,string name,float y,float radius,float thickness,Material material)
    {
        var v=new List<Vector3>();var t=new List<int>(); const int around=24, section=6;
        for(int i=0;i<around;i++) for(int j=0;j<section;j++)
        {
            int n=v.Count;
            for(int q=0;q<4;q++)
            {
                float a=(i+(q==1||q==2?1:0))*Mathf.PI*2/around,b=(j+(q>=2?1:0))*Mathf.PI*2/section;
                float r=radius+Mathf.Cos(b)*thickness;v.Add(new Vector3(Mathf.Cos(a)*r,Mathf.Sin(b)*thickness,Mathf.Sin(a)*r));
            }
            t.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
        }
        var root=MeshObject(parent,name,v,t,material);root.localPosition=new Vector3(0,y,0);return root;
    }
    static Transform MeshObject(Transform parent,string name,List<Vector3> v,List<int> t,Material material)
    {
        var mesh=new Mesh{name=name};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);
        go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<Renderer>().sharedMaterial=material;return go.transform;
    }
    public static void RebuildColorPreviews(PlayerColorId color)
    {
        foreach (var item in ShopCatalog.Items.Where(ShopCatalog.IsCosmetic))
            RenderIcon(Resources.Load<GameObject>("Shop/" + item.id), item.id, color);
    }
    static void RenderIcon(GameObject source,string id,PlayerColorId? tint=null)
    {
        var preview=EditorSceneManager.NewPreviewScene();RenderTexture texture=null;Texture2D image=null;
        try
        {
            var model=Object.Instantiate(source);SceneManager.MoveGameObjectToScene(model,preview);
            model.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);model.transform.localScale=Vector3.one;
            if(ShopCatalog.IsCosmetic(ShopCatalog.Find(id))) WizardCosmeticTint.Apply(model,PlayerColorManager.ToUnityColor(tint??PlayerColorId.Blue));
            if(id.StartsWith("book_",StringComparison.Ordinal))model.transform.rotation=Quaternion.Euler(-70,0,10);
            foreach(var t in model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            foreach(var c in model.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
            var cameraObject=new GameObject("Shop icon camera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraObject,preview);
            var camera=cameraObject.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.05f,.04f,0);
            camera.scene=preview;
            camera.cullingMask=1<<31;camera.orthographic=true;camera.nearClipPlane=.01f;camera.farClipPlane=100;camera.enabled=false;
            var renderers=model.GetComponentsInChildren<Renderer>();Bounds bounds=renderers[0].bounds;
            foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
            float size=Mathf.Max(bounds.size.y,bounds.size.x,bounds.size.z);
            camera.orthographicSize=size*.63f;camera.transform.position=bounds.center+new Vector3(-.35f,.25f,-1).normalized*(size*3+1);
            camera.transform.LookAt(bounds.center);
            foreach(var direction in new[]{new Vector3(35,-30,0),new Vector3(330,140,0)})
            {
                var lightObject=new GameObject("Shop studio light",typeof(Light));SceneManager.MoveGameObjectToScene(lightObject,preview);
                var light=lightObject.GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.6f;light.cullingMask=1<<31;
                light.transform.rotation=Quaternion.Euler(direction);
            }
            int resolution=tint.HasValue?256:384;
            texture=new RenderTexture(resolution,resolution,24,RenderTextureFormat.ARGB32);camera.targetTexture=texture;
            var previous=RenderTexture.active;
            try { camera.Render();RenderTexture.active=texture;image=new Texture2D(resolution,resolution,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,resolution,resolution),0,0);image.Apply(); }
            finally { RenderTexture.active=previous; }
            string directory=Folder+"/Icons"+(tint.HasValue?"/"+tint.Value:"");Directory.CreateDirectory(directory);
            string path=directory+"/"+id+".png";File.WriteAllBytes(path,image.EncodeToPNG());AssetDatabase.ImportAsset(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
        }
        finally { if(texture!=null)Object.DestroyImmediate(texture);if(image!=null)Object.DestroyImmediate(image);EditorSceneManager.ClosePreviewScene(preview); }
    }
    static void InstallPlayer()
    {
        using var edit=new PrefabUtility.EditPrefabContentsScope("Assets/Prefabs/Player.prefab");
        var wizard=edit.prefabContentsRoot.GetComponentInChildren<WizardAppearance>(true);
        if(wizard.GetComponent<WizardCosmetics>()==null)wizard.gameObject.AddComponent<WizardCosmetics>();
    }
    static GameObject Coin(Transform parent,float x,float y,float size)
    {
        var found=parent.Find("W coin");
        var go=found!=null?found.gameObject:new GameObject("W coin",typeof(RectTransform),typeof(CanvasRenderer),typeof(ShopCoinGraphic));
        var rect=go.GetComponent<RectTransform>();rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);
        rect.anchoredPosition=new Vector2(x,y);rect.sizeDelta=new Vector2(size,size);go.layer=parent.gameObject.layer;
        var graphic=go.GetComponent<ShopCoinGraphic>();graphic.color=Color.white;graphic.raycastTarget=false;return go;
    }
    [MenuItem("Tools/Wizard War/Update colour shop and W icons")]
    public static void UpgradeColorShop()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stop Play Mode first.");
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/Menu.unity");bool opened=!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity",OpenSceneMode.Additive);
        var buttons=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<ColorButton>(true)).ToArray();
        foreach(var button in buttons)
        {
            var item=ShopCatalog.Find(ShopCatalog.ColorId(button.ColorId));
            var buttonRect=button.GetComponent<RectTransform>();
            buttonRect.anchoredPosition=new Vector2(buttonRect.anchoredPosition.x,(int)button.ColorId<=5?90:-71);
            var label=Label(button.transform,"Colour price",item.price==0?"":item.price+" W",0,-70,140,32,21);
            var coin=Coin(label.transform,-49,0,24);coin.SetActive(item.price>0);
            var found=button.transform.Find("Purchase lock");
            var locked=found!=null?found.gameObject:new GameObject("Purchase lock",typeof(RectTransform),typeof(CanvasRenderer),typeof(ShopLockGraphic));
            var rect=locked.GetComponent<RectTransform>();rect.SetParent(button.transform,false);rect.anchoredPosition=Vector2.zero;rect.sizeDelta=new Vector2(32,38);
            var graphic=locked.GetComponent<ShopLockGraphic>();graphic.color=new Color(.15f,.09f,.035f);graphic.raycastTarget=false;
            locked.SetActive(item.price>0);
            var mark=button.transform.Find("Selected color checkmark");
            button.ConfigureShop(mark!=null?mark.gameObject:null,locked,label,coin);if(mark!=null)mark.gameObject.SetActive(button.ColorId==PlayerColorId.Blue);
            foreach(var t in button.GetComponentsInChildren<Transform>(true))t.gameObject.layer=button.gameObject.layer;
            EditorUtility.SetDirty(button);
        }
        var parent=buttons[0].transform.parent;
        var colors=parent.GetComponent<ColorShopUI>()??parent.gameObject.AddComponent<ColorShopUI>();
        var heading=parent.parent.Find("ChooseColor text").GetComponent<RectTransform>();heading.anchoredPosition=new Vector2(-110,112);
        colors.wallet=Label(parent,"Colour wallet","— W",285,181,180,36,25);Coin(colors.wallet.transform,-83,0,30);
        colors.status=Label(parent,"Colour status","Нажмите на цвет, чтобы купить или выбрать. Покупка навсегда.",-90,-184,550,34,16);
        colors.refresh=Button(parent,"Refresh colours","Обновить",285,-184,150,34);
        foreach(var t in colors.GetComponentsInChildren<Transform>(true))t.gameObject.layer=parent.gameObject.layer;
        EditorUtility.SetDirty(colors);
        var shop=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<ShopMenuUI>(true)).Single();
        shop.wallet.text="— W";Coin(shop.wallet.transform,-116,0,32);
        Coin(shop.opener.transform,-105,0,25);shop.shortcut.fontSize=18;
        shop.shortcut.rectTransform.anchoredPosition=new Vector2(10,0);shop.shortcut.rectTransform.sizeDelta=new Vector2(216,42);
        foreach(var card in shop.cards)Coin(card.price.transform,-77,0,25);
        foreach(var book in scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<BookPurchaseLock>(true)))
        { book.price.text=ShopCatalog.Find(ShopCatalog.BookId(book.book.Element)).price+" W";Coin(book.price.transform,-61,0,25); }
        EditorUtility.SetDirty(shop);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        if(opened)EditorSceneManager.CloseScene(scene,true);
        AssetDatabase.SaveAssets();
    }
    static void InstallMenu()
    {
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/Menu.unity");bool opened=!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity",OpenSceneMode.Additive);
        if(scene.isDirty)EditorSceneManager.SaveScene(scene,"Logs/Shop/Menu-before-shop.unity",true);
        var shelf=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<InteractiveShelfMenu>(true)).Single();
        var shop=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<ShopMenuUI>(true)).FirstOrDefault();
        if(shop==null)
        {
            var root=new GameObject("Shop UI",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler),typeof(UnityEngine.UI.GraphicRaycaster),typeof(ShopMenuUI));
            SceneManager.MoveGameObjectToScene(root,scene);shop=root.GetComponent<ShopMenuUI>();
        }
        BuildMenu(shop);
        foreach(var book in shelf.Books.Where(x=>!string.IsNullOrEmpty(ShopCatalog.BookId(x.Element))))
        {
            var component=book.GetComponent<BookPurchaseLock>()??book.gameObject.AddComponent<BookPurchaseLock>();
            component.book=book;component.shelf=shelf;
            if(component.marker==null)
            {
                var go=new GameObject("Purchase lock",typeof(RectTransform),typeof(Canvas));go.transform.SetParent(book.transform,false);
                component.marker=go.GetComponent<Canvas>();component.marker.renderMode=RenderMode.WorldSpace;
                var rect=go.GetComponent<RectTransform>();rect.sizeDelta=new Vector2(100,120);rect.localScale=Vector3.one*.0011f;
                // Convert scale through the transformed book hierarchy to a small constant world size.
                var lossy=book.transform.lossyScale;rect.localScale=new Vector3(.0011f/lossy.x,.0011f/lossy.y,.0011f/lossy.z);
                var lockRoot=new GameObject("Padlock",typeof(RectTransform),typeof(CanvasRenderer),typeof(ShopLockGraphic));lockRoot.transform.SetParent(go.transform,false);
                var graphic=lockRoot.GetComponent<ShopLockGraphic>();graphic.color=Gold;graphic.raycastTarget=false;
                graphic.rectTransform.sizeDelta=new Vector2(65,72);graphic.rectTransform.anchoredPosition=new Vector2(0,10);
                component.price=Label(go.transform,"Price","",0,-43,110,34,28);component.price.color=Gold;
            }
            var lockGraphic=component.marker.GetComponentInChildren<ShopLockGraphic>(true);
            if(lockGraphic.GetComponent<CanvasRenderer>()==null)lockGraphic.gameObject.AddComponent<CanvasRenderer>();
            component.marker.enabled=false;EditorUtility.SetDirty(component);
            var bookData=new SerializedObject(book);
            var physical=(Renderer)bookData.FindProperty("bookRenderers").GetArrayElementAtIndex(0).objectReferenceValue;
            RenderIcon(physical.gameObject,ShopCatalog.BookId(book.Element));
        }
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        if(opened)EditorSceneManager.CloseScene(scene,true);
    }
    static void BuildMenu(ShopMenuUI shop)
    {
        shop.canvas=shop.GetComponent<Canvas>();shop.canvas.renderMode=RenderMode.ScreenSpaceOverlay;shop.canvas.sortingOrder=800;
        var scaler=shop.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1280,720);scaler.matchWidthOrHeight=.5f;
        shop.opener=Button(shop.transform,"Open shop","МАГАЗИН",0,0,244,46);
        var shortcutRect=(RectTransform)shop.opener.transform;shortcutRect.anchorMin=shortcutRect.anchorMax=new Vector2(1,1);shortcutRect.pivot=new Vector2(1,1);shortcutRect.anchoredPosition=new Vector2(-26,-24);
        shop.shortcut=shop.opener.GetComponentInChildren<TMP_Text>();
        var modal=Box(shop.transform,"Shop modal",0,0,0,0,new Color(.025f,.02f,.02f,.98f));Stretch(modal);shop.modal=modal.gameObject;
        var dialog=Box(modal,"Shop dialog",0,0,1160,668,new Color(.10f,.073f,.051f));
        Label(dialog,"Eyebrow","КОЛЛЕКЦИЯ МАГА",-350,294,400,25,15).color=Gold;
        Label(dialog,"Title","Лавка артефактов",-270,252,550,55,38).alignment=TextAlignmentOptions.MidlineLeft;
        shop.wallet=Label(dialog,"Wallet","Монеты: —",345,267,270,48,25);shop.wallet.color=Gold;
        shop.close=Button(dialog,"Close","×",530,290,42,42);
        shop.subtitle=Label(dialog,"Subtitle","",0,203,1080,36,18);shop.subtitle.alignment=TextAlignmentOptions.MidlineLeft;
        string[] captions={"Шляпы","Посохи","Одежда","Книги стихий"};shop.tabs=new UnityEngine.UI.Button[4];
        for(int i=0;i<4;i++)shop.tabs[i]=Button(dialog,"Tab "+i,captions[i],-410+i*273,149,262,44);
        var row=Box(dialog,"Items",0,-66,1084,364,Color.clear);row.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
        var layout=row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>()??row.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        layout.spacing=20;layout.childAlignment=TextAnchor.MiddleCenter;layout.childControlWidth=true;layout.childControlHeight=true;
        layout.childForceExpandWidth=true;layout.childForceExpandHeight=true;shop.cards=new ShopMenuUI.ItemCard[3];
        for(int i=0;i<3;i++)
        {
            var card=Box(row,"Item "+i,0,0,344,364,new Color(.16f,.117f,.083f));
            var size=card.GetComponent<UnityEngine.UI.LayoutElement>()??card.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();size.preferredWidth=344;size.preferredHeight=364;
            var imageRoot=card.Find("Preview");var go=imageRoot!=null?imageRoot.gameObject:new GameObject("Preview",typeof(RectTransform),typeof(UnityEngine.UI.RawImage));
            go.transform.SetParent(card,false);var icon=go.GetComponent<UnityEngine.UI.RawImage>();icon.raycastTarget=false;
            icon.rectTransform.sizeDelta=new Vector2(230,210);icon.rectTransform.anchoredPosition=new Vector2(0,61);
            var title=Label(card,"Name","",0,-61,315,58,23);
            var price=Label(card,"Price","",0,-108,310,30,20);price.color=Gold;
            var action=Button(card,"Action","Купить",0,-150,302,42);
            shop.cards[i]=new ShopMenuUI.ItemCard{root=card.gameObject,icon=icon,title=title,price=price,action=action,actionLabel=action.GetComponentInChildren<TMP_Text>()};
        }
        shop.status=Label(dialog,"Status","",-231,-279,626,44,16);shop.status.alignment=TextAlignmentOptions.MidlineLeft;
        shop.reset=Button(dialog,"Reset","Обычный вид",210,-281,196,40);
        shop.refresh=Button(dialog,"Refresh","Обновить",431,-281,198,40);
        foreach(var t in shop.GetComponentsInChildren<Transform>(true))t.gameObject.layer=5;
        shop.modal.SetActive(false);EditorUtility.SetDirty(shop);
    }
    static void InstallRewardUI()
    {
        using var edit=new PrefabUtility.EditPrefabContentsScope("Assets/Prefabs/UI/MatchUI.prefab");
        var ui=edit.prefabContentsRoot.GetComponent<PlayerGameUI>();var so=new SerializedObject(ui);
        var results=(GameObject)so.FindProperty("resultsPanel").objectReferenceValue;var card=results.transform.Find("Results card");
        var scroll=card.Find("Results scroll").GetComponent<RectTransform>();scroll.sizeDelta=new Vector2(940,264);scroll.anchoredPosition=new Vector2(0,5);
        var label=Label(card,"Coin reward","",0,-151,950,36,19);label.color=Gold;
        so.FindProperty("rewardSummary").objectReferenceValue=label;so.ApplyModifiedPropertiesWithoutUndo();
    }
    static RectTransform Box(Transform parent,string name,float x,float y,float w,float h,Color color)
    {
        var child=parent.Find(name);var go=child!=null?child.gameObject:new GameObject(name,typeof(RectTransform),typeof(UnityEngine.UI.Image));
        var rect=go.GetComponent<RectTransform>();rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);
        rect.anchoredPosition=new Vector2(x,y);rect.sizeDelta=new Vector2(w,h);go.GetComponent<UnityEngine.UI.Image>().color=color;return rect;
    }
    static TMP_Text Label(Transform parent,string name,string value,float x,float y,float w,float h,float size)
    {
        var child=parent.Find(name);var go=child!=null?child.gameObject:new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));
        var text=go.GetComponent<TMP_Text>();text.rectTransform.SetParent(parent,false);text.rectTransform.anchorMin=text.rectTransform.anchorMax=new Vector2(.5f,.5f);
        text.rectTransform.anchoredPosition=new Vector2(x,y);text.rectTransform.sizeDelta=new Vector2(w,h);text.font=Font;
        text.fontSize=size;text.text=value;text.color=Ink;text.alignment=TextAlignmentOptions.Center;text.raycastTarget=false;text.richText=false;return text;
    }
    static UnityEngine.UI.Button Button(Transform parent,string name,string text,float x,float y,float w,float h)
    {
        var rect=Box(parent,name,x,y,w,h,new Color(.38f,.24f,.11f));var button=rect.GetComponent<UnityEngine.UI.Button>()??rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic=rect.GetComponent<UnityEngine.UI.Image>();var colors=button.colors;colors.highlightedColor=new Color(1,.86f,.63f);colors.disabledColor=new Color(.65f,.60f,.55f,.65f);button.colors=colors;
        Label(rect,"Label",text,0,0,w-12,h-4,20);return button;
    }
    static void Stretch(RectTransform rect) { rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero; }
}
