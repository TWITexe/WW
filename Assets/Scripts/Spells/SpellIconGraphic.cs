using UnityEngine;
using UnityEngine.UI;

// рисует иконки заклинаний геометрией интерфейса, сохраняя чёткость при изменении масштаба.
[RequireComponent(typeof(CanvasRenderer))]
public class SpellIconGraphic : MaskableGraphic
{
    public Spell spell;
    private VertexHelper mesh;
    private Rect rect;
    // выбираем рисунок по имени ассета заклинания и заполняем его вершины и треугольники.
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); mesh=vh;rect=GetPixelAdjustedRect();
        string id=spell!=null?spell.name:"";
        switch(id)
        {
            case "SteamDash": Wave(-.5f);Line(V(-.8f,.3f),V(.7f,.3f));Line(V(.3f,.7f),V(.7f,.3f));Line(V(.3f,-.1f),V(.7f,.3f));break;
            case "IceMirror": Path(new[]{V(-.6f,-.8f),V(-.6f,.8f),V(.6f,.8f),V(.6f,-.8f),V(-.6f,-.8f)});Line(V(-.3f,-.5f),V(.3f,.5f));break;
            case "StoneWall": Path(new[]{V(-.85f,-.65f),V(-.85f,.65f),V(.85f,.65f),V(.85f,-.65f),V(-.85f,-.65f)});Line(V(-.85f,0),V(.85f,0));Line(V(0,0),V(0,.65f));Line(V(-.4f,-.65f),V(-.4f,0));break;
            case "FireSeal": Circle(Vector2.zero,.8f);Path(new[]{V(0,.6f),V(-.5f,-.4f),V(.5f,-.4f),V(0,.6f)});break;
            case "GravityWell": Circle(Vector2.zero,.35f);Circle(Vector2.zero,.8f);Line(V(-.9f,.9f),V(-.25f,.25f));Line(V(.9f,-.9f),V(.25f,-.25f));break;
            case "SnowDecoy": Circle(V(-.3f,.4f),.25f);Circle(V(.4f,.4f),.25f);Path(new[]{V(-.65f,-.7f),V(-.3f,0),V(.05f,-.7f)});Path(new[]{V(.05f,-.7f),V(.4f,0),V(.75f,-.7f)});break;
            case "FireBall": Poly(new[]{V(0,1),V(-.55f,.15f),V(-.6f,-.5f),V(0,-.9f),V(.65f,-.4f),V(.35f,.45f),V(.2f,0)});break;
            case "WindFlow": Wave(.5f);Wave(0);Wave(-.5f);break;
            case "WaterBolt": Drop();break;
            case "IceShard": Poly(new[]{V(0,1),V(.5f,0),V(0,-1),V(-.5f,0)});break;
            case "Boulder": Poly(new[]{V(-.8f,-.6f),V(-.9f,.3f),V(-.3f,.8f),V(.7f,.5f),V(.9f,-.5f)});break;
            case "SmokeCloud": Circle(V(-.45f,0),.35f);Circle(V(0,.25f),.45f);Circle(V(.5f,0),.3f);Wave(-.5f);break;
            case "SteamCloud": Wave(-.5f);Steam(-.5f);Steam(0);Steam(.5f);break;
            case "BoilingJet": Drop();Line(V(-.85f,-.6f),V(-.85f,.5f));Line(V(.85f,-.6f),V(.85f,.5f));break;
            case "FireTornado":
                for(int i=0;i<5;i++){float y=.8f-i*.35f;float w=.85f-i*.13f;Line(V(-w,y),V(w,y-.12f));}break;
            case "Blizzard": Snow();Wave(-.8f);break;
            case "Mud": Wave(-.5f);Wave(0);Circle(V(-.4f,.55f),.18f);Circle(V(.45f,.5f),.25f);break;
            case "Magma": Circle(Vector2.zero,.8f);Line(V(-.3f,.7f),V(.1f,.1f));Line(V(.1f,.1f),V(-.3f,-.25f));Line(V(-.3f,-.25f),V(.35f,-.75f));break;
            case "FrostNova": Circle(Vector2.zero,.9f);Snow();break;
            case "StoneSkin":
                Path(new[]{V(-.75f,.75f),V(.75f,.75f),V(.6f,-.4f),V(0,-.9f),V(-.6f,-.4f),V(-.75f,.75f)});
                Line(V(-.35f,0),V(-.05f,-.3f));Line(V(-.05f,-.3f),V(.4f,.3f));break;
            default:
                Wave(-.7f);Line(V(0,-.5f),V(0,.9f));Line(V(-.4f,-.5f),V(-.6f,.5f));Line(V(.4f,-.5f),V(.6f,.5f));
                Line(V(-.25f,.6f),V(0,.9f));Line(V(0,.9f),V(.25f,.6f));break;
        }
    }
    // сокращённо задаём точку рисунка в нормализованных координатах.
    static Vector2 V(float x,float y)=>new Vector2(x,y);
    // переводим координаты рисунка в прямоугольник иконки, оставляя небольшой отступ от краёв.
    Vector2 Point(Vector2 p)=>rect.center+Vector2.Scale(p,rect.size)*.43f;
    // заполняем многоугольник треугольниками от средней точки его вершин.
    void Poly(Vector2[] points)
    {
        int start=mesh.currentVertCount;
        Vector2 center=Vector2.zero;foreach(var p in points)center+=p;center/=points.Length;
        mesh.AddVert(Point(center),color,Vector2.zero);
        foreach(var p in points)mesh.AddVert(Point(p),color,Vector2.zero);
        for(int i=0;i<points.Length;i++)mesh.AddTriangle(start,start+1+i,start+1+(i+1)%points.Length);
    }
    // строим толстый отрезок как четырёхугольник.
    void Line(Vector2 a,Vector2 b)
    {
        Vector2 normal=new Vector2(-(b-a).y,(b-a).x).normalized*.065f;
        Poly(new[]{a+normal,b+normal,b-normal,a-normal});
    }
    // соединяем последовательные точки рисунка отрезками.
    void Path(Vector2[] points){for(int i=1;i<points.Length;i++)Line(points[i-1],points[i]);}
    // аппроксимируем окружность тридцатью двумя отрезками.
    void Circle(Vector2 center,float radius)
    {
        for(int i=0;i<32;i++){float a=i*Mathf.PI*2/32,b=(i+1)*Mathf.PI*2/32;Line(center+V(Mathf.Cos(a),Mathf.Sin(a))*radius,center+V(Mathf.Cos(b),Mathf.Sin(b))*radius);}
    }
    // рисуем силуэт капли для водяных заклинаний.
    void Drop()=>Poly(new[]{V(0,1),V(-.65f,-.15f),V(-.5f,-.7f),V(0,-.9f),V(.5f,-.7f),V(.65f,-.15f)});
    // рисуем горизонтальную волну на заданной высоте.
    void Wave(float y){for(int i=0;i<16;i++){float x=-.85f+i*.1f;Line(V(x,y+Mathf.Sin(x*4)*.12f),V(x+.1f,y+Mathf.Sin((x+.1f)*4)*.12f));}}
    // рисуем вертикальную извилистую струю пара с заданным смещением.
    void Steam(float x){for(int i=0;i<8;i++){float y=-.1f+i*.12f;Line(V(x+Mathf.Sin(y*7)*.08f,y),V(x+Mathf.Sin((y+.12f)*7)*.08f,y+.12f));}}
    // рисуем шесть лучей снежинки.
    void Snow(){for(int i=0;i<6;i++){float a=i*Mathf.PI/3;Vector2 d=V(Mathf.Cos(a),Mathf.Sin(a));Line(Vector2.zero,d*.7f);}}
    // выбираем цвет иконки из настроек заклинания либо стандартный цвет исходного снаряда.
    public static Color Tint(Spell value)=>value is TacticalSpell tactical?tactical.tint:value is ElementalSpell elemental?elemental.tint:
        value is FireBall?new Color(1,.4f,.15f):new Color(.4f,.95f,.8f);
}
