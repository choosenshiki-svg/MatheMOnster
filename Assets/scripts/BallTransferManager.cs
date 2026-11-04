using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Handles a piece being dragged across the gray bar into the other world.
/// While pending, the source ball is deleted and a fake visual follows the cursor.
/// On release, spawns the real ball in whichever world the release happened in,
/// or snaps back to origin if released outside both viewports.
/// </summary>
public class BallTransferManager : MonoBehaviour
{
    public static BallTransferManager Instance { get; private set; }

    ///THIS IS WHERE I LEFT OFF I WAS SUPPOSED TO CHANGE THIS
    private struct PendingTransfer
    {
        public bool Active;
        public bool ContentReady;
        public bool ReleaseRequested;
        public bool CancelRequested;
        public BallZone SourceZone;
        public BallZone TargetZone;
        public Vector3 SourceWorldPos;
        public Vector3 ReleaseWorldPos;
        public string DisplayedKey;
        public int Number;
        public char Variable;
        public int Power;
        public byte InputMode;
        public bool IsSolverOutput;
        public bool IsLocked;
        public bool IsBagBall;
        public float RotationZ;
    }

    private PendingTransfer pending;
    private GameObject fakeVisual;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public bool IsTransferInProgress => pending.Active;
    public bool IsFakeVisualActive => fakeVisual != null;

    // ==========================================================
    // Called by BallDrag when a hand piece crosses the divider.
    // ==========================================================
    public void StartTransferFromHand(GameObject handPiece, Vector3 sourceWorldPos)
    {
        if (pending.Active) return;
        var ballNumber = handPiece.GetComponent<BallNumber>();
        if (ballNumber == null) return;

        bool isBag = handPiece.GetComponent<BagBallMarker>() != null;

        // Capture content locally.
        pending.Active = true;
        pending.ContentReady = true;
        pending.ReleaseRequested = false;
        pending.SourceZone = BallZone.Hand;
        pending.SourceWorldPos = sourceWorldPos;
        pending.RotationZ = handPiece.transform.eulerAngles.z;
        pending.DisplayedKey = ballNumber.GetRawDisplayedKey();
        pending.Number = ballNumber.NumberValue;
        pending.Variable = ballNumber.Variable;
        pending.Power = ballNumber.VariablePower;
        pending.InputMode = (byte)ballNumber.InputMode;
        pending.IsSolverOutput = ballNumber.IsSolverOutput;
        pending.IsLocked = ballNumber.IsLocked;
        pending.IsBagBall = isBag;

        // Delete the local hand piece now.
        var spawner = FindFirstObjectByType<Spawner>();
        if (spawner != null) spawner.DestroyHandBall(handPiece);

        // Create the fake visual for the pending piece.
        CreateFakeVisual(pending);
    }

    // ==========================================================
    // Called by BallDrag when a board piece crosses the divider.
    // ==========================================================
    public void StartTransferFromBoard(NetworkBall networkBall, Vector3 sourceWorldPos)
    {
        if (pending.Active) return;
        if (networkBall == null || !networkBall.IsSpawned) return;

        pending.Active = true;
        pending.ContentReady = false;
        pending.ReleaseRequested = false;
        pending.SourceZone = BallZone.ServerBoard;
        pending.SourceWorldPos = sourceWorldPos;
        pending.IsBagBall = networkBall.IsBagBall.Value;
        pending.RotationZ = networkBall.Ball.transform.eulerAngles.z;

        // Ask server to despawn the board piece and send us its content.
        networkBall.RequestBoardToHandServerRpc();
    }

    // ==========================================================
    // Called by NetworkBallManager.ClientRpc when board content arrives.
    // ==========================================================
    public void ReceiveBoardContentFromServer(
    string displayedKey, int number, char variable, int power,
    byte inputMode, bool isSolverOutput, bool isLocked, bool isBagBall, float rotationZ)
    {
        if (!pending.Active || pending.SourceZone != BallZone.ServerBoard) return;

        pending.ContentReady = true;
        pending.DisplayedKey = displayedKey;
        pending.Number = number;
        pending.Variable = variable;
        pending.Power = power;
        pending.InputMode = inputMode;
        pending.IsSolverOutput = isSolverOutput;
        pending.IsLocked = isLocked;
        pending.IsBagBall = isBagBall;   
        pending.RotationZ = rotationZ;

        if (pending.CancelRequested)
        {
            RespawnSource();
            Clear();
            return;
        }

        CreateFakeVisual(pending);

        if (pending.ReleaseRequested)
            ExecuteSpawn();
    }

