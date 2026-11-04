using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public struct BagEntryState : INetworkSerializable, System.IEquatable<BagEntryState>
{
    public FixedString64Bytes Content;
    public int Count;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref Content);
        s.SerializeValue(ref Count);
    }

    public bool Equals(BagEntryState other) => Content.Equals(other.Content) && Count == other.Count;
}

public class BagManager : NetworkBehaviour
{
    public static BagManager Instance { get; private set; }

    [SerializeField] private BagLibrary library;

    // Networked pool (server authoritative).
    private NetworkList<BagEntryState> _networkPool;

    // Offline-only pool.
    private List<BagEntryState> _offlinePool = new List<BagEntryState>();

    private System.Action<string> PendingDrawCallback;

    // Shared online state.
    private readonly NetworkVariable<byte> _activeBagIndex = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<ulong> _editingClientId = new(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Offline-only equivalent of the active index.
    private int _offlineActiveBagIndex = 0;

    // ==================================================================
    // Public read-only views
    // ==================================================================

    public bool IsOnline =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    public int ActiveBagIndex => IsOnline ? _activeBagIndex.Value : _offlineActiveBagIndex;

    public ulong EditingClientId => IsOnline ? _editingClientId.Value : ulong.MaxValue;

    public bool IsBeingEditedBySomeone =>
        EditingClientId != ulong.MaxValue && EditingClientId != LocalClientId;

    public BagLibrary Library => library;

    private ulong LocalClientId =>
        (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            ? NetworkManager.Singleton.LocalClientId
            : 0UL;

    // ==================================================================
    // Lifecycle
    // ==================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _networkPool = new NetworkList<BagEntryState>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
    }

    private void Start()
    {
        if (!IsOnline)
        {
            _offlineActiveBagIndex = Mathf.Clamp(
                PlayerPrefs.GetInt("ActiveBagIndex", 0),
                0,
                library != null && library.Count > 0 ? library.Count - 1 : 0);
            ResetOffline();
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _networkPool.Clear();
            var def = ActiveDefinition();
            if (def != null)
            {
                foreach (var e in def.entries)
                {
                    if (e == null || string.IsNullOrEmpty(e.content) || e.count <= 0) continue;
                    _networkPool.Add(new BagEntryState { Content = e.content, Count = e.count });
                }
            }

            if (NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    private void OnClientDisconnected(ulong id)
    {
        if (IsServer && _editingClientId.Value == id)
            _editingClientId.Value = ulong.MaxValue;
    }

    // ==================================================================
    // Queries
    // ==================================================================

    public int RemainingCount()
    {
        if (IsOnline)
        {
            int total = 0;
            foreach (var e in _networkPool) total += e.Count;
            return total;
        }

        int offline = 0;
        foreach (var e in _offlinePool) offline += e.Count;
        return offline;
    }

    public bool IsEmpty() => RemainingCount() == 0;

    public List<(string content, int count)> GetEntriesSnapshot()
    {
        var list = new List<(string, int)>();
        if (IsOnline)
        {
            foreach (var e in _networkPool) list.Add((e.Content.ToString(), e.Count));
        }
        else
        {
            foreach (var e in _offlinePool) list.Add((e.Content.ToString(), e.Count));
        }
        return list;
    }

    // ==================================================================
    // Bag selection
    // ==================================================================

    public void RequestSelectBag(int index)
    {
        if (library == null || index < 0 || index >= library.Count) return;

        if (!IsOnline)
        {
            _offlineActiveBagIndex = index;
            PlayerPrefs.SetInt("ActiveBagIndex", index);
            PlayerPrefs.Save();
            ResetOffline();
            return;
        }

        if (IsServer) { SelectBagServerSide(index); return; }
        RequestSelectBagServerRpc((byte)index);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestSelectBagServerRpc(byte index, RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (_editingClientId.Value != ulong.MaxValue) return;
        SelectBagServerSide(index);
    }

    private void SelectBagServerSide(int index)
    {
        _activeBagIndex.Value = (byte)index;
        ResetServerSide();
        _editingClientId.Value = ulong.MaxValue;
    }

    // ==================================================================
    // Editing lock
    // ==================================================================

    public void RequestBeginEdit()
    {
        if (!IsOnline) return;
        if (IsServer) BeginEditServerSide(LocalClientId);
        else RequestBeginEditServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestBeginEditServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        BeginEditServerSide(rpcParams.Receive.SenderClientId);
    }

    private void BeginEditServerSide(ulong clientId)
    {
        if (_editingClientId.Value != ulong.MaxValue &&
            _editingClientId.Value != clientId) return;
        _editingClientId.Value = clientId;
    }

    public void RequestEndEdit()
    {
        if (!IsOnline) return;
        if (IsServer) EndEditServerSide(LocalClientId);
        else RequestEndEditServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestEndEditServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        EndEditServerSide(rpcParams.Receive.SenderClientId);
    }

    private void EndEditServerSide(ulong clientId)
    {
        if (_editingClientId.Value == clientId)
            _editingClientId.Value = ulong.MaxValue;
    }

    // ==================================================================
    // Entry edits
    // ==================================================================

    public void RequestAddEntry(string content, int count)
    {
        if (string.IsNullOrEmpty(content) || count <= 0) return;

        if (!IsOnline) { AddEntryOffline(content, count); return; }

        if (IsServer) AddEntryServerSide(content, count, LocalClientId);
        else RequestAddEntryServerRpc(content, count);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestAddEntryServerRpc(string content, int count, RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        AddEntryServerSide(content, count, rpcParams.Receive.SenderClientId);
    }

    private void AddEntryServerSide(string content, int count, ulong sender)
    {
        if (_editingClientId.Value != sender) return;
        for (int i = 0; i < _networkPool.Count; i++)
        {
            var e = _networkPool[i];
            if (e.Content.ToString() == content)
            {
                e.Count += count;
                _networkPool[i] = e;
                return;
            }
        }
        _networkPool.Add(new BagEntryState { Content = content, Count = count });
    }

    private void AddEntryOffline(string content, int count)
    {
        for (int i = 0; i < _offlinePool.Count; i++)
        {
            var e = _offlinePool[i];
            if (e.Content.ToString() == content) { e.Count += count; _offlinePool[i] = e; return; }
        }
        _offlinePool.Add(new BagEntryState { Content = content, Count = count });
    }

    public void RequestSetEntryCount(string content, int newCount)
    {
        if (string.IsNullOrEmpty(content)) return;
        if (!IsOnline) { SetCountOffline(content, newCount); return; }
        if (IsServer) SetCountServerSide(content, newCount, LocalClientId);
        else RequestSetEntryCountServerRpc(content, newCount);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestSetEntryCountServerRpc(string content, int newCount, RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        SetCountServerSide(content, newCount, rpcParams.Receive.SenderClientId);
    }

    private void SetCountServerSide(string content, int newCount, ulong sender)
    {
        if (_editingClientId.Value != sender) return;
        for (int i = 0; i < _networkPool.Count; i++)
        {
            var e = _networkPool[i];
            if (e.Content.ToString() != content) continue;
            if (newCount <= 0) { _networkPool.RemoveAt(i); return; }
            e.Count = newCount;
            _networkPool[i] = e;
            return;
        }
        if (newCount > 0)
            _networkPool.Add(new BagEntryState { Content = content, Count = newCount });
    }

    private void SetCountOffline(string content, int newCount)
    {
        for (int i = 0; i < _offlinePool.Count; i++)
        {
            var e = _offlinePool[i];
            if (e.Content.ToString() != content) continue;
            if (newCount <= 0) { _offlinePool.RemoveAt(i); return; }
            e.Count = newCount;
            _offlinePool[i] = e;
            return;
        }
        if (newCount > 0)
            _offlinePool.Add(new BagEntryState { Content = content, Count = newCount });
    }

    public void RequestRemoveEntry(string content) => RequestSetEntryCount(content, 0);

    // ==================================================================
    // Draw
    // ==================================================================

    public void RequestDraw(System.Action<string> onContentReady)
    {
        if (!IsOnline)
        {
            string content = DrawOffline();
            if (!string.IsNullOrEmpty(content)) onContentReady?.Invoke(content);
            return;
        }

        if (IsServer)
        {
            string content = DrawServerSide();
            if (!string.IsNullOrEmpty(content)) onContentReady?.Invoke(content);
        }
        else
        {
            PendingDrawCallback = onContentReady;
            RequestDrawServerRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestDrawServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer) return;

        string content = DrawServerSide();
        ulong sender = rpcParams.Receive.SenderClientId;

        var target = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { sender } }
        };

        if (string.IsNullOrEmpty(content))
            DrawResultClientRpc(false, string.Empty, target);
        else
            DrawResultClientRpc(true, content, target);
    }

    [ClientRpc]
    private void DrawResultClientRpc(bool success, string content, ClientRpcParams rpcParams = default)
    {
        if (success && PendingDrawCallback != null)
            PendingDrawCallback(content);
        PendingDrawCallback = null;
    }

    private string DrawServerSide()
    {
        int total = 0;
        foreach (var e in _networkPool) total += e.Count;
        if (total <= 0) return null;

        int roll = UnityEngine.Random.Range(0, total);
        for (int i = 0; i < _networkPool.Count; i++)
        {
            var entry = _networkPool[i];
            if (roll < entry.Count)
            {
                string content = entry.Content.ToString();
                entry.Count--;
                if (entry.Count <= 0) _networkPool.RemoveAt(i);
                else _networkPool[i] = entry;
                return content;
            }
            roll -= entry.Count;
        }
        return null;
    }

    private string DrawOffline()
    {
        int total = 0;
        foreach (var e in _offlinePool) total += e.Count;
        if (total <= 0) return null;

        int roll = UnityEngine.Random.Range(0, total);
        for (int i = 0; i < _offlinePool.Count; i++)
        {
            var entry = _offlinePool[i];
            if (roll < entry.Count)
            {
                string content = entry.Content.ToString();
                entry.Count--;
                if (entry.Count <= 0) _offlinePool.RemoveAt(i);
                else _offlinePool[i] = entry;
                return content;
            }
            roll -= entry.Count;
        }
        return null;
    }

    // ==================================================================
    // Return to bag
    // ==================================================================

    public void RequestReturnToBag(string content)
    {
        if (string.IsNullOrEmpty(content)) return;

        if (!IsOnline) { ReturnOffline(content); return; }

        if (IsServer) ReturnServerSide(content);
        else RequestReturnToBagServerRpc(content);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestReturnToBagServerRpc(string content)
    {
        if (!IsServer) return;
        ReturnServerSide(content);
    }

    private void ReturnServerSide(string content)
    {
        for (int i = 0; i < _networkPool.Count; i++)
        {
            var entry = _networkPool[i];
            if (entry.Content.ToString() == content)
            {
                entry.Count++;
                _networkPool[i] = entry;
                return;
            }
        }
        _networkPool.Add(new BagEntryState { Content = content, Count = 1 });
    }

    private void ReturnOffline(string content)
    {
        for (int i = 0; i < _offlinePool.Count; i++)
        {
            var entry = _offlinePool[i];
            if (entry.Content.ToString() == content)
            {
                entry.Count++;
                _offlinePool[i] = entry;
                return;
            }
        }
        _offlinePool.Add(new BagEntryState { Content = content, Count = 1 });
    }

    // ==================================================================
    // Reset
    // ==================================================================

    public void RequestReset()
    {
        if (!IsOnline)
        {
            ResetOffline();

            var spawner = FindFirstObjectByType<Spawner>();
            if (spawner != null) spawner.DeleteAllHandBagBalls();

            var boardBalls = spawner?.GetAllBoardPieces();
            if (boardBalls != null)
            {
                foreach (var go in boardBalls)
                {
                    if (go == null) continue;
                    if (go.GetComponent<BagBallMarker>() != null)
                        spawner.DestroyBoardBall(go);
                }
            }
            return;
        }

        if (IsServer) RequestResetServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestResetServerRpc(RpcParams rpcParams = default)
    {
        if (!IsServer) return;

        ResetServerSide();

        var all = FindObjectsByType<NetworkBall>(FindObjectsSortMode.None);
        foreach (var nb in all)
        {
            if (nb != null && nb.IsSpawned && nb.IsBagBall.Value)
                nb.NetworkObject.Despawn(true);
        }

        ResetHandBagBallsClientRpc();
    }

    [ClientRpc]
    private void ResetHandBagBallsClientRpc()
    {
        var spawner = FindFirstObjectByType<Spawner>();
        if (spawner != null) spawner.DeleteAllHandBagBalls();
    }

    private void ResetServerSide()
    {
        _networkPool.Clear();
        var def = ActiveDefinition();
        if (def == null) return;
        foreach (var e in def.entries)
        {
            if (e == null || string.IsNullOrEmpty(e.content) || e.count <= 0) continue;
            _networkPool.Add(new BagEntryState { Content = e.content, Count = e.count });
        }
    }

    private void ResetOffline()
    {
        _offlinePool.Clear();
        var def = ActiveDefinition();
        if (def == null) return;
        foreach (var e in def.entries)
        {
            if (e == null || string.IsNullOrEmpty(e.content) || e.count <= 0) continue;
            _offlinePool.Add(new BagEntryState { Content = e.content, Count = e.count });
        }
    }

    // ==================================================================
    // Helpers
    // ==================================================================

    private BagDefinition ActiveDefinition()
    {
        if (library == null) return null;
        return library.Get(ActiveBagIndex);
    }

#if UNITY_EDITOR
    internal void SaveActiveDefinitionFromPool()
    {
        var def = ActiveDefinition();
        if (def == null) return;
        def.entries.Clear();
        foreach (var e in _networkPool)
            def.entries.Add(new BagDefinition.Entry
            {
                content = e.Content.ToString(),
                count = e.Count
            });
        UnityEditor.EditorUtility.SetDirty(def);
        UnityEditor.AssetDatabase.SaveAssets();
    }
#endif
}