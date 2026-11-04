using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Mouse-over-viewport-aware pan and zoom. The camera under the pointer gets
/// the wheel zoom; dragging empty space pans that camera only.
/// </summary>
public class DualCameraController : MonoBehaviour
{
    [SerializeField] private float zoomSpeed = 0.8f;
    [SerializeField] private float zoomSmoothTime = 0.10f;
    [SerializeField] private float minOrthoSize = 1.5f;
    [SerializeField] private float maxOrthoSize = 30f;

    private Camera boardCam;
    private Camera handCam;

    private float boardTargetZoom;
    private float boardZoomVel;
    private float handTargetZoom;
    private float handZoomVel;

    private bool isPanning;
    private Camera panCam;
    private Vector3 lastMousePos;

    private void Start()
    {
        var setup = DualCameraSetup.Instance;
        if (setup == null)
        {
            Debug.LogError("DualCameraController requires DualCameraSetup in the scene.");
            enabled = false;
            return;
        }
        boardCam = setup.BoardCamera;
        handCam = setup.HandCamera;
        boardTargetZoom = boardCam.orthographicSize;
        handTargetZoom = handCam.orthographicSize;
    }

    private void Update()
    {
        if (boardCam == null || handCam == null) return;

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (overUI) return;

        var setup = DualCameraSetup.Instance;
        float dividerY = Screen.height * setup.HandFraction;
        bool mouseInHand = Input.mousePosition.y < dividerY;

        // ---- Zoom ----
        // ---- Zoom ----
        if (!TestMenuUI.BlockWheelForMenu)
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                if (mouseInHand)
                    handTargetZoom = Mathf.Clamp(handTargetZoom - scroll * zoomSpeed, minOrthoSize, maxOrthoSize);
                else
                    boardTargetZoom = Mathf.Clamp(boardTargetZoom - scroll * zoomSpeed, minOrthoSize, maxOrthoSize);
            }
        }

        handCam.orthographicSize = Mathf.SmoothDamp(
            handCam.orthographicSize, handTargetZoom, ref handZoomVel, zoomSmoothTime);
        boardCam.orthographicSize = Mathf.SmoothDamp(
            boardCam.orthographicSize, boardTargetZoom, ref boardZoomVel, zoomSmoothTime);

        // ---- Pan ----
        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool lassoActive = LassoSelection.IsLassoActive;

        if (Input.GetMouseButtonDown(0) && !shiftHeld && !lassoActive &&
            !BallDrag.isDragging && !IsMouseOverBall(mouseInHand))
        {
            isPanning = true;
            panCam = mouseInHand ? handCam : boardCam;
            lastMousePos = Input.mousePosition;
        }

        if (Input.GetMouseButton(0) && isPanning && panCam != null &&
            !shiftHeld && !lassoActive && !BallDrag.isDragging)
        {
            Vector3 current = Input.mousePosition;
            Vector3 delta = current - lastMousePos;
            float worldPerPixel = (panCam.orthographicSize * 2f) / (Screen.height * panCam.rect.height);
            panCam.transform.position -= delta * worldPerPixel;
            lastMousePos = current;
        }

        if (Input.GetMouseButtonUp(0))
        {
            isPanning = false;
            panCam = null;
        }
    }

    private bool IsMouseOverBall(bool mouseInHand)
    {
        Camera cam = mouseInHand ? handCam : boardCam;
        if (cam == null) return false;

        Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
        world.z = 0f;

        int mask = mouseInHand
            ? LayerMask.GetMask("HandPieces")
            : LayerMask.GetMask("BoardPieces");

        return Physics2D.OverlapPoint(world, mask) != null;
    }
}