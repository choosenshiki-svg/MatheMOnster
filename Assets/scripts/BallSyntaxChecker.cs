using System.Collections.Generic;
using UnityEngine;

// Per-ball local syntax validation. Runs before the rewrite engine and
// marks balls red if they have no valid role in their immediate neighborhood.
// Under the new semantics:
//   - numbers, algebra, '/N', '/x'       -> operands  (merge by adjacency)
//   - sin/cos/..., %, !, 1/x              -> functions (consume one operand)
//   - -, /, ^, ^N, ^x, *, +               -> operators
//   - (, )                                -> brackets
//   - =                                   -> region splitter
public static class BallSyntaxChecker
{
    public static void MarkLocalSyntaxErrors(
        List<BallNumber> cluster,
        Collider2D[] colliders,
        HashSet<BallNumber> ambiguous)
    {
        var neighborsByBall = new Dictionary<BallNumber, List<BallNumber>>();
        for (int i = 0; i < cluster.Count; i++)
        {
            var list = new List<BallNumber>();
            if (colliders[i] != null)
            {
                for (int j = 0; j < cluster.Count; j++)
                {
                    if (i == j || colliders[j] == null) continue;
                    if (BallConnectionRules.AreConnected(colliders[i], colliders[j]))
                        list.Add(cluster[j]);
                }
            }
            neighborsByBall[cluster[i]] = list;
        }

        MarkLocalSyntaxErrors(cluster, neighborsByBall, ambiguous);
    }

    public static void MarkLocalSyntaxErrors(
        List<BallNumber> cluster,
        Dictionary<BallNumber, List<BallNumber>> neighborsByBall,
        HashSet<BallNumber> ambiguous)
    {
        foreach (BallNumber ball in cluster)
        {
            if (ball == null) continue;

            string content = ball.GetMathContent().Trim();
            if (content.Length == 0) continue;         // empty ball: never red

            if (!neighborsByBall.TryGetValue(ball, out var neighbors))
                continue;

            int operandNeighbors = 0;
            int operatorNeighbors = 0;
            int functionNeighbors = 0;
            int bracketNeighbors = 0;

            foreach (BallNumber nb in neighbors)
            {
                string nbText = nb.GetMathContent().Trim();
                if (IsFunction(nbText)) functionNeighbors++;

                if (IsBracket(nbText))                       { bracketNeighbors++; continue; }
                if (IsOperand(nbText) || nbText.Length == 0) { operandNeighbors++; }
                if (IsOperator(nbText))                      { operatorNeighbors++; continue; }
            }

            int argumentSlots = operandNeighbors + bracketNeighbors + functionNeighbors;

            bool red = false;

            // Two operators touching → both red.
            if (IsOperator(content))
            {
                foreach (BallNumber nb in neighbors)
                {
                    if (IsOperator(nb.GetMathContent().Trim())) { red = true; break; }
                }
            }

            if (IsFunction(content))
            {
                // Functions consume exactly one operand-group.
                bool hasBracketNeighbor = false;
                int operandCount = 0;

                foreach (BallNumber nb in neighbors)
                {
                    string nbText = nb.GetMathContent().Trim();
                    if (IsBracket(nbText)) { hasBracketNeighbor = true; continue; }
                    if (IsOperand(nbText) || nbText.Length == 0) operandCount++;
                }

                if (!hasBracketNeighbor && operandCount != 1) red = true;
                // If a bracket neighbor exists, the bracket will resolve first and
                // become a single group; the arity check runs again post-bracket.
            }
            else if (IsDirectionalOperator(content))
            {
                if (argumentSlots != 2) red = true;
            }
            else if (MathSyntax.IsPowerBall(content))
            {
                if (argumentSlots != 1) red = true;
            }
            else if (IsOperator(content))
            {
                // '*' and '+' need at least 2 operand slots (brackets count).
                if (argumentSlots < 2) red = true;
            }
            else if (IsOperand(content))
            {
                // A plain operand touching 2+ functions is contested.
                if (functionNeighbors >= 2) red = true;
            }

            if (red) ambiguous.Add(ball);
        }
    }

    // --- helpers ---

    private static bool IsOperand(string content)
    {
        if (content.Length == 0) return false;
        if (double.TryParse(content,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            return true;
        if (content.Length == 1 && MathSyntax.IsVariableLetter(content[0])) return true;
        if (MathSyntax.IsConstantName(content)) return true;
        // '/', '/N', '/x' are NUMBERS under the new rules.
        if (MathSyntax.IsFractionBall(content)) return true;
        return false;
    }

    private static bool IsFunction(string content)
    {
        string lower = content.ToLowerInvariant();
        if (MathSyntax.IsUnaryFunctionName(lower)) return true;
        if (lower == "%" || lower == "!" || lower == "1/x") return true;
        return false;
    }

    private static bool IsOperator(string content)
    {
        return DirectionalOperator.IsDirectionalOperatorText(content) ||
               MathSyntax.IsPowerBall(content) ||
               content == "*" || content == "\u00d7" || content == "+";
    }

    private static bool IsDirectionalOperator(string content)
    {
        return DirectionalOperator.IsDirectionalOperatorText(content);
    }

    private static bool IsBracket(string content)
    {
        return content == "(" || content == ")";
    }
}