    // ==========================================================
    // Per-frame: fake visual follows mouse; detect mouse release.
    // ==========================================================
    private void Update()
    {
        if (!pending.Active) return;

        var setup = DualCameraSetup.Instance;
        if (setup == null) return;

        // Update fake visual to follow the mouse in the target world's coords.
        bool mouseInHand = Input.mousePosition.y < Screen.height * setup.HandFraction;
        Camera targetCam = mouseInHand ? setup.HandCamera : setup.BoardCamera;

       

        if (fakeVisual != null)
        {
            Vector3 mouseWorld = targetCam.ScreenToWorldPoint(Input.mousePosition);
            mouseWorld.z = 0f;
            fakeVisual.transform.position = mouseWorld;

            int targetLayer = (targetCam == setup.HandCamera)
                ? LayerMask.NameToLayer("HandPieces")
                : LayerMask.NameToLayer("BoardPieces");
            ApplyLayerRecursive(fakeVisual.transform, targetLayer);
        }

        // Detect release.
        if (Input.GetMouseButtonUp(0))
        {
            // NEW: universal bag-ball return — released over bag button.
            if (pending.IsBagBall &&
                BagButton.Instance != null &&
                BagButton.Instance.IsMouseOver())
            {
                if (BagManager.Instance != null)
                    BagManager.Instance.RequestReturnToBag(pending.DisplayedKey);

                Clear();
                return;
            }

            if (!IsInAnyViewport(setup))
            {
                Cancel();
                return;
            }

            pending.ReleaseRequested = true;
            pending.TargetZone = mouseInHand ? BallZone.Hand : BallZone.ServerBoard;

            Vector3 releaseWorld = targetCam.ScreenToWorldPoint(Input.mousePosition);
            releaseWorld.z = 0f;
            pending.ReleaseWorldPos = releaseWorld;

            if (pending.ContentReady)
                ExecuteSpawn();
        }
    }




    private bool IsInAnyViewport(DualCameraSetup setup)
    {
        // Mouse is considered "in a viewport" if it's on-screen (both viewports
        // together cover the full vertical range in the current layout).
        var m = Input.mousePosition;
        return m.x >= 0 && m.x <= Screen.width && m.y >= 0 && m.y <= Screen.height;
    }

    // ==========================================================
    // Spawn the real ball in the target world with the saved content.
    // ==========================================================
    
    private void ExecuteSpawn()
    {
        var spawner = FindFirstObjectByType<Spawner>();
        if (spawner == null) { Cancel(); return; }

        

        Vector3 pos = pending.ReleaseWorldPos;
        var targetZone = pending.TargetZone;

        if (targetZone == BallZone.Hand)
        {
            if (pending.IsBagBall)
                spawner.SpawnLocalHandBagBall(pos, pending.DisplayedKey, pending.RotationZ);
            else
                spawner.SpawnLocalHandBall(
                    pos, pending.DisplayedKey,
                    pending.Number, pending.Variable, pending.Power, pending.RotationZ);
        }
        else // board
        {
            bool online = Unity.Netcode.NetworkManager.Singleton != null &&
                        Unity.Netcode.NetworkManager.Singleton.IsListening;
            bool isServer = Unity.Netcode.NetworkManager.Singleton != null &&
                            Unity.Netcode.NetworkManager.Singleton.IsServer;

            if (online && isServer && NetworkBallManager.Instance != null)
            {
                if (pending.IsBagBall)
                    NetworkBallManager.Instance.SpawnBoardBagBall(pos, pending.DisplayedKey, pending.RotationZ);
                else
                    NetworkBallManager.Instance.SpawnBoardBall(pos, pending.DisplayedKey, pending.RotationZ);
            }
            else if (online && !isServer && NetworkBallManager.Instance != null)
            {
                if (pending.IsBagBall)
                    NetworkBallManager.Instance.RequestSpawnBoardBagBallServerRpc(
                        pos, pending.DisplayedKey, pending.RotationZ);
                else
                    NetworkBallManager.Instance.RequestSpawnBoardBallServerRpc(
                        pos, pending.DisplayedKey, pending.RotationZ);
            }
            else
            {
                // Offline: spawn locally. For bag balls we need to mark them.
                if (pending.IsBagBall)
                    SpawnOfflineBoardBagBall(spawner, pos, pending.DisplayedKey, pending.RotationZ);
                else
                    spawner.SpawnBallAtPublic(pos, pending.DisplayedKey, pending.RotationZ);
            }
        }

        Clear();
    }

