using UnityEngine;

/// <summary>
/// Draws a horizontal gray divider between the two viewports and lets you
/// drag it up/down to change how much screen space the hand gets.
/// </summary>
public class BoardHandDivider : MonoBehaviour
{
    [SerializeField] private float stripHeightPx = 8f;
    [SerializeField] private Color stripColor = new Color(0.55f, 0.55f, 0.60f, 1f);
    [SerializeField] private Color stripHoverColor = new Color(0.85f, 0.85f, 0.90f, 1f);
    [SerializeField] private float dragSpeedFactor = 1f;

    private bool dragging;

    private void OnGUI()
    {
        var setup = DualCameraSetup.Instance;
        if (setup == null) return;

        float y = Screen.height * (1f - setup.HandFraction);
        Rect strip = new Rect(0f, y - stripHeightPx * 0.5f, Screen.width, stripHeightPx);

        bool hover = strip.Contains(Event.current.mousePosition);
        GUI.color = (dragging || hover) ? stripHoverColor : stripColor;
        GUI.DrawTexture(strip, Texture2D.whiteTexture);
        GUI.color = Color.white;

        if (hover && Input.GetMouseButtonDown(0))
            dragging = true;

        if (Input.GetMouseButtonUp(0))
            dragging = false;

        if (dragging)
        {
            float newFraction = 1f - (Event.current.mousePosition.y + (1 - dragSpeedFactor) * 0f) / Screen.height;
            newFraction = 1f - Event.current.mousePosition.y / Screen.height;
            setup.HandFraction = Mathf.Clamp(newFraction, 0.15f, 0.6f);
        }
    }
}