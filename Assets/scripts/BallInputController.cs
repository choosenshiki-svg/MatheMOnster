using UnityEngine;

// Reads keyboard input for one ball and manages the active edit session.
public class BallInputController : MonoBehaviour
{
    // Only one ball is edited at a time; opening another commits the previous edit.
    private static BallInputController activeEditor;

    // Automatically finish an edit after the player pauses typing.
    private const float EditTimeout = 5f;

    // Ball reference and timing state used to process input once per frame.
    private BallNumber ball;
    private float lastInputTime;
    private int lastInputFrame = -1;

    public bool IsEditing { get; private set; }

    // Find the BallNumber component that owns this input controller.
    private void Awake()
    {
        ball = GetComponent<BallNumber>();
        if (ball == null)
        {
            Debug.LogError("BallInputController requires a BallNumber component.", this);
        }
    }

    // Continue editing the active ball until timeout or Enter is pressed.
    private void Update()
    {
        if (!IsEditing || activeEditor != this || ball == null)
        {
            return;
        }

        if (lastInputFrame == Time.frameCount)
        {
            return;
        }

        if (Time.unscaledTime - lastInputTime >= EditTimeout ||
            Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            FinishEditing();
            return;
        }

        if (ball.InputMode == BallInputMode.DisplayKey)
        {
            ProcessTextInput();
        }
        else
        {
            ProcessNumberInput();
        }
    }

        // Start editing on the first valid key over the ball, or toggle its lock with Enter.
        private void OnMouseOver()
        {
            if (ball == null) return;

        // Bag balls are read-only.
        if (GetComponent<BagBallMarker>() != null) return;
        var nb = GetComponent<NetworkBall>();
        if (nb != null && nb.IsBagBall.Value) return;
            
        if (ball == null)
        {
            return;
        }

        // Don't handle per-ball input while a lasso selection is active.
        if (LassoSelection.GetSelectedBalls().Count > 0)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            ball.ToggleLock();
            if (IsEditing)
            {
                FinishEditing();
            }
            return;
        }

        if (IsEditing)
        {
            return;
        }

        if (ball.InputMode == BallInputMode.DisplayKey)
        {
            if (!HasTextInput())
            {
                return;
            }

            BeginEditing();
            ProcessTextInput();
            return;
        }

        if (!HasNumberInput(ball.InputMode == BallInputMode.Algebra))
        {
            return;
        }

