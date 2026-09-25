using TMPro;
using UnityEngine;

// A visible padlock and price follow the real book; all purchase logic stays in the service.
public class BookPurchaseLock : MonoBehaviour
{
    public ElementBook book;
    public InteractiveShelfMenu shelf;
    public Canvas marker;
    public TMP_Text price;
    void LateUpdate()
    {
        if (book == null || shelf == null || marker == null) return;
        bool locked = !ShopCatalog.Allows(EconomyClient.Instance?.Profile, book.Element);
        marker.enabled = locked && shelf.IsFocused && !ShopMenuUI.IsOpen && !Mirror.NetworkClient.active;
        if (!marker.enabled) return;
        var camera = shelf.MenuCamera;
        Vector3 center = book.HitArea.bounds.center;
        Vector3 toward = (camera.transform.position - center).normalized;
        marker.transform.position = center + toward * (book.HitArea.bounds.extents.magnitude + .025f);
        marker.transform.rotation = camera.transform.rotation;
        price.text = ShopCatalog.Find(ShopCatalog.BookId(book.Element)).price + " W";
    }
}
