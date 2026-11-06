using UnityEngine;
using Unity.Netcode;

public class BallDrag : MonoBehaviour
{
    public static bool isDragging;
    public float releaseVelocityMultiplier = 0.2f;
    public float velocityDamping = 4f;
    public float minimumVelocity = 0.04f;
    private const float MaximumReleaseSpeed = 1.8f;

    private Vector3 offset;
    private Vector2 previousMousePosition;
    private Rigidbody2D body;
    private Vector2 dragVelocity;
    private bool isBeingDragged;
    private bool draggingGroup;
    private DirectionalOperator directionalOperator;
    private bool useKinematicOperatorDrag;
    private bool hasPendingOperatorPosition;
    private Vector2 pendingOperatorPosition;

    private NetworkBall networkBall;

    private bool _waitingForGrab;
    private bool _dragAllowed;

    private Vector2 _lastConnCheckPos;
    private const float ConnectionCheckDistance = 0.05f;

    

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        networkBall = GetComponent<NetworkBall>();
        directionalOperator = GetComponent<DirectionalOperator>();
    }

    private bool IsHandPiece()
    {
        int handLayer = LayerMask.NameToLayer("HandPieces");
        return handLayer >= 0 && gameObject.layer == handLayer;
    }

    private Camera ActiveCamera()
    {
        var setup = DualCameraSetup.Instance;
        if (setup == null) return Camera.main;
        return IsHandPiece() ? setup.HandCamera : setup.BoardCamera;
    }

    // ==================================================================
    // Grab result callback (invoked from NetworkBall.GrabResultClientRpc)
    // ==================================================================

    public static void OnGrabResult(NetworkBall nb, bool acquired)
    {
        if (nb == null) return;
        var drag = nb.GetComponent<BallDrag>();
        if (drag == null) return;

        drag._waitingForGrab = false;

        if (!acquired)
        {
            drag._dragAllowed = false;
            drag.isBeingDragged = false;
            BallDrag.isDragging = false;
            return;
        }

        drag._dragAllowed = true;
        drag.isBeingDragged = true;
        BallDrag.isDragging = true;
        drag.body.linearVelocity = Vector2.zero;
        drag.dragVelocity = Vector2.zero;
        drag.draggingGroup = LassoSelection.IsSelected(drag);
        drag.BeginDirectionalOperatorDrag();

        var cam = drag.ActiveCamera();
        if (cam == null) return;
        Vector3 mp = cam.ScreenToWorldPoint(Input.mousePosition);
        mp.z = 0f;
        drag.offset = drag.transform.position - mp;
        drag.previousMousePosition = mp;
    }

    // ==================================================================
    // Mouse
    // ==================================================================

    private void OnMouseDown()
    {
        if (!Input.GetMouseButtonDown(0) || LassoSelection.IsLassoActive ||
            Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return;

        bool online = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        // Hand piece: drag immediately.
        if (IsHandPiece())
        {
            BeginLocalDrag();
            return;
        }

        // Online board piece: request grab, wait for confirmation.
        if (online && networkBall != null && networkBall.IsSpawned)
        {
            _waitingForGrab = true;
            _dragAllowed = false;
            isBeingDragged = false;
            isDragging = true;   // block other input while we wait
            networkBall.RequestGrabServerRpc();
            return;
        }

        // Offline board piece: drag immediately.
        BeginLocalDrag();
    }

    private void BeginLocalDrag()
    {
        if (directionalOperator == null)
            directionalOperator = GetComponent<DirectionalOperator>();

        isDragging = true;
        isBeingDragged = true;
        _dragAllowed = true;
        _waitingForGrab = false;
        draggingGroup = LassoSelection.IsSelected(this);
        body.linearVelocity = Vector2.zero;
        dragVelocity = Vector2.zero;
        BeginDirectionalOperatorDrag();

        var cam = ActiveCamera();
        if (cam == null) return;
        Vector3 mp = cam.ScreenToWorldPoint(Input.mousePosition);
        mp.z = 0f;
        offset = transform.position - mp;
        previousMousePosition = mp;
    }

    private void OnMouseDrag()
    {
        if (_waitingForGrab) return;
        if (!_dragAllowed) return;

        if (!isBeingDragged || !Input.GetMouseButton(0) || LassoSelection.IsLassoActive) return;

        var setup = DualCameraSetup.Instance;
        if (setup == null) return;

        bool mouseInHand = Input.mousePosition.y < Screen.height * setup.HandFraction;

        bool crossed = false;
        if (IsHandPiece() && !mouseInHand) crossed = true;
        else if (!IsHandPiece() && mouseInHand) crossed = true;

        if (crossed)
        {
            CompleteDirectionalOperatorDrag();
            isBeingDragged = false;
            isDragging = false;
            body.linearVelocity = Vector2.zero;

            // Release any board lock before transferring.
            if (!IsHandPiece() && networkBall != null && networkBall.IsSpawned && _dragAllowed)
                networkBall.RequestReleaseServerRpc();

            _dragAllowed = false;

            bool online = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

            if (IsHandPiece())
            {
                BallTransferManager.Instance.StartTransferFromHand(gameObject, transform.position);
            }
            else if (online && networkBall != null && networkBall.IsSpawned)
            {
                BallTransferManager.Instance.StartTransferFromBoard(networkBall, transform.position);
            }
            else
            {
                BallTransferManager.Instance.StartTransferFromLocalBoard(gameObject, transform.position);
            }

            return;
        }

        var cam = ActiveCamera();
        if (cam == null) return;

        Vector3 mousePosition = cam.ScreenToWorldPoint(Input.mousePosition);
        mousePosition.z = 0f;
        Vector3 nextPosition = mousePosition + offset;

        Vector2 mouseDelta = (Vector2)mousePosition - previousMousePosition;
        float deltaTime = Mathf.Max(Time.deltaTime, Mathf.Epsilon);
        if (mouseDelta.sqrMagnitude > 0f) dragVelocity = mouseDelta / deltaTime;
        previousMousePosition = mousePosition;
        Vector2 movement = (Vector2)(nextPosition - transform.position);

                if (directionalOperator != null && directionalOperator.IsActive)
        {
            directionalOperator.ApplyRotateDrag(mouseDelta);

                        if (useKinematicOperatorDrag)
            {
                float achievedAngle = directionalOperator.CurrentAngle;

                Vector2 finalPos;
                if (directionalOperator.PinGrabPointToCursor)
                {
                    // Pin: keep the grabbed point under the cursor. The center
                    // shifts to compensate for rotation this frame.
                    finalPos = directionalOperator.CenterForMouseToKeepGrabPoint(
                        mousePosition, achievedAngle);
                }
                else
                {
                    // Free: translate the center by the raw mouse delta only.
                    // The triangle can drift relative to the cursor while it
                    // rotates, which removes the pin feedback entirely.
                    finalPos = (Vector2)transform.position + mouseDelta;
                }

                body.position      = finalPos;
                transform.position = finalPos;
                body.rotation      = achievedAngle;
                GetComponent<RotationWatcher>()?.NoteSource("BallDragPin");
                transform.rotation = Quaternion.Euler(0f, 0f, achievedAngle);

                body.angularVelocity = 0f;

                if (networkBall != null && networkBall.IsSpawned &&
                    networkBall.CurrentZone.Value == (byte)BallZone.ServerBoard)
                {
                    networkBall.RequestBoardMoveServerRpc(finalPos, achievedAngle);
                }
            }
        }
        else if (networkBall != null && networkBall.IsSpawned)
        {
            body.position = nextPosition;
            body.transform.position = nextPosition;

            if (networkBall.CurrentZone.Value == (byte)BallZone.ServerBoard)
                networkBall.RequestBoardMoveServerRpc(nextPosition, transform.eulerAngles.z);
        }
        else
        {
            body.position = nextPosition;
            body.transform.position = nextPosition;
        }

        if (!useKinematicOperatorDrag)
            body.linearVelocity = Vector2.zero;
        if (draggingGroup) LassoSelection.TryMoveSelection(this, movement);
    }

    private void OnMouseUp()
    {
        
        // If we released over the bag button, return the bag ball to the bag.
        if (IsBagBall() && BagButton.Instance != null && BagButton.Instance.IsMouseOver())
        {
            CompleteDirectionalOperatorDrag();
            ReturnBagBallToPool();
            return;
        }

        // Release the lock if we held one.
        if (!IsHandPiece() && networkBall != null && networkBall.IsSpawned && _dragAllowed)
            networkBall.RequestReleaseServerRpc();

        _dragAllowed = false;
        _waitingForGrab = false;

        CompleteDirectionalOperatorDrag();
        if (!isBeingDragged) return;

        isDragging = false;
        isBeingDragged = false;
        draggingGroup = false;

        if (!IsHandPiece())
        {
            float speed = Mathf.Min(
                dragVelocity.magnitude * Mathf.Max(0f, releaseVelocityMultiplier),
                MaximumReleaseSpeed);
            if (speed > 0.02f)
            {
                body.WakeUp();
                body.linearVelocity = dragVelocity.normalized * speed;
            }
        }
    }

    private bool IsBagBall()
    {
        if (GetComponent<BagBallMarker>() != null) return true;
        return networkBall != null && networkBall.IsBagBall.Value;
    }

    private void ReturnBagBallToPool()
    {
        string content = GetComponent<BallNumber>()?.GetRawDisplayedKey();
        if (string.IsNullOrEmpty(content)) return;

        if (IsHandPiece())
        {
            var spawner = FindFirstObjectByType<Spawner>();
            if (spawner != null) spawner.DestroyHandBall(gameObject);

            if (BagManager.Instance != null)
                BagManager.Instance.RequestReturnToBag(content);
        }
        else if (networkBall != null && networkBall.IsSpawned)
        {
            // Server RPC despawns + increments pool.
            if (NetworkBallManager.Instance != null)
                NetworkBallManager.Instance.RequestBagReturnFromBoardServerRpc(
                    networkBall.NetworkObject, content);
        }
        else
        {
            // Offline board bag ball: destroy + return locally.
            var spawner = FindFirstObjectByType<Spawner>();
            if (spawner != null) spawner.DestroyBoardBall(gameObject);

            if (BagManager.Instance != null)
                BagManager.Instance.RequestReturnToBag(content);
        }

        _dragAllowed = false;
        _waitingForGrab = false;
        isDragging = false;
        isBeingDragged = false;
    }

    // ==================================================================
    // Misc
    // ==================================================================

    public void MoveByGroupDelta(Vector2 delta)
    {
        if (body == null) return;
        body.position += delta;
        body.linearVelocity = Vector2.zero;
    }

    public static void CancelActiveDragForLasso() => isDragging = false;

    private void FixedUpdate()
    {
        if (isBeingDragged || body.linearVelocity.sqrMagnitude == 0f) return;
        body.linearVelocity *= Mathf.Exp(-Mathf.Max(0f, velocityDamping) * Time.fixedDeltaTime);
        float stopSpeed = Mathf.Max(0f, minimumVelocity);
        if (body.linearVelocity.sqrMagnitude < stopSpeed * stopSpeed) body.linearVelocity = Vector2.zero;
    }

    private void CompleteDirectionalOperatorDrag()
    {
        if (!useKinematicOperatorDrag)
            return;

        directionalOperator.EndRotateDrag();
        directionalOperator.EndDrag();
        useKinematicOperatorDrag = false;
    }

    private void BeginDirectionalOperatorDrag()
    {
        useKinematicOperatorDrag =
            directionalOperator != null && directionalOperator.IsActive;
        if (!useKinematicOperatorDrag) return;

        directionalOperator.BeginDrag();

        // Record where the player grabbed in world space so the triangle can
        // rotate around that point as the mouse moves.
        var cam = ActiveCamera();
        if (cam != null)
        {
            Vector3 mp = cam.ScreenToWorldPoint(Input.mousePosition);
            mp.z = 0f;
            directionalOperator.BeginRotateDrag(mp);
        }
    }
}