        BeginEditing();
        ProcessNumberInput();
    }

    // Commit current text when the pointer leaves the ball.
    private void OnMouseExit()
    {
        if (IsEditing)
        {
            FinishEditing();
        }
    }

    // Stop editing without applying an additional change.
    public void CancelEditing()
    {
        IsEditing = false;
        if (activeEditor == this)
        {
            activeEditor = null;
        }
    }

    // Read a newly pressed digit from either the number row or numeric keypad.
    private bool TryReadDigit(out int digit)
    {
        for (int i = 0; i <= 9; i++)
        {
            KeyCode numberKey = (KeyCode)((int)KeyCode.Alpha0 + i);
            KeyCode keypadNumberKey = (KeyCode)((int)KeyCode.Keypad0 + i);
            if (Input.GetKeyDown(numberKey) || Input.GetKeyDown(keypadNumberKey))
            {
                digit = i;
                return true;
            }
        }

        digit = 0;
        return false;
    }

    // Read a newly pressed alphabetic key and return its lowercase letter.
    private bool TryReadLetter(out char letter)
    {
        for (int i = 0; i < 26; i++)
        {
            KeyCode letterKey = (KeyCode)((int)KeyCode.A + i);
            if (Input.GetKeyDown(letterKey))
            {
                letter = (char)('a' + i);
                return true;
            }
        }

        letter = '\0';
        return false;
    }

    // Detect keys that are valid in algebra/number entry mode.
    private bool HasNumberInput(bool allowLetters)
    {
        return TryReadDigit(out _) ||
               Input.GetKeyDown(KeyCode.Backspace) ||
               allowLetters && TryReadLetter(out _) ||
               Input.GetKeyDown(KeyCode.Plus) ||
               Input.GetKeyDown(KeyCode.KeypadPlus) ||
               Input.GetKeyDown(KeyCode.Equals) ||
               Input.GetKeyDown(KeyCode.Minus) ||
               Input.GetKeyDown(KeyCode.KeypadMinus) ||
               Input.GetKeyDown(KeyCode.KeypadMultiply) ||
               Input.GetKeyDown(KeyCode.Alpha8) ||
               Input.GetKeyDown(KeyCode.Slash) ||
               Input.GetKeyDown(KeyCode.KeypadDivide) ||
               allowLetters &&
               (Input.GetKeyDown(KeyCode.LeftBracket) ||
                Input.GetKeyDown(KeyCode.At) ||
                Input.GetKeyDown(KeyCode.Alpha2) &&
                (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)));
    }

    // Apply one numeric or algebraic key to the current ball value.
    private void ProcessNumberInput()
    {
        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            if (ball.ClearContent())
            {
                MarkInputChanged();
            }
            return;
        }

        int number = ball.NumberValue;
        char variable = ball.Variable;
        int power = ball.VariablePower;

        if (ball.InputMode == BallInputMode.Algebra && (Input.GetKeyDown(KeyCode.At) ||
            (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) &&
            Input.GetKeyDown(KeyCode.Alpha2)))
        {
            ball.SetAlgebraContent(number, '\0', 0);
            MarkInputChanged();
            return;
        }

        if (ball.InputMode == BallInputMode.Algebra && TryReadLetter(out char letter))
        {
            if (variable == letter)
            {
                power++;
            }
            else
            {
                variable = letter;
                power = 1;
                if (number == 0)
                {
                    number = 1;
                }
            }

            ball.SetAlgebraContent(number, variable, power);
            MarkInputChanged();
            return;
        }

        if (ball.InputMode == BallInputMode.Algebra && Input.GetKeyDown(KeyCode.LeftBracket))
        {
            if (power > 1)
            {
                power--;
            }
            else if (power == 1)
            {
                variable = '\0';
                power = 0;
            }

            ball.SetAlgebraContent(number, variable, power);
            MarkInputChanged();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Plus) || Input.GetKeyDown(KeyCode.KeypadPlus) ||
            (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) &&
            Input.GetKeyDown(KeyCode.Equals))
        {
            number++;
            if (number == 0)
            {
                variable = '\0';
                power = 0;
            }

            ball.SetAlgebraContent(number, variable, power);
            MarkInputChanged();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            number--;
            if (number == 0)
            {
                variable = '\0';
                power = 0;
            }

            ball.SetAlgebraContent(number, variable, power);
            MarkInputChanged();
            return;
        }

        if (Input.GetKeyDown(KeyCode.KeypadMultiply) ||
            (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) &&
            Input.GetKeyDown(KeyCode.Alpha8))
        {
            ball.SetAlgebraContent(number * 2, variable, power);
            MarkInputChanged();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Slash) || Input.GetKeyDown(KeyCode.KeypadDivide))
        {
            ball.SetAlgebraContent(Mathf.FloorToInt(number / 2f), variable, power);
            MarkInputChanged();
            return;
        }

        if (TryReadDigit(out int digit))
        {
            ball.SetAlgebraContent(digit, variable, power);
            MarkInputChanged();
        }
    }

    // Detect typed characters, backspace, or space in free-form text mode.
    private bool HasTextInput()
    {
        return !string.IsNullOrEmpty(Input.inputString) ||
               Input.GetKeyDown(KeyCode.Backspace) ||
               Input.GetKeyDown(KeyCode.Space);
    }

    // Apply text-mode input; space resets the ball to empty.
    private void ProcessTextInput()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ball.ResetToEmpty();
            MarkInputChanged();
            return;
        }

        if (ball.ApplyTextInput(Input.inputString, Input.GetKeyDown(KeyCode.Backspace)))
        {
            MarkInputChanged();
        }
    }

    // Make this the active editor, finishing any other ball's edit first.
    private void BeginEditing()
    {
        if (activeEditor != null && activeEditor != this)
        {
            activeEditor.FinishEditing();
        }

        activeEditor = this;
        IsEditing = true;
    }

    // Record activity time/frame and refresh the ball's visible label.
    private void MarkInputChanged()
    {
        lastInputTime = Time.unscaledTime;
        lastInputFrame = Time.frameCount;
        ball.RefreshDisplay();
    }

    // Commit the edit and release the global active-editor slot.
    private void FinishEditing()
    {
        IsEditing = false;
        ball.FinishEditing();
        if (activeEditor == this)
        {
            activeEditor = null;
        }
    }
}
