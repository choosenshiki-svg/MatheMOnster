using UnityEngine;

public class SelectionInputRouter : MonoBehaviour
{
    private void Update()
    {
        var selected = LassoSelection.GetSelectedBalls();
        if (selected == null || selected.Count == 0) return;
        if (TestMenuUI.ShouldBlockSpawnerInput) return;

        // Delete (server-authoritative)
        if (Input.GetKeyDown(KeyCode.Delete))
        {
            foreach (var drag in selected)
            {
                if (drag == null) continue;
                // Skip bag balls — they're read-only and not deletable.
                if (drag.GetComponent<BagBallMarker>() != null) continue;
                var nbCheck = drag.GetComponent<NetworkBall>();
                if (nbCheck != null && nbCheck.IsBagBall.Value) continue;
                var nb = drag.GetComponent<NetworkBall>();
                if (nb != null && nb.IsSpawned) nb.RequestDespawnServerRpc();
                else Destroy(drag.gameObject);
            }
            StartCoroutine(DeselectNextFrame());
            return;
        }

        // Backspace (content edit — routed through server for spawned balls)
        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            foreach (var drag in selected)
            {
                if (drag == null) continue;
                // Skip bag balls — they're read-only and not deletable.
                if (drag.GetComponent<BagBallMarker>() != null) continue;
                var nbCheck = drag.GetComponent<NetworkBall>();
                if (nbCheck != null && nbCheck.IsBagBall.Value) continue;
                var ball = drag.GetComponent<BallNumber>();
                var nb   = drag.GetComponent<NetworkBall>();
                if (ball == null) continue;

                ball.ClearContent();
                ball.RefreshDisplay();

                if (nb != null && nb.IsSpawned)
                {
                    nb.RequestContentServerRpc(
                        ball.NumberValue, ball.Variable, ball.VariablePower,
                        ball.GetRawDisplayedKey(), (byte)ball.InputMode,
                        ball.IsSolverOutput, ball.IsLocked);
                }
            }
            return;
        }

        // Typing
        string typed = Input.inputString;
        if (!string.IsNullOrEmpty(typed))
        {
            string cleaned = string.Empty;
            foreach (char c in typed) if (!char.IsControl(c)) cleaned += c;

            if (cleaned.Length > 0)
            {
                foreach (var drag in selected)
                {
                    if (drag == null) continue;
                    // Skip bag balls — they're read-only and not deletable.
                    if (drag.GetComponent<BagBallMarker>() != null) continue;
                    var nbCheck = drag.GetComponent<NetworkBall>();
                    if (nbCheck != null && nbCheck.IsBagBall.Value) continue;
                    var ball = drag.GetComponent<BallNumber>();
                    var nb   = drag.GetComponent<NetworkBall>();
                    if (ball == null) continue;

                    ball.ClearContent();
                    ball.ApplyTextInput(cleaned, false);
                    ball.RefreshDisplay();

                    if (nb != null && nb.IsSpawned)
                    {
                        nb.RequestContentServerRpc(
                            ball.NumberValue, ball.Variable, ball.VariablePower,
                            ball.GetRawDisplayedKey(), (byte)ball.InputMode,
                            ball.IsSolverOutput, ball.IsLocked);
                    }
                }
            }
        }
    }

    private System.Collections.IEnumerator DeselectNextFrame()
    {
        yield return null;
        LassoSelection.DeselectAll();
    }
}