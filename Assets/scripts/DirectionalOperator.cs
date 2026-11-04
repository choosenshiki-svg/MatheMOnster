using UnityEngine;

/// <summary>
/// Triangle-shaped operator for '-' (subtract), '/' (divide), '^' (power).
///
///     ┌───────────────┐
///     │   flat side   │
///     │    (input)    │
///     │               │
///     └──────────────►│   tip (output)
///
/// The flat side is the input operand ("outside"). The tip is the output
/// operand ("inside"). Rotation is driven explicitly by whoever is dragging.
///
/// This component is logic only. The triangle sprite and polygon collider
/// come from the prefab. Geometry (tip and base positions) is read from the
/// polygon collider once at Awake, so resizing the prefab's triangle is
/// enough — no code changes needed.
/// </summary>
[DisallowMultipleComponent]
public sealed class DirectionalOperator : MonoBehaviour
{
    [Header("Cone tolerances")]
    [Tooltip("Half-angle (degrees) of the input cone (flat side).")]
    [SerializeField] private float inputConeHalfAngle = 75f;

    [Tooltip("Half-angle (degrees) of the output cone (tip side).")]
    [SerializeField] private float outputConeHalfAngle = 75f;

    [Tooltip("Extra reach beyond the operand's own radius.")]
    [SerializeField] private float capturePadding = 0.10f;

    private BallNumber        ball;
    private Rigidbody2D       body;
    private CircleCollider2D  circleCollider;
    private PolygonCollider2D polygonCollider;

    private Vector2 tipLocal;
    private Vector2 baseMidLocal;

    private bool            originalFreezeRotation;
    private RigidbodyType2D originalBodyType;
    private Vector3         originalLabelPosition;
    private Quaternion      originalLabelRotation;
    private Transform       labelTransform;

    private bool  initialized;
    private bool  isBeingDragged;
    private float currentTipAngle;

    private SpriteRenderer inputPortMarker;
    private SpriteRenderer outputPortMarker;
    private static Sprite _dotSprite;

    [SerializeField] private float tipTurnSpeedDegPerSec = 720f;
    private float smoothedTipAngle;

    [Header("Rotate-drag (grab point leads)")]
    [Tooltip("Max turn speed (deg/sec) when aligning the grab point to the mouse movement direction.")]
    [SerializeField] private float grabTurnSpeedDegPerSec = 360f;

    private Vector2 _grabLocal;
    private bool    _hasGrabLocal;
    private float   _grabLocalAngleDeg;

    public bool IsActive { get; private set; }

    public Vector2 TipDirection =>
        new(Mathf.Cos(currentTipAngle * Mathf.Deg2Rad),
            Mathf.Sin(currentTipAngle * Mathf.Deg2Rad));

    // ==================================================================

    private void Awake()
    {
        ball            = GetComponent<BallNumber>();
        body            = GetComponent<Rigidbody2D>();
        circleCollider  = GetComponent<CircleCollider2D>();
        polygonCollider = GetComponent<PolygonCollider2D>();

                if (body != null)
        {
            originalFreezeRotation = body.freezeRotation;
                    originalBodyType = body.bodyType;
        }

        ReadGeometryFromCollider();
        smoothedTipAngle = currentTipAngle;

        CreatePortMarkers();

        if (polygonCollider != null)
            polygonCollider.enabled = false;

        initialized = true;
    }

    private void Start()
    {
        CacheLabelTransform();
        if (ball != null)
            Refresh(ball.GetMathContent().Trim());
    }

    private void Update()
    {
        if (!IsActive) return;

        if (inputPortMarker == null || outputPortMarker == null) return;

        bool inputMatched  = false;
        bool outputMatched = false;

        int mask = gameObject.layer == LayerMask.NameToLayer("HandPieces")
            ? LayerMask.GetMask("HandPieces")
            : LayerMask.GetMask("BoardPieces");

        // Scan a small radius around the triangle's centroid.
        var hits = Physics2D.OverlapCircleAll(transform.position, 3f, mask);
        foreach (var h in hits)
        {
            if (h == null) continue;
            if (h.gameObject == gameObject) continue;

            // Ignore other directional operators (they never connect to us).
            var otherOp = h.GetComponent<DirectionalOperator>();
            if (otherOp != null && otherOp.IsActive) continue;

            if (!TryGetOperandSide(h, out bool isInside)) continue;
            if (isInside) outputMatched = true;
            else          inputMatched  = true;

            if (inputMatched && outputMatched) break;
        }

               inputPortMarker.color  = inputMatched
            ? new Color(1f, 0.25f, 0.25f, 1f)   // bright red = matched
            : new Color(1f, 0.25f, 0.25f, 0.5f);// faded red  = idle
        outputPortMarker.color = outputMatched
            ? new Color(1f, 0.25f, 0.25f, 1f)
            : new Color(1f, 0.25f, 0.25f, 0.5f);
    }