    // ==========================================================
    // Cancel: put the source ball back at its original position.
    // ==========================================================
    private void Cancel()
    {
        // If content hasn't arrived yet (board→hand mid-flight), defer.
        if (!pending.ContentReady)
        {
            pending.CancelRequested = true;
            return;
        }

        RespawnSource();
        Clear();
    }

    private void RespawnSource()
    {
        var spawner = FindFirstObjectByType<Spawner>();
        if (spawner == null) return;

        if (pending.SourceZone == BallZone.Hand)
        {
            if (pending.IsBagBall)
                spawner.SpawnLocalHandBagBall(
                    pending.SourceWorldPos, pending.DisplayedKey, pending.RotationZ);
            else
                spawner.SpawnLocalHandBall(
                    pending.SourceWorldPos,
                    pending.DisplayedKey,
                    pending.Number, pending.Variable, pending.Power, pending.RotationZ);
        }
        else
        {
            bool online = Unity.Netcode.NetworkManager.Singleton != null &&
                        Unity.Netcode.NetworkManager.Singleton.IsListening;
            bool isServer = Unity.Netcode.NetworkManager.Singleton != null &&
                            Unity.Netcode.NetworkManager.Singleton.IsServer;

            if (online && isServer && NetworkBallManager.Instance != null)
            {
                if (pending.IsBagBall)
                    NetworkBallManager.Instance.SpawnBoardBagBall(
                        pending.SourceWorldPos, pending.DisplayedKey, pending.RotationZ);
                else
                    NetworkBallManager.Instance.SpawnBoardBall(
                        pending.SourceWorldPos, pending.DisplayedKey, pending.RotationZ);
            }
            else if (online && !isServer && NetworkBallManager.Instance != null)
            {
                if (pending.IsBagBall)
                    NetworkBallManager.Instance.RequestSpawnBoardBagBallServerRpc(
                        pending.SourceWorldPos, pending.DisplayedKey, pending.RotationZ);
                else
                    NetworkBallManager.Instance.RequestSpawnBoardBallServerRpc(
                        pending.SourceWorldPos, pending.DisplayedKey, pending.RotationZ);
            }
            else
            {
                if (pending.IsBagBall)
                    SpawnOfflineBoardBagBall(
                        spawner, pending.SourceWorldPos, pending.DisplayedKey, pending.RotationZ);
                else
                    spawner.SpawnBallAtPublic(
                        pending.SourceWorldPos, pending.DisplayedKey, pending.RotationZ);
            }
        }
    }

    private void Clear()
    {
        pending = default;
        if (fakeVisual != null)
        {
            Destroy(fakeVisual);
            fakeVisual = null;
        }
    }

    // ==========================================================
    // Fake visual
    // ==========================================================
    
