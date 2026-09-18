using Mirror;
using TMPro;
using UnityEngine;

// показывает локальную цифру урона, которая поднимается и постепенно исчезает.
public class FloatingDamageNumber : MonoBehaviour
{
    public TMP_Text label;
    public float lifetime=1f, riseSpeed=.8f;
    public Color normalColor=Color.white, headColor=new Color(1,.7f,.15f);
    float age;
    Camera view;
    // задаём величину и цвет урона, затем выбираем камеру локального игрока.
    public void Initialize(int damage,bool headshot)
    {
        label.text=damage.ToString();label.color=headshot?headColor:normalColor;
        if(NetworkClient.localPlayer!=null)view=NetworkClient.localPlayer.GetComponentInChildren<RelativeMovement>()?.ViewCamera;
        if(view==null)view=Camera.main;
    }
    // поднимаем цифру, скрываем её за камерой и убираем по окончании времени жизни.
    void Update()
    {
        age+=Time.deltaTime;
        if(age>=lifetime){Destroy(gameObject);return;}
        transform.position+=Vector3.up*(riseSpeed*Time.deltaTime);
        if(view!=null){transform.rotation=view.transform.rotation;label.enabled=Vector3.Dot(view.transform.forward,transform.position-view.transform.position)>0;}
        var color=label.color;color.a=1-Mathf.Clamp01((age/lifetime-.5f)*2);label.color=color;
    }
}
