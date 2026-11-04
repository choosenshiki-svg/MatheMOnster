using System;
using UnityEngine;

/// <summary>
/// Modal name prompt. Calls the provided callback with the confirmed name.
/// While active, blocks other lobby input.
/// </summary>
public class PlayerNamePrompt : MonoBehaviour
{
    public static PlayerNamePrompt Instance { get; private set; }

    [SerializeField] private float panelWidth = 400f;
    [SerializeField] private float panelHeight = 200f;

    private bool _isOpen;
    private string _inputText = "";
    private string _error = "";
    private Action<string> _onConfirm;

    public bool IsOpen => _isOpen;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Open(Action<string> onConfirm, string initialError = "")
    {
        _onConfirm = onConfirm;
        _inputText = "";
        _error = initialError;
        _isOpen = true;
    }

    public void Close()
    {
        _isOpen = false;
        _onConfirm = null;
        _error = "";
    }

    public void SetError(string error)
    {
        _error = error;
        _isOpen = true;
    }

    private void OnGUI()
    {
        if (!_isOpen) return;

        // Dim background.
        GUI.color = new Color(0, 0, 0, 0.5f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        Rect panel = new Rect(
            (Screen.width - panelWidth) * 0.5f,
            (Screen.height - panelHeight) * 0.5f,
            panelWidth, panelHeight);

        GUI.Box(panel, GUIContent.none, GUI.skin.window);

        GUILayout.BeginArea(new Rect(panel.x + 20, panel.y + 20, panel.width - 40, panel.height - 40));

        GUILayout.Label("Enter your name:", new GUIStyle(GUI.skin.label) { fontSize = 16 });
        GUILayout.Space(8);

        GUI.SetNextControlName("NameField");
        _inputText = GUILayout.TextField(_inputText, PlayerManager.MaxNameLength, GUILayout.Height(30));
        GUI.FocusControl("NameField");

        GUILayout.Space(6);
        if (!string.IsNullOrEmpty(_error))
        {
            var errStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            errStyle.normal.textColor = new Color(1f, 0.4f, 0.4f);
            GUILayout.Label(_error, errStyle);
        }

        GUILayout.FlexibleSpace();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Cancel", GUILayout.Height(32)))
        {
            Close();
        }

        GUI.enabled = !string.IsNullOrWhiteSpace(_inputText);

        bool confirmByEnter = Event.current.type == EventType.KeyDown &&
                              Event.current.keyCode == KeyCode.Return;

        if (GUILayout.Button("Confirm", GUILayout.Height(32)) || confirmByEnter)
        {
            string name = _inputText.Trim();
            _isOpen = false;
            var callback = _onConfirm;
            _onConfirm = null;
            callback?.Invoke(name);
        }

        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }
}