    private void CreateFakeVisual(PendingTransfer p)
    {
        if (fakeVisual != null) return;

        var spawner = FindFirstObjectByType<Spawner>();
        if (spawner == null) return;

        bool isDirectional = DirectionalOperator.IsDirectionalOperatorText(
            (p.DisplayedKey ?? string.Empty).Trim());
        GameObject prefab = isDirectional
            ? spawner.directionalOperatorPrefab
            : spawner.ballPrefab;
        if (prefab == null) return;

        fakeVisual = Instantiate(prefab, p.SourceWorldPos, Quaternion.identity);
        fakeVisual.transform.rotation = Quaternion.Euler(0f, 0f, p.RotationZ);
        fakeVisual.name = "FakePendingBallVisual";

        DestroyComponent<NetworkBall>(fakeVisual);
        DestroyComponent<Unity.Netcode.NetworkObject>(fakeVisual);
        DestroyComponent<LocalHandBall>(fakeVisual);
        DestroyComponent<BallDrag>(fakeVisual);
        DestroyComponent<Rigidbody2D>(fakeVisual);
        DestroyComponent<BallInputController>(fakeVisual);

        int defaultLayer = LayerMask.NameToLayer("Default");
        if (defaultLayer >= 0) ApplyLayerRecursive(fakeVisual.transform, defaultLayer);

        var ballNumber = fakeVisual.GetComponent<BallNumber>();
        if (ballNumber != null)
        {
            ballNumber.SetInputModeRaw((BallInputMode)p.InputMode);
            ballNumber.SetAlgebraContentRaw(p.Number, p.Variable, p.Power);
            ballNumber.SetRawDisplayedKey(p.DisplayedKey, p.IsSolverOutput);
            ballNumber.SetLockedRaw(p.IsLocked);
            ballNumber.RefreshDisplay();
        }

        if (isDirectional)
        {
            var dop = fakeVisual.GetComponent<DirectionalOperator>();
            if (dop != null)
                dop.Refresh((p.DisplayedKey ?? string.Empty).Trim());
            fakeVisual.transform.rotation = Quaternion.Euler(0f, 0f, p.RotationZ);
        }

        foreach (Collider2D c in fakeVisual.GetComponents<Collider2D>())
            c.enabled = false;

        if (p.IsBagBall)
        {
            fakeVisual.AddComponent<BagBallMarker>();
            fakeVisual.AddComponent<BagRing>();
        }
    }

    private static void DestroyComponent<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c != null) Destroy(c);
    }

    private static void ApplyLayerRecursive(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform child in t) ApplyLayerRecursive(child, layer);
    }

    public void StartTransferFromLocalBoard(GameObject boardPiece, Vector3 sourceWorldPos)
    {
        if (pending.Active) return;
        var ballNumber = boardPiece.GetComponent<BallNumber>();
        if (ballNumber == null) return;

        bool isBag = boardPiece.GetComponent<BagBallMarker>() != null;

        pending.Active = true;
        pending.ContentReady = true;
        pending.ReleaseRequested = false;
        pending.SourceZone = BallZone.ServerBoard;
        pending.SourceWorldPos = sourceWorldPos;
        pending.RotationZ = boardPiece.transform.eulerAngles.z;
        pending.DisplayedKey = ballNumber.GetRawDisplayedKey();
        pending.Number = ballNumber.NumberValue;
        pending.Variable = ballNumber.Variable;
        pending.Power = ballNumber.VariablePower;
        pending.InputMode = (byte)ballNumber.InputMode;
        pending.IsSolverOutput = ballNumber.IsSolverOutput;
        pending.IsLocked = ballNumber.IsLocked;
        pending.IsBagBall = isBag;

        // Remove the board piece locally.
        var spawner = FindFirstObjectByType<Spawner>();
        if (spawner != null) spawner.DestroyBoardBall(boardPiece);

        CreateFakeVisual(pending);
    }

    private static void SpawnOfflineBoardBagBall(
        Spawner spawner, Vector3 pos, string content, float rotationZ)
    {
        // Offline board pieces are local but still on the BoardPieces layer and
        // still participate in board physics. Reuse SpawnBallAtPublic then mark
        // the new piece as a bag ball.
        spawner.SpawnBallAtPublic(pos, content, rotationZ);

        // The most recently spawned board piece is the one we just created.
        // Mark it as a bag ball. We reach into the Spawner to get the last one.
        var last = spawner.GetAllBoardPieces();
        if (last == null || last.Count == 0) return;
        var newest = last[last.Count - 1];
        if (newest == null) return;

        if (newest.GetComponent<BagBallMarker>() == null)
            newest.AddComponent<BagBallMarker>();
        if (newest.GetComponent<BagRing>() == null)
            newest.AddComponent<BagRing>();
    }
}