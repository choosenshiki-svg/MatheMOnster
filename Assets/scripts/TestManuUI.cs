using System.Collections.Generic;
using UnityEngine;
using System.Text;

/// <summary>
/// Runtime test menu drawn with Unity IMGUI, so no Canvas setup is required.
/// Attach this to the same GameObject as Spawner and TestSpawner.
/// </summary>
public class TestMenuUI : MonoBehaviour
{
    public TestSpawner testSpawner;
    public float menuWidth = 520f;
    public float menuHeight = 520f;

    private bool isOpen;
    private Vector3 spawnWorldPosition;
    private bool spawnInHand;
    private Vector2 menuPosition;
    private Vector2 testListScrollPosition;
    private Vector2 categoryScrollPosition;   
    private string selectedCategory = "SUM";
        private readonly string[] categories = {
        "SUM", "PRODUCT", "FUNCTIONS", "BRACKETS",
        "SYNTAX", "VALID", "STUCK", "COMPLEX", "POSITIONAL",
        "FRACTIONS", "POWERS", "EQUALS", "DANGLING"
    };
    private GUIStyle windowStyle;
    private GUIStyle categoryStyle;
    private GUIStyle selectedCategoryStyle;
    private GUIStyle testButtonStyle;
    private GUIStyle expectedStyle;
    private GUIStyle titleStyle;
    private GUIStyle actionButtonStyle;
    public static bool BlockWheelForMenu { get; private set; }

    // Spawner checks this so a click that closes the menu cannot also affect the game world.
    public static bool ShouldBlockSpawnerInput { get; private set; }

    public bool IsOpen => isOpen;

    private void Awake()
    {
        if (testSpawner == null)
            testSpawner = GetComponent<TestSpawner>();

        if (testSpawner == null)
            Debug.LogError("TestMenuUI needs a TestSpawner component on the same GameObject.", this);
    }

    private void Update()
    {
        if (isOpen)
        {
            Vector2 mouseTopLeft = new Vector2(
                Input.mousePosition.x,
                Screen.height - Input.mousePosition.y);
            BlockWheelForMenu = GetMenuRect().Contains(mouseTopLeft);
        }
        else
        {
            BlockWheelForMenu = false;
        }

        if (isOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

        // Keep world input blocked through the click that closed the menu.
        if (!isOpen && !Input.GetMouseButton(0) && !Input.GetMouseButton(1) &&
            !Input.GetMouseButton(2) && !Input.GetMouseButtonDown(0) &&
            !Input.GetMouseButtonDown(1) && !Input.GetMouseButtonDown(2))
        {
            ShouldBlockSpawnerInput = false;
        }

        if (!isOpen)
            return;

        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (!GetMenuRect().Contains(mouse))
                Close();
        }
    }

    public void OpenAt(Vector3 worldPosition, bool isHand)
    {
        spawnWorldPosition = worldPosition;
        spawnInHand = isHand;

        Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

        float desiredX = mouse.x;
        float desiredY = mouse.y;

        // Clamp position so the menu stays inside its own viewport.
        var setup = DualCameraSetup.Instance;
        if (setup != null)
        {
            float dividerYFromTop = Screen.height * (1f - setup.HandFraction);
            float barThickness = 8f;

            float minY, maxY;
            if (isHand)
            {
                minY = dividerYFromTop + barThickness;
                maxY = Screen.height;
            }
            else
            {
                minY = 0f;
                maxY = dividerYFromTop - barThickness;
            }

            // Ensure the menu fits vertically inside the viewport.
            float availableHeight = Mathf.Max(0f, maxY - minY);
            if (menuHeight > availableHeight)
                menuHeight = availableHeight;   // shrink if viewport is too small

            desiredY = Mathf.Clamp(desiredY, minY, Mathf.Max(minY, maxY - menuHeight));

            // If the menu would spill past the right edge, pull it left.
            desiredX = Mathf.Clamp(desiredX, 0f, Mathf.Max(0f, Screen.width - menuWidth));
        }

        menuPosition = new Vector2(desiredX, desiredY);
        isOpen = true;
        ShouldBlockSpawnerInput = true;
    }

    public void Close()
    {
        isOpen = false;
        // Do not release the block until the closing click has finished.
        ShouldBlockSpawnerInput = true;
    }

    private Rect GetMenuRect()
    {
        return new Rect(menuPosition.x, menuPosition.y, menuWidth, menuHeight);
    }

    private void EnsureStyles()
    {
        if (windowStyle != null)
            return;

        windowStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(12, 12, 10, 12)
        };
        windowStyle.normal.background = MakeTexture(new Color(0.10f, 0.12f, 0.14f, 0.98f));

        categoryStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 12,
            padding = new RectOffset(8, 6, 7, 7)
        };
        categoryStyle.normal.textColor = Color.white;

        selectedCategoryStyle = new GUIStyle(categoryStyle);
        selectedCategoryStyle.normal.background = MakeTexture(new Color(0.12f, 0.48f, 0.42f, 1f));

        testButtonStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 13,
            wordWrap = true,
            padding = new RectOffset(8, 8, 7, 7)
        };

        expectedStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            wordWrap = true
        };
        expectedStyle.normal.textColor = new Color(0.72f, 0.78f, 0.80f);

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        titleStyle.normal.textColor = Color.white;

        actionButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            wordWrap = true,
            padding = new RectOffset(8, 8, 8, 8)
        };
    }

    private void OnGUI()
    {
        if (!isOpen || testSpawner == null)
            return;

        EnsureStyles();
        Rect rect = GetMenuRect();
        GUI.Box(rect, GUIContent.none, windowStyle);

        GUILayout.BeginArea(new Rect(rect.x + 12, rect.y + 8, rect.width - 24, rect.height - 16));
        GUILayout.BeginHorizontal();
        GUILayout.Label("BALL TESTS", titleStyle, GUILayout.Height(28));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("X", GUILayout.Width(30), GUILayout.Height(26)))
        {
            Close();
            Event.current.Use();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            return;
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();

        GUILayout.BeginVertical(GUILayout.Width(150));
        if (GUILayout.Button("SPAWN ALL TESTS", actionButtonStyle, GUILayout.MinHeight(42)))
        {
            testSpawner.SpawnAllTests(spawnWorldPosition, spawnInHand);
            Close();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            return;
        }

        if (GUILayout.Button("COPY FORMATION TO CLIPBOARD",
        actionButtonStyle, GUILayout.MinHeight(42)))
        {
            CopySelectedFormation();
        }

        GUILayout.Space(8);

        // Scroll view for the category list.
        categoryScrollPosition = GUILayout.BeginScrollView(
            categoryScrollPosition,
            GUILayout.Width(150),
            GUILayout.ExpandHeight(true));

        foreach (string category in categories)
        {
            GUIStyle style = category == selectedCategory ? selectedCategoryStyle : categoryStyle;
            if (GUILayout.Button(category, style, GUILayout.Height(34)))
                selectedCategory = category;
        }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();

        GUILayout.Space(12);
        GUILayout.BeginVertical();
        if (GUILayout.Button("SPAWN ALL " + selectedCategory + " TESTS", actionButtonStyle, GUILayout.MinHeight(38)))
        {
            testSpawner.SpawnCategoryTests(selectedCategory, spawnWorldPosition, spawnInHand);
            Close();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            return;
        }

        GUILayout.Space(6);
        List<TestSpawner.TestSetup> visibleTests = new List<TestSpawner.TestSetup>();
        foreach (TestSpawner.TestSetup setup in testSpawner.Tests)
        {
            if (setup.category == selectedCategory)
                visibleTests.Add(setup);
        }

        testListScrollPosition = GUILayout.BeginScrollView(testListScrollPosition, GUILayout.ExpandHeight(true));
        foreach (TestSpawner.TestSetup setup in visibleTests)
        {
            if (GUILayout.Button(setup.name, testButtonStyle, GUILayout.MinHeight(32)))
            {
                testSpawner.SpawnTest(setup, spawnWorldPosition, spawnInHand);
                Close();
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label("Expected: " + setup.expected, expectedStyle);
            GUILayout.Space(5);
        }
        GUILayout.EndScrollView();

        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private Texture2D MakeTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private void OnDestroy()
    {
        DestroyStyleTexture(windowStyle);
        DestroyStyleTexture(selectedCategoryStyle);
        ShouldBlockSpawnerInput = false;
    }

    private void DestroyStyleTexture(GUIStyle style)
    {
        if (style != null && style.normal.background != null)
            Destroy(style.normal.background);
    }

    
private void CopySelectedFormation()
{
    List<BallDrag> selected = LassoSelection.GetSelectedBalls();
    List<BallDrag> validBalls = new List<BallDrag>();
    List<string> contents = new List<string>();

    foreach (BallDrag drag in selected)
    {
        if (drag == null)
            continue;

        BallNumber ball = drag.GetComponent<BallNumber>();
        if (ball == null)
            continue;

        string content = ball.GetMathContent();

        if (string.IsNullOrEmpty(content))
            content = ball.GetSolverOutputContent();

        validBalls.Add(drag);
        contents.Add(content);
    }

    if (validBalls.Count == 0)
    {
        Debug.LogWarning("Select some balls with the lasso first.");
        return;
    }

    float spacing = testSpawner != null ? testSpawner.spacing : 1.8f;
    Vector3 origin = validBalls[0].transform.position;

    System.Text.StringBuilder code = new System.Text.StringBuilder();

    code.AppendLine(
        "tests.Add(new TestSetup(\"SUM\", \"My formation\", \"Expected result\",");

    for (int i = 0; i < validBalls.Count; i++)
    {
        Vector3 position = validBalls[i].transform.position;

        float x = (position.x - origin.x) / spacing;
        float y = (position.y - origin.y) / spacing;

        string xText = x.ToString(
            "0.###", System.Globalization.CultureInfo.InvariantCulture) + "f";

        string yText = y.ToString(
            "0.###", System.Globalization.CultureInfo.InvariantCulture) + "f";

        string content = contents[i]
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");

        code.Append("    new BallPlacement(")
            .Append(xText)
            .Append(", ")
            .Append(yText)
            .Append(", \"")
            .Append(content)
            .Append("\")");

        code.AppendLine(i < validBalls.Count - 1 ? "," : "));");
    }

    GUIUtility.systemCopyBuffer = code.ToString();
    
}


}
