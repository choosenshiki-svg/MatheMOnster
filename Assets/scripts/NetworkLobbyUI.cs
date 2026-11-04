using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

public class NetworkLobbyUI : MonoBehaviour
{
    public NetworkGameSession session;

    private string joinCodeInput = "";
    private string statusMessage = "";
    private bool busy;

    private void OnGUI()
    {
        if (session == null) return;

        // Don't draw lobby UI while the name prompt is open.
        if (PlayerNamePrompt.Instance != null && PlayerNamePrompt.Instance.IsOpen) return;

        bool connected = NetworkManager.Singleton != null &&
                         NetworkManager.Singleton.IsListening;

        GUILayout.BeginArea(new Rect(20, 20, 340, connected ? 220 : 300), GUI.skin.box);

        GUILayout.Label("<b>Multiplayer</b>", new GUIStyle(GUI.skin.label)
            { richText = true, fontSize = 16 });

        if (connected)
        {
            GUILayout.Label(session.IsHost ? "Hosting session" : "Connected to session");

            if (!string.IsNullOrEmpty(session.JoinCode))
            {
                GUILayout.Label($"Room code: {session.JoinCode}");
                if (GUILayout.Button("Copy room code"))
                    GUIUtility.systemCopyBuffer = session.JoinCode;
            }
            else
            {
                GUILayout.Label("No room code is available.");
            }

            if (GUILayout.Button("Leave room"))
            {
                session.Leave();
                statusMessage = "Left room.";
            }

            if (!string.IsNullOrEmpty(statusMessage))
                GUILayout.Label(statusMessage);

            GUILayout.EndArea();
            return;
        }

        GUI.enabled = !busy;

        if (GUILayout.Button("Host Room", GUILayout.Height(34)))
        {
            PromptForNameAndThen(name =>
            {
                busy = true;
                _ = HostAsync(name);
            });
        }

        GUILayout.Space(8);
        GUILayout.Label("Join code:");
        joinCodeInput = GUILayout.TextField(joinCodeInput, 8).ToUpperInvariant();

        if (GUILayout.Button("Join Room", GUILayout.Height(34)) &&
            joinCodeInput.Length >= 4)
        {
            string code = joinCodeInput;
            PromptForNameAndThen(name =>
            {
                busy = true;
                _ = JoinAsync(code, name);
            });
        }

        GUI.enabled = true;

        if (!string.IsNullOrEmpty(session.JoinCode))
        {
            GUILayout.Space(8);
            GUILayout.Label($"Room code: {session.JoinCode}");
            if (GUILayout.Button("Copy"))
                GUIUtility.systemCopyBuffer = session.JoinCode;
        }

        if (!string.IsNullOrEmpty(statusMessage))
            GUILayout.Label(statusMessage);

        GUILayout.EndArea();
    }

    private void PromptForNameAndThen(System.Action<string> onConfirm)
    {
        if (PlayerNamePrompt.Instance == null)
        {
            // No prompt in scene — fall back to auto-name.
            onConfirm?.Invoke($"Player{Random.Range(1000, 9999)}");
            return;
        }

        PlayerNamePrompt.Instance.Open(onConfirm);
    }

    private async Task HostAsync(string playerName)
    {
        try
        {
            await session.HostRoomAsync();
            statusMessage = "Hosting.";
            _ = SubmitNameAsync(playerName);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            statusMessage = "Host failed: " + e.Message;
        }
        finally { busy = false; }
    }

    private async Task JoinAsync(string code, string playerName)
    {
        try
        {
            await session.JoinRoomAsync(code);
            statusMessage = "Joined.";
            _ = SubmitNameAsync(playerName);
        }
        catch (System.Exception e)
        {
            statusMessage = "Join failed: " + e.Message;
            // Reopen prompt with error.
            if (PlayerNamePrompt.Instance != null)
                PlayerNamePrompt.Instance.SetError("Connection failed, try again.");
        }
        finally { busy = false; }
    }

    private async Task SubmitNameAsync(string name)
    {
        // Wait a moment for PlayerData to be spawned by PlayerManager.
        for (int i = 0; i < 100; i++)
        {
            if (PlayerManager.Instance != null && NetworkManager.Singleton != null)
            {
                var pd = FindMyPlayerData();
                if (pd != null) break;
            }
            await Task.Delay(50);
        }

        // Send name to server.
        PlayerNameSubmitter.SubmitName(name);
    }

    private PlayerData FindMyPlayerData()
    {
        if (NetworkManager.Singleton == null) return null;
        ulong myId = NetworkManager.Singleton.LocalClientId;

        foreach (var pd in FindObjectsByType<PlayerData>(FindObjectsSortMode.None))
        {
            if (pd != null && pd.OwnerClientId == myId && pd.IsSpawned)
                return pd;
        }
        return null;
    }
}