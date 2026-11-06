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
    [Tooltip("Half-angle (degrees) of the input cone (flat side).")]
    [Range(0f, 180f)]
    [SerializeField] private float inputConeHalfAngle = 75f;

    [Tooltip("Half-angle (degrees) of the output cone (tip side).")]
    [Range(0f, 180f)]
    [SerializeField] private float outputConeHalfAngle = 75f;

    [Tooltip("Extra reach beyond the operand's own radius.")]
    [Range(0f, 1f)]
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

        [Range(0f, 1440f)]
    [SerializeField] private float tipTurnSpeedDegPerSec = 720f;

    private float smoothedTipAngle;

                [Header("Rotate-drag (grab point leads)")]
 [Tooltip("If true, the grabbed point stays glued to the cursor (position is " +
             "pinned each frame from the rotation). If false, the triangle moves by " +
             "raw mouse delta and can drift relative to the cursor while rotating.")]
    [SerializeField] private bool pinGrabPointToCursor = true;

        [Tooltip("Width of the fade-in band below which mouse speed is damped. " +
             "At 0 the multiplier is deadZoneFadeMin; at this speed it's 1.")]
    [Range(0f, 200f)]
    [SerializeField] private float grabDeadZoneSpeed = 10f;

    [Tooltip("How much the fade-in band reduces turn rate at speed 0. " +
             "0 = fully fades to zero (old behavior), 1 = no damping. " +
             "Values between scale the rate linearly across the band.")]
    [Range(0f, 1f)]
    [SerializeField] private float deadZoneFadeMin = 0.4f;

    [Tooltip("Unused legacy field. Kept only so existing prefab data doesn't shift.")]
    [Range(0.1f, 20f)]
    [SerializeField] private float grabMinFollowRate = 1.5f;

    [Tooltip("Follow rate at the high end (1/sec). Higher = snappier tracking on fast drags.")]
    [Range(5f, 200f)]
    [SerializeField] private float grabMaxFollowRate = 30f;

    [Tooltip("Mouse speed (world units/sec) at which the follow rate reaches its maximum. " +
             "Lower = ramps up faster.")]
    [Range(1f, 1000f)]
    [SerializeField] private float grabMidpointMouseSpeed = 250f;

        private Vector2 _grabLocal;
    private bool    _hasGrabLocal;
    private float   _grabLocalAngleDeg;

    private Vector2 _dragAccum;
    private float   _dragAccumAge;
    private const float DragAccumWindow = 0.08f;

    public bool IsActive { get; private set; }
        public bool PinGrabPointToCursor => pinGrabPointToCursor;

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

        if (GetComponent<RotationWatcher>() == null)
            gameObject.AddComponent<RotationWatcher>();

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

        Debug.LogWarning($"[TIP] SetTipDirection on {name} dir={worldDirection}");

        // The triangle's visual tip lies along local +Y. A rotation of θ
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

        GetComponent<RotationWatcher>()?.NoteSource("SetTipDirection");
        if (body != null) body.MoveRotation(currentTipAngle);
        else              transform.rotation = Quaternion.Euler(0f, 0f, currentTipAngle);
    }

        public void BeginDrag()
    {
        if (!IsActive || body == null || isBeingDragged) return;

        isBeingDragged  = true;
        body.freezeRotation = false;
        body.linearVelocity  = Vector2.zero;
        body.angularVelocity = 0f;

        Debug.Log($"[BEGIN] freezeRotation={body.freezeRotation} type={body.bodyType} constraints={body.constraints}");
    }

        public void EndDrag()
    {
        if (!isBeingDragged || body == null) return;

        isBeingDragged = false;
        body.freezeRotation = IsActive || originalFreezeRotation;
        body.linearVelocity  = Vector2.zero;
        body.angularVelocity = 0f;
        MathEquationBuilder.MarkConnectionsChanged();

        Debug.Log($"[END] freezeRotation={body.freezeRotation}");
    }

      public void BeginRotateDrag(Vector2 grabWorldPos)
    {
        if (!IsActive) return;

        _grabLocal    = transform.InverseTransformPoint(grabWorldPos);
        _hasGrabLocal = true;
        _dragAccum    = Vector2.zero;
        _dragAccumAge = 0f;

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
        _dragAccum    = Vector2.zero;
        _dragAccumAge = 0f;
    }

         public void ApplyRotateDrag(Vector2 mouseWorldDelta)
    {
        if (!IsActive || !_hasGrabLocal) return;
        if (_grabLocal.sqrMagnitude < 1e-6f) return;

        // Accumulate over a short window so the *direction* is stable.
        _dragAccum    += mouseWorldDelta;
        _dragAccumAge += Time.deltaTime;
        if (_dragAccumAge < DragAccumWindow) return;

        Vector2 dirDelta = _dragAccum;
        float   dt       = Mathf.Max(_dragAccumAge, 1e-5f);
        _dragAccum    = Vector2.zero;
        _dragAccumAge = 0f;

                float mouseSpeed = dirDelta.magnitude / dt;

        // Compute the "normal" rate this speed would get from the main curve.
        // Above the band, this is what we use directly. Below the band, we
        // scale it down linearly toward zero so slow motion fades in smoothly
        // instead of hitting a hard cutoff.
        float speedFactor = Mathf.InverseLerp(0f, grabMidpointMouseSpeed, mouseSpeed);

        // Mostly linear with a slight quadratic bump: rate = x + 0.1x².
        // Clamp01 keeps the top pinned at max (x=1 gives 1.1 unclamped).
        float easedFactor = speedFactor + 0.1f * speedFactor * speedFactor;
        easedFactor = Mathf.Clamp01(easedFactor);

        float normalRate = Mathf.Lerp(0f, grabMaxFollowRate, easedFactor);

                // Fade-in band: below grabDeadZoneSpeed, multiply normalRate by a
        // linear ramp from deadZoneFadeMin (at speed 0) up to 1 (at the
        // dead-zone speed). Above the band the multiplier is 1. Setting
        // deadZoneFadeMin to 0 restores the old "fade fully to zero" feel;
        // setting it to 1 disables the band entirely.
        float fade = 1f;
        if (grabDeadZoneSpeed > 0.0001f && mouseSpeed < grabDeadZoneSpeed)
        {
            float t = mouseSpeed / grabDeadZoneSpeed;   // 0..1 across the band
            fade = Mathf.Lerp(deadZoneFadeMin, 1f, t);
        }

        float followRate = normalRate * fade;

        // ---- DIAGNOSTIC: uncomment to trace while tuning ----
        // Debug.Log($"[Rot] speed={mouseSpeed:F1} factor={speedFactor:F3} eased={easedFactor:F3} rate={followRate:F2}");

        float moveAngleDeg = Mathf.Atan2(dirDelta.y, dirDelta.x) * Mathf.Rad2Deg;
        float targetRotation = moveAngleDeg - _grabLocalAngleDeg;
        targetRotation = Mathf.Repeat(targetRotation + 180f, 360f) - 180f;

        float deltaAngle      = Mathf.DeltaAngle(currentTipAngle, targetRotation);
        float closedThisWindow = deltaAngle * (1f - Mathf.Exp(-followRate * dt));
        currentTipAngle      += closedThisWindow;

        currentTipAngle  = Mathf.Repeat(currentTipAngle + 180f, 360f) - 180f;
        smoothedTipAngle = currentTipAngle;

        Quaternion q = Quaternion.Euler(0f, 0f, currentTipAngle);
        GetComponent<RotationWatcher>()?.NoteSource("ApplyRotateDrag");
        transform.rotation = q;
        if (body != null) body.rotation = currentTipAngle;
    }
    
        /// <summary>The angle the triangle actually has after this frame's drag step.</summary>
    public float CurrentAngle => currentTipAngle;

    /// <summary>Kept for compatibility. Same value as CurrentAngle.</summary>
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