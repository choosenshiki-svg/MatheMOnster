using System.Globalization;
using UnityEngine;

// Selects whether a ball accepts algebra, numbers only, or free-form display-key text.
public enum BallInputMode
{
    // Store an integer coefficient with an optional variable and exponent.
    Algebra,
    // Store a numeric coefficient without allowing variables.
    NumbersOnly,
    // Store the exact text typed into the ball.
    DisplayKey
}

// Stores a ball's mathematical value and coordinates editing, solving, and display.
public class BallNumber : MonoBehaviour
{
    // Cached helper components responsible for visuals and keyboard input.
    private BallAppearance appearance;
    private BallInputController inputController;
    private DirectionalOperator directionalOperator;

    // Algebra-mode value: an integer coefficient, optional variable, and its exponent.
    private int ballNumber;
    private char ballVariable;
    private int variablePower;

    // Display-key text is stored separately from algebra-mode values.
    private string displayedKey = string.Empty;
    private BallInputMode inputMode = BallInputMode.Algebra;
    private bool isSolverOutput;
    private bool isLocked;

        // Prefab-swap tracking (see BallPrefabSwapper).
    private bool _swapQueued;
    private bool _allowSwaps;

    // Read-only views used by input handling and other gameplay scripts.
    public int NumberValue
    {
        get { return ballNumber; }
    }

    public char Variable
    {
        get { return ballVariable; }
    }

    public int VariablePower
    {
        get { return variablePower; }
    }

    public BallInputMode InputMode
    {
        get { return inputMode; }
    }

    public bool IsSolverOutput
    {
        get { return isSolverOutput; }
    }

    public bool IsLocked
    {
        get { return isLocked; }
    }

    public bool IsBeingEdited
    {
        get { return inputController != null && inputController.IsEditing; }
    }

    public long ChangeVersion 
    { 
        get; private set; 
    }

    // Find or add the companion components, then show the initial empty value.
    private void Awake()
    {
        appearance = GetComponent<BallAppearance>();
        if (appearance == null)
        {
            appearance = gameObject.AddComponent<BallAppearance>();
        }

        inputController = GetComponent<BallInputController>();
        if (inputController == null)
        {
            inputController = gameObject.AddComponent<BallInputController>();
        }

        directionalOperator = GetComponent<DirectionalOperator>();

        RefreshDisplay();

        StartCoroutine(AllowSwapsNextFrame());
    }

    // Switch entry modes and clear the value left over from the previous mode.
    public void SetInputMode(BallInputMode mode)
    {
        inputController.CancelEditing();
        inputMode = mode;
        ResetToEmpty();
    }

    // Clear this ball and remove its locked state.
    public void ResetToEmpty()
    {
        ClearContent();
        isLocked = false;
    }

    // Erase all stored content and report whether anything changed.
    public bool ClearContent()
    {
        bool changed = ballNumber != 0 || ballVariable != '\0' || variablePower != 0 ||
                       displayedKey.Length > 0 || isSolverOutput;
        ballNumber = 0;
        ballVariable = '\0';
        variablePower = 0;
        displayedKey = string.Empty;
        isSolverOutput = false;
        if (changed) { ChangeVersion++; MathEquationBuilder.MarkContentChanged(); }
        RefreshDisplay();;
        return changed;
    }

    // Provide text to the equation builder only for display-key balls.
    public string GetMathContent()
    {
        return inputMode == BallInputMode.DisplayKey && (!isSolverOutput || isLocked)
            ? displayedKey
            : string.Empty;
    }

    // Read calculated content without exposing it as user-entered equation text.
    public string GetSolverOutputContent()
    {
        return isSolverOutput ? displayedKey : string.Empty;
    }

    public string GetSolverOutputDisplayContent()
    {
        return isSolverOutput ? FormatSolverOutputForDisplay(displayedKey) : string.Empty;
    }

    public void SetEquationState(byte state)
    {
        if (appearance != null) appearance.SetEquationState(state);
    }

    public void SetAmbiguousOperand(bool isAmbiguous)
    {
        if (appearance != null)
        {
            appearance.SetAmbiguousOperand(isAmbiguous);
        }
    }

    // Toggle whether this ball is protected from editing or solver output.
    public void ToggleLock()
    {
        isLocked = !isLocked;
        ChangeVersion++;
        MathEquationBuilder.MarkContentChanged();
        RefreshDisplay();
    }

    public void SetAlgebraContent(int number, char variable, int power)
    {
        ballNumber = number;
        ballVariable = variable;
        variablePower = power;
        ChangeVersion++;
        MathEquationBuilder.MarkContentChanged();
    }

    // Apply typed display-key text, or clear it when backspace is pressed.
    public bool ApplyTextInput(string typedText, bool backspaceKeyDown)
    {
        if (backspaceKeyDown || typedText.IndexOf('\b') >= 0)
        {
            return ClearContent();
        }

        bool changed = false;
        if (isSolverOutput && !string.IsNullOrEmpty(typedText))
        {
            displayedKey = string.Empty;
            isSolverOutput = false;
            changed = true;
        }

        foreach (char character in typedText)
        {
            if (!char.IsControl(character) && displayedKey.Length < 24)
            {
                displayedKey += character;
                changed = true;
            }
        }

                if (changed) { ChangeVersion++; MathEquationBuilder.MarkContentChanged(); }
        return changed;
    }

    // Store a calculated result as display-key content and redraw the ball.
    public void SetSolverOutput(string content)
    {
        inputMode = BallInputMode.DisplayKey;
        displayedKey = content;
        isSolverOutput = true;
        RefreshDisplay();
    }

