using UnityEngine;

// Creates each ball's text label and colors the ball according to its content type.
public class BallAppearance : MonoBehaviour
{
    private static readonly Color DefaultLabelColor = new Color(0.85f, 0.86f, 0.86f);
    private static readonly Color EquationIdleColor = new Color(0.20f, 0.45f, 0.95f);   // blue
    private static readonly Color CorrectEquationBallColor = new Color(0.28f, 0.72f, 0.32f);   // green
    private static readonly Color AmbiguousOperandColor = new Color(0.92f, 0.16f, 0.18f);   // red


    private static readonly Color[] NumberAndSymbolColors =
    {
        new Color(0.22f, 0.24f, 0.26f),
        new Color(0.36f, 0.38f, 0.40f),
        new Color(0.50f, 0.52f, 0.54f),
        new Color(0.64f, 0.66f, 0.68f),
        new Color(0.78f, 0.80f, 0.82f)
    };

    private static readonly Color[] AlgebraColors =
    {
        new Color(1.00f, 0.50f, 0.04f),
        new Color(1.00f, 0.58f, 0.08f),
        new Color(0.96f, 0.46f, 0.02f),
        new Color(1.00f, 0.66f, 0.12f),
        new Color(0.92f, 0.52f, 0.04f),
        new Color(1.00f, 0.42f, 0.01f)
    };

    private static readonly Color[] MixedContentColors =
    {
        new Color(1.00f, 0.76f, 0.08f),
        new Color(1.00f, 0.84f, 0.12f),
        new Color(0.94f, 0.70f, 0.04f),
        new Color(1.00f, 0.90f, 0.20f)
    };

    private static readonly Color[] MathSymbolColors =
    {
        new Color(0.16f, 0.39f, 0.78f),
        new Color(0.20f, 0.51f, 0.86f),
        new Color(0.28f, 0.62f, 0.91f),
        new Color(0.12f, 0.45f, 0.70f),
        new Color(0.35f, 0.55f, 0.82f)
    };

    private static readonly Color[] SolverOutputColors =
    {
        new Color(0.20f, 0.62f, 0.24f),
        new Color(0.28f, 0.72f, 0.32f),
        new Color(0.36f, 0.58f, 0.18f),
        new Color(0.16f, 0.50f, 0.22f),
        new Color(0.42f, 0.76f, 0.30f),
        new Color(0.24f, 0.66f, 0.38f)
    };

    private static readonly Color[] InvalidMathColors =
    {
        new Color(0.92f, 0.16f, 0.18f),
        new Color(0.82f, 0.10f, 0.12f),
        new Color(1.00f, 0.28f, 0.24f),
        new Color(0.72f, 0.08f, 0.10f)
    };

    private static readonly Color[] OtherContentColors =
    {
        new Color(0.20f, 0.66f, 0.67f),
        new Color(0.20f, 0.51f, 0.76f),
        new Color(0.35f, 0.73f, 0.51f),
        new Color(0.25f, 0.75f, 0.78f),
        new Color(0.34f, 0.62f, 0.57f),
        new Color(0.32f, 0.44f, 0.76f)
    };

    // Rim color drawn under the ball sprite when this piece is in the player's hand.
    private static readonly Color HandPieceRimColor = new Color(1f, 0.85f, 0.2f, 0.9f);
    private SpriteRenderer handRim;
    private bool isInHand;

    // Cached render components and the last selected color category.
    private TextMesh label;
    private MeshRenderer labelRenderer;
    private SpriteRenderer ballRenderer;
    private Color defaultBallColor;
    private Color baseBallColor;
    private Color baseLabelColor = DefaultLabelColor;
    private int currentColorKind = -1;
    private bool currentIsSolverOutput;
    private byte equationState;  
    private bool isAmbiguousOperand;
    

    

    public void SetEquationState(byte state)
    {
        if (equationState == state) return;
        equationState = state;
        ApplyBallColor();
    }