    private void LateUpdate()
    {
        if (IsActive && labelTransform != null)
            labelTransform.rotation = Quaternion.identity;
    }

    private void CacheLabelTransform()
    {
        if (labelTransform != null) return;
        Transform label = transform.Find("Ball Number");
        if (label != null)
        {
            labelTransform        = label;
            originalLabelPosition = label.localPosition;
            originalLabelRotation = label.localRotation;
        }
    }

        private void ReadGeometryFromCollider()
    {
        tipLocal     = new Vector2(0f,  0.577f);
        baseMidLocal = new Vector2(0f, -0.289f);

        if (polygonCollider == null || polygonCollider.pathCount == 0) return;
        Vector2[] path = polygonCollider.GetPath(0);
        if (path == null || path.Length < 3) return;

        // The built-in Triangle sprite is drawn with its tip pointing +Y
        // in local space. The visual tip is the vertex with the maximum
        // local Y; the base is the midpoint of the other two vertices.
        int tipIndex = 0;
        for (int i = 1; i < path.Length; i++)
            if (path[i].y > path[tipIndex].y) tipIndex = i;

        tipLocal = path[tipIndex];

        Vector2 sumOthers = Vector2.zero;
        int others = 0;
        for (int i = 0; i < path.Length; i++)
        {
            if (i == tipIndex) continue;
            sumOthers += path[i];
            others++;
        }
        if (others > 0) baseMidLocal = sumOthers / others;
    }

    // ==================================================================

    public static bool IsDirectionalOperatorText(string content)
        => content == "-" || content == "/" || content == "^";

    public static bool IsDirectionalOperator(BallNumber b)
        => b != null && IsDirectionalOperatorText(b.GetMathContent().Trim());

    public void Refresh(string content)
    {
        if (!initialized) return;

        CacheLabelTransform();
        bool shouldBeActive = IsDirectionalOperatorText(content);
        if (IsActive == shouldBeActive) return;

        IsActive = shouldBeActive;

        if (circleCollider != null)
            circleCollider.enabled = !IsActive;

        if (polygonCollider != null)
            polygonCollider.enabled = IsActive;

        if (body != null)
        {
            body.freezeRotation = !isBeingDragged && (IsActive || originalFreezeRotation);
            if (IsActive) body.angularVelocity = 0f;
        }

        if (labelTransform != null)
        {
            labelTransform.localPosition = originalLabelPosition;
            if (!IsActive) labelTransform.localRotation = originalLabelRotation;
        }

        MathEquationBuilder.MarkConnectionsChanged();
    }

    public void SetTipDirection(Vector2 worldDirection)
    {
        if (!IsActive) return;
        if (worldDirection.sqrMagnitude < 1e-6f) return;

        // The triangle's visual tip lies along local +Y. A rotation of θ
        // maps local +Y to (-sin θ, cos θ), so we solve for the target
        // angle that makes that point along worldDirection.
        float targetAngle = Mathf.Atan2(-worldDirection.x, worldDirection.y) * Mathf.Rad2Deg;

        // Ease the current angle toward the target, respecting max turn speed.
        float delta   = Mathf.DeltaAngle(smoothedTipAngle, targetAngle);
        float maxStep = tipTurnSpeedDegPerSec * Time.unscaledDeltaTime;
        if (Mathf.Abs(delta) <= maxStep)
            smoothedTipAngle = targetAngle;
        else
            smoothedTipAngle += Mathf.Sign(delta) * maxStep;

        smoothedTipAngle = Mathf.Repeat(smoothedTipAngle + 180f, 360f) - 180f;
        currentTipAngle  = smoothedTipAngle;

        if (body != null) body.MoveRotation(currentTipAngle);
        else              transform.rotation = Quaternion.Euler(0f, 0f, currentTipAngle);
    }

    public void BeginDrag()
    {
        if (!IsActive || body == null || isBeingDragged) return;

        isBeingDragged  = true;
        body.freezeRotation = false;   // allow rotation during drag
        body.linearVelocity  = Vector2.zero;
        body.angularVelocity = 0f;
    }

    public void EndDrag()
    {
        if (!isBeingDragged || body == null) return;

        isBeingDragged = false;
        body.freezeRotation = IsActive || originalFreezeRotation;   // restore
        body.linearVelocity  = Vector2.zero;
        body.angularVelocity = 0f;
        MathEquationBuilder.MarkConnectionsChanged();
    }

