using System.IO;
using Unity.Netcode;
using UnityEngine;

public enum BallZone : byte { Hand = 0, ServerBoard = 1 }

[RequireComponent(typeof(BallNumber))]
public class NetworkBall : NetworkBehaviour
{
    [SerializeField] private float positionSendHz = 20f;

    private BallNumber ball;
    private float nextSendTime;
    private float _lastLockActivity;
    private const float LockTimeoutSeconds = 5f;

    public BallNumber Ball => ball;

    public readonly NetworkVariable<byte> CurrentZone = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public readonly NetworkVariable<int> SlotIndex = new(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public readonly NetworkVariable<ulong> LockedBy = new(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public readonly NetworkVariable<bool> IsBagBall = new(
    false,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server);

    private void Awake() => ball = GetComponent<BallNumber>();

    // ==================================================================
    // Spawn / despawn
    // ==================================================================

    public override void OnNetworkSpawn()
    {
        CurrentZone.OnValueChanged += OnZoneChanged;
        IsBagBall.OnValueChanged += OnIsBagBallChanged;

        if (IsServer)
        {
            ApplyVisibilityForZone(CurrentZone.Value);
            SendStateToAll();
            if (NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        ApplyLayerForZone(CurrentZone.Value);

        var appearance = GetComponent<BallAppearance>();
        if (appearance != null)
            appearance.SetHandPiece(CurrentZone.Value == (byte)BallZone.Hand);

        // Attach lock-ring visual (client-side only; safe on server too, will no-op).
        if (GetComponent<LockRing>() == null)
            gameObject.AddComponent<LockRing>();

        if (IsBagBall.Value && GetComponent<BagRing>() == null)
        gameObject.AddComponent<BagRing>();

        if (NetworkBallManager.Instance != null)
            NetworkBallManager.Instance.NotifyBallSpawned(gameObject);
    }

    private void OnIsBagBallChanged(bool oldValue, bool newValue)
    {
        if (newValue && GetComponent<BagRing>() == null)
            gameObject.AddComponent<BagRing>();
    }

    public override void OnNetworkDespawn()
    {
        CurrentZone.OnValueChanged -= OnZoneChanged;

        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;

        if (IsServer) LockedBy.Value = ulong.MaxValue;

        if (NetworkBallManager.Instance != null)
            NetworkBallManager.Instance.NotifyBallDespawned(gameObject);
    }

    private void OnZoneChanged(byte oldZone, byte newZone)
    {
        ApplyVisibilityForZone(newZone);
        ApplyLayerForZone(newZone);

        var appearance = GetComponent<BallAppearance>();
        if (appearance != null)
            appearance.SetHandPiece(newZone == (byte)BallZone.Hand);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (LockedBy.Value == clientId)
            LockedBy.Value = ulong.MaxValue;
    }

    // ==================================================================
    // Layer + visibility
    // ==================================================================

    private void ApplyLayerForZone(byte zone)
    {
        int layer = zone == (byte)BallZone.Hand
            ? LayerMask.NameToLayer("HandPieces")
            : LayerMask.NameToLayer("BoardPieces");

        if (layer < 0)
        {
            Debug.LogError(
                $"Layer for zone {zone} not found. Add 'HandPieces' and 'BoardPieces' " +
                $"in Project Settings -> Tags and Layers.", this);
            return;
        }

        gameObject.layer = layer;
        foreach (Transform child in transform)
            ApplyLayerRecursive(child, layer);
    }

    private void ApplyLayerRecursive(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform child in t)
            ApplyLayerRecursive(child, layer);
    }

    private void ApplyVisibilityForZone(byte zone)
    {
        if (!IsServer || !IsSpawned) return;

        if (zone == (byte)BallZone.Hand)
        {
            foreach (var clientId in NetworkManager.ConnectedClientsIds)
            {
                if (clientId != OwnerClientId)
                    NetworkObject.NetworkHide(clientId);
                else
                    NetworkObject.NetworkShow(clientId);
            }
        }
        else
        {
            foreach (var clientId in NetworkManager.ConnectedClientsIds)
                NetworkObject.NetworkShow(clientId);
        }
    }

    // ==================================================================
    // State broadcast
    // ==================================================================

    private void Update()
    {        
        if (!IsSpawned || !IsServer) return;

        // Stale-lock timeout.
        if (LockedBy.Value != ulong.MaxValue &&
            Time.unscaledTime - _lastLockActivity > LockTimeoutSeconds)
        {
            LockedBy.Value = ulong.MaxValue;
        }

        if (Time.unscaledTime < nextSendTime) return;
        nextSendTime = Time.unscaledTime + 1f / Mathf.Max(1f, positionSendHz);
        SendStateToAll();
    }

    public void PushImmediate()
    {
        if (IsServer) SendStateToAll();
    }

    private void SendStateToAll()
    {
        var state = BallState.FromBall(
            ball,
            (uint)OwnerClientId,
            CurrentZone.Value,
            IsBagBall.Value);
        byte[] payload = Serialize(state);
        ReceiveStateClientRpc(payload);
    }

    [ClientRpc]
    private void ReceiveStateClientRpc(byte[] payload)
    {
        if (IsServer) return;

        var state = Deserialize(payload);
        if (IsOwner && BallDrag.isDragging && CurrentZone.Value == (byte)BallZone.Hand)
            state.position = ball.transform.position;

        state.ApplyTo(ball, authoritative: true);
    }

    // ==================================================================
    // Lock RPCs
    // ==================================================================

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestGrabServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (CurrentZone.Value != (byte)BallZone.ServerBoard) return;

        ulong sender = rpcParams.Receive.SenderClientId;

        // Clear stale locks first.
        if (LockedBy.Value != ulong.MaxValue &&
            Time.unscaledTime - _lastLockActivity > LockTimeoutSeconds)
        {
            LockedBy.Value = ulong.MaxValue;
        }

        if (LockedBy.Value == ulong.MaxValue || LockedBy.Value == sender)
        {
            LockedBy.Value = sender;
            _lastLockActivity = Time.unscaledTime;
            var targetParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { sender }
            }
        };
        GrabResultClientRpc(true, targetParams);
        }
        else
        {
            var targetParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { sender }
            }
        };
        GrabResultClientRpc(false, targetParams);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestReleaseServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer) return;

        ulong sender = rpcParams.Receive.SenderClientId;
        if (LockedBy.Value == sender)
        {
            LockedBy.Value = ulong.MaxValue;
            var directionalOperator = GetComponent<DirectionalOperator>();
            if (directionalOperator != null && directionalOperator.IsActive)
                directionalOperator.EndDrag();
        }
    }

    [ClientRpc]
    private void GrabResultClientRpc(bool acquired, ClientRpcParams rpcParams = default)
    {
        BallDrag.OnGrabResult(this, acquired);
    }

    // ==================================================================
    // Server RPCs
    // ==================================================================

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestHandPositionServerRpc(Vector2 pos, RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (CurrentZone.Value != (byte)BallZone.Hand) return;
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;

        ball.transform.position = pos;
        var rb = ball.GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;
        SendStateToAll();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestBoardMoveServerRpc(
        Vector2 pos, float rotationZ, RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (CurrentZone.Value != (byte)BallZone.ServerBoard) return;

        ulong sender = rpcParams.Receive.SenderClientId;

        // Reject if someone else holds the lock.
        if (LockedBy.Value != ulong.MaxValue && LockedBy.Value != sender)
        {
            SendStateToAll();
            return;
        }

        // Implicit grab if no lock was set (safety net).
        if (LockedBy.Value == ulong.MaxValue)
            LockedBy.Value = sender;

        _lastLockActivity = Time.unscaledTime;

        var directionalOperator = GetComponent<DirectionalOperator>();
        if (directionalOperator != null && directionalOperator.IsActive)
        {
            directionalOperator.BeginDrag();
            var operatorBody = ball.GetComponent<Rigidbody2D>();
            if (operatorBody != null)
            {
                operatorBody.MovePosition(pos);
                operatorBody.MoveRotation(rotationZ);
            }
            return;
        }

        ball.transform.position = pos;
        var rb = ball.GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;
        SendStateToAll();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestDespawnServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (IsBagBall.Value) return;   // ← bag balls cannot be deleted normally

        ulong sender = rpcParams.Receive.SenderClientId;
        if (LockedBy.Value != ulong.MaxValue && LockedBy.Value != sender) return;

        NetworkObject.Despawn(true);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestContentServerRpc(int number, char variable, int power,
                                    string key, byte mode, bool solverOutput, bool locked,
                                    RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (IsBagBall.Value) return;

        ulong sender = rpcParams.Receive.SenderClientId;

        if (CurrentZone.Value == (byte)BallZone.Hand &&
            sender != OwnerClientId) return;

        if (LockedBy.Value != ulong.MaxValue && LockedBy.Value != sender) return;

        ball.SetInputModeRaw((BallInputMode)mode);
        ball.SetAlgebraContentRaw(number, variable, power);
        ball.SetRawDisplayedKey(key ?? string.Empty, solverOutput);
        ball.SetLockedRaw(locked);
        ball.RefreshDisplay();
        SendStateToAll();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestBoardToHandServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (CurrentZone.Value != (byte)BallZone.ServerBoard) return;

        ulong sender = rpcParams.Receive.SenderClientId;
        if (LockedBy.Value != ulong.MaxValue && LockedBy.Value != sender) return;

        NetworkBallManager.Instance.PerformBoardToHandTransfer(this, sender);
    }

    // ==================================================================
    // Serialization
    // ==================================================================

    private static byte[] Serialize(in BallState state)
    {
        using var ms = new MemoryStream(64);
        using var bw = new BinaryWriter(ms);
        state.Write(bw);
        bw.Flush();
        return ms.ToArray();
    }

    private static BallState Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        return BallState.Read(br);
    }
}