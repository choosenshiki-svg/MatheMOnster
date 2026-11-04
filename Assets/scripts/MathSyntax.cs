// Shared character tests and syntax validation for mathematical input text.
public static class MathSyntax
{
    // Variables are currently single English letters, upper- or lowercase.
    public static bool IsVariableLetter(char character)
    {
        return character >= 'a' && character <= 'z' ||
               character >= 'A' && character <= 'Z';
    }

    // Return whether a character is one of the symbols recognized by the UI/parser.
    public static bool IsMathSymbol(char character)
    {
        return character == '+' ||
               character == '-' ||
               character == '*' || character == '/' ||
               character == '\u00d7' || character == '\u00f7' ||
               character == '=' || character == '^' ||
               character == '\u2205' ||
               character == '(' || character == ')' ||
               character == '[' || character == ']' ||
               character == '.' || character == '%' ||
               character == '!' || character == ',' ||
               character == '>';
    }

    // Check whether content contains any number, variable, or recognized math symbol.
    public static bool IsMathematicalSyntaxContent(string content)
    {
        foreach (char character in content)
        {
            if (char.IsDigit(character) || IsVariableLetter(character) || IsMathSymbol(character))
            {
                return true;
            }
        }

        return false;
    }

    // Check expression structure, optional single equality, and complete input use.
    public static bool HasValidSyntax(string content)
    {
        return string.IsNullOrWhiteSpace(content) ||
               IsStandaloneOperator(content.Trim()) ||
               IsUnaryFunctionName(content.Trim()) ||
               IsUnaryPrefixName(content.Trim()) ||
               content.Trim() == "(" ||
               content.Trim() == ")" ||
               MathEquationSolver.TryValidateExpression(content);
    }

    public static bool IsFunctionName(string identifier)
    {
        switch (identifier.ToLowerInvariant())
        {
            case "root":
            case "log":
            case "factorial":
            case "sin":
            case "cos":
            case "tan":
            case "asin":
            case "acos":
            case "atan":
            case "lim":
                return true;
            default:
                return false;
        }
    }

    public static bool IsUnaryFunctionName(string identifier)
    {
        string name = identifier.ToLowerInvariant();
        return name == "root" || name == "log" || name == "factorial" ||
               name == "sin" || name == "cos" || name == "tan" ||
               name == "asin" || name == "acos" || name == "atan";
    }

    public static bool IsConstantName(string identifier)
    {
        string name = identifier.ToLowerInvariant();
        return name == "pi" || name == "e" || name == "i";
    }

    // Standalone operators are allowed while users are composing an expression.
    private static bool IsStandaloneOperator(string content)
    {
        return content == "+" || content == "-" ||
               content == "*" || content == "/" || content == "^" ||
               content == "\u00d7" || content == "\u00f7" ||
               content == "=" || content == "%" ||
               content == "!" || content == "1/x";
    }

    // ============================================================
    // Explicit reciprocal and power-token helpers (/N, /x, ^N, ^x).
    // ============================================================

    // Bare symbols are directional operators; these tokens require a suffix.
    public static bool IsUnaryPrefixName(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        char prefix = text[0];
        if (prefix != '/' && prefix != '^') return false;

        string rest = text.Substring(1).Trim();
        return rest.Length > 0 && IsValidUnaryPrefixSuffix(rest);
    }

    // Extract the suffix from an explicit reciprocal or power token.
    public static string UnaryPrefixBoundValue(string text)
    {
        if (!IsUnaryPrefixName(text)) return "";
        return text.Substring(1).Trim();
    }

    // Fraction tokens are numeric operands; suffixed power tokens are powers.
    public static bool IsFractionBall(string text)
    {
        return IsUnaryPrefixName(text) && text[0] == '/' &&
               UnaryPrefixBoundValue(text).Length > 0;
    }

    public static bool IsPowerBall(string text)
    {
        return IsUnaryPrefixName(text) && text[0] == '^' &&
               UnaryPrefixBoundValue(text).Length > 0;
    }

    private static bool IsValidUnaryPrefixSuffix(string rest)
    {
        if (rest.Length == 1 && char.IsDigit(rest[0])) return true;          // /3
        if (rest.Length == 1 && IsVariableLetter(rest[0])) return true;      // /x
        if (double.TryParse(rest,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out _)) return true;                                         // /12, /3.5
        if (IsConstantName(rest)) return true;                               // /pi
        return false;
    }
}