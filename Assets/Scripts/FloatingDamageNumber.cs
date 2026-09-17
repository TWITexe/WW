using Mirror;
using TMPro;
using UnityEngine;

public class FloatingDamageNumber : MonoBehaviour
{
    public TMP_Text label;
    public float lifetime=1f, riseSpeed=.8f;
    public Color normalColor=Color.white, headColor=new Color(1,.7f,.15f);
    float age;
    Camera view;
    public void Initialize(int damage,bool headshot)
    {
        label.text=damage.ToString();label.color=headshot?headColor:normalColor;
        if(NetworkClient.localPlayer!=null)view=NetworkClient.localPlayer.GetComponentInChildren<RelativeMovement>()?.ViewCamera;
        if(view==null)view=Camera.main;
    }
    void Update()
    {
        age+=Time.deltaTime;
        if(age>=lifetime){Destroy(gameObject);return;}
        transform.position+=Vector3.up*(riseSpeed*Time.deltaTime);
        if(view!=null){transform.rotation=view.transform.rotation;label.enabled=Vector3.Dot(view.transform.forward,transform.position-view.transform.position)>0;}
        var color=label.color;color.a=1-Mathf.Clamp01((age/lifetime-.5f)*2);label.color=color;
    }
}
