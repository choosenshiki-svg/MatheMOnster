using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hardcoded test arrangements for the algebra-ball solver.
/// Attach this to the same GameObject as Spawner.
/// </summary>
public class TestSpawner : MonoBehaviour
{
    [Serializable]
    public class BallPlacement
    {
        public Vector2 offset;
        public string content;

        public BallPlacement(float x, float y, string content)
        {
            offset = new Vector2(x, y);
            this.content = content;
        }
    }

    [Serializable]
    public class TestSetup
    {
        public string category;
        public string name;
        public string expected;
        public BallPlacement[] balls;

        public TestSetup(string category, string name, string expected, params BallPlacement[] balls)
        {
            this.category = category;
            this.name = name;
            this.expected = expected;
            this.balls = balls;
        }
    }

    [Min(0.1f)]
    [Tooltip("Distance between neighboring ball centers in row-based tests. Adjust if balls do not connect.")]
    public float spacing = 1.8f;

    [Min(0f)]
    [Tooltip("Vertical gap between test constructions when spawning a whole category or all tests.")]
    public float stackGap = 2.5f;

    private Spawner spawner;
    private readonly List<TestSetup> tests = new List<TestSetup>();

    public IReadOnlyList<TestSetup> Tests => tests;

    private void Awake()
    {
        spawner = GetComponent<Spawner>();
        if (spawner == null)
            Debug.LogError("TestSpawner needs to be on the same GameObject as Spawner.", this);

        BuildTests();
    }

    public void SpawnTest(TestSetup setup, Vector3 worldOrigin, bool isHand)
    {
        if (!CanSpawn(setup))
            return;

        SpawnTestAt(setup, worldOrigin, isHand);
    }

    public void SpawnAllTests(Vector3 worldOrigin, bool isHand)
    {
        SpawnStacked(tests, worldOrigin, "ALL TESTS", isHand);
    }

    public void SpawnCategoryTests(string category, Vector3 worldOrigin, bool isHand)
    {
        List<TestSetup> categoryTests = new List<TestSetup>();
        foreach (TestSetup setup in tests)
        {
            if (setup.category == category)
                categoryTests.Add(setup);
        }

        SpawnStacked(categoryTests, worldOrigin, category, isHand);
    }

    private bool CanSpawn(TestSetup setup)
    {
        if (spawner == null)
            spawner = GetComponent<Spawner>();

        if (spawner == null || setup == null || setup.balls == null)
        {
            Debug.LogError("Cannot spawn test: Spawner or test setup is missing.", this);
            return false;
        }

        return true;
    }

    private void SpawnStacked(IReadOnlyList<TestSetup> setups, Vector3 worldOrigin, string label, bool isHand)
    {
        if (setups == null || setups.Count == 0)
            return;

        float verticalOffset = 0f;
        foreach (TestSetup setup in setups)
        {
            if (!CanSpawn(setup))
                continue;

            GetVerticalBounds(setup, out float minY, out float maxY);
            Vector3 testOrigin = new Vector3(
                worldOrigin.x,
                worldOrigin.y - verticalOffset - maxY * spacing,
                0f);

            SpawnTestAt(setup, testOrigin, isHand);
            verticalOffset += (maxY - minY) * spacing + stackGap * spacing;
        }

    }

    private void SpawnTestAt(TestSetup setup, Vector3 worldOrigin, bool isHand)
    {
        worldOrigin.z = 0f;

        foreach (BallPlacement ball in setup.balls)
        {
            // Empty strings are intentional: they spawn the blank output ball at the end of a test.
            if (ball == null || ball.content == null)
                continue;

            Vector3 position = worldOrigin + new Vector3(ball.offset.x * spacing, ball.offset.y * spacing, 0f);

            if (isHand)
                spawner.SpawnHandBallAt(position, ball.content);
            else
                spawner.SpawnBallWithContent(position, ball.content);
        }
    }