    // Cache the ball sprite and create a child TextMesh for its label.
    private void Awake()
    {
        ballRenderer = GetComponent<SpriteRenderer>();
        if (ballRenderer != null)
        {
            Color c = ballRenderer.color;
            if (c.a < 0.01f) c = Color.white;
            ballRenderer.color = c;
            defaultBallColor = c;
            baseBallColor = c;
        }

        CreateLabel();
    }

    // Refit the text each frame so changes in text length remain inside the ball.
    private void LateUpdate()
    {
        if (labelRenderer != null && labelRenderer.enabled)
        {
            FitLabel();
        }
    }

    // Set the label and optionally classify its text to choose a ball color.
    public void SetContent(string content, bool colorFromContents, bool isSolverOutput)
    {
        Debug.Log($"[Appearance] SetContent('{content}', colorFromContents={colorFromContents}, solverOut={isSolverOutput}) " +
              $"on {name}, label={(label == null ? "NULL" : "ok")}, " +
              $"ballRenderer={(ballRenderer == null ? "NULL" : "ok")}");
        
        if (label == null)
        {
            return;
        }

      
        label.text = content;
        labelRenderer.enabled = !string.IsNullOrEmpty(content);

        if (!colorFromContents)
        {
            currentColorKind = -1;
            currentIsSolverOutput = false;
            baseLabelColor = DefaultLabelColor;
            ApplyLabelColor();
            if (ballRenderer != null)
            {
                baseBallColor = string.IsNullOrEmpty(content) ? Color.white : defaultBallColor;
                ApplyBallColor();
            }

            return;
        }

        MathContentKind kind = MathExpression.IsMathematicalSyntaxContent(content) &&
            !MathExpression.HasValidSyntax(content)
            ? MathContentKind.InvalidMath
            : MathExpression.Classify(content);
        baseLabelColor = kind == MathContentKind.InvalidMath
            ? new Color(0.16f, 0.17f, 0.18f)
            : DefaultLabelColor;
        ApplyLabelColor();

        int colorKind = (int)kind;
        if (colorKind == currentColorKind && isSolverOutput == currentIsSolverOutput)
        {
            ApplyBallColor();
            return;
        }

        currentColorKind = colorKind;
        currentIsSolverOutput = isSolverOutput;
        if (ballRenderer == null)
        {
            return;
        }

        if (isSolverOutput)
        {
            baseBallColor = SolverOutputColors[Random.Range(0, SolverOutputColors.Length)];
            ApplyBallColor();
            return;
        }

        if (kind == MathContentKind.Empty)
        {
            baseBallColor = Color.white;
            ApplyBallColor();
            return;
        }

        Color[] colors = kind switch
        {
            MathContentKind.Algebra => AlgebraColors,
            MathContentKind.Mixed => MixedContentColors,
            MathContentKind.Operator => MathSymbolColors,
            MathContentKind.InvalidMath => InvalidMathColors,
            MathContentKind.Other => OtherContentColors,
            _ => NumberAndSymbolColors
        };
        baseBallColor = colors[Random.Range(0, colors.Length)];
        ApplyBallColor();
    }

    // Early-out if the value hasn't changed, so per-tick re-application is cheap.
    public void SetAmbiguousOperand(bool isAmbiguous)
    {
        if (isAmbiguousOperand == isAmbiguous)
        {
            return;
        }

        isAmbiguousOperand = isAmbiguous;
        ApplyBallColor();
    }

   
    private void ApplyLabelColor()
    {
        if (label != null)
        {
            label.color = baseLabelColor;
        }
    }

