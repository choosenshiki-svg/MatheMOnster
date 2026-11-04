using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class Spawner : MonoBehaviour
{
    private const int TendrilPointCount = 7;
    private const float MaxBallRadius = 2f;

    public GameObject ballPrefab;
    public GameObject directionalOperatorPrefab;
    public EquationListDisplay equationListDisplay;

    private readonly List<GameObject> boardBalls = new List<GameObject>();
    private readonly List<GameObject> handBalls  = new List<GameObject>();
        private int _lastStructuralVersion = -1;
    private List<MathEquationBuilder.CachedCluster> _boardClusters = new();
    private List<MathEquationBuilder.CachedCluster> _handClusters  = new();
    private MathEquationBuilder.BuildResult _boardCache;
    private MathEquationBuilder.BuildResult _handCache;

    private readonly List<LineRenderer> boardTendrils = new List<LineRenderer>();
    private readonly List<LineRenderer> handTendrils  = new List<LineRenderer>();

    private Material tendrilMaterial;
    private RandomBallSummoner randomBallSummoner;
    private TestMenuUI testMenuUI;

    [Header("Connection Lines")]
    [SerializeField] private float tendrilWidth = 0.03f;
    [SerializeField] private Color tendrilColor = new Color(1f, 1f, 1f, 0.9f);
    [Tooltip("Extra sorting order above the ball sprite's order. Higher = drawn in front.")]
    [SerializeField] private int tendrilSortingOrderOffset = 10;
    [Tooltip("Leave empty to auto-match the balls' sorting layer.")]
    [SerializeField] private string tendrilSortingLayer = "";

        [System.Serializable]
    public class StartingHandBall
    {
        public string content = "";
        [Tooltip("Horizontal offset from the hand-viewport center, in viewport units (-0.5 to 0.5).")]
        public float xOffset;
    }

    [Header("Starting Hand")]
    [SerializeField] private StartingHandBall[] startingHandBalls = new StartingHandBall[]
    {
        new StartingHandBall { content = "", xOffset = -0.16f },
        new StartingHandBall { content = "", xOffset = -0.08f },
        new StartingHandBall { content = "", xOffset =  0.00f },
        new StartingHandBall { content = "", xOffset =  0.08f },
        new StartingHandBall { content = "", xOffset =  0.16f },
    };


    void Awake()
    {
        testMenuUI = GetComponent<TestMenuUI>();
        randomBallSummoner = GetComponent<RandomBallSummoner>();
        var shader = Shader.Find("Sprites/Default");
        if (shader == null) { Debug.LogError("Missing Sprites/Default shader.", this); return; }
        tendrilMaterial = new Material(shader);
    }

    void Start()
    {
        // Offline: spawn the starting hand locally.
        var nm = Unity.Netcode.NetworkManager.Singleton;
        bool online = nm != null && nm.IsListening;
        if (!online)
            SpawnStartingLocalHand();
    }

             void Update()
    {
        HandleMouseInput();

        if (BallDrag.isDragging)
            MathEquationBuilder.MarkConnectionsChanged();

        int structural = MathEquationBuilder.CurrentStructuralVersion;
        bool connectionChanged = MathEquationBuilder.ConsumeConnectionDirty();
        bool contentChanged    = MathEquationBuilder.ConsumeContentDirty();

        bool repartition =
            structural != _lastStructuralVersion || connectionChanged;

        if (repartition)
        {
            _lastStructuralVersion = structural;
            _boardClusters = Repartition(_boardClusters, boardBalls);
            _handClusters  = Repartition(_handClusters,  handBalls);
        }

        if (repartition || contentChanged)
        {
            _boardCache = SolveDirtyClusters(_boardClusters, "board");
            _handCache  = SolveDirtyClusters(_handClusters,  "hand");
        }

        var boardMask = LayerMask.GetMask("BoardPieces");
        var handMask  = LayerMask.GetMask("HandPieces");

        if (_boardCache != null)
            RefreshTendrils(_boardCache.connectedPairs, boardTendrils, boardMask);
        if (_handCache != null)
            RefreshTendrils(_handCache.connectedPairs,  handTendrils,  handMask);
    }

    void OnDestroy()
    {
        if (tendrilMaterial != null) Destroy(tendrilMaterial);
    }

    // ============================================================
    // Camera routing
    // ============================================================

    private Camera GetCameraForScreenPoint(Vector2 screenPos)
    {
        var setup = DualCameraSetup.Instance;
        if (setup == null) return Camera.main;
        bool isHand = screenPos.y < Screen.height * setup.HandFraction;
        return isHand ? setup.HandCamera : setup.BoardCamera;
    }

    // ============================================================
    // Spawn — board (networked)
    // ============================================================

    public void SpawnBall()
    {
        var setup = DualCameraSetup.Instance;
        Camera cam = setup != null ? setup.HandCamera : Camera.main;
        if (cam == null) return;

        Vector3 p = cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, 0f));
        p.z = 0f;

        // Small random offset so repeated presses don't stack pieces.
        float radius = 0.6f * (cam.orthographicSize / 5f);   // scale offset with zoom
        Vector2 dir = Random.insideUnitCircle.normalized;
        p.x += dir.x * Random.Range(0.2f, 1f) * radius;
        p.y += dir.y * Random.Range(0.2f, 1f) * radius;

        SpawnHandBallAt(p);
    }


    public void SpawnBallAtPublic(Vector3 position, string content, float rotationZ = 0f)
{
    SpawnBallAt(position, content, rotationZ);
}