   public void BeginRotateDrag(Vector2 grabWorldPos)
    {
        if (!IsActive) return;

        _grabLocal    = transform.InverseTransformPoint(grabWorldPos);
        _hasGrabLocal = true;

        if (_grabLocal.sqrMagnitude < 1e-6f)
        {
            // Grabbed dead center — no defined "leading" direction.
            _grabLocalAngleDeg = 0f;
            return;
        }

        _grabLocalAngleDeg = Mathf.Atan2(_grabLocal.y, _grabLocal.x) * Mathf.Rad2Deg;
    }

    public void EndRotateDrag()
    {
        _hasGrabLocal = false;
    }

    /// <summary>
    /// Aligns the grab point with the mouse movement direction. The grabbed
    /// corner leads; the rest of the triangle trails behind. Eased at
    /// grabTurnSpeedDegPerSec so it doesn't snap on sudden direction changes.
    /// </summary>
    public void ApplyRotateDrag(Vector2 mouseWorldDelta)
    {
        if (!IsActive || !_hasGrabLocal) return;
        if (_grabLocal.sqrMagnitude < 1e-6f) return;      // grabbed center: no leading corner
        if (mouseWorldDelta.sqrMagnitude < 1e-8f) return; // no motion: hold current angle

        // Angle of mouse motion in world space.
        float moveAngleDeg = Mathf.Atan2(mouseWorldDelta.y, mouseWorldDelta.x) * Mathf.Rad2Deg;

        // Target triangle rotation so the grab point points along the motion.
        float targetRotation = moveAngleDeg - _grabLocalAngleDeg;
        targetRotation       = Mathf.Repeat(targetRotation + 180f, 360f) - 180f;

        // Ease currentTipAngle toward targetRotation at max turn speed.
        float deltaAngle = Mathf.DeltaAngle(currentTipAngle, targetRotation);
        float maxStep    = grabTurnSpeedDegPerSec * Time.deltaTime;
        if (Mathf.Abs(deltaAngle) <= maxStep)
            currentTipAngle = targetRotation;
        else
            currentTipAngle += Mathf.Sign(deltaAngle) * maxStep;

        currentTipAngle  = Mathf.Repeat(currentTipAngle + 180f, 360f) - 180f;
        smoothedTipAngle = currentTipAngle;

        // Teleport rotation directly — same mechanism the ball uses for position.
        Quaternion q = Quaternion.Euler(0f, 0f, currentTipAngle);
        transform.rotation = q;
        if (body != null) body.rotation = currentTipAngle;
    }

    public float CurrentTargetAngle => currentTipAngle;

    /// <summary>
    /// Drives the dynamic body toward a target position and rotation by setting
    /// velocity toward each. The physics engine handles collisions, giving the
    /// operator the same push behavior as a ball.
    /// </summary>
    public void DriveToward(Vector2 targetPosition, float targetAngleDeg)
    {
        if (!IsActive || body == null) return;

        // Position: velocity toward target, capped.
        Vector2 toTarget = targetPosition - (Vector2)transform.position;
        Vector2 desiredVel = toTarget / Mathf.Max(Time.fixedDeltaTime, 1e-4f);

        const float maxDragSpeed = 10f;
        if (desiredVel.sqrMagnitude > maxDragSpeed * maxDragSpeed)
            desiredVel = desiredVel.normalized * maxDragSpeed;

        body.linearVelocity = desiredVel;

        // Rotation: angular velocity toward target angle, capped.
        float angleErr = Mathf.DeltaAngle(transform.eulerAngles.z, targetAngleDeg);
        float desiredAngVel = angleErr / Mathf.Max(Time.fixedDeltaTime, 1e-4f);

        const float maxAngVel = 720f;
        desiredAngVel = Mathf.Clamp(desiredAngVel, -maxAngVel, maxAngVel);

        body.angularVelocity = desiredAngVel;
    }

    public void StopDriving()
    {
        if (body == null) return;
        body.linearVelocity  = Vector2.zero;
        body.angularVelocity = 0f;
    }

    /// <summary>
    /// Returns where the center must be so that the grab point sits exactly
    /// under mouseWorldPos, given the triangle's current rotation.
    /// </summary>
    public Vector2 CenterForMouseToKeepGrabPoint(Vector2 mouseWorldPos)
    {
        return CenterForMouseToKeepGrabPoint(mouseWorldPos, transform.eulerAngles.z);
    }

    public Vector2 CenterForMouseToKeepGrabPoint(Vector2 mouseWorldPos, float targetAngleDeg)
    {
        if (!_hasGrabLocal) return (Vector2)transform.position;
        Quaternion q = Quaternion.Euler(0f, 0f, targetAngleDeg);
        Vector2 grabWorldOffset = q * _grabLocal;
        return mouseWorldPos - grabWorldOffset;
    }

