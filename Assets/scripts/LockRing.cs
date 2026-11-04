using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Draws a red ring around a board piece while it's locked by a player.
/// Crescent-aware, same strategy as BagRing.
/// </summary>
[RequireComponent(typeof(NetworkBall))]
public class LockRing : MonoBehaviour
{
    [SerializeField] private Color ringColor     = new Color(1f, 0.15f, 0.15f, 1f);
    [SerializeField] private float ringWidth     = 0.055f;
    [SerializeField] private float radiusPadding = 0.08f;
    [SerializeField] private int   segments      = 48;

    private NetworkBall  _networkBall;
    private LineRenderer _line;

    private void Awake()
    {
        _networkBall = GetComponent<NetworkBall>();
    }

    private void Start()
    {
        if (_networkBall == null) return;

        var go = new GameObject("Lock Ring");
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
        _line.sortingOrder   = 25;
        _line.numCapVertices    = 4;
        _line.numCornerVertices = 4;
        _line.enabled = false;

        RebuildGeometry();
    }

    private void Update()
    {
        if (_networkBall == null || _line == null) return;

        bool locked = _networkBall.LockedBy.Value != ulong.MaxValue;
        if (_line.enabled != locked)
        {
            _line.enabled = locked;
            if (locked) RebuildGeometry();
        }

        int ringLayer = LayerMask.NameToLayer(
            _networkBall.CurrentZone.Value == (byte)BallZone.Hand
                ? "HandPieces"
                : "BoardPieces");
        if (ringLayer >= 0) _line.gameObject.layer = ringLayer;

        if (locked) _line.transform.rotation = Quaternion.identity;
    }

    private void RebuildGeometry()
    {
        float radius = GetActiveRadius() + radiusPadding;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            _line.SetPosition(i, new Vector3(
                Mathf.Cos(a) * radius,
                Mathf.Sin(a) * radius,
                -0.05f));
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