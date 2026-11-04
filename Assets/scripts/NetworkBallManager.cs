using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkBallManager : NetworkBehaviour
{
    public static NetworkBallManager Instance { get; private set; }

    [Header("Prefab")]
    [SerializeField] private GameObject ballPrefab;
    [SerializeField] private GameObject directionalOperatorPrefab;

    [Header("Board world-space")]
    [SerializeField] private Vector2 boardCenter = new Vector2(0f, 0f);
    [SerializeField] private Vector2 boardSize   = new Vector2(12f, 8f);

    private Spawner spawner;

    public Vector2 BoardCenter => boardCenter;
    public Vector2 BoardSize   => boardSize;

    private void Awake()
    {
        Instance = this;
        spawner = GetComponent<Spawner>();
        if (spawner == null)
            Debug.LogError("NetworkBallManager must live on the same GameObject as Spawner.");
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && NetworkManager.IsHost)
            StartCoroutine(ConvertLocalBoardPiecesToNetworked());
    }

    private System.Collections.IEnumerator ConvertLocalBoardPiecesToNetworked()
    {
        // Wait one frame so the network is fully up before converting.
        yield return null;

        var spawner = GetComponent<Spawner>();
        if (spawner == null) yield break;

        var local = spawner.GetAllBoardPieces();
        foreach (var go in local)
        {
            if (go == null) continue;

            // Skip pieces that are already networked (avoid duplicates on re-host).
            var nb = go.GetComponent<NetworkBall>();
            if (nb != null && nb.IsSpawned) continue;

            var ball = go.GetComponent<BallNumber>();
            if (ball == null) continue;

            string content = ball.GetRawDisplayedKey();
            Vector3 pos = go.transform.position;

            // Host IS the server → direct spawn, no RPC.
            SpawnBoardBall(pos, content);

            // Remove the local version.
            spawner.DestroyBoardBall(go);
        }
    }

    // ==================================================================
    // Board-side spawn (server, unchanged)
    // ==================================================================

    public NetworkBall SpawnBoardBall(Vector3 pos, string content, float rotationZ = 0f)
    {
        if (!IsServer) return null;
        var nb = SpawnBallServerInternal(pos, content,
            ownerId: NetworkManager.ServerClientId,
            zone: BallZone.ServerBoard,
            rotationZ: rotationZ);
        if (nb != null) nb.PushImmediate();
        return nb;
    }

    public NetworkBall SpawnBoardBagBall(Vector3 pos, string content, float rotationZ = 0f)
    {
        if (!IsServer) return null;
        var nb = SpawnBallServerInternal(pos, content,
            ownerId: NetworkManager.ServerClientId,
            zone: BallZone.ServerBoard,
            rotationZ: rotationZ);
        if (nb == null) return null;

        nb.IsBagBall.Value = true;   // set after SpawnWithOwnership so it's a proper NetworkVariable write
        nb.PushImmediate();
        return nb;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSpawnBoardBagBallServerRpc(Vector3 position, string content, float rotationZ)
    {
        if (!IsServer) return;
        SpawnBoardBagBall(position, content, rotationZ);
    }

    private NetworkBall SpawnBallServerInternal(Vector3 position, string content,
        ulong ownerId, BallZone zone, float rotationZ = 0f)
    {
        bool isDirectionalOperator = DirectionalOperator.IsDirectionalOperatorText(
            content == null ? string.Empty : content.Trim());
        GameObject prefab = isDirectionalOperator
            ? directionalOperatorPrefab
            : ballPrefab;
        if (prefab == null)
        {
            Debug.LogError(isDirectionalOperator
                ? "NetworkBallManager: directionalOperatorPrefab not assigned."
                : "NetworkBallManager: ballPrefab not assigned.");
            return null;
        }

        var go = Instantiate(prefab, position, Quaternion.Euler(0f, 0f, rotationZ));
        var netObj = go.GetComponent<NetworkObject>();
        var ball   = go.GetComponent<BallNumber>();
        var nb     = go.GetComponent<NetworkBall>();

        ball.SetInputMode(BallInputMode.DisplayKey);
        if (!string.IsNullOrEmpty(content))
        {
            ball.ApplyTextInput(content, false);
            ball.RefreshDisplay();
        }

        nb.CurrentZone.Value = (byte)zone;
        netObj.SpawnWithOwnership(ownerId, destroyWithScene: true);

        return nb;

    }

    public void DespawnBallServer(GameObject go)
    {
        if (!IsServer) return;
        var netObj = go.GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned) netObj.Despawn(true);
        else Destroy(go);
    }

    // ==================================================================
    // Hand → Board transfer
    // ==================================================================

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestHandToBoardServerRpc(Vector3 worldPos, string content,
        int number, char variable, int power, float rotationZ, RpcParams rpcParams = default)
    {
        if (!IsServer) return;

        // Server spawns a brand-new board piece with the hand piece's content.
        var nb = SpawnBallServerInternal(
            worldPos,
            content ?? string.Empty,
            ownerId: NetworkManager.ServerClientId,
            zone: BallZone.ServerBoard,
            rotationZ: rotationZ);

        if (nb == null) return;

        // Apply algebra data if it wasn't just text.
        if (number != 0 || variable != '\0' || power != 0)
        {
            nb.Ball.SetAlgebraContentRaw(number, variable, power);
            nb.Ball.RefreshDisplay();
        }

        nb.PushImmediate();

      
    }

 

    //(client → server, for hand→board transfer from a client)
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSpawnBoardBallServerRpc(Vector3 position, string content, float rotationZ)
    {
        if (!IsServer) return;
        SpawnBoardBall(position, content, rotationZ);
    }


    // ==================================================================
    // Board → Hand transfer
    // ==================================================================

    // Called server-side when a client requests taking a board piece into hand.
    public void PerformBoardToHandTransfer(NetworkBall nb, ulong requestingClientId)
    {
        if (!IsServer || nb == null) return;

        var ball = nb.Ball;
        string content = ball.GetRawDisplayedKey();
        int number = ball.NumberValue;
        char variable = ball.Variable;
        int power = ball.VariablePower;
        float rotationZ = ball.transform.eulerAngles.z;
        byte inputMode = (byte)ball.InputMode;
        bool solverOutput = ball.IsSolverOutput;
        bool locked = ball.IsLocked;
        bool isBagBall = nb.IsBagBall.Value;   // ← NEW

        nb.NetworkObject.Despawn(true);

        TransferBoardContentToClientRpc(
            requestingClientId, content, number, variable, power,
            inputMode, solverOutput, locked, isBagBall, rotationZ);
    }

    [ClientRpc]
    private void TransferBoardContentToClientRpc(
        ulong targetClientId, string content, int number, char variable, int power,
        byte inputMode, bool solverOutput, bool locked, bool isBagBall, float rotationZ)
    {
        if (NetworkManager.Singleton.LocalClientId != targetClientId) return;
        if (BallTransferManager.Instance == null) return;
        BallTransferManager.Instance.ReceiveBoardContentFromServer(
            content, number, variable, power, inputMode, solverOutput, locked, isBagBall, rotationZ);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestBagReturnFromBoardServerRpc(NetworkObjectReference netObjRef, string content, RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (!netObjRef.TryGet(out NetworkObject netObj)) return;

        var nb = netObj.GetComponent<NetworkBall>();
        if (nb == null || !nb.IsBagBall.Value) return;

        if (BagManager.Instance != null)
            BagManager.Instance.RequestReturnToBag(content);

        nb.NetworkObject.Despawn(true);
    }

    // ==================================================================
    // Register / unregister with Spawner
    // ==================================================================

    public void NotifyBallSpawned(GameObject go)
    {
        if (spawner != null) spawner.RegisterBall(go);
    }

    public void NotifyBallDespawned(GameObject go)
    {
        if (spawner != null) spawner.UnregisterBall(go);
    }

        // ============================================================
    // Prefab-swap support
    // ============================================================

    public NetworkBall SpawnBoardOperator(Vector3 pos, string content, float rotationZ = 0f)
    {
        if (!IsServer) return null;
        if (directionalOperatorPrefab == null)
        {
            Debug.LogError("NetworkBallManager: directionalOperatorPrefab not assigned.");
            return null;
        }

        var go = Instantiate(directionalOperatorPrefab, pos,
                             Quaternion.Euler(0f, 0f, rotationZ));
        var netObj = go.GetComponent<NetworkObject>();
        var ball   = go.GetComponent<BallNumber>();
        var nb     = go.GetComponent<NetworkBall>();

        ball.SetInputMode(BallInputMode.DisplayKey);
        if (!string.IsNullOrEmpty(content))
        {
            ball.ApplyTextInput(content, false);
            ball.RefreshDisplay();
        }

        nb.CurrentZone.Value = (byte)BallZone.ServerBoard;
        netObj.SpawnWithOwnership(NetworkManager.ServerClientId, destroyWithScene: true);
        nb.PushImmediate();
        return nb;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestSwapBallServerRpc(
        NetworkObjectReference netObjRef,
        bool shouldBeDirectional,
        string key, int number, char variable, int power,
        byte mode, bool solverOutput, bool locked, bool isBag, float rotZ,
        RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (!netObjRef.TryGet(out NetworkObject netObj)) return;

        var nb = netObj.GetComponent<NetworkBall>();
        if (nb == null) return;

        // Server-side lock check: only allow a swap if the sender holds the
        // lock, or nobody does, or the ball is unowned.
        ulong sender = rpcParams.Receive.SenderClientId;
        if (nb.LockedBy.Value != ulong.MaxValue && nb.LockedBy.Value != sender)
            return;

        Vector3 pos = netObj.transform.position;
        netObj.Despawn(true);

        NetworkBall newBall = shouldBeDirectional
            ? SpawnBoardOperator(pos, key, rotZ)
            : SpawnBoardBall(pos, key, rotZ);

        if (newBall == null) return;

        newBall.Ball.SetInputModeRaw((BallInputMode)mode);
        newBall.Ball.SetRawDisplayedKey(key, solverOutput);
        newBall.Ball.SetAlgebraContentRaw(number, variable, power);
        newBall.Ball.SetLockedRaw(locked);
        newBall.Ball.RefreshDisplay();

        if (isBag)
        {
            newBall.IsBagBall.Value = true;
        }
        newBall.PushImmediate();
    }
}