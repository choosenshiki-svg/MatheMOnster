using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// Converts touching ball clusters into expression trees and solves them.
// Rewrite-based: balls form a graph; priority-ordered rules collapse the graph
// into a single ExprNode per side of '='. The ball GameObjects are never touched.
public static class MathEquationBuilder
{
            // ============================================================
    // Change versioning (used to cache solver results)
    // ============================================================

        private static int _structuralVersion;   // spawn / despawn
    private static bool _connectionDirty;    // a ball moved
    private static bool _contentDirty;       // a user edited a ball

    public static void MarkBallSpawned()        => _structuralVersion++;
    public static void MarkBallDespawned()      => _structuralVersion++;
    public static void MarkContentChanged()     => _contentDirty = true;
    public static void MarkConnectionsChanged() => _connectionDirty = true;

    public static int CurrentStructuralVersion => _structuralVersion;

    public static bool ConsumeContentDirty()
    {
        bool d = _contentDirty;
        _contentDirty = false;
        return d;
    }

    public static bool ConsumeConnectionDirty()
    {
        bool d = _connectionDirty;
        _connectionDirty = false;
        return d;
    }
    
        // ============================================================
    // Per-cluster caching support
    // ============================================================

    public sealed class CachedCluster
    {
        public List<BallNumber> balls;
        // Sum of per-ball content versions at last solve. Not exact, but a
        // monotone signal: if it changes, at least one ball changed.
        public long contentVersionAtLastSolve = -1;
        public BuildResult result;
    }

    /// <summary>
    /// Partitions activeBalls into connected components using a spatial hash
    /// so the cost is O(n * k) instead of O(n^2). Cell size must be at least
    /// 2x the maximum connection reach so that all possible connections are
    /// found inside the 3x3 neighborhood.
    /// </summary>
    public static List<List<BallNumber>> PartitionClusters(List<BallNumber> activeBalls)
    {
        var clusters = new List<List<BallNumber>>();
        if (activeBalls == null || activeBalls.Count == 0) return clusters;

        // The directional crescent's operand ports extend farther than a
        // standard ball's connection radius, so use a larger neighborhood.
        const float CellSize = 6.0f;

        var grid = new Dictionary<Vector2Int, List<int>>();
        var colliders = new Collider2D[activeBalls.Count];
        for (int i = 0; i < activeBalls.Count; i++)
        {
            var ball = activeBalls[i];
            if (ball == null) continue;
            colliders[i] = ball.GetComponent<Collider2D>();
            if (colliders[i] == null) continue;

            var center = colliders[i].bounds.center;
            var cell = new Vector2Int(
                Mathf.FloorToInt(center.x / CellSize),
                Mathf.FloorToInt(center.y / CellSize));
            if (!grid.TryGetValue(cell, out var list)) grid[cell] = list = new List<int>();
            list.Add(i);
        }

        bool[] visited = new bool[activeBalls.Count];

        for (int start = 0; start < activeBalls.Count; start++)
        {
            if (visited[start]) continue;
            visited[start] = true;

            var group = new List<BallNumber>();
            var pending = new Queue<int>();
            pending.Enqueue(start);
            group.Add(activeBalls[start]);

            while (pending.Count > 0)
            {
                int i = pending.Dequeue();
                var ci = colliders[i];
                if (ci == null) continue;

                var center = ci.bounds.center;
                var baseCell = new Vector2Int(
                    Mathf.FloorToInt(center.x / CellSize),
                    Mathf.FloorToInt(center.y / CellSize));

                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var cell = new Vector2Int(baseCell.x + dx, baseCell.y + dy);
                    if (!grid.TryGetValue(cell, out var candidates)) continue;

                    foreach (int j in candidates)
                    {
                        if (visited[j] || j == i) continue;
                        if (colliders[j] == null) continue;
                        if (!BallConnectionRules.AreConnected(ci, colliders[j])) continue;
                        visited[j] = true;
                        pending.Enqueue(j);
                        group.Add(activeBalls[j]);
                    }
                }
            }

            clusters.Add(group);
        }

