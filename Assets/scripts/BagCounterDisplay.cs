using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// IMGUI panel that shows bag ball counts: on board, per player's hand,
/// and remaining in bag. Only visible when online.
/// </summary>
public class BagCounterDisplay : MonoBehaviour
{
    [SerializeField] private float panelWidth = 300f;
    [SerializeField] private float panelMargin = 16f;

    private GUIStyle _titleStyle;
    private GUIStyle _rowStyle;
    private GUIStyle _headerStyle;
    private GUIStyle _totalStyle;
    private bool _stylesReady;

    private void EnsureStyles()
    {
        if (_stylesReady) return;

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white }
        };

        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(0.7f, 0.85f, 1f) }
        };

        _rowStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white }
        };

        _totalStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(1f, 0.9f, 0.4f) }
        };

        _stylesReady = true;
    }

    private void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return;
        if (BagManager.Instance == null) return;

        EnsureStyles();

        int boardCount = CountBoardBagBalls();

        

        var playerLines = new List<(string name, int count)>();
        if (PlayerManager.Instance != null)
        {
            foreach (var pd in FindObjectsByType<PlayerData>(FindObjectsSortMode.None))
            {
                if (pd == null || !pd.IsSpawned) continue;
                string name = pd.PlayerName.Value.Length > 0
                    ? pd.PlayerName.Value.ToString()
                    : $"Player {pd.OwnerClientId}";
                playerLines.Add((name, pd.HandBagCount.Value));
            }
        }

        int totalHand = 0;
        foreach (var line in playerLines) totalHand += line.count;

        int inBag = BagManager.Instance.RemainingCount();

        // Layout dimensions.
        float lineHeight = 22f;
        float panelHeight = 60f + (playerLines.Count + 3) * lineHeight;
        Rect panelRect = new Rect(
            panelMargin,
            Screen.height - panelHeight - panelMargin,
            panelWidth,
            panelHeight);

        // Background.
        GUI.color = new Color(0.07f, 0.12f, 0.16f, 0.9f);
        GUI.DrawTexture(panelRect, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(
            panelRect.x + 12,
            panelRect.y + 10,
            panelRect.width - 24,
            panelRect.height - 20));

        string bagName = BagManager.Instance.Library != null
        ? BagManager.Instance.Library.GetName(BagManager.Instance.ActiveBagIndex)
        : "?";
        GUILayout.Label($"Bag Balls - {bagName}", _titleStyle);
        GUILayout.Space(4);
        DrawDivider();
        GUILayout.Space(4);

        // Board.
        GUILayout.Label($"Board:            {boardCount}", _rowStyle);

        // Per-player hand.
        if (playerLines.Count > 0)
        {
            GUILayout.Space(6);
            GUILayout.Label("Hands:", _headerStyle);
            foreach (var line in playerLines)
            {
                GUILayout.Label($"  {line.name}:  {line.count}", _rowStyle);
            }
        }

        GUILayout.Space(4);
        DrawDivider();
        GUILayout.Space(4);

        GUILayout.Label($"Total in hands:   {totalHand}", _totalStyle);
        GUILayout.Label($"In bag:           {inBag}", _totalStyle);

        GUILayout.EndArea();
    }

    private void DrawDivider()
    {
        Rect r = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
        GUI.color = new Color(0.4f, 0.5f, 0.6f, 0.6f);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private int CountBoardBagBalls()
    {
        int count = 0;
        var all = FindObjectsByType<NetworkBall>(FindObjectsSortMode.None);
        foreach (var nb in all)
        {
            if (nb == null || !nb.IsSpawned) continue;
            if (nb.CurrentZone.Value != (byte)BallZone.ServerBoard) continue;
            if (nb.IsBagBall.Value) count++;
        }
        return count;
    }
}