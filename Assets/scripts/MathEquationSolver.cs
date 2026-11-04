using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

// Parses and evaluates equation text, including implicit multiplication and functions.
public static class MathEquationSolver
{
    public const char UnknownSymbol = '\u0001';
    public const char ImplicitMultiplicationSymbol = '\u0002';

    private enum NodeKind
    {
        Number,
        Variable,
        Unknown,
        Unary,
        Binary,
        Function,
        Postfix,
        Limit
    }

    private sealed class Node
    {
        public NodeKind kind;
        public string operation;
        public Complex number;
        public char variable;
        public Node left;
        public Node right;
    }

    private sealed class Parser
    {
        private readonly string expression;
        private int index;

        public Parser(string expression)
        {
            this.expression = expression;
        }

        public bool TryParse(out Node left, out Node right)
        {
            left = null;
            right = null;
            if (!TryParseAdditive(out left))
            {
                return false;
            }

            SkipWhitespace();
            if (TryConsume('='))
            {
                if (!TryParseAdditive(out right))
                {
                    return false;
                }

                SkipWhitespace();
                if (index < expression.Length && expression[index] == '=')
                {
                    return false;
                }
            }

            SkipWhitespace();
            return index == expression.Length;
        }

        private bool TryParseAdditive(out Node result)
        {
            if (!TryParseMultiplicative(out result))
            {
                return false;
            }

            while (true)
            {
                SkipWhitespace();
                if (!TryConsume('+') && !TryConsume('-'))
                {
                    return true;
                }

                string operation = expression[index - 1].ToString();
                if (!TryParseMultiplicative(out Node right))
                {
                    return false;
                }

                result = CreateNode(NodeKind.Binary, operation, result, right);
            }
        }

        private bool TryParseMultiplicative(out Node result)
        {
            if (!TryParseUnary(out result))
            {
                return false;
            }

            while (true)
            {
                SkipWhitespace();
                if (TryConsume('*') || TryConsume('\u00d7') ||
                    TryConsume(ImplicitMultiplicationSymbol))
                {
                    if (!TryParseUnary(out Node right))
                    {
                        return false;
                    }

                    result = CreateNode(NodeKind.Binary, "*", result, right);
                }
                else if (TryConsume('/') || TryConsume('\u00f7'))
                {
                    if (!TryParseUnary(out Node right))
                    {
                        return false;
                    }

                    result = CreateNode(NodeKind.Binary, "/", result, right);
                }
                else if (StartsPrimary())
                {
                    if (!TryParseUnary(out Node right))
                    {
                        return false;
                    }

                    result = CreateNode(NodeKind.Binary, "*", result, right);
                }
                else
                {
                    return true;
                }
            }
        }

        private bool TryParseUnary(out Node result)
        {
            SkipWhitespace();
            if (TryConsume('+') || TryConsume('-'))
            {
                string operation = expression[index - 1].ToString();

                // Accept one leading sign where an operand is expected, such as
                // 4 + -4 or 4 * -4, but reject repeated signs such as + - + 4.
                // Parsing the power here still allows signed exponents (for example 2^-3).
                if (!TryParsePower(out Node operand))
                {
                    result = null;
                    return false;
                }

                result = CreateNode(NodeKind.Unary, operation, operand, null);
                return true;
            }

            return TryParsePower(out result);
        }

        private bool TryParsePower(out Node result)
        {
            if (!TryParsePostfix(out result))
            {
                return false;
            }

            SkipWhitespace();
            if (TryConsume('^'))
            {
                if (!TryParseUnary(out Node exponent))
                {
                    return false;
                }

                result = CreateNode(NodeKind.Binary, "^", result, exponent);
            }

            return true;
        }

        private bool TryParsePostfix(out Node result)
        {
            if (!TryParsePrimary(out result))
            {
                return false;
            }

            while (true)
            {
                SkipWhitespace();
                if (TryConsume('!'))
                {
                    result = CreateNode(NodeKind.Postfix, "!", result, null);
                }
                else if (TryConsume('%'))
                {
                    result = CreateNode(NodeKind.Postfix, "%", result, null);
                }
                else
                {
                    return true;
                }
            }
        }