        return clusters;
    }

    /// <summary>
    /// Solves a single already-partitioned cluster. Returns a BuildResult
    /// scoped to this cluster only: its equations, connected pairs, and
    /// ambiguity/ignore sets. Never touches any other cluster's balls.
    /// </summary>
    public static BuildResult SolveCluster(List<BallNumber> cluster)
    {
        var result = new BuildResult();
        if (cluster == null || cluster.Count < 2) return result;

        // Record connected pairs for the tendril layer.
        for (int i = 0; i < cluster.Count; i++)
        {
            if (cluster[i] == null) continue;
            Collider2D ci = cluster[i].GetComponent<Collider2D>();
            if (ci == null) continue;

            for (int j = i + 1; j < cluster.Count; j++)
            {
                if (cluster[j] == null) continue;
                Collider2D cj = cluster[j].GetComponent<Collider2D>();
                if (cj == null) continue;
                if (BallConnectionRules.AreConnected(ci, cj))
                    result.connectedPairs.Add((cluster[i], cluster[j]));
            }
        }

        // Cheap filter: no '=' -> no equation.
        bool hasEquals = false;
        foreach (BallNumber b in cluster)
        {
            if (b == null) continue;
            if (b.GetMathContent().Trim() == "=") { hasEquals = true; break; }
        }
        if (!hasEquals) return result;

                if (TryBuildConnectedEquation(cluster,
                out List<ConnectedEquation> eqs,
                out HashSet<BallNumber> filled,
                out HashSet<BallNumber> clusterIgnored,
                out HashSet<BallNumber> clusterAmbiguous))
        {
            result.equations.AddRange(eqs);
            result.filledBalls.UnionWith(filled);
            result.allAmbiguousBalls.UnionWith(clusterAmbiguous);
        }
        else
        {
            result.allAmbiguousBalls.UnionWith(clusterAmbiguous);
        }

        return result;
    }


    // ============================================================
    // Public result types (consumed by Spawner and EquationListDisplay)
    // ============================================================

    public sealed class ConnectedEquation
    {
        public ExprNode left;
        public ExprNode right;
        public List<BallNumber> balls = new List<BallNumber>();
        public BallNumber equalsBall;
        public HashSet<BallNumber> ambiguousBalls = new HashSet<BallNumber>();
        public string expression;

        // NEW:
        public List<ExprNode> slots = new List<ExprNode>();
        public List<List<BallNumber>> slotBalls = new List<List<BallNumber>>();
        public byte equationState = 0;   // 0 blue, 1 green, 2 red
    }

    public sealed class BuildResult
    {
        public List<ConnectedEquation> equations = new List<ConnectedEquation>();
        public HashSet<BallNumber> allAmbiguousBalls = new HashSet<BallNumber>();

        /// <summary>Balls whose solver output was written during this solve pass.</summary>
        public HashSet<BallNumber> filledBalls = new HashSet<BallNumber>();

        public List<(BallNumber a, BallNumber b)> connectedPairs =
            new List<(BallNumber, BallNumber)>();
    }

     

    // ============================================================
    // Expression nodes
    // ============================================================

    public abstract class ExprNode
    {
        public List<BallNumber> sourceBalls = new List<BallNumber>();
    }

    public sealed class NumberNode : ExprNode
    {
        public double value;
        public string display;
    }

    public sealed class VarNode : ExprNode
    {
        public char name;
    }

    public sealed class UnknownNode : ExprNode { }

    public sealed class UnaryNode : ExprNode
    {
        public char op;
        public ExprNode operand;
    }

    public sealed class OpNode : ExprNode
    {
        public char op;             // + - * / ^
        public ExprNode left;
        public ExprNode right;
        public ExprNode fixedExponent;
        public bool hasFixedExponent;
        public DirectionalOperator directionalOperator;
        public bool implicitMultiplication;   // NEW: render with no '*' between operands
    }

    public sealed class FuncNode : ExprNode
    {
        public string name;
        public ExprNode arg;
    }

    public sealed class BracketNode : ExprNode
    {
        public bool isOpen;
    }

    // ============================================================
    // Internal graph
    // ============================================================

              private sealed class Node
    {
        public ExprNode expr;
        public List<Node> neighbors = new List<Node>();
                  public Vector2 position;
                  public bool alive = true;
                  public bool isBracketResult = false;
    }    

    private static List<BallNumber> FindTouchingGroup(
        List<BallNumber> activeBalls, Collider2D[] colliders,
        bool[] visited, int start)
    {
        var group = new List<BallNumber>();
        var pending = new Queue<int>();
        pending.Enqueue(start);
        visited[start] = true;

        while (pending.Count > 0)
        {
            int i = pending.Dequeue();
            group.Add(activeBalls[i]);
            Collider2D ci = colliders[i];
            if (ci == null) continue;

            for (int j = 0; j < activeBalls.Count; j++)
            {
                if (visited[j] || colliders[j] == null) continue;
                if (BallConnectionRules.AreConnected(ci, colliders[j]))
                {
                    visited[j] = true;
                    pending.Enqueue(j);
                }
            }
        }
        return group;
    }

    // ============================================================
    // Cluster -> equation
    // ============================================================

        private static bool TryBuildConnectedEquation(
        List<BallNumber> cluster,
        out List<ConnectedEquation> equations,
        out HashSet<BallNumber> filledBalls,
        out HashSet<BallNumber> clusterIgnored,
        out HashSet<BallNumber> clusterAmbiguous)
    {
        equations = new List<ConnectedEquation>();
        filledBalls = new HashSet<BallNumber>();
        clusterIgnored = new HashSet<BallNumber>();
        clusterAmbiguous = new HashSet<BallNumber>();

                

        // Red-flag any ball whose content was unrecognizable and was therefore
        // excluded from the graph (unknown non-empty, non-'=' content).
        foreach (BallNumber ball in cluster)
        {
            if (ball == null) continue;
            string raw = ball.GetMathContent().Trim();
            if (raw.Length == 0 || raw == "=") continue;

            ExprNode leaf = MakeLeaf(ball);
            if (leaf is UnknownNode)
                clusterAmbiguous.Add(ball);
        }

                    // --- Pass 1: raw adjacency for the local syntax checker ---
        // Use physical proximity (colliders), not graph edges, because the graph
        // doesn't exist yet. This is what the checker expects.
        var neighborsByBall = new Dictionary<BallNumber, List<BallNumber>>();
        for (int i = 0; i < cluster.Count; i++)
        {
            if (cluster[i] == null) continue;
            Collider2D ci = cluster[i].GetComponent<Collider2D>();
            var list = new List<BallNumber>();
            if (ci != null)
            {
                for (int j = 0; j < cluster.Count; j++)
                {
                    if (i == j || cluster[j] == null) continue;
                    Collider2D cj = cluster[j].GetComponent<Collider2D>();
                    if (cj == null) continue;
                    if (BallConnectionRules.AreConnected(ci, cj))
                        list.Add(cluster[j]);
                }
            }
            neighborsByBall[cluster[i]] = list;
        }

        HashSet<BallNumber> localSyntaxErrors = new HashSet<BallNumber>();
        BallSyntaxChecker.MarkLocalSyntaxErrors(cluster, neighborsByBall, localSyntaxErrors);

        // --- Pass 2: build the removal set ---
        // Red balls act as empty space: they are removed from the graph, their
        // neighbors do not connect through them, and they don't participate in
        // any region. Unknown-content balls (non-empty, not '=', MakeLeaf gave
        // UnknownNode) are treated the same way.
        var removed = new HashSet<BallNumber>(localSyntaxErrors);
        foreach (BallNumber ball in cluster)
        {
            if (ball == null) continue;
            string raw = ball.GetMathContent().Trim();
            if (raw.Length == 0 || raw == "=") continue;
            if (MakeLeaf(ball) is UnknownNode)
            {
                removed.Add(ball);
                clusterAmbiguous.Add(ball);
            }
        }
                clusterAmbiguous.UnionWith(localSyntaxErrors);

        // Balls that were removed from the graph (red-flagged or unknown content)
        // are excluded from the equation's ambiguous set. They still get red via
        // clusterAmbiguous → allAmbiguousBalls in Spawner, but they don't block
        // the solve. Only live-graph ambiguous balls should block it.
        var liveAmbiguous = new HashSet<BallNumber>();
        foreach (var b in clusterAmbiguous)
            if (!removed.Contains(b))
                liveAmbiguous.Add(b);

        // --- Pass 3: build the reduced graph ---
        List<Node> graph = BuildGraph(cluster, removed);

        // --- Find all '=' balls ---
        var equalsNodes = new List<Node>();
        foreach (Node n in graph)
            if (n.expr is EqualsMarker)
                equalsNodes.Add(n);

        if (equalsNodes.Count == 0)
            return false;   // no equation to build

        // --- Partition the graph by '=' into regions ---
        // Each region is a connected component of non-'=' nodes after removing all '=' nodes.
        var regions = PartitionByEquals(graph, equalsNodes);

        // --- Solve each region recursively ---
        var regionResults = new List<ExprNode>();
        var regionBalls = new List<List<BallNumber>>();
        foreach (var region in regions)
        {
            if (region.Count == 0) continue;
            ExprNode value = SolveRegion(region, clusterAmbiguous);
            regionResults.Add(value);
            var bl = new List<BallNumber>();
            foreach (Node n in region)
                bl.AddRange(n.expr.sourceBalls);
            regionBalls.Add(bl);
        }

        // --- Assign each region to the '=' slots it touches ---
        // Slots are (equalsNode, sideIndex) pairs. For now, treat a chain of
        // '=' balls connected to each other as a single multi-slot equation.
        // Each region touches one or more '=' balls; each '=' ball has the set
        // of regions adjacent to it as its slots.

        // --- Build the ConnectedEquation ---
        // We only support one equation per cluster for now (same as before:
        // multiple '=' in a cluster is rejected). If your game supports
        // separate equations in one cluster, this is where to branch.
                // --- Build one ConnectedEquation per '=' ---
        // Each '=' gets its own slot list: the regions graph-adjacent to it.
        // A region adjacent to two '=' contributes its value to both.
        foreach (Node equalsNode in equalsNodes)
        {
            var slotRegions = new List<ExprNode>();
            var slotBalls = new List<List<BallNumber>>();
            var seenRegionIndexes = new HashSet<int>();

            foreach (Node nb in equalsNode.neighbors)
            {
                if (nb == null || !nb.alive) continue;
                int idx = regionIndexOf(regions, nb);
                if (idx < 0) continue;
                if (!seenRegionIndexes.Add(idx)) continue;   // dedupe
                slotRegions.Add(regionResults[idx]);
                slotBalls.Add(regionBalls[idx]);
            }

            // Balls belonging to this '=' : union of slot balls + the '=' ball.
            var eqBalls = new List<BallNumber>();
            var eqBallSet = new HashSet<BallNumber>();
            foreach (var bl in slotBalls)
                foreach (var b in bl)
                    if (b != null && eqBallSet.Add(b)) eqBalls.Add(b);
            if (equalsNode.expr.sourceBalls.Count > 0)
            {
                var eqBall = equalsNode.expr.sourceBalls[0];
                if (eqBall != null && eqBallSet.Add(eqBall)) eqBalls.Add(eqBall);
            }

            var eq = new ConnectedEquation
            {
                left = slotRegions.Count > 0 ? slotRegions[0] : null,
                right = slotRegions.Count > 1 ? slotRegions[1] : null,
                balls = eqBalls,
                equalsBall = equalsNode.expr.sourceBalls.Count > 0
                    ? equalsNode.expr.sourceBalls[0] : null,
                slots = slotRegions,
                slotBalls = slotBalls,
                ambiguousBalls = liveAmbiguous,
                expression = RenderSlots(slotRegions),
                equationState = 0,
            };
            equations.Add(eq);
        }

        // Centralized fill + color pass across every '=' in this cluster.
        FillAndColorCluster(equations, filledBalls);

        return true;
    }

        /// <summary>
    /// Cluster-wide fill + color pass. Runs once after every '=' in the
    /// cluster has a fully built slot list.
    ///
    /// Fill:
    ///   - A '=' proposes a value for a blank only when exactly one of its
    ///     slots is a bare UnknownNode (single blank ball, nothing else) and
    ///     every other slot evaluates numerically to the same value.
    ///   - A blank is filled iff every '=' that touches it proposes the same
    ///     value. If any '=' touching it proposes a different value, or none
    ///     propose, the blank stays empty.
    ///
    /// Color:
    ///   - After fills are applied, each '=' re-checks its slots. All numeric
    ///     and equal → green. Any two disagree → red. Any unresolved slot
    ///     (blank, variable, unsolvable region) → blue.
    /// </summary>
    private static void FillAndColorCluster(
        List<ConnectedEquation> eqs, HashSet<BallNumber> filledBalls)
    {
        // --- A. Collect proposals per blank ball ---
        // Key: the blank ball. Value: set of numeric values proposed for it.
        var proposals = new Dictionary<BallNumber, HashSet<double>>();
        // Also track which (eqIndex, slotIndex) slots propose a fill, so we
        // can patch them on fill.
        var slotProposals = new List<(int eqIndex, int slotIndex, BallNumber blank, double value)>();

        for (int ei = 0; ei < eqs.Count; ei++)
        {
            var eq = eqs[ei];
            if (eq.slots.Count < 2) continue;

            int blankSlotIndex = -1;
            BallNumber blankBall = null;
            bool multipleBlanksInSlot = false;
            bool otherSlotNonNumeric = false;
            double? commonValue = null;

            for (int si = 0; si < eq.slots.Count; si++)
            {
                var slot = eq.slots[si];

                if (IsBareBlankSlot(slot, eq.slotBalls[si], out BallNumber bb))
                {
                    if (blankSlotIndex >= 0)
                    {
                        // More than one bare-blank slot → cannot fill.
                        multipleBlanksInSlot = true;
                        break;
                    }
                    blankSlotIndex = si;
                    blankBall = bb;
                    continue;
                }

                if (!TryEvaluateNode(slot, out double v))
                {
                    otherSlotNonNumeric = true;
                    break;
                }

                if (commonValue == null) commonValue = v;
                else if (!ValuesAreEquivalent(commonValue.Value, v))
                {
                    // Other slots disagree among themselves — no single
                    // value to propose.
                    otherSlotNonNumeric = true;   // reuse flag to abort
                    break;
                }
            }

            if (multipleBlanksInSlot || otherSlotNonNumeric) continue;
            if (blankSlotIndex < 0 || blankBall == null || commonValue == null) continue;

            if (!proposals.TryGetValue(blankBall, out var set))
                proposals[blankBall] = set = new HashSet<double>();

            // De-duplicate on tolerance.
            bool alreadyPresent = false;
            foreach (double existing in set)
                if (ValuesAreEquivalent(existing, commonValue.Value)) { alreadyPresent = true; break; }

            if (!alreadyPresent) set.Add(commonValue.Value);

            slotProposals.Add((ei, blankSlotIndex, blankBall, commonValue.Value));
        }

        // --- B. Fill blanks that are unanimously agreed ---
        // Track which (eqIndex, slotIndex) were patched so we don't double-patch.
        var patched = new HashSet<(int, int)>();

        foreach (var kv in proposals)
        {
            BallNumber blankBall = kv.Key;
            HashSet<double> set = kv.Value;
            if (set.Count != 1) continue;   // contradictory or empty → don't fill

            double value = 0;
            foreach (double v in set) { value = v; break; }

            blankBall.SetSolverOutput(
                value.ToString("G", CultureInfo.InvariantCulture));
            filledBalls.Add(blankBall);

            // Patch every slot that proposed for this blank into a NumberNode.
            var replacement = new NumberNode
            {
                value = value,
                display = value.ToString("G", CultureInfo.InvariantCulture),
            };
            replacement.sourceBalls.Add(blankBall);

            foreach (var p in slotProposals)
            {
                if (p.blank != blankBall) continue;
                if (!patched.Add((p.eqIndex, p.slotIndex))) continue;
                eqs[p.eqIndex].slots[p.slotIndex] = replacement;
            }
        }

        // --- C. Color each '=' based on its current (post-fill) slots ---
        foreach (var eq in eqs)
            eq.equationState = ClassifyEquation(eq);
    }

    /// <summary>
    /// True when the slot's ExprNode is a bare UnknownNode whose slotBalls
    /// contains exactly one ball, and that ball is currently blank.
    /// </summary>
    private static bool IsBareBlankSlot(
        ExprNode slot, List<BallNumber> slotBalls, out BallNumber blankBall)
    {
        blankBall = null;
        if (!(slot is UnknownNode)) return false;
        if (slotBalls == null || slotBalls.Count != 1) return false;
        var b = slotBalls[0];
        if (b == null) return false;
        if (!string.IsNullOrEmpty(b.GetMathContent().Trim())) return false;
        blankBall = b;
        return true;
    }

    /// <summary>
    /// 0 = blue (unknown/unresolved), 1 = green (all numeric and equal),
    /// 2 = red (all numeric and not equal).
    /// </summary>
    private static byte ClassifyEquation(ConnectedEquation eq)
    {
        if (eq.slots.Count < 2) return 0;

        var values = new List<double>(eq.slots.Count);
        foreach (var slot in eq.slots)
        {
            if (!TryEvaluateNode(slot, out double v)) return 0;
            values.Add(v);
        }

        for (int i = 1; i < values.Count; i++)
            if (!ValuesAreEquivalent(values[0], values[i])) return 2;

        return 1;
    }

    private static List<List<Node>> PartitionByEquals(
        List<Node> graph, List<Node> equalsNodes)
    {
        var regions = new List<List<Node>>();
        var equalsSet = new HashSet<Node>(equalsNodes);
        var visited = new HashSet<Node>();

        foreach (Node start in graph)
        {
            if (visited.Contains(start)) continue;
            if (equalsSet.Contains(start))
            {
                visited.Add(start);
                continue;
            }

            var region = new List<Node>();
            var queue = new Queue<Node>();
            queue.Enqueue(start);
            visited.Add(start);

            while (queue.Count > 0)
            {
                Node cur = queue.Dequeue();
                region.Add(cur);
                foreach (Node nb in cur.neighbors)
                {
                    if (visited.Contains(nb)) continue;
                    if (equalsSet.Contains(nb)) continue;   // don't cross '='
                    visited.Add(nb);
                    queue.Enqueue(nb);
                }
            }

            if (region.Count > 0) regions.Add(region);
        }

        return regions;
    }

    private static ExprNode SolveRegion(
        List<Node> nodes, HashSet<BallNumber> ambiguous)
    {
        if (nodes == null || nodes.Count == 0) return null;

        // Each step mutates nodes in place (kills merged nodes
        // and adds a replacement node).

        // Step 1: brackets (recursive).
        ResolveBrackets(nodes, ambiguous);

        // Step 2: adjacency merge (numbers/algebra/fractions only).
        MergeAdjacent(nodes);

        // Step 3: functions.
        ApplyFunctions(nodes, ambiguous);

        // Step 4: powers.
        ApplyPowers(nodes, ambiguous);

        // Step 5: products and division.
        ApplyProducts(nodes, ambiguous);

        // Step 6: sums and subtraction.
        ApplySums(nodes, ambiguous);

                // Count live nodes. If not exactly one, the region is unsolvable.
                int liveCount = 0;
        Node only = null;
        foreach (Node n in nodes)
        {
            if (!n.alive) continue;
            if (n.expr is UnknownNode) continue;   // blanks don't count toward collapse
            liveCount++;
            only = n;
        }

                Debug.Log($"[Region] liveCount={liveCount} nodes=" +
                  string.Join(",", nodes.FindAll(n => n.alive).ConvertAll(n => Content(n))));

                if (liveCount == 1 && only != null) return only.expr;

        // If the only leftovers are blanks, the region is "just a blank".
        // Return an UnknownNode carrying them, but don't red-flag anything.
        if (liveCount == 0)
        {
            var blankOnly = new UnknownNode();
            foreach (Node n in nodes)
            {
                if (!n.alive) continue;
                if (n.expr is UnknownNode) blankOnly.sourceBalls.AddRange(n.expr.sourceBalls);
            }
            return blankOnly;
        }

        // Region did not fully collapse. Flag every remaining live non-blank
        // ball red and return an UnknownNode so the '=' cannot solve it.
        var unknown = new UnknownNode();
        foreach (Node n in nodes)
        {
            if (!n.alive) continue;
            if (n.expr is UnknownNode) continue;   // blanks never red-flag
            foreach (var b in n.expr.sourceBalls) ambiguous.Add(b);
            unknown.sourceBalls.AddRange(n.expr.sourceBalls);
        }
        return unknown;
    }

    private static void ResolveBrackets(
        List<Node> nodes, HashSet<BallNumber> ambiguous)
    {
        while (true)
        {
            // Find an innermost '(' / ')' pair (one with no live bracket between them).
            Node open = null, close = null;
            foreach (Node n in nodes)
            {
                if (!n.alive || !(n.expr is BracketNode b) || !b.isOpen) continue;

                // BFS from open through non-bracket live neighbors until we hit a ')'.
                // Ignore already-consumed brackets. If we hit another '(' first, that
                // pair is inner and must resolve first — skip this open for now.
                var queue = new Queue<Node>();
                var seen = new HashSet<Node> { n };
                queue.Enqueue(n);
                bool foundInnerOpen = false;
                Node candidateClose = null;

                                while (queue.Count > 0 && candidateClose == null)
                {
                    Node cur = queue.Dequeue();
                    foreach (Node nb in cur.neighbors)
                    {
                        if (nb == n || !nb.alive || seen.Contains(nb)) continue;
                        seen.Add(nb);

                        if (nb.expr is BracketNode cb)
                        {
                            // Other open brackets are not walkable for this
                            // search: they mark the start of a nested pair that
                            // must resolve first. Only a close here is a match.
                            if (cb.isOpen) continue;
                            candidateClose = nb;
                            break;
                        }

                        if (nb.expr is EqualsMarker) continue;   // never cross '='

                        queue.Enqueue(nb);
                    }
                }

                if (candidateClose == null) continue;
                open = n; close = candidateClose;
                break;
            }

                        if (open == null)
            {
                Debug.Log($"[Bracket] no pair found in region of {nodes.Count} nodes: " +
                          string.Join(",", nodes.ConvertAll(n => Content(n))));
                return;
            }

            Debug.Log($"[Bracket] pairing open='{Content(open)}' close='{Content(close)}'");

                       // Build the interior: candidates reachable from 'open' without
            // crossing 'close' or '=', then filtered by 1D projection between
            // 'open' and 'close' so exterior balls adjacent to the bracket
            // cannot leak in.
            var candidates = new List<Node>();
            var interiorQueue = new Queue<Node>();
            var interiorSeen = new HashSet<Node> { open, close };
            foreach (Node nb in open.neighbors)
            {
                if (nb == close || !nb.alive || interiorSeen.Contains(nb)) continue;
                if (nb.expr is EqualsMarker) continue;
                interiorSeen.Add(nb);
                interiorQueue.Enqueue(nb);
            }
            while (interiorQueue.Count > 0)
            {
                Node cur = interiorQueue.Dequeue();
                candidates.Add(cur);
                foreach (Node nb in cur.neighbors)
                {
                    if (interiorSeen.Contains(nb)) continue;
                    if (nb.expr is BracketNode) continue;
                    if (nb.expr is EqualsMarker) continue;
                    interiorSeen.Add(nb);
                    interiorQueue.Enqueue(nb);
                }
            }

            Vector2 openPos = SourcePos(open);
            Vector2 closePos = SourcePos(close);
            Vector2 dir = closePos - openPos;
            float D = dir.magnitude;
            if (D < 1e-4f) return;
            Vector2 dirN = dir / D;

                        var interior = new List<Node>();
            foreach (Node c in candidates)
            {
                float t = Vector2.Dot(SourcePos(c) - openPos, dirN);
                bool inside = t > 1e-3f && t < D - 1e-3f;
                Debug.Log($"[Bracket] candidate '{Content(c)}' t={t:F3} D={D:F3} inside={inside}");
                if (inside) interior.Add(c);
            }

            var interiorSet = new HashSet<Node>(interior);

                        Debug.Log($"[Bracket] interior count={interior.Count}: " +
                      string.Join(",", interior.ConvertAll(n => Content(n))));
            ExprNode interiorResult = SolveRegion(interior, ambiguous);
            Debug.Log($"[Bracket] interiorResult='{(interiorResult != null ? Render(interiorResult) : "null")}'");
            if (interiorResult == null)
            {
                open.alive = false;
                close.alive = false;
                ambiguous.Add(open.expr.sourceBalls[0]);
                ambiguous.Add(close.expr.sourceBalls[0]);
                continue;
            }

            // Create a merged node holding the interior result plus both brackets.
            var mergedExpr = interiorResult;
            mergedExpr.sourceBalls.AddRange(open.expr.sourceBalls);
            mergedExpr.sourceBalls.AddRange(close.expr.sourceBalls);

            var mergedNode = new Node
            {
                expr = mergedExpr,
                position = (open.position + close.position) * 0.5f,
                isBracketResult = true,
            };

                        // Reattach neighbors: exterior neighbors of open/close/interior.
            // Use interiorSet (projection-filtered) so exterior candidates that
            // merely touch the bracket (e.g. 'sin' in 'sin(0+0)') stay attached
            // to the merged node.
            var mergedNeighbors = new HashSet<Node>();
            foreach (Node m in new[] { open, close })
                foreach (Node nb in m.neighbors)
                    if (nb.alive && nb != open && nb != close && !interiorSet.Contains(nb))
                        mergedNeighbors.Add(nb);
            foreach (Node i in interior)
                foreach (Node nb in i.neighbors)
                    if (nb.alive && nb != open && nb != close && !interiorSet.Contains(nb))
                        mergedNeighbors.Add(nb);

            foreach (Node nb in mergedNeighbors)
            {
                nb.neighbors.RemoveAll(x => x == open || x == close || interiorSet.Contains(x));
                nb.neighbors.Add(mergedNode);
                mergedNode.neighbors.Add(nb);
            }

            open.alive = false;
            close.alive = false;
            foreach (Node i in interior) i.alive = false;

            nodes.Add(mergedNode);
        }
    }

    // ---- Step 2: adjacency (numbers / algebra / fractions only) ----

    private static void MergeAdjacent(List<Node> nodes)
    {
        while (true)
        {
                                   Node a = null, b = null;
            foreach (Node n in nodes)
            {
                if (!n.alive || !IsAdjacencyLeaf(n.expr)) continue;
                foreach (Node nb in n.neighbors)
                {
                    if (nb.alive && IsAdjacencyLeaf(nb.expr)) { a = n; b = nb; break; }
                }
                if (a != null) break;
            }
            if (a == null) return;

            ExprNode product = MakeProduct(a.expr, b.expr, implicitMultiplication: true);
            MergeInto(nodes, new List<Node> { a, b }, product);
        }
    }

           private static bool IsAdjacencyLeaf(ExprNode expr)
    {
        // Blanks are unknown operands: they participate in adjacency exactly
        // like variables, so '2 [blank]' becomes '2 * ?' and the equation
        // solver can determine the blank's value.
        return expr is NumberNode || expr is VarNode || expr is UnknownNode;
    }

    public static byte ReEvaluateAfterFill(ConnectedEquation eq)
    {
        if (eq == null || eq.slots.Count < 2) return eq?.equationState ?? 0;

        // Find the ball whose value was filled in by the solver.
        BallNumber filled = null;
        foreach (BallNumber b in eq.balls)
        {
            if (b != null && b.IsSolverOutput &&
                !string.IsNullOrEmpty(b.GetSolverOutputContent()))
            {
                if (filled != null) return eq.equationState;   // multiple fills: leave
                filled = b;
            }
        }
        if (filled == null) return eq.equationState;

        // Each slot containing the filled ball needs its UnknownNode replaced with
        // the filled numeric value. We do this on a shallow clone of the slot so
        // the original graph is untouched.
        if (!double.TryParse(
                filled.GetSolverOutputContent(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double filledValue))
            return eq.equationState;

        var values = new List<double>();
        foreach (var slot in eq.slots)
        {
            var substituted = SubstituteUnknown(slot, filledValue);
            if (!TryEvaluateNode(substituted, out double v))
                return eq.equationState;
            values.Add(v);
        }

        for (int i = 1; i < values.Count; i++)
            if (!ValuesAreEquivalent(values[0], values[i]))
                return 2;

        return 1;
    }

    /// <summary>
    /// Returns a shallow copy of 'node' with any UnknownNode replaced by
    /// NumberNode(filledValue). Handles the shapes the graph actually produces.
    /// </summary>
    private static ExprNode SubstituteUnknown(ExprNode node, double filledValue)
    {
        switch (node)
        {
            case UnknownNode _:
                return new NumberNode
                {
                    value   = filledValue,
                    display = filledValue.ToString("G",
                        System.Globalization.CultureInfo.InvariantCulture),
                };

            case NumberNode _:
            case VarNode _:
                return node;

            case FuncNode f when f.arg != null:
                return new FuncNode { name = f.name, arg = SubstituteUnknown(f.arg, filledValue) };

            case OpNode o when o.left != null && o.right != null:
                return new OpNode
                {
                    op    = o.op,
                    left  = SubstituteUnknown(o.left,  filledValue),
                    right = SubstituteUnknown(o.right, filledValue),
                };

            case OpNode o when o.left != null && o.hasFixedExponent:
                return new OpNode
                {
                    op               = '^',
                    left             = SubstituteUnknown(o.left, filledValue),
                    fixedExponent    = SubstituteUnknown(o.fixedExponent, filledValue),
                    hasFixedExponent = true,
                };

            default:
                return node;
        }
    }

    

    private static ExprNode MakeProduct(ExprNode a, ExprNode b, bool implicitMultiplication = false)
    {
        if (a is NumberNode na && b is NumberNode nb)
        {
            double v = na.value * nb.value;
            var merged = new NumberNode
            {
                value   = v,
                display = v.ToString("G", CultureInfo.InvariantCulture),
            };
            return merged;
        }
        var op = new OpNode { op = '*', implicitMultiplication = implicitMultiplication };
        op.left  = a;
        op.right = b;
        return op;
    }

    private static ExprNode FoldNumeric(ExprNode a, ExprNode b, char op, string sep = null)
    {
        if (!(a is NumberNode na) || !(b is NumberNode nb))
            return null;

        double v;
        switch (op)
        {   
            case '+': v = na.value + nb.value; break;
            case '-': v = na.value - nb.value; break;
            case '*': v = na.value * nb.value; break;
            case '/':
                if (nb.value == 0) return null;
                v = na.value / nb.value;
                break;
            case '^': v = System.Math.Pow(na.value, nb.value); break;
            default:  return null;
        }

        var n = new NumberNode
        {
            value   = v,
            display = v.ToString("G", CultureInfo.InvariantCulture),
        };
        n.sourceBalls.AddRange(a.sourceBalls);
        n.sourceBalls.AddRange(b.sourceBalls);
        return n;
    }

    private static bool TryEvaluateNode(ExprNode node, out double value)
    {
        value = 0;
        switch (node)
        {
            case NumberNode n:
                value = n.value;
                return true;

            case OpNode o when o.left != null && o.right != null:
                if (!TryEvaluateNode(o.left, out double l) ||
                    !TryEvaluateNode(o.right, out double r)) return false;
                switch (o.op)
                {
                    case '+': value = l + r; return true;
                    case '-': value = l - r; return true;
                    case '*': value = l * r; return true;
                    case '/': if (r == 0) return false;
                            value = l / r; return true;
                    case '^': value = System.Math.Pow(l, r); return true;
                }
                return false;

            case OpNode o when o.left != null && o.hasFixedExponent:
                if (!TryEvaluateNode(o.left, out double b) ||
                    !TryEvaluateNode(o.fixedExponent, out double e)) return false;
                value = System.Math.Pow(b, e);
                return true;

            case FuncNode f when f.arg != null:
                if (!TryEvaluateNode(f.arg, out double av)) return false;
                return TryEvaluateFunction(f.name, av, out value);

            default:
                return false;
        }
    }

    // ---- Step 3: functions ----

    private static void ApplyFunctions(List<Node> nodes, HashSet<BallNumber> ambiguous)
    {
        while (true)
        {
            Node fn = null;
            foreach (Node n in nodes)
            {
                if (!n.alive || !(n.expr is FuncNode f)) continue;
                if (f.name == "1/x" && f.arg != null) continue;   // already bound
                if (f.arg != null) continue;                       // ordinary function already bound
                fn = n; break;
            }
            if (fn == null) return;

            var func = (FuncNode)fn.expr;
            List<Node> neighbors = LiveNeighbors(fn);

                        // Count real operands. Bracket results count as operands even
            // though their internal ExprNode is often an OpNode. Operators,
            // brackets, and equals markers do not.
            var operands = new List<Node>();
            foreach (Node nb in neighbors)
            {
                if (nb.isBracketResult) { operands.Add(nb); continue; }
                if (nb.expr is OpNode) continue;
                if (nb.expr is BracketNode) continue;
                if (nb.expr is EqualsMarker) continue;
                operands.Add(nb);
            }

            if (operands.Count != 1)
            {
                // 0 or 2+ operands → red, inert.
                foreach (BallNumber b in func.sourceBalls) ambiguous.Add(b);
                fn.alive = false;
                continue;
            }

            Node arg = operands[0];

            // Try numeric collapse for pow / 1/x / normal functions.
            ExprNode collapsed = TryCollapseFunction(func, arg.expr);
            if (collapsed != null)
            {
                collapsed.sourceBalls.AddRange(func.sourceBalls);
                collapsed.sourceBalls.AddRange(arg.expr.sourceBalls);
                AbsorbInto(nodes, fn, new List<Node> { arg });
                fn.expr = collapsed;
            }
            else
            {
                func.arg = arg.expr;
                AbsorbInto(nodes, fn, new List<Node> { arg });
            }
        }
    }

    private static ExprNode TryCollapseFunction(FuncNode func, ExprNode arg)
    {
        if (func.name == "1/x" && func.arg != null)
        {
            // Already bound (from a '/x' ball).
            if (arg is NumberNode an && an.value != 0)
            {
                return new NumberNode
                {
                    value = 1.0 / an.value,
                    display = "(1/" + an.display + ")",
                };
            }
            return null;
        }

        if (arg is NumberNode num &&
            TryEvaluateFunction(func.name, num.value, out double result))
        {
            return new NumberNode
            {
                value = result,
                display = FormatFunctionDisplay(func.name, num.display, result),
            };
        }
        return null;
    }

    // ---- Step 4: powers ----

    private static void ApplyPowers(List<Node> nodes, HashSet<BallNumber> ambiguous)
    {
        ApplyExplicitPowers(nodes, ambiguous);
        ApplyDirectionalOperators(nodes, '^', ambiguous);
    }

    private static void ApplyExplicitPowers(List<Node> nodes, HashSet<BallNumber> ambiguous)
    {
        while (true)
        {
            Node powerNode = null;
            foreach (Node node in nodes)
            {
                if (!node.alive || !(node.expr is OpNode op) ||
                    op.op != '^' || !op.hasFixedExponent ||
                    op.left != null || op.right != null)
                    continue;

                powerNode = node;
                break;
            }

            if (powerNode == null)
                return;

            var power = (OpNode)powerNode.expr;
            Node argument = null;
            bool invalidNeighbor = false;
            foreach (Node neighbor in LiveNeighbors(powerNode))
            {
                if (IsPendingOperator(neighbor.expr))
                    continue;
                if (argument != null)
                    invalidNeighbor = true;
                argument = neighbor;
            }

            if (invalidNeighbor || argument == null)
            {
                foreach (BallNumber ball in power.sourceBalls)
                    ambiguous.Add(ball);
                powerNode.alive = false;
                continue;
            }

            var folded = FoldNumeric(argument.expr, power.fixedExponent, '^');
            ExprNode result = folded;
            if (result == null)
            {
                result = new OpNode
                {
                    op    = '^',
                    left  = argument.expr,
                    right = power.fixedExponent,
                };
            }
            result.sourceBalls.AddRange(power.sourceBalls);
            powerNode.expr = result;
            AbsorbInto(nodes, powerNode, new List<Node> { argument });
        }
    }

    // ---- Step 5: products (*, /, and implicit bracket-number) ----

    private static void ApplyProducts(List<Node> nodes, HashSet<BallNumber> ambiguous)
    {
        ApplyDirectionalOperators(nodes, '/', ambiguous);

        while (true)
        {
            // First: any '*' with 2+ live groups touching it merges them.
            Node star = null;
            foreach (Node n in nodes)
            {
                if (!n.alive || !(n.expr is OpNode op) || op.op != '*') continue;
                if (op.left != null || op.right != null) continue;
                if (LiveNeighbors(n).Count >= 2) { star = n; break; }
            }

            if (star != null)
            {
                var neighbors = LiveNeighbors(star);
                ExprNode acc = neighbors[0].expr;
                for (int i = 1; i < neighbors.Count; i++)
                {
                    var folded = FoldNumeric(acc, neighbors[i].expr, '*');
                    if (folded != null) { acc = folded; continue; }

                    var op = new OpNode { op = '*' };
                    op.left  = acc;
                    op.right = neighbors[i].expr;
                    acc = op;
                }
                acc.sourceBalls.AddRange(star.expr.sourceBalls);
                AbsorbInto(nodes, star, neighbors);
                star.expr = acc;
                continue;
            }

                        // Then: implicit '*' between a bracket result and an adjacent
            // number/variable leaf. Exactly one side must be a bracket result,
            // and the other side must be a plain leaf. This prevents the
            // bracket result from consuming '=', operators, functions, or
            // another bracket (those are handled by their own steps).
            bool didImplicit = false;
            foreach (Node n in nodes)
            {
                if (!n.alive) continue;
                foreach (Node nb in n.neighbors)
                {
                    if (!nb.alive || nb == n) continue;
                    if (n.isBracketResult == nb.isBracketResult) continue;

                    Node otherSide = n.isBracketResult ? nb : n;
                    if (!IsAdjacencyLeaf(otherSide.expr)) continue;

                    ExprNode combined = FoldNumeric(n.expr, nb.expr, '*', "")
                    ?? MakeProduct(n.expr, nb.expr, implicitMultiplication: true);
                    combined.sourceBalls.AddRange(n.expr.sourceBalls);
                    combined.sourceBalls.AddRange(nb.expr.sourceBalls);
                    var mergedNode = MergeInto(nodes, new List<Node> { n, nb }, combined);
                    mergedNode.isBracketResult = false;
                    didImplicit = true;
                    break;
                }
                if (didImplicit) break;
            }
            if (didImplicit) continue;

                        // Flag dangling '*' as red. Skip already-merged '*' nodes.
            foreach (Node n in nodes)
            {
                if (!n.alive || !(n.expr is OpNode op) || op.op != '*') continue;
                if (op.left != null || op.right != null) continue;   // already merged
                if (LiveNeighbors(n).Count < 2)
                {
                    ambiguous.Add(n.expr.sourceBalls[0]);
                    n.alive = false;
                }
            }

            return;
        }
    }

    

    // ---- Step 6: sums (+, -) ----

    private static void ApplySums(List<Node> nodes, HashSet<BallNumber> ambiguous)
    {
        ApplyDirectionalOperators(nodes, '-', ambiguous);

        while (true)
        {
            Node plus = null;
            foreach (Node node in nodes)
            {
                if (!node.alive || !(node.expr is OpNode op) || op.op != '+')
                    continue;
                if (op.left == null && op.right == null && LiveNeighbors(node).Count >= 2)
                {
                    plus = node;
                    break;
                }
            }

            if (plus != null)
            {
                var neighbors = LiveNeighbors(plus);
                ExprNode acc = neighbors[0].expr;
                for (int i = 1; i < neighbors.Count; i++)
                {
                    var folded = FoldNumeric(acc, neighbors[i].expr, '+');
                    if (folded != null) { acc = folded; continue; }

                    var op = new OpNode { op = '+' };
                    op.left  = acc;
                    op.right = neighbors[i].expr;
                    acc = op;
                }
                acc.sourceBalls.AddRange(plus.expr.sourceBalls);
                AbsorbInto(nodes, plus, neighbors);
                plus.expr = acc;
                continue;
            }

            foreach (Node node in nodes)
            {
                if (!node.alive || !(node.expr is OpNode op) || op.op != '+')
                    continue;
                if (op.left != null || op.right != null)
                    continue;
                if (LiveNeighbors(node).Count < 2)
                {
                    ambiguous.Add(node.expr.sourceBalls[0]);
                    node.alive = false;
                }
            }

            return;
        }
    }

        private static void ApplyDirectionalOperators(
        List<Node> nodes, char operation, HashSet<BallNumber> ambiguous)
    {
        // Shared-input rule: two+ same-type triangles competing for the same
        // input operand are ambiguous. Red them before any chain work.
        MarkSharedInputTriangles(nodes, operation, ambiguous);

        // ---- Chain-aware binding ----
        // Repeatedly find a chain-start (a triangle whose outside/input
        // operand is not produced by another unbound triangle of the same
        // type), bind it, and loop. A chain-start always exists for any
        // acyclic chain; if none can be found, we have a circular chain and
        // the region collapses to leftover nodes → red via fallback.
        while (true)
        {
            var triangles = CollectUnboundTriangles(nodes, operation);
            if (triangles.Count == 0) return;

            Node chainStart = FindChainStart(triangles);
            if (chainStart == null)
            {
                // Circular or otherwise unorderable chain — red every
                // remaining triangle of this type.
                foreach (Node t in triangles)
                {
                    foreach (BallNumber ball in t.expr.sourceBalls)
                        if (ball != null) ambiguous.Add(ball);
                    t.alive = false;
                }
                return;
            }

            // ---- Bind this chain-start exactly the way the old loop did ----
            var opExpr = (OpNode)chainStart.expr;
            Node outsideOperand = null;
            Node insideOperand  = null;
            bool invalidNeighbor = false;
            bool adjacentCrescent = false;

            foreach (Node neighbor in LiveNeighbors(chainStart))
            {
                if (neighbor.expr is EqualsMarker) continue;

                if (neighbor.expr is OpNode nbOp &&
                    nbOp.directionalOperator != null &&
                    nbOp.left == null && nbOp.right == null)
                {
                    adjacentCrescent = true;
                    continue;
                }

                if (IsPendingOperator(neighbor.expr))
                    continue;

                if (neighbor.expr.sourceBalls.Count == 0)
                {
                    invalidNeighbor = true;
                    continue;
                }

                bool anyInside  = false;
                bool anyOutside = false;
                bool anyInRange = false;

                foreach (BallNumber ball in neighbor.expr.sourceBalls)
                {
                    if (ball == null) continue;
                    Collider2D collider = ball.GetComponent<Collider2D>();
                    if (collider == null) continue;

                    Vector2 center = collider.bounds.center;
                    float radius = Mathf.Max(
                        collider.bounds.extents.x,
                        collider.bounds.extents.y);

                    if (!opExpr.directionalOperator.TryGetOperandSide(
                            center, radius, out bool isInside))
                        continue;

                    anyInRange = true;
                    if (isInside) anyInside = true;
                    else          anyOutside = true;
                }

                if (!anyInRange) continue;
                if (anyInside && anyOutside) { invalidNeighbor = true; continue; }

                                if (operation == '^')
                {
                    // For power triangles, tip side (isInside) is the BASE
                    // (left operand of base^exp). Flat side (outside) is the
                    // EXPONENT (right operand). We store them as
                    // outsideOperand = base, insideOperand = exponent so the
                    // rest of the method (result construction, AbsorbInto)
                    // works unchanged.
                    if (anyInside)
                    {
                        if (outsideOperand != null) invalidNeighbor = true;
                        outsideOperand = neighbor;   // tip side = base
                    }
                    else
                    {
                        if (insideOperand != null) invalidNeighbor = true;
                        insideOperand = neighbor;    // flat side = exponent
                    }
                }
                else
                {
                    if (anyInside)
                    {
                        if (insideOperand != null) invalidNeighbor = true;
                        insideOperand = neighbor;
                    }
                    else
                    {
                        if (outsideOperand != null) invalidNeighbor = true;
                        outsideOperand = neighbor;
                    }
                }
            }

            if (adjacentCrescent || invalidNeighbor ||
                outsideOperand == null || insideOperand == null)
            {
                foreach (BallNumber ball in opExpr.sourceBalls)
                    if (ball != null) ambiguous.Add(ball);
                chainStart.alive = false;
                continue;
            }

            var folded = FoldNumeric(outsideOperand.expr, insideOperand.expr, operation);
            ExprNode result = folded;
            if (result == null)
            {
                result = new OpNode
                {
                    op                  = operation,
                    left                = outsideOperand.expr,
                    right               = insideOperand.expr,
                    directionalOperator = opExpr.directionalOperator,
                };
            }
            result.sourceBalls.AddRange(opExpr.sourceBalls);
            chainStart.expr = result;
            AbsorbInto(nodes, chainStart, new List<Node> { outsideOperand, insideOperand });
        }
    }

        /// <summary>
    /// Every live, unbound triangle of the given operator type.
    /// </summary>
    private static List<Node> CollectUnboundTriangles(List<Node> nodes, char operation)
    {
        var result = new List<Node>();
        foreach (Node n in nodes)
        {
            if (!n.alive) continue;
            if (!(n.expr is OpNode op)) continue;
            if (op.directionalOperator == null) continue;
            if (op.op != operation) continue;
            if (op.left != null || op.right != null) continue;   // already bound
            result.Add(n);
        }
        return result;
    }

    /// <summary>
    /// A chain-start is a triangle whose input (outside/flat-side) operand is
    /// NOT the output (inside/tip-side) operand of another unbound triangle of
    /// the same type. Returns null if no chain-start exists (circular chain).
    /// </summary>
    private static Node FindChainStart(List<Node> triangles)
    {
        if (triangles.Count == 0) return null;
        if (triangles.Count == 1) return triangles[0];

        // For each triangle, figure out which of its live neighbors is its
        // input (outside cone). If none of those is the inside operand of
        // another triangle, it's a start.
        foreach (Node t in triangles)
        {
            var opExpr = (OpNode)t.expr;
            Node inputNeighbor = FindSingleOperand(t, opExpr, wantInside: false);
            if (inputNeighbor == null) continue;   // can't classify; skip

            bool consumedByAnother = false;
            foreach (Node other in triangles)
            {
                if (other == t) continue;

                Node otherOutput = FindSingleOperand(
                    other, (OpNode)other.expr, wantInside: true);

                if (otherOutput == inputNeighbor)
                {
                    consumedByAnother = true;
                    break;
                }
            }

            if (!consumedByAnother) return t;
        }

        return null;
    }

    /// <summary>
    /// Locates the single live neighbor of 't' that is on 't's inside
    /// (tip) or outside (flat) side. Returns null if zero or multiple
    /// matches — the triangle can't be classified as start or non-start.
    /// </summary>
    private static Node FindSingleOperand(Node t, OpNode opExpr, bool wantInside)
    {
        Node found = null;
        foreach (Node neighbor in LiveNeighbors(t))
        {
            if (neighbor.expr is EqualsMarker) continue;

            if (neighbor.expr is OpNode nbOp &&
                nbOp.directionalOperator != null &&
                nbOp.left == null && nbOp.right == null)
                continue;   // adjacent crescent, not an operand

            if (IsPendingOperator(neighbor.expr)) continue;
            if (neighbor.expr.sourceBalls.Count == 0) continue;

            bool matches = false;
            foreach (BallNumber ball in neighbor.expr.sourceBalls)
            {
                if (ball == null) continue;
                Collider2D collider = ball.GetComponent<Collider2D>();
                if (collider == null) continue;

                Vector2 center = collider.bounds.center;
                float radius = Mathf.Max(
                    collider.bounds.extents.x,
                    collider.bounds.extents.y);

                if (!opExpr.directionalOperator.TryGetOperandSide(
                        center, radius, out bool isInside))
                    continue;

                if (isInside == wantInside) { matches = true; break; }
            }

            if (matches)
            {
                if (found != null) return null;   // multiple matches → ambiguous
                found = neighbor;
            }
        }
        return found;
    }

        /// <summary>
    /// Detects a shared input operand among same-type directional operators
    /// and flags them red.
    ///
    /// A node N is a shared input when two or more live triangles with
    /// op == operation each have N in their outside (input) cone. Those
    /// triangles cannot all consume N as their input, so the whole cluster
    /// is ambiguous. We mark N and the competing triangles as ambiguous and
    /// kill their nodes so the rest of the rewrite skips them.
    ///
    /// Runs once per ApplyDirectionalOperators call, so:
    ///   - it fires on raw single-ball inputs (first call of each op type), and
    ///   - it fires again after any earlier fold that produced a shared input
    ///     group (later call of the same op type, or later op type in the
    ///     precedence chain).
    ///
    /// Same-output sharing (2 -> 3 <- 4) and chains (2 -> 3 -> 4) never fire:
    /// they are captured as either shared inside operands or output→input,
    /// neither of which matches this check.
    /// </summary>
    private static void MarkSharedInputTriangles(
        List<Node> nodes, char operation, HashSet<BallNumber> ambiguous)
    {
        // Collect the live triangle nodes of this exact operator type.
        var triangles = new List<Node>();
        foreach (Node n in nodes)
        {
            if (!n.alive) continue;
            if (!(n.expr is OpNode op)) continue;
            if (op.directionalOperator == null) continue;
            if (op.op != operation) continue;
            if (op.left != null || op.right != null) continue;   // already bound
            triangles.Add(n);
        }
        if (triangles.Count < 2) return;

        // For each live non-triangle node that has operands, count how many of
        // these triangles treat it as their input (outside cone). If ≥2, mark.
        var toKill = new HashSet<Node>();

        foreach (Node candidate in nodes)
        {
            if (!candidate.alive) continue;
            if (toKill.Contains(candidate)) continue;

            // Skip candidates that are themselves pending triangles of this type.
            if (candidate.expr is OpNode co &&
                co.directionalOperator != null &&
                co.op == operation &&
                co.left == null && co.right == null)
                continue;

            // Skip nodes with no source balls — nothing geometric to test.
            if (candidate.expr.sourceBalls.Count == 0) continue;

            var consumers = new List<Node>();
            foreach (Node t in triangles)
            {
                if (t == candidate) continue;
                if (!t.alive) continue;

                bool seesAsInput = false;
                foreach (BallNumber ball in candidate.expr.sourceBalls)
                {
                    if (ball == null) continue;
                    Collider2D collider = ball.GetComponent<Collider2D>();
                    if (collider == null) continue;

                    Vector2 center = collider.bounds.center;
                    float radius = Mathf.Max(
                        collider.bounds.extents.x,
                        collider.bounds.extents.y);

                    if (!((OpNode)t.expr).directionalOperator.TryGetOperandSide(
                            center, radius, out bool isInside))
                        continue;

                    if (!isInside)
                    {
                        seesAsInput = true;
                        break;
                    }
                }

                if (seesAsInput) consumers.Add(t);
            }

            if (consumers.Count >= 2)
            {
                // Flag the shared input node's balls.
                foreach (BallNumber ball in candidate.expr.sourceBalls)
                    if (ball != null) ambiguous.Add(ball);

                // Flag every competing triangle's balls.
                foreach (Node t in consumers)
                {
                    foreach (BallNumber ball in t.expr.sourceBalls)
                        if (ball != null) ambiguous.Add(ball);
                    toKill.Add(t);
                }

                toKill.Add(candidate);
            }
        }

        foreach (Node n in toKill)
            n.alive = false;
    }

    private static bool IsPendingOperator(ExprNode expression)
    {
        return expression is OpNode op && op.left == null && op.right == null;
    }

    // ---- Step 7: equals evaluation ----

    private static bool EvaluateEquals(
    List<ExprNode> slots,
    List<BallNumber> emptyBalls,
    out byte state)
    {
        state = 2;
        if (slots == null || slots.Count < 2) { state = 0; return false; }

        var values = new List<double>();
        foreach (var slot in slots)
        {
            if (!TryEvaluateNode(slot, out double v))
            {
                if (emptyBalls.Count == 1) { state = 0; return false; }
                state = 2;
                return false;
            }
            values.Add(v);
        }

        for (int i = 1; i < values.Count; i++)
        {
            if (!ValuesAreEquivalent(values[0], values[i]))
            {
                state = 2;
                return false;
            }
        }

        state = 1;
        return true;
    }

    private static int regionIndexOf(List<List<Node>> regions, Node node)
    {
        for (int i = 0; i < regions.Count; i++)
            if (regions[i].Contains(node)) return i;
        return -1;
    }

    private static string RenderSlots(List<ExprNode> slots)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < slots.Count; i++)
        {
            if (i > 0) sb.Append('=');
            sb.Append(Render(slots[i]));
        }
        return sb.ToString();
    }

    // Marker node for the '=' ball; never participates in rewrite rules.
    private sealed class EqualsMarker : ExprNode { }

    // ============================================================
    // Graph construction
    // ============================================================

            private static List<Node> BuildGraph(
        List<BallNumber> cluster,
        HashSet<BallNumber> removed)
    {
        var byBall = new Dictionary<BallNumber, Node>();
        var nodes = new List<Node>();

        foreach (BallNumber ball in cluster)
        {
            ExprNode expr = MakeLeaf(ball);
            byBall[ball] = new Node { expr = expr, position = ball.transform.position };

            if (removed != null && removed.Contains(ball))
                continue;   // not added to the graph; acts as empty space

            nodes.Add(byBall[ball]);
        }

        for (int i = 0; i < cluster.Count; i++)
        {
            if (removed != null && removed.Contains(cluster[i])) continue;
            Collider2D ci = cluster[i].GetComponent<Collider2D>();
            if (ci == null) continue;

            for (int j = i + 1; j < cluster.Count; j++)
            {
                if (removed != null && removed.Contains(cluster[j])) continue;
                Collider2D cj = cluster[j].GetComponent<Collider2D>();
                if (cj == null) continue;
                if (!BallConnectionRules.AreConnected(ci, cj)) continue;

                Node a = byBall[cluster[i]];
                Node b = byBall[cluster[j]];
                a.neighbors.Add(b);
                b.neighbors.Add(a);
            }
        }

        return nodes;
    }

    private static ExprNode MakeLeaf(BallNumber ball)
    {
        string text = ball.GetMathContent().Trim();

        if (text.Length == 0)
        {
            var u = new UnknownNode();
            u.sourceBalls.Add(ball);
            return u;
        }

        if (text == "=")
        {
            var m = new EqualsMarker();
            m.sourceBalls.Add(ball);
            return m;
        }

        if (text == "(" || text == ")")
        {
            var b = new BracketNode { isOpen = text == "(" };
            b.sourceBalls.Add(ball);
            return b;
        }

        if (DirectionalOperator.IsDirectionalOperatorText(text))
        {
            var op = new OpNode
            {
                op = text[0],
                directionalOperator = ball.GetComponent<DirectionalOperator>(),
            };
            op.sourceBalls.Add(ball);
            return op;
        }

        // --- Explicit fraction balls: '/N', '/x' are NUMBERS ---
        if (MathSyntax.IsFractionBall(text))
        {
            string suffix = MathSyntax.UnaryPrefixBoundValue(text);

            // '/N' -> number 1/N.
            if (double.TryParse(suffix,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double denom) && denom != 0)
            {
                var n = new NumberNode
                {
                    value = 1.0 / denom,
                    display = "1/" + suffix,
                };
                n.sourceBalls.Add(ball);
                return n;
            }

            // '/x' -> symbolic 1/x, baked in, no neighbor consumed.
            if (suffix.Length == 1 && MathSyntax.IsVariableLetter(suffix[0]))
            {
                var v = new VarNode { name = suffix[0] };
                v.sourceBalls.Add(ball);
                var f = new FuncNode { name = "1/x", arg = v };
                f.sourceBalls.Add(ball);
                return f;
            }

            // '/pi', '/e' -> constants.
            if (MathSyntax.IsConstantName(suffix))
            {
                double cv = suffix.ToLowerInvariant() == "pi" ? System.Math.PI : System.Math.E;
                var n = new NumberNode { value = 1.0 / cv, display = "1/" + suffix };
                n.sourceBalls.Add(ball);
                return n;
            }

            var bad = new UnknownNode();
            bad.sourceBalls.Add(ball);
            return bad;
        }

        // --- Explicit power balls: '^N', '^x' bind one base operand ---
        if (MathSyntax.IsPowerBall(text))
        {
            string suffix = MathSyntax.UnaryPrefixBoundValue(text);

            ExprNode exponentExpr;
            if (double.TryParse(suffix,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double exp))
            {
                exponentExpr = new NumberNode { value = exp, display = suffix };
            }
            else if (suffix.Length == 1 && MathSyntax.IsVariableLetter(suffix[0]))
            {
                exponentExpr = new VarNode { name = suffix[0] };
            }
            else
            {
                var bad = new UnknownNode();
                bad.sourceBalls.Add(ball);
                return bad;
            }

            var power = new OpNode
            {
                op = '^',
                fixedExponent = exponentExpr,
                hasFixedExponent = true,
            };
            power.sourceBalls.Add(ball);
            return power;
        }

        // --- Remaining function balls (sin, cos, ..., %, !, 1/x typed literally) ---
        if (IsFunctionText(text))
        {
            var f = new FuncNode { name = text.ToLowerInvariant() };
            f.sourceBalls.Add(ball);
            return f;
        }

        // --- Multiplication operator ---
        if (text == "*" || text == "\u00d7")
        {
            var op = new OpNode { op = '*' };
            op.sourceBalls.Add(ball);
            return op;
        }

        // --- Addition operator ---
        if (text == "+")
        {
            var op = new OpNode { op = '+' };
            op.sourceBalls.Add(ball);
            return op;
        }

        // --- Numbers / constants / variables ---
        if (double.TryParse(text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double num))
        {
            var n = new NumberNode { value = num, display = text };
            n.sourceBalls.Add(ball);
            return n;
        }

        string lowerText = text.ToLowerInvariant();
        if (lowerText == "pi" || lowerText == "e")
        {
            double constantValue = lowerText == "pi" ? System.Math.PI : System.Math.E;
            var c = new NumberNode { value = constantValue, display = text };
            c.sourceBalls.Add(ball);
            return c;
        }

        if (text.Length == 1 && MathSyntax.IsVariableLetter(text[0]))
        {
            var v = new VarNode { name = text[0] };
            v.sourceBalls.Add(ball);
            return v;
        }

        var unknown = new UnknownNode();
        unknown.sourceBalls.Add(ball);
        return unknown;
    }

    private static bool IsFunctionText(string text)
    {
        // Bare '-', '/', and '^' are directional operators handled earlier.
        return MathSyntax.IsUnaryFunctionName(text) ||
            text == "%" || text == "!" || text == "1/x";
    }


    private static Node FirstLive(List<Node> nodes)
    {
        foreach (Node n in nodes) if (n.alive) return n;
        return null;
    }

        private static string Content(Node n)
    {
        if (n == null) return "<null>";
        if (n.expr == null) return "<noexpr>";
        string s = Render(n.expr);
        return s.Length == 0 ? "<empty>" : s;
    }

        private static Vector2 SourcePos(Node n)
    {
        if (n?.expr?.sourceBalls != null && n.expr.sourceBalls.Count > 0)
        {
            var b = n.expr.sourceBalls[0];
            if (b != null) return b.transform.position;
        }
        return Vector2.zero;
    }

    private static List<Node> LiveNeighbors(Node n)
    {
        var list = new List<Node>();
        foreach (Node nb in n.neighbors) if (nb.alive) list.Add(nb);
        return list;
    }

    // ============================================================
    // Graph surgery
    // ============================================================

    // Replace 'toMerge' with a single new node whose ExprNode is 'replacement'.
    private static Node MergeInto(
        List<Node> allNodes, List<Node> toMerge, ExprNode replacement)
    {
        var mergedSet = new HashSet<Node>(toMerge);
        var newNeighbors = new List<Node>();

        foreach (Node m in toMerge)
            foreach (Node nb in m.neighbors)
                if (!mergedSet.Contains(nb) && !newNeighbors.Contains(nb))
                    newNeighbors.Add(nb);

        Vector2 mergedPosition = Vector2.zero;
        foreach (Node node in toMerge)
            mergedPosition += node.position;
        if (toMerge.Count > 0)
            mergedPosition /= toMerge.Count;

        var mergedNode = new Node { expr = replacement, position = mergedPosition };

        foreach (Node nb in newNeighbors)
        {
            nb.neighbors.RemoveAll(x => mergedSet.Contains(x));
            nb.neighbors.Add(mergedNode);
            mergedNode.neighbors.Add(nb);
        }

        foreach (Node m in toMerge)
            foreach (BallNumber b in m.expr.sourceBalls)
                replacement.sourceBalls.Add(b);

        foreach (Node m in toMerge) m.alive = false;

        allNodes.Add(mergedNode);
        return mergedNode;
    }

    // Absorb 'absorbed' into 'keeper'. Keeper survives; absorbed die.
    private static void AbsorbInto(
        List<Node> allNodes, Node keeper, List<Node> absorbed)
    {
        var absorbedSet = new HashSet<Node>(absorbed);

        foreach (Node a in absorbed)
        {
            foreach (BallNumber b in a.expr.sourceBalls)
                keeper.expr.sourceBalls.Add(b);

            foreach (Node nb in a.neighbors)
            {
                if (absorbedSet.Contains(nb) || nb == keeper) continue;
                if (!keeper.neighbors.Contains(nb)) keeper.neighbors.Add(nb);
                nb.neighbors.RemoveAll(x => absorbedSet.Contains(x) || x == a);
                if (!nb.neighbors.Contains(keeper)) nb.neighbors.Add(keeper);
            }

            a.alive = false;
        }
    }

   
    private static string FormatFunctionDisplay(string name, string argDisplay, double value)
    {
        switch (name)
        {
            case "%":   return "(" + argDisplay + "%)";
            case "!":   return "(" + argDisplay + "!)";
            case "1/x": return "(1/(" + argDisplay + "))";
            default:    return name + "(" + argDisplay + ")";
        }
    }

    private static bool TryEvaluateFunction(string name, double arg, out double result)
    {
        string expr;
        switch (name)
        {
            case "%":   expr = "(" + arg.ToString("G", CultureInfo.InvariantCulture) + "%)"; break;
            case "!":   expr = "(" + arg.ToString("G", CultureInfo.InvariantCulture) + "!)"; break;
            case "1/x": expr = "(1/(" + arg.ToString("G", CultureInfo.InvariantCulture) + "))"; break;
            default:    expr = name + "(" + arg.ToString("G", CultureInfo.InvariantCulture) + ")"; break;
        }

        var empty = new Dictionary<char, double>();
        if (MathEquationSolver.TryEvaluateNumericExpression(expr, empty, out result))
            return true;

        if (MathEquationSolver.TryEvaluateNumericExpressionComplex(expr, empty, out var c) &&
            System.Math.Abs(c.Imaginary) < 1e-10)
        {
            result = c.Real;
            return true;
        }

        result = 0;
        return false;
    }

    // ============================================================
    // Rendering (used only for display and solver input)
    // ============================================================

    private static string RenderEquation(ExprNode left, ExprNode right)
    {
        return Render(left) + "=" + Render(right);
    }

    public static string Render(ExprNode node)
    {
        switch (node)
        {
            case NumberNode n:
                return n.display.Contains("/") ? "(" + n.display + ")" : n.display;
            case VarNode v:
                return v.name.ToString();
            case UnknownNode _:
                return MathEquationSolver.UnknownSymbol.ToString();
            case BracketNode b:
                return b.isOpen ? "(" : ")";
            case UnaryNode u:
                return u.op + Render(u.operand);
            case OpNode o:
                return RenderOp(o);
            case FuncNode f:
                return RenderFunc(f);
            default:
                return string.Empty;
        }
    }

    private static string RenderOp(OpNode o)
    {
        string left  = Render(o.left);
        string right = Render(o.right);
        string sep   = o.implicitMultiplication ? "" : o.op.ToString();

        return "(" + left + sep + right + ")";
    }

    private static string RenderFunc(FuncNode f)
    {
        string arg = f.arg != null ? Render(f.arg) : "";
        switch (f.name)
        {
            case "%":   return "(" + arg + "%)";
            case "!":   return "(" + arg + "!)";
            case "1/x": return "(1/(" + arg + "))";
            default:    return f.name + "(" + arg + ")";
        }
    }


    // ============================================================
    // Variable definitions (unchanged externally; uses rendered strings)
    // ============================================================

        public static Dictionary<char, double> BuildVariableDefinitions(
        List<ConnectedEquation> equations)
    {
        // Variables we've decided can never be resolved because two
        // equations propose different values for them.
        var conflicts = new HashSet<char>();

        Dictionary<char, double> definitions = new Dictionary<char, double>();
        bool foundDefinition;
        do
        {
            Dictionary<char, double> candidates = new Dictionary<char, double>();
            HashSet<char> conflictingCandidates = new HashSet<char>();

            foreach (ConnectedEquation equation in equations)
            {
                if (HasAmbiguousOperands(equation)) continue;
                if (!TryGetCompleteEquation(equation, out string completeEquation)) continue;

                HashSet<char> variables = GetVariables(completeEquation);
                foreach (char variable in variables)
                {
                    if (definitions.ContainsKey(variable) ||
                        conflicts.Contains(variable)) continue;

                    string candidateEquation = ReplaceVariableWithUnknown(completeEquation, variable);
                    if (!TrySolveEqualityChain(candidateEquation, definitions, out string result))
                        continue;
                    if (!double.TryParse(result, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double value))
                        continue;

                    if (candidates.TryGetValue(variable, out double existing) &&
                        !ValuesAreEquivalent(existing, value))
                    {
                        conflictingCandidates.Add(variable);
                    }
                    else
                    {
                        candidates[variable] = value;
                    }
                }
            }

            foundDefinition = false;
            foreach (KeyValuePair<char, double> candidate in candidates)
            {
                if (conflictingCandidates.Contains(candidate.Key))
                {
                    conflicts.Add(candidate.Key);
                    continue;
                }
                definitions.Add(candidate.Key, candidate.Value);
                foundDefinition = true;
            }
        }
        while (foundDefinition);

        return definitions;
    }

    // ============================================================
    // Solver hooks (unchanged externally)
    // ============================================================

    public static void TrySolveEquation(
        ConnectedEquation equation,
        Dictionary<char, double> definitions,
        HashSet<BallNumber> updatedAnswers)
    {
        if (HasAmbiguousOperands(equation)) return;

        BallNumber emptyBall = null;
        foreach (BallNumber ball in equation.balls)
        {
            if (string.IsNullOrEmpty(ball.GetMathContent().Trim()))
            {
                if (ball.IsLocked || ball.IsBeingEdited || emptyBall != null) return;
                emptyBall = ball;
            }
        }
        if (emptyBall == null) return;

        if (TrySolveEqualityChain(equation.expression, definitions, out string result))
        {
            emptyBall.SetSolverOutput(result);
            updatedAnswers.Add(emptyBall);
        }
    }


    // ============================================================
    // Helpers reused from the old implementation
    // ============================================================

    private static bool HasAmbiguousOperands(ConnectedEquation equation)
    {
        return equation.ambiguousBalls != null && equation.ambiguousBalls.Count > 0;
    }

    private static bool TryGetCompleteEquation(ConnectedEquation eq, out string equation)
    {
        equation = string.Empty;
        foreach (BallNumber ball in eq.balls)
        {
            if (string.IsNullOrEmpty(ball.GetMathContent().Trim()) || ball.IsBeingEdited)
                return false;
        }
        equation = eq.expression;
        return TrySplitEqualityChain(equation, out _);
    }

    private static bool TrySolveEqualityChain(
        string equation,
        Dictionary<char, double> definitions,
        out string result)
    {
        result = string.Empty;
        if (!TrySplitEqualityChain(equation, out string[] parts)) return false;

        int unknownPartIndex = -1;
        var knownPartIndexes = new List<int>();
        var knownValues = new List<System.Numerics.Complex>();

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].IndexOf(MathEquationSolver.UnknownSymbol) >= 0)
            {
                if (unknownPartIndex >= 0) return false;
                unknownPartIndex = i;
                continue;
            }

            knownPartIndexes.Add(i);
            if (MathEquationSolver.TryEvaluateNumericExpressionComplex(
                    parts[i], definitions, out System.Numerics.Complex value))
            {
                knownValues.Add(value);
            }
            else if (parts.Length > 2)
            {
                return false;
            }
        }

        if (unknownPartIndex < 0 || knownPartIndexes.Count == 0 ||
            knownValues.Count > 0 && knownValues.Count != knownPartIndexes.Count)
            return false;

        for (int i = 1; i < knownValues.Count; i++)
            if (!ValuesAreEquivalent(knownValues[0], knownValues[i]))
                return false;

        string knownPart = parts[knownPartIndexes[0]];
        string unknownPart = parts[unknownPartIndex];

        if (MathEquationSolver.TrySolve(unknownPart + "=" + knownPart,
                definitions, out result))
            return true;

        return parts.Length == 2 &&
               unknownPart == MathEquationSolver.UnknownSymbol.ToString() &&
               TrySimplifyWithDefinitions(knownPart, definitions, out result);
    }

    private static bool TrySimplifyWithDefinitions(
        string expression, Dictionary<char, double> definitions, out string simplified)
    {
        string substituted = MathEquationSolver.SubstituteVariables(expression, definitions);
        if (MathExpression.TrySimplifyAddition(substituted, out simplified)) return true;
        simplified = substituted;
        return false;
    }

    private static bool TrySplitEqualityChain(string equation, out string[] parts)
    {
        parts = equation.Split('=');
        if (parts.Length < 2) return false;
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = parts[i].Trim();
            if (parts[i].Length == 0) return false;
        }
        return true;
    }

    private static HashSet<char> GetVariables(string equation)
    {
        return MathEquationSolver.GetVariables(equation);
    }

    private static string ReplaceVariableWithUnknown(string equation, char variable)
    {
        return MathEquationSolver.ReplaceVariableWithUnknown(equation, variable);
    }

    private static bool ValuesAreEquivalent(double l, double r)
    {
        double t = 1e-9 * System.Math.Max(1d, System.Math.Max(System.Math.Abs(l), System.Math.Abs(r)));
        return System.Math.Abs(l - r) <= t;
    }

    private static bool ValuesAreEquivalent(System.Numerics.Complex l, System.Numerics.Complex r)
    {
        return (l - r).Magnitude <=
               1e-9 * System.Math.Max(1d, System.Math.Max(l.Magnitude, r.Magnitude));
    }

    private static bool TryGetVariableLetter(string expression, out char variable)
    {
        variable = '\0';
        if (expression.Length != 1 || !MathSyntax.IsVariableLetter(expression[0])) return false;
        variable = expression[0];
        return true;
    }
}