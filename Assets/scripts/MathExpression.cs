// Categories used to select a ball's color and detect invalid-looking content.
public enum MathContentKind
{
    // No content has been entered.
    Empty,
    // Ordinary numeric text or a symbol that is not an operator.
    NumberOrSymbol,
    // Text containing one or more variables but no separate number.
    Algebra,
    // Text containing both variables and separate numeric values.
    Mixed,
    // A standalone arithmetic/equality operator.
    Operator,
    // Text made of math characters that fails the syntax check.
    InvalidMath,
    // Text containing characters outside the recognized math syntax.
    Other
}

// Classifies mathematical text and forwards syntax/simplification requests.
public static class MathExpression
{
    // Distinguish numbers, variables, operators, mixed content, and other text.
    public static MathContentKind Classify(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return MathContentKind.Empty;
        }

        bool hasVariable = false;
        bool hasStandaloneNumber = false;
        bool hasMathSymbol = false;
        bool hasOtherCharacter = false;

        for (int i = 0; i < content.Length; i++)
        {
            char character = content[i];
            if (char.IsLetter(character))
            {
                int identifierStart = i;
                while (i + 1 < content.Length && char.IsLetter(content[i + 1]))
                {
                    i++;
                }

                string identifier = content.Substring(identifierStart, i - identifierStart + 1);
                if (MathSyntax.IsFunctionName(identifier))
                {
                    hasMathSymbol = true;
                }
                else if (MathSyntax.IsConstantName(identifier))
                {
                    hasStandaloneNumber = true;
                }
                else
                {
                    hasVariable = true;
                }
            }
            else if (char.IsDigit(character))
            {
                int numberStart = i;
                while (i + 1 < content.Length && char.IsDigit(content[i + 1]))
                {
                    i++;
                }

                bool nextToVariable =
                    numberStart > 0 && MathSyntax.IsVariableLetter(content[numberStart - 1]) ||
                    i + 1 < content.Length && MathSyntax.IsVariableLetter(content[i + 1]);
                bool isExponent = numberStart > 1 &&
                    content[numberStart - 1] == '^' &&
                    MathSyntax.IsVariableLetter(content[numberStart - 2]);
                if (!nextToVariable && !isExponent)
                {
                    hasStandaloneNumber = true;
                }
            }
            else if (character == '.' &&
                     i > 0 && char.IsDigit(content[i - 1]) &&
                     i + 1 < content.Length && char.IsDigit(content[i + 1]))
            {
                continue;
            }
            else if ((character == '+' || character == '-') &&
                     IsLeadingNumericSign(content, i))
            {
                continue;
            }
            else if (MathSyntax.IsMathSymbol(character))
            {
                hasMathSymbol = true;
            }
            else if (!char.IsWhiteSpace(character))
            {
                hasOtherCharacter = true;
            }
        }

        if (hasOtherCharacter)
        {
            return MathContentKind.Other;
        }

        if (hasVariable && hasStandaloneNumber)
        {
            return MathContentKind.Mixed;
        }

        if (hasVariable)
        {
            return MathContentKind.Algebra;
        }

        return hasMathSymbol ? MathContentKind.Operator : MathContentKind.NumberOrSymbol;
    }

    // A leading sign belongs to a numeric literal, not to a standalone operator.
    private static bool IsLeadingNumericSign(string content, int signIndex)
    {
        for (int index = 0; index < signIndex; index++)
        {
            if (!char.IsWhiteSpace(content[index]))
            {
                return false;
            }
        }

        int nextIndex = signIndex + 1;
        while (nextIndex < content.Length && char.IsWhiteSpace(content[nextIndex]))
        {
            nextIndex++;
        }

        return nextIndex < content.Length && char.IsDigit(content[nextIndex]);
    }

    // Report whether any recognized mathematical character occurs in the string.
    public static bool IsMathematicalSyntaxContent(string content)
    {
        return MathSyntax.IsMathematicalSyntaxContent(content);
    }

    // Validate the expression grammar used by typed mathematical content.
    public static bool HasValidSyntax(string content)
    {
        return MathSyntax.HasValidSyntax(content);
    }

    // Simplify like terms when this text is a supported addition-only expression.
    public static bool TrySimplifyAddition(string expression, out string simplified)
    {
        return MathSimplifier.TrySimplifyAddition(expression, out simplified);
    }
}