        private bool TryParsePrimary(out Node result)
        {
            SkipWhitespace();
            result = null;
            if (index >= expression.Length)
            {
                return false;
            }

            if (expression[index] == UnknownSymbol)
            {
                index++;
                result = new Node { kind = NodeKind.Unknown };
                return true;
            }

            if (expression[index] == '(')
            {
                index++;
                if (!TryParseAdditive(out result))
                {
                    return false;
                }

                SkipWhitespace();
                return TryConsume(')');
            }

            if (char.IsLetter(expression[index]))
            {
                int start = index;
                while (index < expression.Length && char.IsLetter(expression[index]))
                {
                    index++;
                }

                string identifier = expression.Substring(start, index - start).ToLowerInvariant();
                if (identifier == "pi")
                {
                    result = new Node { kind = NodeKind.Number, number = new Complex(Math.PI, 0d) };
                    return true;
                }

                if (identifier == "e")
                {
                    result = new Node { kind = NodeKind.Number, number = new Complex(Math.E, 0d) };
                    return true;
                }

                if (identifier == "i")
                {
                    result = new Node { kind = NodeKind.Number, number = Complex.ImaginaryOne };
                    return true;
                }

                if (IsFunction(identifier))
                {
                    return TryParseFunction(identifier, out result);
                }

                if (identifier.Length == 1)
                {
                    result = new Node { kind = NodeKind.Variable, variable = expression[start] };
                    return true;
                }

                return false;
            }

            int numberStart = index;
            bool hasDigit = false;
            while (index < expression.Length && char.IsDigit(expression[index]))
            {
                hasDigit = true;
                index++;
            }

            if (index < expression.Length && expression[index] == '.')
            {
                index++;
                while (index < expression.Length && char.IsDigit(expression[index]))
                {
                    hasDigit = true;
                    index++;
                }
            }

            if (!hasDigit ||
                !double.TryParse(
                    expression.Substring(numberStart, index - numberStart),
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out double number))
            {
                index = numberStart;
                return false;
            }

            result = new Node { kind = NodeKind.Number, number = new Complex(number, 0d) };
            return true;
        }

        private bool TryParseFunction(string name, out Node result)
        {
            result = null;
            SkipWhitespace();
            if (name == "lim")
            {
                if (!TryConsume('(') ||
                    !TryParseAdditive(out Node body))
                {
                    return false;
                }

                SkipWhitespace();
                if (!TryConsume(','))
                {
                    return false;
                }

                SkipWhitespace();
                if (index >= expression.Length || !char.IsLetter(expression[index]))
                {
                    return false;
                }

                char variable = expression[index++];
                SkipWhitespace();
                if (!TryConsume('-') || !TryConsume('>') ||
                    !TryParseAdditive(out Node point))
                {
                    return false;
                }

                SkipWhitespace();
                if (!TryConsume(')'))
                {
                    return false;
                }

                result = CreateNode(NodeKind.Limit, variable.ToString(), body, point);
                return true;
            }

            if (TryConsume('('))
            {
                if (!TryParseAdditive(out Node argument))
                {
                    return false;
                }

                SkipWhitespace();
                if (!TryConsume(')'))
                {
                    return false;
                }

                result = CreateNode(NodeKind.Function, name, argument, null);
                return true;
            }

            if (!TryParseUnary(out Node firstArgument))
            {
                return false;
            }

            Node combinedArgument = firstArgument;
            while (true)
            {
                SkipWhitespace();
                if (TryConsume(ImplicitMultiplicationSymbol))
                {
                    if (!TryParsePostfix(out Node markedAdjacentArgument))
                    {
                        return false;
                    }

                    combinedArgument = CreateNode(
                        NodeKind.Binary,
                        "*",
                        combinedArgument,
                        markedAdjacentArgument);
                    continue;
                }

                if (!StartsPrimary())
                {
                    break;
                }

                if (!TryParsePostfix(out Node adjacentArgument))
                {
                    return false;
                }

                combinedArgument = CreateNode(
                    NodeKind.Binary,
                    "*",
                    combinedArgument,
                    adjacentArgument);
            }

            result = CreateNode(NodeKind.Function, name, combinedArgument, null);
            return true;
        }

