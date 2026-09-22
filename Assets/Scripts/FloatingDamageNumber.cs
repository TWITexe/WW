using Mirror;
using TMPro;
using UnityEngine;

// показывает локальную цифру урона, которая поднимается и постепенно исчезает.
public class FloatingDamageNumber : MonoBehaviour
{
    public TMP_Text label;
    public float lifetime=1f, riseSpeed=.8f;
    public Color normalColor=new Color(1,1,.3f), headColor=new Color(1,.08f,.06f);
    float age;
    bool incoming;
    Vector2 screenOffset;
    Camera view;
    // задаём величину и цвет урона, затем выбираем камеру локального игрока.
    public void Initialize(int damage,bool headshot,bool received=false)
    {
        incoming=received;
        screenOffset=new Vector2(Random.Range(.57f,.66f),Random.Range(.37f,.45f));
        label.fontStyle=FontStyles.Bold;
        label.outlineColor=new Color32(25,12,12,255);
        label.outlineWidth=.18f;
        if(incoming) transform.localScale*=.22f;
        label.text=damage.ToString();label.color=headshot?headColor:normalColor;
        if(NetworkClient.localPlayer!=null)view=NetworkClient.localPlayer.GetComponentInChildren<RelativeMovement>()?.ViewCamera;
        if(view==null)view=Camera.main;
    }
    // поднимаем цифру, скрываем её за камерой и убираем по окончании времени жизни.
    void Update()
    {
        age+=Time.deltaTime;
        if(age>=lifetime){Destroy(gameObject);return;}
        if(incoming && view!=null)
            transform.position=view.ViewportToWorldPoint(new Vector3(screenOffset.x,screenOffset.y+age*.065f,2f));
        else transform.position+=Vector3.up*(riseSpeed*Time.deltaTime);
        if(view!=null){transform.rotation=view.transform.rotation;label.enabled=Vector3.Dot(view.transform.forward,transform.position-view.transform.position)>0;}
        var color=label.color;color.a=1-Mathf.Clamp01((age/lifetime-.5f)*2);label.color=color;
    }
}