    private void GetVerticalBounds(TestSetup setup, out float minY, out float maxY)
    {
        minY = 0f;
        maxY = 0f;
        if (setup.balls == null || setup.balls.Length == 0)
            return;

        minY = float.MaxValue;
        maxY = float.MinValue;
        foreach (BallPlacement ball in setup.balls)
        {
            if (ball == null)
                continue;
            minY = Mathf.Min(minY, ball.offset.y);
            maxY = Mathf.Max(maxY, ball.offset.y);
        }

        if (minY == float.MaxValue)
            minY = maxY = 0f;
    }

    private void BuildTests()
    {
        // SUM
        tests.Add(new TestSetup("SUM", "Two numbers", "2 + 3 = 5",
            Row("2", "+", "3", "=")));
        tests.Add(new TestSetup("SUM", "Three terms", "2 + 3 + 4 = 9",
            Row("2", "+", "3", "+", "4", "=")));
        tests.Add(new TestSetup("SUM", "Subtraction", "8 - 3 = 5",
            Row("8", "-", "3", "=")));
        tests.Add(new TestSetup("SUM", "Mixed precedence", "2 + 3 * 4 = 14",
            Row("2", "+", "3", "*", "4", "=")));
        tests.Add(new TestSetup("SUM", "Branched sum", "Check branch traversal and grouping",
            new BallPlacement(0, 0, "2"),
            new BallPlacement(1, 0, "+"),
            new BallPlacement(2, 0, "3"),
            new BallPlacement(1, 1, "+"),
            new BallPlacement(1, 2, "4"),
            new BallPlacement(3, 0, "="),
            new BallPlacement(4, 0, "")));
        
        // MY TESTS

        tests.Add(new TestSetup("SUM", "number and + touching", "Expected result",
            new BallPlacement(0f, 0f, "2"),
            new BallPlacement(0.303f, 1.137f, "+"),
            new BallPlacement(1.103f, -0.112f, "="),
            new BallPlacement(2.21f, -0.062f, ""),
            new BallPlacement(-0.786f, 0.782f, "3")));
        
        tests.Add(new TestSetup("SUM", "T Fork", "Expected result",
            new BallPlacement(0f, 0f, ""),
            new BallPlacement(-2.197f, -0.232f, "3"),
            new BallPlacement(-2.245f, -1.339f, "-"),
            new BallPlacement(-2.481f, -2.422f, "1"),
            new BallPlacement(-2.677f, 1.986f, "2"),
            new BallPlacement(-1.107f, -0.035f, "="),
            new BallPlacement(-2.504f, 0.889f, "+")));

        tests.Add(new TestSetup("SUM", "2 touching number groups", "Expected result",
            new BallPlacement(0f, 0f, "+"),
            new BallPlacement(1.835f, 1.846f, "+"),
            new BallPlacement(4.351f, 1.224f, ""),
            new BallPlacement(3.242f, 1.224f, "="),
            new BallPlacement(-1.104f, -0.093f, "2"),
            new BallPlacement(1.757f, 2.952f, "2"),
            new BallPlacement(2.161f, 0.787f, "3"),
            new BallPlacement(1.027f, 0.415f, "2")));

        tests.Add(new TestSetup("SUM", "minus then plus", "Expected result",
            new BallPlacement(0f, 0f, "5"),
            new BallPlacement(6.462f, -0.779f, ""),
            new BallPlacement(2.192f, -0.076f, "2"),
            new BallPlacement(5.362f, -0.979f, "="),
            new BallPlacement(1.09f, -0.2f, "-"),
            new BallPlacement(4.345f, -0.538f, "162"),
            new BallPlacement(3.243f, -0.428f, "+")));

        tests.Add(new TestSetup("PRODUCT", "touching numbers test", "Expected result",
            new BallPlacement(0f, 0f, "/"),
            new BallPlacement(1.186f, -0.205f, "2"),
            new BallPlacement(-1.347f, -1.277f, "2"),
            new BallPlacement(1.138f, -1.313f, "2"),
            new BallPlacement(-1.114f, -0.09f, "8"),
            new BallPlacement(3.4f, -0.363f, ""),
            new BallPlacement(2.292f, -0.363f, "=")));

        







        // PRODUCT
        tests.Add(new TestSetup("PRODUCT", "Multiplication", "2 * 3 = 6",
            Row("2", "*", "3", "=")));
        tests.Add(new TestSetup("PRODUCT", "Implicit multiplication", "2 touching 3 means 2 * 3 = 6",
            Row("2", "3", "=")));
        tests.Add(new TestSetup("PRODUCT", "Division", "8 / 2 = 4",
            Row("8", "/", "2", "=")));
        tests.Add(new TestSetup("PRODUCT", "Power", "2 ^ 3 = 8",
            Row("2", "^", "3", "=")));
        tests.Add(new TestSetup("PRODUCT", "Branched product", "Check branch traversal around multiplication",
            new BallPlacement(0, 0, "2"),
            new BallPlacement(1, 0, "*"),
            new BallPlacement(2, 0, "3"),
            new BallPlacement(1, 1, "*"),
            new BallPlacement(1, 2, "4"),
            new BallPlacement(3, 0, "="),
            new BallPlacement(4, 0, "")));

        // FUNCTIONS
        tests.Add(new TestSetup("FUNCTIONS", "sin", "sin(0) = 0",
            Row("sin", "0", "=")));
        tests.Add(new TestSetup("FUNCTIONS", "cos", "cos(0) = 1",
            Row("cos", "0", "=")));
        tests.Add(new TestSetup("FUNCTIONS", "tan", "tan(0) = 0",
            Row("tan", "0", "=")));
        tests.Add(new TestSetup("FUNCTIONS", "log", "log(1) = 0",
            Row("log", "1", "=")));
        tests.Add(new TestSetup("FUNCTIONS", "root", "root(9) = 3",
            Row("root", "9", "=")));
        tests.Add(new TestSetup("FUNCTIONS", "factorial", "factorial(5) = 120",
            Row("factorial", "5", "=")));
        tests.Add(new TestSetup("FUNCTIONS", "Inverse trig", "asin(0) = 0",
            Row("asin", "0", "=")));
        tests.Add(new TestSetup("FUNCTIONS", "Function with two touching numbers", "Check whether function captures adjacent numbers until the next operator",
            Row("sin", "2", "3", "+", "4", "=")));
        tests.Add(new TestSetup("FUNCTIONS", "Function branch", "Check function input when a number branches off",
            new BallPlacement(0, 0, "sin"),
            new BallPlacement(1, 0, "3"),
            new BallPlacement(1, 1, "2"),
            new BallPlacement(2, 0, "+"),
            new BallPlacement(3, 0, "4"),
            new BallPlacement(4, 0, "="),
            new BallPlacement(5, 0, "")));

        //my tests
        tests.Add(new TestSetup("FUNCTIONS", "2 touch f()", "Expected result",
            new BallPlacement(0f, 0f, "sin"),
            new BallPlacement(2.036f, -0.264f, "="),
            new BallPlacement(-1.077f, -0.267f, "pi"),
            new BallPlacement(0.964f, -0.546f, "0"),
            new BallPlacement(3.144f, -0.288f, "")));



        // BRACKETS
        tests.Add(new TestSetup("BRACKETS", "Simple brackets", "(2 + 3) = 5",
            Row("(", "2", "+", "3", ")", "=")));
        tests.Add(new TestSetup("BRACKETS", "Bracket multiplication", "(2 + 3) * 4 = 20",
            Row("(", "2", "+", "3", ")", "*", "4", "=")));
        tests.Add(new TestSetup("BRACKETS", "Nested brackets", "((2 + 3) * 4) = 20",
            Row("(", "(", "2", "+", "3", ")", "*", "4", ")", "=")));
        tests.Add(new TestSetup("BRACKETS", "Function around brackets", "sin(0 + 0) = 0",
            Row("sin", "(", "0", "+", "0", ")", "=")));


        // SYNTAX
        tests.Add(new TestSetup("SYNTAX", "Function with two numbers (sin 2 3)", "sin red",
            Row("sin", "2", "3", "=")));

        tests.Add(new TestSetup("SYNTAX", "Function with three numbers (sin 2 3 4)", "sin red",
            Row("sin", "2", "3", "4", "=")));

        tests.Add(new TestSetup("SYNTAX", "Function with no argument (sin =)", "sin red, nothing to apply to",
            Row("sin", "=")));

        tests.Add(new TestSetup("SYNTAX", "Function touching operator (sin + 2)", "sin red, + red",
            Row("sin", "+", "2", "=")));

        tests.Add(new TestSetup("SYNTAX", "Multiplication with one operand (2 * =)", "* red",
            Row("2", "*", "=")));

        tests.Add(new TestSetup("SYNTAX", "Division with one operand (2 / =)", "/ red",
            Row("2", "/", "=")));

        tests.Add(new TestSetup("SYNTAX", "Power with one operand (2 ^ =)", "^ red",
            Row("2", "^", "=")));

        tests.Add(new TestSetup("SYNTAX", "Multiplication with three operands (2 * 3 * 4)", "nothing red — chainable",
            Row("2", "*", "3", "*", "4", "=")));

        tests.Add(new TestSetup("SYNTAX", "Division with three operands (2 / 3 / 4)", "/ red (both), ambiguous chain",
            Row("2", "/", "3", "/", "4", "=")));

        tests.Add(new TestSetup("SYNTAX", "Power with three operands (2 ^ 3 ^ 4)", "^ red, ambiguous chain",
            Row("2", "^", "3", "^", "4", "=")));

        tests.Add(new TestSetup("SYNTAX", "Plus with one operand (2 + =)", "nothing red — unary allowed",
            Row("2", "+", "=")));

        tests.Add(new TestSetup("SYNTAX", "Minus with one operand (2 - =)", "nothing red — unary allowed",
            Row("2", "-", "=")));

        tests.Add(new TestSetup("SYNTAX", "Two unary pluses (2 + + 3)", "nothing red — unary chain valid",
            Row("2", "+", "+", "3", "=")));

        tests.Add(new TestSetup("SYNTAX", "Three unary pluses (2 + + + 3)", "no red, solves to 5",
            Row("2", "+", "+", "+", "3", "=")));

        tests.Add(new TestSetup("SYNTAX", "Three unary minuses (2 - - - 3)", "no red, solves to -1",
            Row("2", "-", "-", "-", "3", "=")));

        tests.Add(new TestSetup("SYNTAX", "Mixed unary chain (2 + - + 3)", "no red, solves to -1",
            Row("2", "+", "-", "+", "3", "=")));

        tests.Add(new TestSetup("SYNTAX", "Times unary minus (2 * - 3)", "no red, solves to -6",
            Row("2", "*", "-", "3", "=")));

        tests.Add(new TestSetup("SYNTAX", "Divide unary minus (8 / - 2)", "no red, solves to -4",
            Row("8", "/", "-", "2", "=")));

        tests.Add(new TestSetup("SYNTAX", "Power unary minus (2 ^ - 2)", "no red, solves to 0.25",
            Row("2", "^", "-", "2", "=")));

        tests.Add(new TestSetup("SYNTAX", "Two unary minuses (2 - - 3)", "nothing red — unary chain valid",
            Row("2", "-", "-", "3", "=")));

        tests.Add(new TestSetup("SYNTAX", "Number between two functions", "number red",
            new BallPlacement(-1, 0, "sin"),
            new BallPlacement(0, 0, "2"),
            new BallPlacement(1, 0, "cos"),
            new BallPlacement(2, 0, "=")));

        tests.Add(new TestSetup("SYNTAX", "Number between three functions", "number red",
            new BallPlacement(-1, 0, "sin"),
            new BallPlacement(0, 0, "2"),
            new BallPlacement(1, 0, "cos"),
            new BallPlacement(0, 1, "tan"),
            new BallPlacement(2, 0, "=")));


        //VALIDITY

        tests.Add(new TestSetup("VALID", "Simple sum", "no red, solves",
            Row("2", "+", "3", "=")));

        tests.Add(new TestSetup("VALID", "Simple product", "no red, solves",
            Row("2", "*", "3", "=")));

        tests.Add(new TestSetup("VALID", "Simple division", "no red, solves",
            Row("8", "/", "2", "=")));

        tests.Add(new TestSetup("VALID", "Simple power", "no red, solves",
            Row("2", "^", "3", "=")));

        tests.Add(new TestSetup("VALID", "Simple brackets", "no red, solves",
            Row("(", "2", "+", "3", ")", "=")));

        tests.Add(new TestSetup("VALID", "Bracket times number", "no red, solves",
            Row("(", "2", "+", "3", ")", "*", "4", "=")));

        tests.Add(new TestSetup("VALID", "Number times bracket", "no red, solves",
            Row("4", "*", "(", "2", "+", "3", ")", "=")));

        tests.Add(new TestSetup("VALID", "Function with brackets", "no red, solves",
            Row("sin", "(", "0", "+", "0", ")", "=")));

        tests.Add(new TestSetup("VALID", "Nested brackets", "no red, solves",
            Row("(", "(", "2", "+", "3", ")", "*", "4", ")", "=")));

        tests.Add(new TestSetup("VALID", "Chain of plus", "no red, solves",
            Row("2", "+", "3", "+", "4", "=")));

        tests.Add(new TestSetup("VALID", "Chain of times", "no red, solves",
            Row("2", "*", "3", "*", "4", "=")));

        tests.Add(new TestSetup("VALID", "Mixed plus and times", "no red, solves",
            Row("2", "+", "3", "*", "4", "=")));

        tests.Add(new TestSetup("VALID", "Minus then plus", "no red, solves",
            Row("5", "-", "2", "+", "162", "=")));

        tests.Add(new TestSetup("VALID", "Division then times", "no red, solves",
            Row("8", "/", "2", "*", "4", "=")));


        tests.Add(new TestSetup("STUCK", "Operator with no operands", "stuck, operator red via stuck",
            Row("+", "=")));

        tests.Add(new TestSetup("STUCK", "Two numbers with no operator", "number pair merges, side stuck, red",
            new BallPlacement(0, 0, "2"),
            new BallPlacement(1, 0, "3"),
            new BallPlacement(3, 0, "=")));
            // Numbers merge into 6, but then nothing else can happen. Should be red.

        tests.Add(new TestSetup("STUCK", "Only operators", "all stuck, red",
            Row("+", "*", "=")));

        tests.Add(new TestSetup("STUCK", "Two equals", "invalid, no equations built",
            Row("2", "=", "3", "=")));

        tests.Add(new TestSetup("STUCK", "No equals", "invalid cluster, nothing red (or everything red)",
            Row("2", "+", "3")));
            // Nothing to solve, no '=' ball. What should happen?

        tests.Add(new TestSetup("STUCK", "Dangling operator before equals", "operator red via stuck",
            Row("2", "+", "=")));

        tests.Add(new TestSetup("STUCK", "Bracket without close", "whole thing fails, red",
            Row("(", "2", "+", "3", "=")));

        tests.Add(new TestSetup("STUCK", "Close bracket without open", "whole thing fails, red",
            Row("2", "+", "3", ")", "=")));

            tests.Add(new TestSetup("COMPLEX", "Bracket inside function", "no red, solves",
            Row("sin", "(", "0", ")", "=")));

        tests.Add(new TestSetup("COMPLEX", "Function result plus number", "no red, solves",
            Row("sin", "0", "+", "4", "=")));

        tests.Add(new TestSetup("COMPLEX", "Number times function", "no red, solves",
            Row("2", "*", "sin", "0", "=")));

        tests.Add(new TestSetup("COMPLEX", "Implicit multiplication of constants", "no red",
            Row("2", "pi", "=")));

        tests.Add(new TestSetup("COMPLEX", "Constant pi alone", "no red",
            Row("pi", "=")));

        tests.Add(new TestSetup("COMPLEX", "Constant e plus 1", "no red",
            Row("e", "+", "1", "=")));

        tests.Add(new TestSetup("COMPLEX", "Variable plus number", "no red",
            Row("x", "+", "2", "=")));

        tests.Add(new TestSetup("COMPLEX", "Deeply nested brackets", "no red, solves",
            Row("(", "(", "(", "2", "+", "3", ")", ")", ")", "=")));

        tests.Add(new TestSetup("COMPLEX", "Two separate brackets multiplied", "no red, solves",
            Row("(", "2", ")", "*", "(", "3", ")", "=")));

        tests.Add(new TestSetup("COMPLEX", "Function on bracketed sum", "no red, solves",
            Row("sin", "(", "0", "+", "0", ")", "=")));

        tests.Add(new TestSetup("COMPLEX", "Bracket containing function", "no red, solves",
            Row("(", "sin", "0", ")", "=")));

            tests.Add(new TestSetup("POSITIONAL", "T-fork of plus", "some balls red — ambiguous",
        new BallPlacement(0, 0, "2"),
        new BallPlacement(1, 0, "+"),
        new BallPlacement(2, 0, "3"),
        new BallPlacement(1, 1, "+"),
        new BallPlacement(1, 2, "4"),
        new BallPlacement(3, 0, "=")));

        tests.Add(new TestSetup("POSITIONAL", "Two functions fighting over a number", "middle number red",
            new BallPlacement(0, 0, "2"),
            new BallPlacement(1, 1, "sin"),
            new BallPlacement(2, 2, "5"),
            new BallPlacement(1, -1, "cos"),
            new BallPlacement(3, 1, "=")));

        tests.Add(new TestSetup("POSITIONAL", "Operator between two brackets", "no red, solves",
            new BallPlacement(0, 0, "("),
            new BallPlacement(1, 0, "1"),
            new BallPlacement(2, 0, ")"),
            new BallPlacement(3, 0, "*"),
            new BallPlacement(4, 0, "("),
            new BallPlacement(5, 0, "2"),
            new BallPlacement(6, 0, ")"),
            new BallPlacement(7, 0, "=")));

        tests.Add(new TestSetup("POSITIONAL", "Vertical bracket pair", "no red, solves",
            new BallPlacement(0, 0, "("),
            new BallPlacement(1, 1, "2"),
            new BallPlacement(1, -1, "+"),
            new BallPlacement(2, 0, "3"),
            new BallPlacement(3, 0, ")"),
            new BallPlacement(4, 0, "=")));

        tests.Add(new TestSetup("POSITIONAL", "Operators on both sides of a number", "number red if two functions",
            new BallPlacement(0, 0, "2"),
            new BallPlacement(1, 1, "sin"),
            new BallPlacement(1, -1, "cos"),
            new BallPlacement(2, 0, "=")));

                    // ============================================================
        // NEW TESTS: fractions, powers, brackets-implicit-mult, equals states
        // ============================================================

        // ---------- FRACTIONS ----------
        tests.Add(new TestSetup("DIRECTIONAL OPERATORS", "Outside / inside = division", "8 / 2 = 4",
            Row("8", "/", "2", "=", "4")));
        tests.Add(new TestSetup("FRACTIONS", "'/3' = 1/3", "0.333...",
            Row("/3", "=")));
        tests.Add(new TestSetup("FRACTIONS", "'/x' symbolic", "1/x (unsolvable numeric)",
            Row("/x", "=")));
        tests.Add(new TestSetup("FRACTIONS", "2 touching 1/3 = 2/3", "0.666...",
            Row("2", "/3", "=")));
        tests.Add(new TestSetup("FRACTIONS", "1/3 * 1/4 = 1/12", "0.08333...",
            Row("/3", "/4", "=")));
        tests.Add(new TestSetup("FRACTIONS", "2 * 1/3 * 3 = 2", "2",
            Row("2", "/3", "*", "3", "=")));

        // ---------- POWERS ----------
        tests.Add(new TestSetup("DIRECTIONAL OPERATORS", "Outside base / inside exponent", "2 ^ 3 = 8",
            Row("2", "^", "3", "=", "8")));
        tests.Add(new TestSetup("POWERS", "Explicit '^2' squares operand", "2^2 = 4",
            Row("2", "^2", "=", "4")));
        tests.Add(new TestSetup("POWERS", "'^3' cubes operand", "2^3 = 8",
            Row("2", "^3", "=")));
        tests.Add(new TestSetup("POWERS", "'^x' symbolic", "2^x (unsolvable without x)",
            Row("2", "^x", "=")));
        tests.Add(new TestSetup("DIRECTIONAL OPERATORS", "Outside minus inside", "8 - 3 = 5",
            Row("8", "-", "3", "=", "5")));

        // ---------- FUNCTIONS (new cases) ----------
        tests.Add(new TestSetup("FUNCTIONS", "sin sees two numbers → red", "sin red",
            Row("2", "sin", "3", "=")));
        tests.Add(new TestSetup("FUNCTIONS", "sin sees two with bracket → red", "sin red",
            Row("2", "sin", "(", "8", "+", "2", ")", "=")));
        tests.Add(new TestSetup("FUNCTIONS", "sin with one bracket group → ok", "sin(10)",
            Row("sin", "(", "8", "+", "2", ")", "=")));

        // ---------- BRACKETS (new cases) ----------
        tests.Add(new TestSetup("BRACKETS", "Number touching bracket = implicit *", "2 (3+4) = 14",
            Row("2", "(", "3", "+", "4", ")", "=")));
        tests.Add(new TestSetup("BRACKETS", "Number and bracket and *", "2 (3+4) * 5 = 70",
            Row("2", "(", "3", "+", "4", ")", "*", "5", "=")));
        tests.Add(new TestSetup("BRACKETS", "Bracket with no interior → red", "( ) =",
            Row("(", ")", "=")));
        tests.Add(new TestSetup("BRACKETS", "Unmatched '(' → red", "( 2 =",
            Row("(", "2", "=")));
        tests.Add(new TestSetup("BRACKETS", "Unmatched ')' → red", "2 ) =",
            Row("2", ")", "=")));

        // ---------- EQUALS STATES ----------
        tests.Add(new TestSetup("EQUALS", "Equals with 1 slot → blue", "2 = (blue)",
            Row("2", "=")));
        tests.Add(new TestSetup("EQUALS", "Equals with 2 equal slots → green", "2 = 2",
            Row("2", "=", "2")));
        tests.Add(new TestSetup("EQUALS", "Equals with 2 disagreeing slots → red", "2 = 3",
            Row("2", "=", "3")));
        tests.Add(new TestSetup("EQUALS", "Empty ball solved to 4", "2+2 = [4]",
            Row("2", "+", "2", "=", "")));

        // ---------- DANGLING ----------
        tests.Add(new TestSetup("DANGLING", "Dangling '+' → red", "2 + =",
            Row("2", "+", "=")));
        tests.Add(new TestSetup("DANGLING", "Dangling '*' → red", "2 * =",
            Row("2", "*", "=")));
        tests.Add(new TestSetup("DANGLING", "Dangling function → red", "sin =",
            Row("sin", "=")));  

    }

    // Row-based tests always finish with '=' followed by a blank output ball.
    private BallPlacement[] Row(params string[] contents)
    {
        int equalsIndex = Array.LastIndexOf(contents, "=");
        bool hasEquals = equalsIndex >= 0;
        int totalCount = contents.Length + (hasEquals ? 1 : 0);
        var placements = new BallPlacement[totalCount];
        float middle = (contents.Length - 1) / 2f;

        for (int i = 0; i < contents.Length; i++)
            placements[i] = new BallPlacement(i - middle, 0f, contents[i]);

        if (hasEquals)
            placements[contents.Length] = new BallPlacement(contents.Length - middle, 0f, "");

        return placements;
    }
}
