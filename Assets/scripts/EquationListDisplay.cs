using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class EquationListDisplay : MonoBehaviour
{
    private readonly List<Text> equationLabels = new List<Text>();
    private Transform equationListContent;
    private GameObject panelRoot;

    private void Awake()
    {
        CreateEquationListUi();
    }

    public void Refresh(List<MathEquationBuilder.ConnectedEquation> equations)
    {
         var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm != null && nm.IsListening) return;
        
        List<MathEquationBuilder.ConnectedEquation> sortedEquations =
            new List<MathEquationBuilder.ConnectedEquation>(equations);

        // Keep the original order as the final tie-breaker so the list does not
        // shuffle when two equations have the same topmost position.
        Dictionary<MathEquationBuilder.ConnectedEquation, int> originalOrder =
            new Dictionary<MathEquationBuilder.ConnectedEquation, int>();
        for (int index = 0; index < equations.Count; index++)
        {
            originalOrder[equations[index]] = index;
        }

        sortedEquations.Sort((left, right) =>
        {
            Vector3 leftTop = GetTopmostBallPosition(left);
            Vector3 rightTop = GetTopmostBallPosition(right);

            const float positionTolerance = 0.01f;
            float heightDifference = rightTop.y - leftTop.y;
            if (Mathf.Abs(heightDifference) > positionTolerance)
            {
                // Higher topmost ball first.
                return heightDifference > 0f ? 1 : -1;
            }

            float horizontalDifference = leftTop.x - rightTop.x;
            if (Mathf.Abs(horizontalDifference) > positionTolerance)
            {
                // If heights are equal, the construction farther left comes first.
                return horizontalDifference < 0f ? -1 : 1;
            }

            return originalOrder[left].CompareTo(originalOrder[right]);
        });

        bool unchanged = equationLabels.Count >= sortedEquations.Count;
        for (int index = 0; unchanged && index < equationLabels.Count; index++)
        {
            Text label = equationLabels[index];
            bool shouldBeActive = index < sortedEquations.Count;
            if (label.transform.parent.gameObject.activeSelf != shouldBeActive ||
                shouldBeActive && label.text != GetEquationListText(sortedEquations[index]))
            {
                unchanged = false;
            }
        }

        if (unchanged)
        {
            return;
        }

        while (equationLabels.Count < sortedEquations.Count)
        {
            equationLabels.Add(CreateEquationListRow());
        }

        for (int index = 0; index < equationLabels.Count; index++)
        {
            Text label = equationLabels[index];
            bool hasEquation = index < sortedEquations.Count;
            label.transform.parent.gameObject.SetActive(hasEquation);
            if (hasEquation)
            {
                label.text = GetEquationListText(sortedEquations[index]);
            }
        }
    }

    private void Update()
    {
        if (panelRoot == null) return;
        var nm = Unity.Netcode.NetworkManager.Singleton;
        bool online = nm != null && nm.IsListening;
        if (panelRoot.activeSelf != !online)
            panelRoot.SetActive(!online);
    }

    private static Vector3 GetTopmostBallPosition(
        MathEquationBuilder.ConnectedEquation equation)
    {
        if (equation == null || equation.balls == null || equation.balls.Count == 0)
        {
            return Vector3.zero;
        }

        // Use the highest ball in the construction. If multiple balls share that
        // height, use the leftmost one as the construction's sorting position.
        Vector3 topmostPosition = equation.balls[0] != null
            ? equation.balls[0].transform.position
            : Vector3.zero;

        foreach (BallNumber ball in equation.balls)
        {
            if (ball == null)
            {
                continue;
            }

            Vector3 position = ball.transform.position;
            const float positionTolerance = 0.01f;
            if (position.y > topmostPosition.y + positionTolerance ||
                Mathf.Abs(position.y - topmostPosition.y) <= positionTolerance &&
                position.x < topmostPosition.x)
            {
                topmostPosition = position;
            }
        }

        return topmostPosition;
    }

    private void CreateEquationListUi()
    {
        GameObject panelObject = CreateUiObject("Equation List Panel", transform);
        panelRoot = panelObject;
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0.08f);
        panelRect.anchorMax = new Vector2(0f, 0.92f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.anchoredPosition = new Vector2(16f, 0f);
        panelRect.sizeDelta = new Vector2(350f, 0f);

        Image panelImage = panelObject.AddComponent<Image>();
        panelImage.color = new Color(0.07f, 0.12f, 0.16f, 0.9f);

        GameObject titleObject = CreateUiObject("Equation List Title", panelObject.transform);
        RectTransform titleRect = titleObject.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -12f);
        titleRect.sizeDelta = new Vector2(-24f, 32f);
        Text title = titleObject.AddComponent<Text>();
        ConfigureUiText(title, 22, FontStyle.Bold, TextAnchor.MiddleLeft);
        title.text = "Equations";

        GameObject viewportObject = CreateUiObject("Equation List Viewport", panelObject.transform);
        RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(10f, 10f);
        viewportRect.offsetMax = new Vector2(-10f, -54f);
        Image viewportImage = viewportObject.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.02f);
        Mask viewportMask = viewportObject.AddComponent<Mask>();
        viewportMask.showMaskGraphic = false;

        GameObject contentObject = CreateUiObject("Equation List Content", viewportObject.transform);
        RectTransform contentRect = contentObject.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = Vector2.one;
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.padding = new RectOffset(2, 2, 2, 2);
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter contentSize = contentObject.AddComponent<ContentSizeFitter>();
        contentSize.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        contentSize.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scrollRect = viewportObject.AddComponent<ScrollRect>();
        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 30f;
        equationListContent = contentObject.transform;
    }

    private static GameObject CreateUiObject(string objectName, Transform parent)
    {
        GameObject uiObject = new GameObject(objectName, typeof(RectTransform));
        uiObject.layer = parent.gameObject.layer;
        uiObject.transform.SetParent(parent, false);
        return uiObject;
    }

    private static void ConfigureUiText(
        Text text,
        int fontSize,
        FontStyle fontStyle,
        TextAnchor alignment)
    {
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = Color.white;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
    }

    private Text CreateEquationListRow()
    {
        GameObject rowObject = CreateUiObject("Equation List Row", equationListContent);
        Image rowImage = rowObject.AddComponent<Image>();
        rowImage.color = new Color(0.13f, 0.34f, 0.40f, 1f);
        LayoutElement rowLayout = rowObject.AddComponent<LayoutElement>();
        rowLayout.minHeight = 48f;
        rowLayout.preferredHeight = 58f;

        GameObject labelObject = CreateUiObject("Equation Text", rowObject.transform);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(10f, 4f);
        labelRect.offsetMax = new Vector2(-10f, -4f);
        Text label = labelObject.AddComponent<Text>();
        ConfigureUiText(label, 18, FontStyle.Normal, TextAnchor.MiddleLeft);
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 12;
        label.resizeTextMaxSize = 18;
        return label;
    }

    private static string GetEquationListText(
        MathEquationBuilder.ConnectedEquation equation)
    {
        string expression = equation.expression;
        string text = expression.Replace(
            MathEquationSolver.ImplicitMultiplicationSymbol.ToString(),
            "*");
        foreach (BallNumber ball in equation.balls)
        {
            if (ball.GetMathContent().Trim().Length != 0)
            {
                continue;
            }

            string displayedValue = ball.IsSolverOutput
                ? ball.GetSolverOutputDisplayContent()
                : "?";
            text = text.Replace(
                MathEquationSolver.UnknownSymbol.ToString(),
                displayedValue);
            int equalsIndex = text.IndexOf('=');
            int unknownIndex = expression.IndexOf(MathEquationSolver.UnknownSymbol);
            if (equalsIndex > 0 && equalsIndex == text.LastIndexOf('=') &&
                unknownIndex >= 0 &&
                unknownIndex < expression.IndexOf('='))
            {
                string leftSide = text.Substring(0, equalsIndex);
                string rightSide = text.Substring(equalsIndex + 1);
                return rightSide + "=" + leftSide;
            }

            return text;
        }

        return text;
    }
}
