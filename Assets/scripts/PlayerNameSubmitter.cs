using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Helper to send the client's name to the server after PlayerData spawns.
/// </summary>
public class PlayerNameSubmitter : NetworkBehaviour
{
    public static PlayerNameSubmitter Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public static void SubmitName(string name)
    {
        if (Instance == null)
        {
            Debug.LogWarning("PlayerNameSubmitter.Instance is null — name not sent.");
            return;
        }
        Instance.SubmitNameServerRpc(name);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SubmitNameServerRpc(string name, RpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (PlayerManager.Instance == null) return;

        ulong sender = rpcParams.Receive.SenderClientId;

        if (PlayerManager.Instance.TrySetName(sender, name, out string error))
        {
            NameResultClientRpc(true, "", TargetedTo(sender));
        }
        else
        {
            NameResultClientRpc(false, error ?? "Name rejected.", TargetedTo(sender));
        }
    }

    [ClientRpc]
    private void NameResultClientRpc(bool success, string error, ClientRpcParams rpcParams = default)
    {
        if (success)
        {
            Debug.Log("[Name] Name accepted.");
        }
        else
        {
            Debug.Log($"[Name] Name rejected: {error}");
            // Reopen the prompt with the error message.
            if (PlayerNamePrompt.Instance != null)
            {
                PlayerNamePrompt.Instance.SetError(error);
            }
        }
    }

    private static ClientRpcParams TargetedTo(ulong clientId)
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };
    }
}