        private bool StartsPrimary()
        {
            return index < expression.Length &&
                   (expression[index] == '(' ||
                    expression[index] == UnknownSymbol ||
                    char.IsDigit(expression[index]) ||
                    char.IsLetter(expression[index]) ||
                    expression[index] == '.');
        }

        private bool TryConsume(char character)
        {
            if (index >= expression.Length || expression[index] != character)
            {
                return false;
            }

            index++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (index < expression.Length && char.IsWhiteSpace(expression[index]))
            {
                index++;
            }
        }
    }

    public static bool TryValidateExpression(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return true;
        }

        return new Parser(expression).TryParse(out _, out _);
    }

    public static HashSet<char> GetVariables(string expression)
    {
        HashSet<char> variables = new HashSet<char>();
        for (int index = 0; index < expression.Length;)
        {
            if (!char.IsLetter(expression[index]))
            {
                index++;
                continue;
            }

            int start = index++;
            while (index < expression.Length && char.IsLetter(expression[index]))
            {
                index++;
            }

            string identifier = expression.Substring(start, index - start);
            if (identifier.Length == 1 && !IsReservedIdentifier(identifier))
            {
                variables.Add(identifier[0]);
            }
        }

        return variables;
    }

    public static string ReplaceVariableWithUnknown(string expression, char variable)
    {
        return TransformIdentifiers(expression, identifier =>
            identifier.Length == 1 && identifier[0] == variable
                ? UnknownSymbol.ToString()
                : identifier);
    }

    public static string SubstituteVariables(
        string expression,
        IDictionary<char, double> variables)
    {
        if (variables == null)
        {
            throw new ArgumentNullException(nameof(variables));
        }

        return TransformIdentifiers(expression, identifier =>
        {
            if (identifier.Length != 1 ||
                !variables.TryGetValue(identifier[0], out double value))
            {
                return identifier;
            }

            return "(" + value.ToString("G", CultureInfo.InvariantCulture) + ")";
        });
    }

    private static string TransformIdentifiers(string expression, Func<string, string> transform)
    {
        StringBuilder result = new StringBuilder(expression.Length);
        for (int index = 0; index < expression.Length;)
        {
            if (!char.IsLetter(expression[index]))
            {
                result.Append(expression[index++]);
                continue;
            }

            int start = index++;
            while (index < expression.Length && char.IsLetter(expression[index]))
            {
                index++;
            }

            result.Append(transform(expression.Substring(start, index - start)));
        }

        return result.ToString();
    }

    private static bool IsReservedIdentifier(string identifier)
    {
        string name = identifier.ToLowerInvariant();
        return name == "pi" || name == "e" || name == "i" || IsFunction(name);
    }

    private static bool IsFunction(string name)
    {
        switch (name)
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

    public static bool TrySolve(string equation, out string result)
    {
        return TrySolve(equation, new Dictionary<char, double>(), out result);
    }

    public static bool TrySolve(
        string equation,
        IDictionary<char, double> variables,
        out string result)
    {
        result = string.Empty;
        if (variables == null)
        {
            throw new ArgumentNullException(nameof(variables));
        }

        if (!new Parser(equation).TryParse(out Node left, out Node right) ||
            right == null ||
            ContainsUnknown(left) == ContainsUnknown(right))
        {
            return false;
        }

        Node unknownExpression = ContainsUnknown(left) ? left : right;
        Node knownExpression = ContainsUnknown(left) ? right : left;
        if (!TryEvaluate(knownExpression, Complex.Zero, ToComplexVariables(variables), out Complex knownValue) ||
            !TrySolveNode(unknownExpression, knownValue, ToComplexVariables(variables), out Complex unknownValue))
        {
            return false;
        }

        if (!TryEvaluate(left, unknownValue, ToComplexVariables(variables), out Complex leftValue) ||
            !TryEvaluate(right, unknownValue, ToComplexVariables(variables), out Complex rightValue) ||
            !ValuesAreEquivalent(leftValue, rightValue))
        {
            return false;
        }

        result = FormatComplex(unknownValue);
        return true;
    }

    public static bool TryEvaluateNumericExpression(
        string expression,
        IDictionary<char, double> variables,
        out double value)
    {
        value = 0d;
        if (!TryEvaluateNumericExpressionComplex(expression, variables, out Complex result) ||
            Math.Abs(result.Imaginary) > 1e-10 * Math.Max(1d, Math.Abs(result.Real)))
        {
            return false;
        }

        value = result.Real;
        return IsFinite(value);
    }

    public static bool TryEvaluateNumericExpressionComplex(
        string expression,
        IDictionary<char, double> variables,
        out Complex value)
    {
        value = Complex.Zero;
        if (variables == null)
        {
            throw new ArgumentNullException(nameof(variables));
        }

        if (!new Parser(expression).TryParse(out Node parsedExpression, out Node right) || right != null)
        {
            return false;
        }

        return !ContainsUnknown(parsedExpression) &&
               TryEvaluate(parsedExpression, Complex.Zero, ToComplexVariables(variables), out value);
    }

    private static Dictionary<char, Complex> ToComplexVariables(IDictionary<char, double> variables)
    {
        Dictionary<char, Complex> converted = new Dictionary<char, Complex>();
        foreach (KeyValuePair<char, double> variable in variables)
        {
            converted.Add(variable.Key, new Complex(variable.Value, 0d));
        }

        return converted;
    }

    private static bool TrySolveNode(
        Node node,
        Complex target,
        IDictionary<char, Complex> variables,
        out Complex value)
    {
        value = Complex.Zero;
        if (node.kind == NodeKind.Unknown)
        {
            value = target;
            return IsFinite(value);
        }

        if (node.kind == NodeKind.Unary)
        {
            return TrySolveNode(
                node.left,
                node.operation == "-" ? -target : target,
                variables,
                out value);
        }

        if (node.kind == NodeKind.Function)
        {
            if (!ContainsUnknown(node.left) ||
                !TryInvertFunction(node.operation, target, out Complex inverse))
            {
                return false;
            }

            return TrySolveNode(node.left, inverse, variables, out value);
        }

        if (node.kind == NodeKind.Postfix)
        {
            if (node.operation != "%")
            {
                return false;
            }

            return TrySolveNode(node.left, target * 100d, variables, out value);
        }

        if (node.kind != NodeKind.Binary)
        {
            return false;
        }

        bool unknownOnLeft = ContainsUnknown(node.left);
        Node knownNode = unknownOnLeft ? node.right : node.left;
        if (knownNode == null || ContainsUnknown(knownNode) ||
            !TryEvaluate(knownNode, Complex.Zero, variables, out Complex knownValue))
        {
            return false;
        }

        Complex nextTarget;
        switch (node.operation)
        {
            case "+":
                nextTarget = target - knownValue;
                break;
            case "-":
                nextTarget = unknownOnLeft ? target + knownValue : knownValue - target;
                break;
            case "*":
                if (knownValue == Complex.Zero)
                {
                    return false;
                }

                nextTarget = target / knownValue;
                break;
            case "/":
                if (unknownOnLeft)
                {
                    nextTarget = target * knownValue;
                }
                else
                {
                    if (target == Complex.Zero)
                    {
                        return false;
                    }

                    nextTarget = knownValue / target;
                }

                break;
            case "^":
                if (unknownOnLeft)
                {
                    if (knownValue == Complex.Zero)
                    {
                        return false;
                    }

                    nextTarget = Complex.Pow(target, Complex.One / knownValue);
                }
                else
                {
                    if (target == Complex.Zero || knownValue == Complex.Zero ||
                        Complex.Log(knownValue) == Complex.Zero)
                    {
                        return false;
                    }

                    nextTarget = Complex.Log(target) / Complex.Log(knownValue);
                }

                break;
            default:
                return false;
        }

        return IsFinite(nextTarget) && TrySolveNode(
            unknownOnLeft ? node.left : node.right,
            nextTarget,
            variables,
            out value);
    }

    private static bool TryInvertFunction(string function, Complex target, out Complex inverse)
    {
        switch (function)
        {
            case "root":
                inverse = target * target;
                return true;
            case "log":
                inverse = Complex.Pow(new Complex(10d, 0d), target);
                return true;
            case "sin":
                inverse = Complex.Asin(target);
                return true;
            case "cos":
                inverse = Complex.Acos(target);
                return true;
            case "tan":
                inverse = Complex.Atan(target);
                return true;
            case "asin":
                inverse = Complex.Sin(target);
                return true;
            case "acos":
                inverse = Complex.Cos(target);
                return true;
            case "atan":
                inverse = Complex.Tan(target);
                return true;
            default:
                inverse = Complex.Zero;
                return false;
        }
    }

    private static bool TryEvaluate(
        Node node,
        Complex unknownValue,
        IDictionary<char, Complex> variables,
        out Complex value)
    {
        value = Complex.Zero;
        if (node.kind == NodeKind.Number)
        {
            value = node.number;
            return true;
        }

        if (node.kind == NodeKind.Unknown)
        {
            value = unknownValue;
            return true;
        }

        if (node.kind == NodeKind.Variable)
        {
            return variables.TryGetValue(node.variable, out value) && IsFinite(value);
        }

        if (node.kind == NodeKind.Limit)
        {
            return TryEvaluateLimit(node, unknownValue, variables, out value);
        }

        if (!TryEvaluate(node.left, unknownValue, variables, out Complex leftValue))
        {
            return false;
        }

        if (node.kind == NodeKind.Unary)
        {
            value = node.operation == "-" ? -leftValue : leftValue;
            return IsFinite(value);
        }

        if (node.kind == NodeKind.Postfix)
        {
            if (node.operation == "%")
            {
                value = leftValue / 100d;
                return IsFinite(value);
            }

            if (node.operation == "!" &&
                Math.Abs(leftValue.Imaginary) < 1e-12 &&
                leftValue.Real >= 0d &&
                leftValue.Real <= 170d &&
                Math.Floor(leftValue.Real) == leftValue.Real)
            {
                double factorial = 1d;
                for (int factor = 2; factor <= (int)leftValue.Real; factor++)
                {
                    factorial *= factor;
                }

                value = new Complex(factorial, 0d);
                return true;
            }

            return false;
        }

        if (node.kind == NodeKind.Function)
        {
            return TryEvaluateFunction(node.operation, leftValue, out value);
        }

        if (!TryEvaluate(node.right, unknownValue, variables, out Complex rightValue))
        {
            return false;
        }

        switch (node.operation)
        {
            case "+":
                value = leftValue + rightValue;
                break;
            case "-":
                value = leftValue - rightValue;
                break;
            case "*":
                value = leftValue * rightValue;
                break;
            case "/":
                if (rightValue == Complex.Zero)
                {
                    return false;
                }

                value = leftValue / rightValue;
                break;
            case "^":
                value = Complex.Pow(leftValue, rightValue);
                break;
            default:
                return false;
        }

        return IsFinite(value);
    }

    private static bool TryEvaluateFunction(string function, Complex argument, out Complex value)
    {
        switch (function)
        {
            case "factorial":
                if (Math.Abs(argument.Imaginary) >= 1e-12 ||
                    argument.Real < 0d ||
                    argument.Real > 170d ||
                    Math.Floor(argument.Real) != argument.Real)
                {
                    value = Complex.Zero;
                    return false;
                }

                double factorial = 1d;
                for (int factor = 2; factor <= (int)argument.Real; factor++)
                {
                    factorial *= factor;
                }

                value = new Complex(factorial, 0d);
                return true;
            case "root":
                value = Complex.Sqrt(argument);
                break;
            case "log":
                value = Complex.Log(argument) / Math.Log(10d);
                break;
            case "sin":
                value = Complex.Sin(argument);
                break;
            case "cos":
                value = Complex.Cos(argument);
                break;
            case "tan":
                value = Complex.Tan(argument);
                break;
            case "asin":
                value = Complex.Asin(argument);
                break;
            case "acos":
                value = Complex.Acos(argument);
                break;
            case "atan":
                value = Complex.Atan(argument);
                break;
            default:
                value = Complex.Zero;
                return false;
        }

        return IsFinite(value);
    }

    private static bool TryEvaluateLimit(
        Node node,
        Complex unknownValue,
        IDictionary<char, Complex> variables,
        out Complex value)
    {
        value = Complex.Zero;
        if (!TryEvaluate(node.right, unknownValue, variables, out Complex point) ||
            Math.Abs(point.Imaginary) > 1e-12)
        {
            return false;
        }

        char variable = node.operation[0];
        Complex? previousAverage = null;
        double initialStep = Math.Max(1d, Math.Abs(point.Real)) * 0.1d;
        for (int iteration = 0; iteration < 9; iteration++)
        {
            double step = initialStep * Math.Pow(0.1d, iteration);
            Dictionary<char, Complex> leftVariables = new Dictionary<char, Complex>(variables)
            {
                [variable] = new Complex(point.Real - step, 0d)
            };
            Dictionary<char, Complex> rightVariables = new Dictionary<char, Complex>(variables)
            {
                [variable] = new Complex(point.Real + step, 0d)
            };
            if (!TryEvaluate(node.left, unknownValue, leftVariables, out Complex leftValue) ||
                !TryEvaluate(node.left, unknownValue, rightVariables, out Complex rightValue))
            {
                continue;
            }

            Complex average = (leftValue + rightValue) / 2d;
            double scale = Math.Max(1d, Math.Max(leftValue.Magnitude, rightValue.Magnitude));
            bool sidesAgree = (leftValue - rightValue).Magnitude <= 1e-5d * scale;
            bool stable = previousAverage.HasValue &&
                (average - previousAverage.Value).Magnitude <= 1e-5d * Math.Max(1d, average.Magnitude);
            if (sidesAgree && stable && IsFinite(average))
            {
                value = average;
                return true;
            }

            previousAverage = average;
        }

        return false;
    }

    private static bool ContainsUnknown(Node node)
    {
        return node != null &&
               (node.kind == NodeKind.Unknown ||
                ContainsUnknown(node.left) ||
                ContainsUnknown(node.right));
    }

    private static Node CreateNode(NodeKind kind, string operation, Node left, Node right)
    {
        return new Node
        {
            kind = kind,
            operation = operation,
            left = left,
            right = right
        };
    }

    private static bool ValuesAreEquivalent(Complex left, Complex right)
    {
        return (left - right).Magnitude <=
               1e-9d * Math.Max(1d, Math.Max(left.Magnitude, right.Magnitude));
    }

    private static bool IsFinite(Complex value)
    {
        return IsFinite(value.Real) && IsFinite(value.Imaginary);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static string FormatComplex(Complex value)
    {
        if (Math.Abs(value.Imaginary) < 1e-12)
        {
            return value.Real.ToString("G", CultureInfo.InvariantCulture);
        }

        if (Math.Abs(value.Real) < 1e-12)
        {
            if (Math.Abs(value.Imaginary - 1d) < 1e-12)
            {
                return "i";
            }

            if (Math.Abs(value.Imaginary + 1d) < 1e-12)
            {
                return "-i";
            }

            return value.Imaginary.ToString("G", CultureInfo.InvariantCulture) + "i";
        }

        string sign = value.Imaginary < 0d ? "-" : "+";
        return value.Real.ToString("G", CultureInfo.InvariantCulture) +
               sign +
               Math.Abs(value.Imaginary).ToString("G", CultureInfo.InvariantCulture) +
               "i";
    }
}