private void SpawnBallAt(Vector3 position, string initialContent = null, float rotationZ = 0f)
{
    MathEquationBuilder.MarkBallSpawned();
    

    bool netActive = Unity.Netcode.NetworkManager.Singleton != null &&
                     Unity.Netcode.NetworkManager.Singleton.IsListening;

    if (netActive)
    {
        if (Unity.Netcode.NetworkManager.Singleton.IsServer &&
            NetworkBallManager.Instance != null)
            NetworkBallManager.Instance.SpawnBoardBall(position, initialContent, rotationZ);
        return;
    }

    // Offline board piece
    GameObject prefab = GetPrefabForContent(initialContent);
    if (prefab == null) return;
    var go = Instantiate(prefab, position, Quaternion.Euler(0f, 0f, rotationZ));
    var netObj = go.GetComponent<Unity.Netcode.NetworkObject>();
    if (netObj != null) Destroy(netObj);
    var networkBall = go.GetComponent<NetworkBall>();
    if (networkBall != null) Destroy(networkBall);
    var ballNumber = go.GetComponent<BallNumber>();
    if (ballNumber == null) { Destroy(go); return; }
    ballNumber.SetInputMode(BallInputMode.DisplayKey);
    if (!string.IsNullOrEmpty(initialContent))
    {
        ballNumber.ApplyTextInput(initialContent, false);
        ballNumber.RefreshDisplay();
    }
    int layer = LayerMask.NameToLayer("BoardPieces");
    if (layer >= 0) ApplyLayerRecursive(go.transform, layer);
    boardBalls.Add(go);
}

    public void SpawnBallWithContent(Vector3 position, string content)
        => SpawnBallAt(position, content);

    private GameObject GetPrefabForContent(string content)
    {
        if (DirectionalOperator.IsDirectionalOperatorText(content == null ? string.Empty : content.Trim()))
        {
            if (directionalOperatorPrefab == null)
            {
                Debug.LogError("directionalOperatorPrefab not assigned.", this);
                return null;
            }

            return directionalOperatorPrefab;
        }

        if (ballPrefab == null)
            Debug.LogError("ballPrefab not assigned.", this);
        return ballPrefab;
    }

    // ============================================================
    // Spawn — hand (local only)
    // ============================================================

    public void SpawnHandBallAt(Vector3 handWorldPos, string initialContent = null)
    {
        SpawnLocalHandBall(handWorldPos, initialContent, 0, '\0', 0);
    }

    public void SpawnLocalHandBagBall(Vector3 handWorldPos, string content, float rotationZ = 0f)
    {
        GameObject prefab = GetPrefabForContent(content);
        if (prefab == null) return;

        var go = Instantiate(prefab, handWorldPos, Quaternion.Euler(0f, 0f, rotationZ));

        // Strip networking.
        var netObj = go.GetComponent<Unity.Netcode.NetworkObject>();
        if (netObj != null) Destroy(netObj);
        var networkBall = go.GetComponent<NetworkBall>();
        if (networkBall != null) Destroy(networkBall);

        // Mark as a hand piece and a bag ball.
        go.AddComponent<LocalHandBall>().Initialize(GetLocalClientId());
        go.AddComponent<BagBallMarker>();
        go.AddComponent<BagRing>();

        var ballNumber = go.GetComponent<BallNumber>();
        if (ballNumber == null) { Destroy(go); return; }

        ballNumber.SetInputMode(BallInputMode.DisplayKey);
        if (!string.IsNullOrEmpty(content))
        {
            ballNumber.ApplyTextInput(content, false);
            ballNumber.RefreshDisplay();
        }

                int layer = LayerMask.NameToLayer("HandPieces");
        if (layer >= 0) ApplyLayerRecursive(go.transform, layer);

                handBalls.Add(go);
        MathEquationBuilder.MarkBallSpawned();
        
    }

    public void SpawnLocalHandBall(Vector3 handWorldPos, string content,
        int number = 0, char variable = '\0', int power = 0, float rotationZ = 0f)
    {
        GameObject prefab = GetPrefabForContent(content);
        if (prefab == null) return;

        // Hand pieces must NOT be networked. Strip NetworkObject and NetworkBall.
        var go = Instantiate(prefab, handWorldPos, Quaternion.Euler(0f, 0f, rotationZ));
        var netObj = go.GetComponent<Unity.Netcode.NetworkObject>();
        if (netObj != null) Destroy(netObj);
        var networkBall = go.GetComponent<NetworkBall>();
        if (networkBall != null) Destroy(networkBall);

        // Mark as a local hand piece.
        go.AddComponent<LocalHandBall>().Initialize(GetLocalClientId());

        var ballNumber = go.GetComponent<BallNumber>();
        if (ballNumber == null) { Destroy(go); return; }

        ballNumber.SetInputMode(BallInputMode.DisplayKey);
        if (!string.IsNullOrEmpty(content))
        {
            ballNumber.ApplyTextInput(content, false);
            ballNumber.RefreshDisplay();
        }
        if (number != 0 || variable != '\0' || power != 0)
        {
            ballNumber.SetAlgebraContentRaw(number, variable, power);
            ballNumber.RefreshDisplay();
        }

                int layer = LayerMask.NameToLayer("HandPieces");
        if (layer >= 0) ApplyLayerRecursive(go.transform, layer);

                handBalls.Add(go);
        MathEquationBuilder.MarkBallSpawned();
        
    }

    private ulong GetLocalClientId()
    {
        var nm = Unity.Netcode.NetworkManager.Singleton;
        return (nm != null && nm.IsListening) ? nm.LocalClientId : 0;
    }

   

    // ============================================================
    // Mouse input
    // ============================================================

    private void HandleMouseInput()
    {
        if (TestMenuUI.ShouldBlockSpawnerInput) return;

        Camera cam = GetCameraForScreenPoint(Input.mousePosition);
        if (cam == null) return;

        Vector3 mousePos = Input.mousePosition;
        mousePos.z = Mathf.Abs(cam.transform.position.z);
        Vector3 worldPosition = cam.ScreenToWorldPoint(mousePos);
        worldPosition.z = 0f;

        bool rightClickPressed = Input.GetMouseButtonDown(1);
        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (rightClickPressed && shiftHeld)
        {
            var menuSetup = DualCameraSetup.Instance;
            bool menuOverHand = menuSetup != null &&
                                Input.mousePosition.y < Screen.height * menuSetup.HandFraction;

            if (testMenuUI != null) testMenuUI.OpenAt(worldPosition, menuOverHand);
            else if (randomBallSummoner != null) randomBallSummoner.SpawnRandomBallsAt(worldPosition);
            return;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        var setup = DualCameraSetup.Instance;
        bool overHand = setup != null &&
                        Input.mousePosition.y < Screen.height * setup.HandFraction;

        if (rightClickPressed && GetBallAtPosition(worldPosition, overHand) == null)
        {
            if (overHand) SpawnHandBallAt(worldPosition);
            else          SpawnBallAt(worldPosition);
        }

        if (Input.GetKeyDown(KeyCode.Delete))
        {
            if (LassoSelection.GetSelectedBalls().Count > 0) return;
            BallNumber ball = GetBallAtPosition(worldPosition, overHand);
            if (ball != null) TryDeleteBall(ball);
        }
    }

    private void TryDeleteBall(BallNumber ball)
    {
        if (ball == null) return;

        // Skip bag balls — they cannot be deleted by normal means.
        if (ball.GetComponent<BagBallMarker>() != null) return;
        var nbCheck = ball.GetComponent<NetworkBall>();
        if (nbCheck != null && nbCheck.IsBagBall.Value) return;

        var go = ball.gameObject;
        bool netActive = Unity.Netcode.NetworkManager.Singleton != null &&
                        Unity.Netcode.NetworkManager.Singleton.IsListening;

        var nb = go.GetComponent<NetworkBall>();
        if (netActive && nb != null && nb.IsSpawned)
        {
            nb.RequestDespawnServerRpc();
            return;
        }

        if (handBalls.Remove(go))  Destroy(go);
        else if (boardBalls.Remove(go)) Destroy(go);
    }

    private BallNumber GetBallAtPosition(Vector3 position, bool handWorld)
    {
        int mask = handWorld
            ? LayerMask.GetMask("HandPieces")
            : LayerMask.GetMask("BoardPieces");

        Collider2D[] colliders = Physics2D.OverlapPointAll(position, mask);   // ← masked
        foreach (Collider2D collider in colliders)
        {
            BallNumber ball = collider.GetComponentInParent<BallNumber>();
            if (ball != null && (boardBalls.Contains(ball.gameObject) || handBalls.Contains(ball.gameObject)))
                return ball;
        }
        return null;
    }

    public void SpawnGrayBall() => SpawnBall();
    public void SpawnKeyBall()  => SpawnBall();

    public void DeleteAllBalls()
    {
        foreach (var go in new List<GameObject>(handBalls))
        {
            if (go == null) continue;
            if (go.GetComponent<BagBallMarker>() != null) continue;   // ← skip bag balls
            Destroy(go);
        }

                // Keep bag balls in the list; rebuild without them.
        handBalls.RemoveAll(go => go == null || go.GetComponent<BagBallMarker>() == null);
        MathEquationBuilder.MarkBallDespawned();
        
    }

    public void DeleteAllBoardBalls()
    {
        var snapshot = new List<GameObject>(boardBalls);
        bool isServer = Unity.Netcode.NetworkManager.Singleton != null &&
                        Unity.Netcode.NetworkManager.Singleton.IsServer;
        bool netActive = Unity.Netcode.NetworkManager.Singleton != null &&
                        Unity.Netcode.NetworkManager.Singleton.IsListening;

                foreach (var go in snapshot)
        {
            if (go == null) continue;

            var nb = go.GetComponent<NetworkBall>();
            if (nb != null && nb.IsSpawned && nb.IsBagBall.Value) continue;   // skip networked bag balls
            if (go.GetComponent<BagBallMarker>() != null) continue;           // skip offline bag balls

            if (netActive && nb != null && nb.IsSpawned)
            {
                if (isServer) nb.RequestDespawnServerRpc();
            }
            else
            {
                boardBalls.Remove(go);
                Destroy(go);
            }
        }

                if (!netActive) boardBalls.RemoveAll(go => go == null);
        MathEquationBuilder.MarkBallDespawned();
        
    }

    // ============================================================
    // Solver — runs separately per world (board, hand)
    // ============================================================

    private List<BallNumber> GetActiveBalls(List<GameObject> source)
    {
        var list = new List<BallNumber>();
        foreach (var go in source)
        {
            if (go == null) continue;
            var b = go.GetComponent<BallNumber>();
            if (b != null) list.Add(b);
        }
        return list;
    }

        private List<MathEquationBuilder.CachedCluster> Repartition(
        List<MathEquationBuilder.CachedCluster> oldClusters,
        List<GameObject> source)
    {
        var active = GetActiveBalls(source);
        var fresh = MathEquationBuilder.PartitionClusters(active);

        var result = new List<MathEquationBuilder.CachedCluster>(fresh.Count);
        foreach (var balls in fresh)
        {
            MathEquationBuilder.CachedCluster reused = null;
            foreach (var old in oldClusters)
            {
                if (old.balls == null) continue;
                if (SameBallSet(old.balls, balls)) { reused = old; break; }
            }
            if (reused != null) result.Add(reused);
            else result.Add(new MathEquationBuilder.CachedCluster { balls = balls });
        }
        return result;
    }

    private static bool SameBallSet(List<BallNumber> a, List<BallNumber> b)
    {
        if (a.Count != b.Count) return false;
        var set = new HashSet<BallNumber>(a);
        for (int i = 0; i < b.Count; i++)
            if (!set.Contains(b[i])) return false;
        return true;
    }

    private MathEquationBuilder.BuildResult SolveDirtyClusters(
        List<MathEquationBuilder.CachedCluster> clusters,
        string context)
    {
        var aggregate = new MathEquationBuilder.BuildResult();

        foreach (var c in clusters)
        {
            long sum = 0;
            foreach (var b in c.balls) if (b != null) sum += b.ChangeVersion;
            bool dirty = c.result == null || c.contentVersionAtLastSolve != sum;
            if (dirty)
            {
                c.result = MathEquationBuilder.SolveCluster(c.balls);
                c.contentVersionAtLastSolve = sum;
            }
            aggregate.equations.AddRange(c.result.equations);
            aggregate.allAmbiguousBalls.UnionWith(c.result.allAmbiguousBalls);
            aggregate.connectedPairs.AddRange(c.result.connectedPairs);
            aggregate.filledBalls.UnionWith(c.result.filledBalls);
        }

        var definitions = MathEquationBuilder.BuildVariableDefinitions(aggregate.equations);

        foreach (var c in clusters)
            foreach (var b in c.balls)
            {
                if (b == null) continue;
                b.SetEquationState(0);   // reset to neutral
                b.SetAmbiguousOperand(false);
            }
        foreach (var b in aggregate.allAmbiguousBalls) b.SetAmbiguousOperand(true);

                // Fills already happened inside TryBuildConnectedEquation via
        // FillAndColorCluster; aggregate.filledBalls lists every ball whose
        // solver output was written this pass. Clear stale solver outputs for
        // every other ball in the cluster.
        foreach (var c in clusters)
            foreach (var b in c.balls)
                if (b != null && b.IsSolverOutput && !b.IsLocked &&
                    !aggregate.filledBalls.Contains(b))
                    b.ClearSolverOutput();

        foreach (var eq in aggregate.equations)
        {
            foreach (var b in eq.ambiguousBalls) b.SetAmbiguousOperand(true);
            eq.equalsBall?.SetEquationState(eq.equationState);
        }

        if (context == "board" && equationListDisplay != null)
            equationListDisplay.Refresh(aggregate.equations);

        if (context == "board" &&
            Unity.Netcode.NetworkManager.Singleton != null &&
            Unity.Netcode.NetworkManager.Singleton.IsServer)
        {
            foreach (var nb in FindObjectsByType<NetworkBall>(FindObjectsSortMode.None))
            {
                if (nb == null || !nb.IsSpawned) continue;
                if (nb.CurrentZone.Value == (byte)BallZone.ServerBoard) nb.PushImmediate();
            }
        }

        return aggregate;
    }


    // ============================================================
    // Tendrils (per-context pools)
    // ============================================================

        private void RefreshTendrils(List<(BallNumber a, BallNumber b)> pairs,
        List<LineRenderer> pool, int layerMask)
    {
        int layer = layerMask == LayerMask.GetMask("HandPieces")
            ? LayerMask.NameToLayer("HandPieces")
            : LayerMask.NameToLayer("BoardPieces");

        string sortingLayer = "Default";
        int sortingOrder = tendrilSortingOrderOffset;

        int activeCount = 0;
        foreach (var (ballA, ballB) in pairs)
        {
            if (ballA == null || ballB == null) continue;

            Collider2D ci = ballA.GetComponent<Collider2D>();
            Collider2D cj = ballB.GetComponent<Collider2D>();
            if (ci == null || cj == null) continue;

            if (activeCount == 0)
            {
                var sr = ballA.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    sortingLayer = string.IsNullOrEmpty(tendrilSortingLayer)
                        ? sr.sortingLayerName
                        : tendrilSortingLayer;
                    sortingOrder = sr.sortingOrder + tendrilSortingOrderOffset;
                }
            }

            var tendril = GetOrCreateTendril(
                pool, activeCount++, layer, sortingLayer, sortingOrder);

            // Endpoints: closest point on each collider to the other's
            // center. Correct for circles and triangles alike.
            Vector2 centerA = ci.bounds.center;
            Vector2 centerB = cj.bounds.center;

            Vector3 start = ci.ClosestPoint(centerB);
            Vector3 end   = cj.ClosestPoint(centerA);

            // Small pull-back so the line doesn't poke out of the ball.
            Vector2 dir = (Vector2)(end - start);
            float len = dir.magnitude;
            if (len > 0.0001f)
            {
                Vector2 dirN = dir / len;
                const float inset = 0.02f;
                if (len > inset * 2f)
                {
                    start += (Vector3)(dirN * inset);
                    end   -= (Vector3)(dirN * inset);
                }
            }

            SetTendrilPoints(tendril, start, end, ci.GetInstanceID() ^ cj.GetInstanceID());
        }

        for (int i = activeCount; i < pool.Count; i++) pool[i].enabled = false;
    }
    


    private LineRenderer GetOrCreateTendril(List<LineRenderer> pool, int index, int layer,
    string sortingLayer, int sortingOrder)
    {
        LineRenderer lr;
        if (index < pool.Count)
        {
            lr = pool[index];
            lr.enabled = true;
        }
        else
        {
            var go = new GameObject("Ball Connection");
            go.transform.SetParent(transform, false);
            lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = TendrilPointCount;
            lr.sharedMaterial = tendrilMaterial;
            lr.numCapVertices = 3;
            pool.Add(lr);
        }

        lr.startWidth = tendrilWidth;
        lr.endWidth = tendrilWidth;
        lr.startColor = tendrilColor;
        lr.endColor = tendrilColor;
        lr.sortingLayerName = sortingLayer;
        lr.sortingOrder = sortingOrder;
        if (layer >= 0) lr.gameObject.layer = layer;
        return lr;
    }

    private void SetTendrilPoints(LineRenderer tendril, Vector3 start, Vector3 end, int seed)
    {
        for (int i = 0; i < TendrilPointCount; i++)
        {
            float t = i / (TendrilPointCount - 1f);
            Vector3 p = Vector3.Lerp(start, end, t);
            p.z = -0.05f;
            tendril.SetPosition(i, p);
        }
    }

    // ============================================================
    // Registration
    // ============================================================

        public void RegisterBall(GameObject go)
    {
        if (go == null) return;
        if (boardBalls.Contains(go) || handBalls.Contains(go)) return;
        // Networked board piece → boardBalls.
        if (go.GetComponent<NetworkBall>() != null) boardBalls.Add(go);
        else handBalls.Add(go);
        MathEquationBuilder.MarkBallSpawned();
        
    }

        public void UnregisterBall(GameObject go)
    {
        if (go == null) return;
        boardBalls.Remove(go);
        handBalls.Remove(go);
        MathEquationBuilder.MarkBallDespawned();
       
    }

    private static void ApplyLayerRecursive(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform child in t) ApplyLayerRecursive(child, layer);
    }

    // ============================================================
    // Offline starting hand
    // ============================================================

        private void SpawnStartingLocalHand()
    {
        var setup = DualCameraSetup.Instance;
        if (setup == null) return;

        if (startingHandBalls == null) return;

        foreach (var entry in startingHandBalls)
        {
            if (entry == null) continue;

            Vector3 world = setup.HandCamera.ViewportToWorldPoint(
                new Vector3(0.5f + entry.xOffset, 0.5f, 10f));
            world.z = 0f;
            SpawnLocalHandBall(world, entry.content ?? "");
        }
    }

        public void DestroyHandBall(GameObject handPiece)
    {
        if (handPiece == null) return;
        handBalls.Remove(handPiece);
        Destroy(handPiece);
        MathEquationBuilder.MarkBallDespawned();
       
    }

        public void DestroyBoardBall(GameObject boardPiece)
    {
        if (boardPiece == null) return;
        boardBalls.Remove(boardPiece);
        Destroy(boardPiece);
        MathEquationBuilder.MarkBallDespawned();
        
    }

    public List<GameObject> GetAllBoardPieces()
        {
            return new List<GameObject>(boardBalls);
        }

    public void OnBagButtonClicked()
    {
        if (BagManager.Instance == null) return;

        BagManager.Instance.RequestDraw(content =>
        {
            if (string.IsNullOrEmpty(content)) return;

            var setup = DualCameraSetup.Instance;
            Camera cam = setup != null ? setup.HandCamera : Camera.main;
            if (cam == null) return;

            Vector3 p = cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, 0f));
            p.z = 0f;

            float radius = 0.6f * (cam.orthographicSize / 5f);
            Vector2 dir = Random.insideUnitCircle.normalized;
            p.x += dir.x * Random.Range(0.2f, 1f) * radius;
            p.y += dir.y * Random.Range(0.2f, 1f) * radius;

            SpawnLocalHandBagBall(p, content);
        });
    }

    public int GetHandBagBallCount()
    {
        int count = 0;
        foreach (var go in handBalls)
        {
            if (go == null) continue;
            if (go.GetComponent<BagBallMarker>() != null) count++;
        }
        return count;
    }

        public void DeleteAllHandBagBalls()
    {
        var toDelete = new List<GameObject>();
        foreach (var go in handBalls)
        {
            if (go == null) continue;
            if (go.GetComponent<BagBallMarker>() != null)
                toDelete.Add(go);
        }
        foreach (var go in toDelete)
        {
            handBalls.Remove(go);
            Destroy(go);
        }
        if (toDelete.Count > 0) MathEquationBuilder.MarkBallDespawned();
    }

        // ============================================================
    // Prefab-swap helpers (used by BallPrefabSwapper)
    // ============================================================

    public GameObject SpawnLocalHandOperator(Vector3 handWorldPos, string content, float rotationZ = 0f)
    {
        if (directionalOperatorPrefab == null)
        {
            Debug.LogError("Spawner: directionalOperatorPrefab not assigned.", this);
            return null;
        }

        var go = Instantiate(directionalOperatorPrefab, handWorldPos,
                             Quaternion.Euler(0f, 0f, rotationZ));

        var netObj = go.GetComponent<Unity.Netcode.NetworkObject>();
        if (netObj != null) Destroy(netObj);
        var networkBall = go.GetComponent<NetworkBall>();
        if (networkBall != null) Destroy(networkBall);

        go.AddComponent<LocalHandBall>().Initialize(GetLocalClientId());

        var ballNumber = go.GetComponent<BallNumber>();
        if (ballNumber == null) { Destroy(go); return null; }
        ballNumber.SetInputMode(BallInputMode.DisplayKey);
        if (!string.IsNullOrEmpty(content))
        {
            ballNumber.ApplyTextInput(content, false);
            ballNumber.RefreshDisplay();
        }

        int layer = LayerMask.NameToLayer("HandPieces");
        if (layer >= 0) ApplyLayerRecursive(go.transform, layer);

        handBalls.Add(go);
        MathEquationBuilder.MarkBallSpawned();
        return go;
    }

    public GameObject SpawnLocalHandBallReturning(
        Vector3 handWorldPos, string content,
        int number, char variable, int power, float rotationZ = 0f)
    {
        // Same as SpawnLocalHandBall but returns the new GameObject so the
        // caller can restore state (solver output, lock, bag flag).
        GameObject prefab = GetPrefabForContent(content);
        if (prefab == null) return null;

        var go = Instantiate(prefab, handWorldPos, Quaternion.Euler(0f, 0f, rotationZ));

        var netObj = go.GetComponent<Unity.Netcode.NetworkObject>();
        if (netObj != null) Destroy(netObj);
        var networkBall = go.GetComponent<NetworkBall>();
        if (networkBall != null) Destroy(networkBall);

        go.AddComponent<LocalHandBall>().Initialize(GetLocalClientId());

        var ballNumber = go.GetComponent<BallNumber>();
        if (ballNumber == null) { Destroy(go); return null; }
        ballNumber.SetInputMode(BallInputMode.DisplayKey);
        if (!string.IsNullOrEmpty(content))
        {
            ballNumber.ApplyTextInput(content, false);
            ballNumber.RefreshDisplay();
        }
        if (number != 0 || variable != '\0' || power != 0)
        {
            ballNumber.SetAlgebraContentRaw(number, variable, power);
            ballNumber.RefreshDisplay();
        }

        int layer = LayerMask.NameToLayer("HandPieces");
        if (layer >= 0) ApplyLayerRecursive(go.transform, layer);

        handBalls.Add(go);
        MathEquationBuilder.MarkBallSpawned();
        return go;
    }

    public GameObject SpawnLocalBoardOperator(Vector3 position, string content, float rotationZ = 0f)
    {
        if (directionalOperatorPrefab == null)
        {
            Debug.LogError("Spawner: directionalOperatorPrefab not assigned.", this);
            return null;
        }

        var go = Instantiate(directionalOperatorPrefab, position,
                             Quaternion.Euler(0f, 0f, rotationZ));

        var netObj = go.GetComponent<Unity.Netcode.NetworkObject>();
        if (netObj != null) Destroy(netObj);
        var networkBall = go.GetComponent<NetworkBall>();
        if (networkBall != null) Destroy(networkBall);

        var ballNumber = go.GetComponent<BallNumber>();
        if (ballNumber == null) { Destroy(go); return null; }
        ballNumber.SetInputMode(BallInputMode.DisplayKey);
        if (!string.IsNullOrEmpty(content))
        {
            ballNumber.ApplyTextInput(content, false);
            ballNumber.RefreshDisplay();
        }

        int layer = LayerMask.NameToLayer("BoardPieces");
        if (layer >= 0) ApplyLayerRecursive(go.transform, layer);

        boardBalls.Add(go);
        MathEquationBuilder.MarkBallSpawned();
        return go;
    }

    public GameObject SpawnBallAtPublicReturning(Vector3 position, string content, float rotationZ = 0f)
    {
        // Same as SpawnBallAtPublic but returns the new GameObject.
        // Uses GetPrefabForContent, so it picks the right prefab.
        SpawnBallAt(position, content, rotationZ);

        // The most recently added entry in boardBalls is the one we just made.
        if (boardBalls.Count == 0) return null;
        return boardBalls[boardBalls.Count - 1];
    }

}