    // Remove a previous calculated answer when its equation is no longer valid.
    public void ClearSolverOutput()
    {
        if (!isSolverOutput)
        {
            return;
        }

        displayedKey = string.Empty;
        isSolverOutput = false;
        RefreshDisplay();
    }

    // Format the current mode's content and send it to the appearance component.
    public void RefreshDisplay()
    {
        if (appearance == null)
        {
            return;
        }

        string content = inputMode == BallInputMode.DisplayKey
            ? displayedKey
            : FormatNumber();

        // Explicit reciprocal tokens such as '/3' render as '1/3'.
        if (inputMode == BallInputMode.DisplayKey && !isSolverOutput &&
            content.Length > 0 && content[0] == '/')
        {
            string suffix = content.Substring(1).Trim();
            if (suffix.Length > 0)
                content = "1/" + suffix;
        }

        if (isSolverOutput)
        {
            content = FormatSolverOutputForDisplay(content);
        }

        if (directionalOperator != null)
        {
            string operatorContent = inputMode == BallInputMode.DisplayKey && !isSolverOutput
                ? displayedKey.Trim()
                : string.Empty;
            directionalOperator.Refresh(operatorContent);
        }

        if (content.Trim() == "=") appearance.MarkAsEqualsBall();

        appearance.SetContent(
            content,
            inputMode == BallInputMode.DisplayKey,
            isSolverOutput && !isLocked);
        CheckForPrefabSwap();
    }

    // Simplify editable addition text before ending the edit session.
    public void FinishEditing()
    {
        if (inputMode == BallInputMode.DisplayKey &&
            MathExpression.TrySimplifyAddition(displayedKey, out string simplified))
        {
            displayedKey = simplified;
        }

        RefreshDisplay();
    }

    // Limit solver-result display to two decimal places without changing stored text.
    private string FormatSolverOutputForDisplay(string content)
    {
        if (!decimal.TryParse(
                content,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out decimal value))
        {
            return content;
        }

        string formatted = value.ToString(CultureInfo.InvariantCulture);
        int decimalPoint = formatted.IndexOf('.');
        if (decimalPoint >= 0 && formatted.Length - decimalPoint > 3)
        {
            return formatted.Substring(0, decimalPoint + 3);
        }

        return formatted;
    }

    // Turn the algebra fields into text such as "7", "-x", or "3y^2".
    private string FormatNumber()
    {
        if (ballNumber == 0)
        {
            return string.Empty;
        }

        if (ballVariable == '\0')
        {
            return ballNumber.ToString();
        }

        string coefficient = ballNumber == 1
            ? string.Empty
            : ballNumber == -1
                ? "-"
                : ballNumber.ToString();
        string power = variablePower > 1 ? "^" + variablePower : string.Empty;
        return coefficient + ballVariable + power;
    }

    // ---- Network-facing raw accessors (add to BallNumber) ----

        public string GetRawDisplayedKey() => displayedKey;

        public void SetRawDisplayedKey(string key, bool solverOutput)
        {
            displayedKey   = key ?? string.Empty;
            isSolverOutput = solverOutput;
                        // Solver writes must NOT invalidate the cache; user input must.
            if (!solverOutput) { ChangeVersion++; MathEquationBuilder.MarkContentChanged(); }
        }

        public void SetInputModeRaw(BallInputMode mode)
        {
            inputMode = mode;
            // Intentionally skip ResetToEmpty() here; the caller sends the full state.
        }

                public void SetAlgebraContentRaw(int number, char variable, int power)
        {
            ballNumber    = number;
            ballVariable  = variable;
            variablePower = power;
        }

        public void SetLockedRaw(bool locked) => isLocked = locked;

            // ---- Prefab swapping (see BallPrefabSwapper) ----

    private System.Collections.IEnumerator AllowSwapsNextFrame()
    {
        // Let the first RefreshDisplay (from Awake/Start) settle before we
        // start comparing prefab kinds. Otherwise a freshly spawned triangle
        // with content "-" would immediately try to swap back to a ball.
        yield return null;
        _allowSwaps = true;
    }

    private void CheckForPrefabSwap()
    {
        string contentNow = GetMathContent().Trim();
        bool currDir = GetComponent<DirectionalOperator>() != null;
        bool shouldDir = DirectionalOperator.IsDirectionalOperatorText(contentNow);
        Debug.Log($"[Swap] ball={name} content='{contentNow}' " +
                $"currentlyDirectional={currDir} shouldBeDirectional={shouldDir} " +
                $"_allowSwaps={_allowSwaps} _swapQueued={_swapQueued}");

        if (!_allowSwaps) return;
        if (_swapQueued) return;

        bool currentlyDirectional = GetComponent<DirectionalOperator>() != null;
        bool shouldBeDirectional  = DirectionalOperator.IsDirectionalOperatorText(
            GetMathContent().Trim());

        if (currentlyDirectional == shouldBeDirectional) return;

        _swapQueued = true;
        StartCoroutine(ExecuteSwapNextFrame());
    }

    private System.Collections.IEnumerator ExecuteSwapNextFrame()
    {
        // Defer by one frame: RefreshDisplay is still executing on this
        // object, and we're about to destroy it.
        yield return null;

        if (BallPrefabSwapper.Instance != null)
            BallPrefabSwapper.Instance.Swap(this);
        else
            Debug.LogError("BallPrefabSwapper.Instance is null. Add a " +
                           "BallPrefabSwapper component to a scene GameObject.");

        _swapQueued = false;   // in case Swap didn't destroy us (e.g. no-op)
    }
}
