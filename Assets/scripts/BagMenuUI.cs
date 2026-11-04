using Unity.Netcode;
using UnityEngine;

public class BagMenuUI : MonoBehaviour
{
    [SerializeField] private float panelWidth = 420f;
    [SerializeField] private float panelHeight = 480f;

    private bool _isOpen;
    private Vector2 _scroll;
    private string _newContent = "";
    private string _newCount = "1";

    public void Open()
    {
        _isOpen = true;
        var bag = BagManager.Instance;
        if (bag != null && bag.IsOnline) bag.RequestBeginEdit();
    }

    private void Close()
    {
        _isOpen = false;
        BagManager.Instance?.RequestEndEdit();
    }

    private ulong LocalId() =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
            ? NetworkManager.Singleton.LocalClientId : 0UL;

    private void OnGUI()
    {
        if (!_isOpen) return;
        var bag = BagManager.Instance;
        if (bag == null) return;

        Rect panel = new Rect(
            (Screen.width - panelWidth) * 0.5f,
            (Screen.height - panelHeight) * 0.5f,
            panelWidth, panelHeight);

        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Box(panel, GUIContent.none, GUI.skin.window);
        GUILayout.BeginArea(new Rect(panel.x + 16, panel.y + 12,
                                     panel.width - 32, panel.height - 24));

        // Header
        GUILayout.BeginHorizontal();
        GUILayout.Label("<b>Bag Manager</b>", new GUIStyle(GUI.skin.label)
        {
            richText = true, fontSize = 16, alignment = TextAnchor.MiddleLeft
        }, GUILayout.Height(28));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("X", GUILayout.Width(28), GUILayout.Height(24)))
        {
            Close();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            return;
        }
        GUILayout.EndHorizontal();

        bool lockedByOther = bag.IsBeingEditedBySomeone;
        bool lockedByMe = bag.IsOnline
                          && bag.EditingClientId == LocalId()
                          && bag.EditingClientId != ulong.MaxValue;

        // Bag picker
        GUILayout.Space(6);
        GUILayout.Label("Active bag:", GUI.skin.label);
        var lib = bag.Library;
        if (lib != null)
        {
            for (int i = 0; i < lib.Count; i++)
            {
                bool selected = i == bag.ActiveBagIndex;
                GUI.enabled = !bag.IsBeingEditedBySomeone;
                if (GUILayout.Button(
                        (selected ? "[x] " : "[ ] ") + lib.GetName(i),
                        GUILayout.Height(28)))
                {
                    bag.RequestSelectBag(i);
                }
                GUI.enabled = true;
            }
        }
        else
        {
            GUILayout.Label("No BagLibrary assigned on BagManager.");
        }

        GUILayout.Space(8);

        // Lock gate
        if (lockedByOther)
        {
            var style = new GUIStyle(GUI.skin.label);
            style.normal.textColor = new Color(1f, 0.7f, 0.35f);
            string editor = PlayerManager.GetPlayerName(bag.EditingClientId);
            GUILayout.Label($"Locked - {editor} is editing this bag.", style);
            if (GUILayout.Button("Close", GUILayout.Height(28)))
            {
                Close();
                GUILayout.EndArea();
                return;
            }
            GUILayout.EndArea();
            return;
        }

        if (bag.IsOnline && !lockedByMe)
        {
            if (GUILayout.Button("Start editing", GUILayout.Height(30)))
            {
                bag.RequestBeginEdit();
            }
            GUILayout.EndArea();
            return;
        }

        // Entries


        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
        var entries = bag.GetEntriesSnapshot();
        int total = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var (content, count) = entries[i];
            total += count;
            GUILayout.BeginHorizontal();
            GUILayout.Label(content, GUILayout.Width(80));
            GUILayout.Label($"x{count}", GUILayout.Width(50));

            if (GUILayout.Button("-", GUILayout.Width(28)))
                bag.RequestSetEntryCount(content, count - 1);
            if (GUILayout.Button("+", GUILayout.Width(28)))
                bag.RequestSetEntryCount(content, count + 1);
            if (GUILayout.Button("del", GUILayout.Width(40)))
                bag.RequestRemoveEntry(content);

            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        GUILayout.Space(4);
        GUILayout.Label($"Total in bag: {total}");

        // Add
        GUILayout.Space(6);
        GUILayout.Label("Add content:", GUI.skin.label);
        GUILayout.BeginHorizontal();
        _newContent = GUILayout.TextField(_newContent, 24, GUILayout.Width(180));
        _newCount = GUILayout.TextField(_newCount, 4, GUILayout.Width(60));
        if (GUILayout.Button("Add"))
        {
            if (int.TryParse(_newCount, out int c) && c > 0 && !string.IsNullOrWhiteSpace(_newContent))
            {
                bag.RequestAddEntry(_newContent.Trim(), c);
                _newContent = "";
                _newCount = "1";
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Done editing", GUILayout.Height(30)))
        {
            bag.RequestEndEdit();
            _isOpen = false;
        }
        if (GUILayout.Button("Reset bag", GUILayout.Height(30)))
        {
            bag.RequestReset();
        }
#if UNITY_EDITOR
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            if (GUILayout.Button("Save to asset", GUILayout.Height(30)))
                bag.SaveActiveDefinitionFromPool();
        }
#endif
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }
}