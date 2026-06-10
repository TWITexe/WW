using UnityEngine;
using UnityEngine.UI;

public class ColorButton : MonoBehaviour
{
    [SerializeField] private PlayerColorId colorId;

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(SelectColor);
    }

    private void SelectColor()
    {
        LocalPlayerSettings.Instance.SetPreferredColor(colorId);
    }
}