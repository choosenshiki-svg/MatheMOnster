using UnityEngine;

/// <summary>
/// Decides whether two balls are "connected" for math purposes.
///
/// Works for any Collider2D combination (circle / polygon / crescent).
/// Directional operators ('-', '/', '^') use crescent-shaped colliders and
/// have special rules: they connect ONLY to a ball placed on the correct
/// operand side (inside the crescent's cavity, or just outside its outer arm).
/// </summary>
public static class BallConnectionRules
{
    /// <summary>Max edge-to-edge gap (in world units) for two ordinary balls to count as connected.</summary>
    public const float ConnectionGap = 0.28f;

    /// <summary>Max edge-to-edge gap for a directional operator's port to capture an operand.</summary>
    private const float OperandCaptureGap = 0.30f;

    public static bool AreConnected(Collider2D first, Collider2D second)
    {
        if (first == null || second == null) return false;
        if (first == second) return false;

        DirectionalOperator opFirst  = first.GetComponent<DirectionalOperator>();
        DirectionalOperator opSecond = second.GetComponent<DirectionalOperator>();

        bool firstIsOp  = opFirst  != null && opFirst.IsActive;
        bool secondIsOp = opSecond != null && opSecond.IsActive;

        // --- directional operator vs. ordinary ball ---
        if (firstIsOp && !secondIsOp)
            return opFirst.ConnectsTo(second);
        if (secondIsOp && !firstIsOp)
            return opSecond.ConnectsTo(first);

        // --- directional operator vs. directional operator ---
        // Two crescents never carry a connection between them; they're
        // resolved by the rewrite engine as invalid when they touch.
        if (firstIsOp && secondIsOp)
            return false;

        // --- ordinary ball vs. ordinary ball: edge-to-edge gap ---
        return EdgeGap(first, second) <= ConnectionGap;
    }

    /// <summary>
    /// Signed gap between the two colliders (negative means overlapping).
    /// Uses Unity's built-in closest-point query, which handles any shape.
    /// </summary>
    private static float EdgeGap(Collider2D a, Collider2D b)
    {
        // Distance() returns 0 when overlapping; we subtract a small fudge
        // so tiny overlaps still read as "touching" rather than "connected by 0".
        if (a.Distance(b).isOverlapped) return -1f;
        return a.Distance(b).distance;
    }
}