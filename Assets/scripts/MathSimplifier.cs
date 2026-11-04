using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Combines like terms in simple addition/subtraction expressions without evaluation.
public static class MathSimplifier
{
    // A term is a coefficient times either a constant or one variable power.
    private struct Term
    {
        public char variable;
        public int power;
        public int coefficient;
    }

    // Parse a sum of terms, combine equal variable/power pairs, and format the result.
    public static bool TrySimplifyAddition(string expression, out string simplified)
    {
        simplified = expression;
        List<Term> terms = new List<Term>();
        int index = 0;
        bool firstTerm = true;

        while (true)
        {
            SkipWhitespace(expression, ref index);
            if (index >= expression.Length)
            {
                break;
            }

            int sign = 1;
            bool foundSign = false;
            while (index < expression.Length &&
                (expression[index] == '+' || expression[index] == '-'))
            {
                if (expression[index] == '-')
                {
                    sign *= -1;
                }

                foundSign = true;
                index++;
                SkipWhitespace(expression, ref index);
            }

            if (!firstTerm && !foundSign)
            {
                return false;
            }

            if (!TryParseTerm(expression, ref index, sign, out Term term))
            {
                return false;
            }

            int matchingTerm = terms.FindIndex(existing =>
                existing.variable == term.variable && existing.power == term.power);
            if (matchingTerm >= 0)
            {
                Term existing = terms[matchingTerm];
                long combinedCoefficient = (long)existing.coefficient + term.coefficient;
                if (combinedCoefficient > int.MaxValue || combinedCoefficient < int.MinValue)
                {
                    return false;
                }

                existing.coefficient = (int)combinedCoefficient;
                terms[matchingTerm] = existing;
            }
            else
            {
                terms.Add(term);
            }

            firstTerm = false;
        }

        if (firstTerm)
        {
            return false;
        }

        StringBuilder result = new StringBuilder();
        foreach (Term term in terms)
        {
            if (term.coefficient == 0)
            {
                continue;
            }

            bool negative = term.coefficient < 0;
            long absoluteCoefficient = System.Math.Abs((long)term.coefficient);
            if (result.Length > 0)
            {
                result.Append(negative ? "-" : "+");
            }
            else if (negative)
            {
                result.Append("-");
            }

            if (term.variable == '\0' || absoluteCoefficient != 1)
            {
                result.Append(absoluteCoefficient.ToString(CultureInfo.InvariantCulture));
            }

            if (term.variable != '\0')
            {
                result.Append(term.variable);
                if (term.power > 1)
                {
                    result.Append("^");
                    result.Append(term.power.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        simplified = result.Length == 0 ? "0" : result.ToString();
        return true;
    }

    // Parse one signed coefficient, optional variable, and optional exponent.
    private static bool TryParseTerm(string expression, ref int index, int sign, out Term term)
    {
        term = new Term();
        int numberStart = index;
        while (index < expression.Length && char.IsDigit(expression[index]))
        {
            index++;
        }

        int coefficient = 1;
        if (index > numberStart &&
            !int.TryParse(
                expression.Substring(numberStart, index - numberStart),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out coefficient))
        {
            return false;
        }

        SkipWhitespace(expression, ref index);
        char variable = '\0';
        int power = 0;
        if (index < expression.Length && MathSyntax.IsVariableLetter(expression[index]))
        {
            variable = expression[index];
            power = 1;
            index++;
            SkipWhitespace(expression, ref index);

            if (index < expression.Length && expression[index] == '^')
            {
                index++;
                SkipWhitespace(expression, ref index);
                int powerStart = index;
                while (index < expression.Length && char.IsDigit(expression[index]))
                {
                    index++;
                }

                if (powerStart == index ||
                    !int.TryParse(
                        expression.Substring(powerStart, index - powerStart),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out power))
                {
                    return false;
                }

                if (power == 0)
                {
                    variable = '\0';
                }
            }
        }
        else if (numberStart == index)
        {
            return false;
        }

        long signedCoefficient = (long)coefficient * sign;
        if (signedCoefficient > int.MaxValue || signedCoefficient < int.MinValue)
        {
            return false;
        }

        term.variable = variable;
        term.power = variable == '\0' ? 0 : power;
        term.coefficient = (int)signedCoefficient;
        return true;
    }

    // Skip spaces while advancing through the input expression.
    private static void SkipWhitespace(string expression, ref int index)
    {
        while (index < expression.Length && char.IsWhiteSpace(expression[index]))
        {
            index++;
        }
    }
}
