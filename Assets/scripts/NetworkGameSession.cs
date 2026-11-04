using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Networking.Transport.Relay;
using UnityEngine;

public class NetworkGameSession : MonoBehaviour
{
    public static NetworkGameSession Instance { get; private set; }

    public string JoinCode { get; private set; }
    public bool   IsHost   { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public async Task InitializeAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    public async Task<string> HostRoomAsync(int maxPlayers = 8)
    {
        NetworkManager networkManager = GetNetworkManager();
        UnityTransport transport = GetUnityTransport(networkManager);

        await InitializeAsync();

        Allocation alloc = await RelayService.Instance.CreateAllocationAsync(maxPlayers);
        if (alloc == null)
            throw new InvalidOperationException("Relay did not return an allocation.");

        string joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
        if (string.IsNullOrWhiteSpace(joinCode))
            throw new InvalidOperationException("Relay did not return a room code.");

        Debug.Log($"Relay allocation and room code created ({joinCode}). Configuring transport.");

        transport.SetRelayServerData(new RelayServerData(alloc, "dtls"));

        Debug.Log("Relay transport is configured. Starting the network host.");
        if (!networkManager.StartHost())
            throw new InvalidOperationException("NetworkManager.StartHost() returned false. Check the Unity Netcode logs for the startup failure.");

        JoinCode = joinCode;
        IsHost = true;
        return JoinCode;
    }

    public async Task JoinRoomAsync(string joinCode)
    {
        if (string.IsNullOrWhiteSpace(joinCode))
            throw new ArgumentException("A room code is required.", nameof(joinCode));

        joinCode = joinCode.Trim().ToUpperInvariant();
        NetworkManager networkManager = GetNetworkManager();
        UnityTransport transport = GetUnityTransport(networkManager);

        await InitializeAsync();

        JoinAllocation alloc = await RelayService.Instance.JoinAllocationAsync(joinCode);
        transport.SetRelayServerData(new RelayServerData(alloc, "dtls"));

        if (!networkManager.StartClient())
            throw new InvalidOperationException("NetworkManager.StartClient() returned false. Check the Unity Netcode logs for the startup failure.");

        IsHost = false;
        JoinCode = joinCode;
    }

    private static NetworkManager GetNetworkManager()
    {
        if (NetworkManager.Singleton != null)
            return NetworkManager.Singleton;

        NetworkManager[] managers = FindObjectsByType<NetworkManager>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        string managerDetails = managers.Length == 0
            ? "none"
            : string.Join("; ", Array.ConvertAll(managers, manager =>
                $"{manager.name} (enabled={manager.enabled}, active={manager.gameObject.activeInHierarchy}, scene={manager.gameObject.scene.name})"));

        string message = $"NetworkManager.Singleton is null. Runtime NetworkManager components found: {managerDetails}.";
        Debug.LogError(message);
        throw new InvalidOperationException(message);
    }

    private static UnityTransport GetUnityTransport(NetworkManager networkManager)
    {
        UnityTransport transport = networkManager.GetComponent<UnityTransport>();
        if (transport == null)
            throw new InvalidOperationException("The active NetworkManager has no UnityTransport component.");

        if (networkManager.NetworkConfig == null ||
            networkManager.NetworkConfig.NetworkTransport != transport)
            throw new InvalidOperationException("The active NetworkManager's Network Transport is not its UnityTransport component.");

        return transport;
    }

    public void Leave()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
        JoinCode = null;
        IsHost = false;
    }
}