    private void ApplyBallColor()
    {
        if (ballRenderer == null) return;
    Debug.Log($"[Appearance] ApplyBallColor on {name}, " +
              $"eqState={equationState}, isInHand={isInHand}, isAmbiguous={isAmbiguousOperand}, " +
              $"baseBallColor={baseBallColor}, currentColor={(ballRenderer != null ? ballRenderer.color.ToString() : "null")}");
    // ...


        // Priority:
        // 1. '=' ball green (correct)
        // 2. '=' ball red (incorrect)
        // 3. '=' ball blue (idle)
        // 4. red (ambiguous operand / function)
        // 5. hand piece (untouched)
        // 6. normal base color
        if (equationState == 1) { ballRenderer.color = CorrectEquationBallColor; return; }
        if (equationState == 2) { ballRenderer.color = AmbiguousOperandColor; return; }
        if (equationState == 0 && IsEqualsBall()) { ballRenderer.color = EquationIdleColor; return; }

        if (isAmbiguousOperand) { ballRenderer.color = AmbiguousOperandColor; return; }
        if (isInHand) return;
        ballRenderer.color = baseBallColor;
    }

    // Helper: is this ball the '=' ball?
    private bool IsEqualsBall()
    {
        // The '=' ball has no label styling we can rely on here, so check
        // the GameObject's stored state. If BallAppearance doesn't know the
        // ball's content, add a public bool field set by BallNumber.
        return _isEqualsBall;
    }

    private bool _isEqualsBall;
    public void MarkAsEqualsBall() => _isEqualsBall = true;

    // Create and configure the child text mesh used to display ball contents.
    private void CreateLabel()
    {
        GameObject labelObject = new GameObject("Ball Number");
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition = new Vector3(0f, 0f, 0.02f);

        label = labelObject.AddComponent<TextMesh>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.characterSize = 0.2f;
        label.fontSize = 64;
        label.fontStyle = FontStyle.Bold;
        label.color = DefaultLabelColor;
        labelRenderer = labelObject.GetComponent<MeshRenderer>();
        labelRenderer.sortingOrder = ballRenderer != null ? ballRenderer.sortingOrder + 1 : 1;
        labelRenderer.enabled = false;
    }

    // Scale and orient the label so it stays centered and fits within the sprite.
    private void FitLabel()
    {
        if (ballRenderer == null || labelRenderer == null)
        {
            return;
        }

        Transform labelTransform = label.transform;
        labelTransform.localScale = Vector3.one;

        Vector3 labelSize = labelRenderer.bounds.size;
        Vector3 ballSize = ballRenderer.bounds.size;
        if (labelSize.x <= Mathf.Epsilon || labelSize.y <= Mathf.Epsilon)
        {
            return;
        }

        float widthScale = ballSize.x * 0.62f / labelSize.x;
        float heightScale = ballSize.y * 0.62f / labelSize.y;
        float fitScale = Mathf.Min(1f, widthScale, heightScale);
        labelTransform.localScale = Vector3.one * fitScale;
        labelTransform.rotation = Quaternion.identity;
    }

    /// <summary>
    /// Draws (or removes) a colored ring around the ball to mark it as a hand piece.
    /// Only affects visuals — nothing network-related.
    /// </summary>
    public void SetHandPiece(bool inHand)
    {
        if (isInHand == inHand) return;
        isInHand = inHand;

        if (inHand)
        {
            if (handRim == null) handRim = CreateHandRim();
            handRim.enabled = true;
            if (ballRenderer != null)
            {
                Color c = ballRenderer.color;
                float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                ballRenderer.color = Color.Lerp(c, new Color(lum, lum, lum, c.a), 0.35f);
            }
        }
        else
        {
            if (handRim != null) handRim.enabled = false;
            ApplyBallColor();
        }
    }

    private SpriteRenderer CreateHandRim()
    {
        var go = new GameObject("Hand Rim");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = ballRenderer != null ? ballRenderer.sprite : null;
        sr.color = HandPieceRimColor;
        sr.sortingOrder = ballRenderer != null ? ballRenderer.sortingOrder - 1 : -1;
        go.transform.localScale = Vector3.one * 1.12f;
        return sr;
    }
}