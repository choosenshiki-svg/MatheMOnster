using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Draws a blue ring around a bag ball. Crescent-aware: if the ball is a
/// directional operator, the ring wraps the polygon's world bounds instead
/// of the disabled CircleCollider2D.
/// </summary>
public class BagRing : MonoBehaviour
{
    [SerializeField] private Color ringColor    = new Color(0.2f, 0.6f, 1f, 1f);
    [SerializeField] private float ringWidth    = 0.05f;
    [SerializeField] private float radiusPadding = 0.12f;
    [SerializeField] private int   segments     = 48;

    private NetworkBall      _networkBall;
    private BagBallMarker    _marker;
    private LineRenderer     _line;

    private void Awake()
    {
        _networkBall = GetComponent<NetworkBall>();
        _marker      = GetComponent<BagBallMarker>();
    }

    private void Start()
    {
        var go = new GameObject("Bag Ring");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        _line = go.AddComponent<LineRenderer>();
        _line.useWorldSpace  = false;
        _line.loop           = true;
        _line.positionCount  = segments;
        _line.startWidth     = ringWidth;
        _line.endWidth       = ringWidth;
        _line.material       = new Material(Shader.Find("Sprites/Default"));
        _line.startColor     = ringColor;
        _line.endColor       = ringColor;
        _line.sortingOrder   = 24;
        _line.numCapVertices    = 4;
        _line.numCornerVertices = 4;
        _line.enabled = false;

        RebuildGeometry();
        UpdateVisibility();
    }

    private void Update()
    {
        if (_line == null) return;
        UpdateVisibility();
    }

    private bool IsBagBall()
    {
        if (_marker != null) return true;
        if (_networkBall != null) return _networkBall.IsBagBall.Value;
        return false;
    }

    private void UpdateVisibility()
    {
        bool isBag = IsBagBall();
        if (_line.enabled != isBag)
        {
            _line.enabled = isBag;
            if (isBag) RebuildGeometry();
        }

        if (isBag)
        {
            int layer = gameObject.layer;
            if (_line.gameObject.layer != layer)
                _line.gameObject.layer = layer;
        }
    }

    /// <summary>
    /// Rebuild the ring in local space sized to whichever collider is active
    /// on the ball. Safe to call every frame if needed.
    /// </summary>
    private void RebuildGeometry()
    {
        float radius = GetActiveRadius() + radiusPadding;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            _line.SetPosition(i, new Vector3(
                Mathf.Cos(a) * radius,
                Mathf.Sin(a) * radius,
                -0.04f));
        }
    }

    private float GetActiveRadius()
    {
        var dop = GetComponent<DirectionalOperator>();
        if (dop != null && dop.IsActive)
        {
            var poly = GetComponent<PolygonCollider2D>();
            if (poly != null && poly.enabled)
                return Mathf.Max(poly.bounds.extents.x, poly.bounds.extents.y);
        }

        var circle = GetComponent<CircleCollider2D>();
        if (circle != null && circle.enabled)
            return circle.radius;

        return 0.5f;
    }
}