using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Marker for the Bag button. Exposes its screen rect so BallDrag can detect drops on it,
/// and greys out the button when the bag pool is empty.
/// </summary>
public class BagButton : MonoBehaviour
{
    public static BagButton Instance { get; private set; }

    private RectTransform _rectTransform;
    private Canvas _canvas;
    private Button _button;

    public Rect ScreenRect { get; private set; }

    private void Awake()
    {
        Instance = this;
        _rectTransform = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();
        _button = GetComponent<Button>();
    }

    private void Update()
    {
        if (_rectTransform == null) return;

        // --- Screen rect for drop detection ---
        Camera cam = (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? _canvas.worldCamera
            : null;

        Vector3[] corners = new Vector3[4];
        _rectTransform.GetWorldCorners(corners);

        Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

        ScreenRect = new Rect(min.x, min.y, max.x - min.x, max.y - min.y);

        // --- Grey-out when the bag is empty ---
        if (_button != null && BagManager.Instance != null)
        {
            bool hasBalls = !BagManager.Instance.IsEmpty();
            bool bagLocked = BagManager.Instance.IsBeingEditedBySomeone;
            bool interactable = hasBalls && !bagLocked;
            if (_button.interactable != interactable)
                _button.interactable = interactable;
        }   
    }

    public bool IsMouseOver()
    {
        return ScreenRect.Contains(Input.mousePosition);
    }
}