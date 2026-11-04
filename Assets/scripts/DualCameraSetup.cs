using UnityEngine;

/// <summary>
/// Creates two orthographic cameras: BoardCamera (top viewport) and
/// HandCamera (bottom viewport). Each renders only its own layer.
/// Exposes a HandFraction used by the divider and controller.
/// </summary>
public class DualCameraSetup : MonoBehaviour
{
    
    
    public static DualCameraSetup Instance { get; private set; }

    [Range(0.15f, 0.6f)]
    [SerializeField] private float handFraction = 0.32f;

    [SerializeField] private Color boardBackground = new Color(0.10f, 0.12f, 0.16f, 1f);
    [SerializeField] private Color handBackground  = new Color(0.22f, 0.20f, 0.18f, 1f);

    [SerializeField] private float initialOrthoSize = 6f;

    public Camera BoardCamera { get; private set; }
    public Camera HandCamera  { get; private set; }

    public float HandFraction
    {
        get => handFraction;
        set
        {
            handFraction = Mathf.Clamp(value, 0.15f, 0.6f);
            ApplyViewports();
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        BoardCamera = CreateCamera("BoardCamera", new Color(0f, 0f, 0f, 0f));
        BoardCamera.cullingMask = LayerMask.GetMask("BoardPieces");

        HandCamera = CreateCamera("HandCamera", new Color(0f, 0f, 0f, 0f));
        HandCamera.cullingMask = LayerMask.GetMask("HandPieces");

        BoardCamera.depth = 0;
        HandCamera.depth = 1;
        
        // Both cameras see the world at y≈0 initially (we'll separate via
        // world Y offsets for pieces once Pass 2 is in).
        BoardCamera.transform.position = new Vector3(0f, 0f, -10f);
        HandCamera.transform.position  = new Vector3(0f, 0f, -10f);   
        

        BoardCamera.orthographicSize = initialOrthoSize;
        HandCamera.orthographicSize  = initialOrthoSize * 0.8f;

        // Hide the original Main Camera's rendering of these layers so it
        // doesn't double-draw them. If you already deleted Main Camera, skip.
        var main = Camera.main;
        if (main != null)
        {
            main.cullingMask &= ~LayerMask.GetMask("BoardPieces", "HandPieces");
        }

        ApplyViewports();
    }

    private void ApplyViewports()
    {
        if (BoardCamera == null || HandCamera == null) return;

        BoardCamera.rect = new Rect(0f, handFraction, 1f, 1f - handFraction);
        HandCamera.rect  = new Rect(0f, 0f, 1f, handFraction);

        BoardCamera.backgroundColor = boardBackground;
        HandCamera.backgroundColor  = handBackground;
    }

    private Camera CreateCamera(string name, Color bg)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = bg;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 100f;
        cam.depth = 0;         // board renders first
        cam.allowHDR = false;
        cam.allowMSAA = false;
        return cam;
    }
}