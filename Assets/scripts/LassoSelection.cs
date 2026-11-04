using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

// Separate component for freeform ball selection and moving selected groups.
// Add this component to the same camera GameObject as CameraController.
public class LassoSelection : MonoBehaviour
{
    public Color selectionColor = new Color(0.2f, 1f, 0.8f, 1f);
    public float selectionOutlineWidth = 0.055f;

    private bool isLassoing;
    private readonly List<Vector2> lassoPoints = new List<Vector2>();
    private readonly HashSet<BallDrag> selectedBalls = new HashSet<BallDrag>();
    private readonly Dictionary<BallDrag, LineRenderer> selectionOutlines = new Dictionary<BallDrag, LineRenderer>();
    private Texture2D lassoPixel;

    public static LassoSelection Instance { get; private set; }
    public static bool IsLassoActive => Instance != null && Instance.isLassoing;

    private void Awake()
    {
        Instance = this;
        lassoPixel = new Texture2D(1, 1);
        lassoPixel.SetPixel(0, 0, Color.white);
        lassoPixel.Apply();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ClearSelection();
            isLassoing = false;
            lassoPoints.Clear();
            BallDrag.CancelActiveDragForLasso();
            return;
        }

        bool pointerOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetMouseButtonDown(0) && shiftHeld && !pointerOverUi)
        {
            isLassoing = true;
            lassoPoints.Clear();
            lassoPoints.Add(Input.mousePosition);
            BallDrag.CancelActiveDragForLasso();
        }

        if (isLassoing && Input.GetMouseButton(0))
        {
            Vector2 point = Input.mousePosition;
            if (lassoPoints.Count == 0 || Vector2.Distance(lassoPoints[lassoPoints.Count - 1], point) >= 3f)
                lassoPoints.Add(point);
        }

        if (isLassoing && Input.GetMouseButtonUp(0))
        {
            FinishLasso();
            isLassoing = false;
        }
    }

    public static bool IsSelected(BallDrag ball) =>
        Instance != null && ball != null && Instance.selectedBalls.Contains(ball);

    public static bool TryMoveSelection(BallDrag grabbedBall, Vector2 worldDelta)
    {
        if (Instance == null || grabbedBall == null || !Instance.selectedBalls.Contains(grabbedBall))
            return false;

        foreach (BallDrag ball in Instance.selectedBalls)
        {
            if (ball != null && ball != grabbedBall) ball.MoveByGroupDelta(worldDelta);
        }
        return true;
    }

    public static void DeselectAll()
    {
        if (Instance != null)
        {
            Instance.ClearSelection();
            Instance.isLassoing = false;
            Instance.lassoPoints.Clear();
        }
    }

    private void FinishLasso()
    {
        ClearSelection();
        if (lassoPoints.Count < 3) return;

        var setup = DualCameraSetup.Instance;
        if (setup == null) return;

        // Lasso operates in whichever viewport the mouse is in at completion.
        bool mouseInHand = Input.mousePosition.y < Screen.height * setup.HandFraction;
        Camera camera = mouseInHand ? setup.HandCamera : setup.BoardCamera;

        int mask = mouseInHand
            ? LayerMask.GetMask("HandPieces")
            : LayerMask.GetMask("BoardPieces");

        // Only get balls in this world.
        var relevantBalls = new List<BallDrag>();
        foreach (var drag in FindObjectsByType<BallDrag>(FindObjectsSortMode.None))
        {
            if (drag == null) continue;
            if (((1 << drag.gameObject.layer) & mask) == 0) continue;
            relevantBalls.Add(drag);
        }

        foreach (var ball in relevantBalls)
        {
            Vector3 screen = camera.WorldToScreenPoint(ball.transform.position);
            if (PointInsidePolygon(new Vector2(screen.x, screen.y), lassoPoints))
            {
                selectedBalls.Add(ball);
                CreateOutline(ball);
            }
        }
    }

    private static bool PointInsidePolygon(Vector2 point, List<Vector2> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[j];
            bool crosses = ((a.y > point.y) != (b.y > point.y)) &&
                (point.x < (b.x - a.x) * (point.y - a.y) /
                ((b.y - a.y) == 0f ? 0.0001f : b.y - a.y) + a.x);
            if (crosses) inside = !inside;
        }
        return inside;
    }

    private void CreateOutline(BallDrag ball)
    {
        GameObject outlineObject = new GameObject("Selection Outline");
        outlineObject.transform.SetParent(ball.transform, false);
        outlineObject.transform.localPosition = Vector3.zero;

        LineRenderer line = outlineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop          = true;
        line.positionCount = 48;
        line.startWidth    = selectionOutlineWidth;
        line.endWidth      = selectionOutlineWidth;
        line.material      = new Material(Shader.Find("Sprites/Default"));
        line.startColor    = selectionColor;
        line.endColor      = selectionColor;
        line.sortingOrder  = 20;

        // Crescent-aware radius pick.
        float radius = 0.5f;
        var dop = ball.GetComponent<DirectionalOperator>();
        if (dop != null && dop.IsActive)
        {
            var poly = ball.GetComponent<PolygonCollider2D>();
            if (poly != null && poly.enabled)
                radius = Mathf.Max(poly.bounds.extents.x, poly.bounds.extents.y);
        }
        else
        {
            var circle = ball.GetComponent<CircleCollider2D>();
            if (circle != null && circle.enabled)
                radius = circle.radius;
        }
        radius += selectionOutlineWidth * 1.5f;

        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i * Mathf.PI * 2f / line.positionCount;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, -0.05f));
        }
        selectionOutlines[ball] = line;
    }

    private void ClearSelection()
    {
        foreach (LineRenderer outline in selectionOutlines.Values)
            if (outline != null) Destroy(outline.gameObject);
        selectionOutlines.Clear();
        selectedBalls.Clear();
    }

    private void OnGUI()
    {
        if (!isLassoing || lassoPoints.Count < 2) return;
        Color oldColor = GUI.color;
        GUI.color = selectionColor;
        for (int i = 1; i < lassoPoints.Count; i++) DrawScreenLine(lassoPoints[i - 1], lassoPoints[i], 2f);
        DrawScreenLine(lassoPoints[lassoPoints.Count - 1], lassoPoints[0], 2f);
        GUI.color = oldColor;
    }

    private void DrawScreenLine(Vector2 a, Vector2 b, float width)
    {
        a.y = Screen.height - a.y;
        b.y = Screen.height - b.y;
        Vector2 delta = b - a;
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        GUIUtility.RotateAroundPivot(angle, a);
        GUI.DrawTexture(new Rect(a.x, a.y - width / 2f, delta.magnitude, width), lassoPixel);
        GUIUtility.RotateAroundPivot(-angle, a);
    }

    private void OnDestroy()
    {
        ClearSelection();
        if (lassoPixel != null) Destroy(lassoPixel);
        if (Instance == this) Instance = null;
    }

    public static List<BallDrag> GetSelectedBalls()
    {
        if (Instance == null)
            return new List<BallDrag>();

        return new List<BallDrag>(Instance.selectedBalls);
    }


}
