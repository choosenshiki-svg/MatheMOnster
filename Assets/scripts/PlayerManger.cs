using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Spawns a PlayerData NetworkObject per connected client and manages
/// name uniqueness. Scene-level singleton.
/// </summary>
public class PlayerManager : NetworkBehaviour
{
    public static PlayerManager Instance { get; private set; }

    [SerializeField] private GameObject playerDataPrefab;

    private readonly Dictionary<ulong, PlayerData> _players = new();
    private readonly HashSet<string> _claimedNames = new();

    public const int MaxNameLength = 16;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

            // Host is already connected by the time OnNetworkSpawn runs.
            if (NetworkManager.IsHost && NetworkManager.LocalClientId >= 0)
                SpawnPlayerData(NetworkManager.LocalClientId);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        SpawnPlayerData(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (_players.TryGetValue(clientId, out var pd))
        {
            if (pd != null && pd.PlayerName.Value.Length > 0)
                _claimedNames.Remove(pd.PlayerName.Value.ToString());

            if (pd != null && pd.IsSpawned)
                pd.NetworkObject.Despawn(true);

            _players.Remove(clientId);
        }
    }

    private void SpawnPlayerData(ulong clientId)
    {
        if (_players.ContainsKey(clientId)) return;
        if (playerDataPrefab == null)
        {
            Debug.LogError("PlayerManager: playerDataPrefab not assigned.");
            return;
        }

        var go = Instantiate(playerDataPrefab);
        var netObj = go.GetComponent<NetworkObject>();
        var pd = go.GetComponent<PlayerData>();

        netObj.SpawnWithOwnership(clientId, destroyWithScene: true);
        _players[clientId] = pd;
    }

    /// <summary>
    /// Server-side: attempts to set the player's name.
    /// Called via RPC from the owning client.
    /// </summary>
    public bool TrySetName(ulong clientId, string name, out string error)
    {
        error = null;

        if (!_players.TryGetValue(clientId, out var pd) || pd == null)
        {
            error = "Player data not found.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Name cannot be empty.";
            return false;
        }

        name = name.Trim();
        if (name.Length > MaxNameLength)
        {
            error = $"Name must be at most {MaxNameLength} characters.";
            return false;
        }

        // If they already had a name, release it first.
        if (pd.PlayerName.Value.Length > 0)
        {
            _claimedNames.Remove(pd.PlayerName.Value.ToString());
        }

        if (_claimedNames.Contains(name))
        {
            // Re-add their old name if it existed and re-set it back.
            if (pd.PlayerName.Value.Length > 0)
                _claimedNames.Add(pd.PlayerName.Value.ToString());

            error = "That name is already taken.";
            return false;
        }

        _claimedNames.Add(name);
        pd.PlayerName.Value = new FixedString32Bytes(name);
        return true;
    }

    /// <summary>
    /// Returns the display name for a client. Falls back to a default
    /// if the player hasn't set their name yet.
    /// </summary>
    public static string GetPlayerName(ulong clientId)
    {
        var inst = Instance;
        if (inst == null) return $"Player {clientId}";

        if (inst._players.TryGetValue(clientId, out var pd) &&
            pd != null &&
            pd.PlayerName.Value.Length > 0)
        {
            return pd.PlayerName.Value.ToString();
        }

        return $"Player {clientId}";
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReportHandBagCountServerRpc(int count, RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        ulong sender = rpcParams.Receive.SenderClientId;

        if (_players.TryGetValue(sender, out var pd) && pd != null)
        {
            pd.HandBagCount.Value = Mathf.Max(0, count);
        }
    }
}