    public bool TryGetOperandSide(Collider2D operand, out bool isInside)
    {
        isInside = false;
        if (operand == null) return false;

        // Hard gap check: same distance balls use. If the collider edges are
        // farther apart than a ball-to-ball connection would allow, reject.
        if (polygonCollider != null)
        {
            var d = polygonCollider.Distance(operand);
            if (!d.isOverlapped && d.distance > BallConnectionRules.ConnectionGap)
                return false;
        }

        float radius = Mathf.Max(operand.bounds.extents.x, operand.bounds.extents.y);
        return TryGetOperandSide(operand.bounds.center, radius, out isInside);
    }

        public bool TryGetOperandSide(Vector2 worldPosition, float operandRadius, out bool isInside)
    {
        isInside = false;
        if (!IsActive) return false;

        Vector2 local = transform.InverseTransformPoint(worldPosition);

        // Overall reach cap in world units, matching ball-to-ball connection.
        float avgScale = (Mathf.Abs(transform.lossyScale.x) +
                          Mathf.Abs(transform.lossyScale.y)) * 0.5f;
        if (avgScale < 0.0001f) avgScale = 1f;

        float worldDistFromOrigin = Vector2.Distance(worldPosition, transform.position);
        float triangleRadiusWorld = 0.577f * avgScale;
        float maxReach            = BallConnectionRules.ConnectionGap;

        if (worldDistFromOrigin > triangleRadiusWorld + operandRadius + maxReach)
            return false;

        Vector2 axisLocal = (tipLocal - baseMidLocal).normalized;
        Vector2 perpAxis  = new(-axisLocal.y, axisLocal.x);

        // --- Output side (past the tip) ---
        Vector2 relTip     = local - tipLocal;
        float   forwardTip = Vector2.Dot(relTip, axisLocal) * avgScale;
        float   lateralTip = Mathf.Abs(Vector2.Dot(relTip, perpAxis)) * avgScale;

        if (forwardTip >= -operandRadius)
        {
            float angle = Mathf.Atan2(lateralTip, Mathf.Max(forwardTip, 0f)) * Mathf.Rad2Deg;
            float lateralCap = operandRadius + 0.10f;
            if (angle <= outputConeHalfAngle && lateralTip <= lateralCap)
            {
                isInside = true;
                return true;
            }
        }

        // --- Input side (past the base) ---
        Vector2 relBase     = local - baseMidLocal;
        float   forwardBase = Vector2.Dot(relBase, -axisLocal) * avgScale;
        float   lateralBase = Mathf.Abs(Vector2.Dot(relBase, perpAxis)) * avgScale;

        if (forwardBase >= -operandRadius)
        {
            float angle = Mathf.Atan2(lateralBase, Mathf.Max(forwardBase, 0f)) * Mathf.Rad2Deg;
            float lateralCap = operandRadius + 0.10f;
            if (angle <= inputConeHalfAngle && lateralBase <= lateralCap)
            {
                isInside = false;
                return true;
            }
        }

        return false;
    }

    public bool ConnectsTo(Collider2D other)
    {
        if (!IsActive || other == null) return false;
        if (other.GetComponent<DirectionalOperator>() is DirectionalOperator dop && dop.IsActive)
            return false;
        return TryGetOperandSide(other, out _);
    }

        private void CreatePortMarkers()
    {
        inputPortMarker  = CreateMarker("InputPortMarker",  baseMidLocal);
        outputPortMarker = CreateMarker("OutputPortMarker", tipLocal);
    }

        private SpriteRenderer CreateMarker(string childName, Vector2 localPos)
    {
        Transform existing = transform.Find(childName);
        if (existing != null)
        {
            var srExisting = existing.GetComponent<SpriteRenderer>();
            if (srExisting != null)
            {
                existing.localPosition = localPos;
                existing.localScale    = Vector3.one * 0.28f;
                return srExisting;
            }
        }

        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale    = Vector3.one * 0.28f;

                var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetDotSprite();
        sr.color  = new Color(1f, 0.25f, 0.25f, 0.5f);
        sr.sortingOrder = 2;   // just hardcode

        // Same unlit material the triangle uses. Without this, dots render
        // through Sprites-Lit-Default and come out black in scenes with no
        // 2D lights.
        var parentSr = GetComponent<SpriteRenderer>();
        if (parentSr != null && parentSr.sharedMaterial != null)
            sr.sharedMaterial = parentSr.sharedMaterial;

        return sr;
    }

    private static Sprite GetDotSprite()
    {
        if (_dotSprite != null) return _dotSprite;

        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px  = new Color32[size * size];
        float c = size * 0.5f;
        float r = size * 0.5f - 1f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - c, dy = y - c;
            float d  = Mathf.Sqrt(dx * dx + dy * dy);
            byte a   = (byte)(Mathf.Clamp01((r - d) / 2f) * 255f);
            px[y * size + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(px);
        tex.Apply();

        _dotSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f), size);
        _dotSprite.name = "PortDot";
        return _dotSprite;
    }

 
}