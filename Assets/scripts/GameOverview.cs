// ===== FILE: Assets/scripts/GameOverview.cs =====
//
// =====================================================================
//  MatheMonster — Game Overview
// =====================================================================
//
//  A 2D math-puzzle game where players drag physical "balls" (each
//  showing a number, variable, operator, function, bracket, or equals
//  sign) onto a board and connect them by touching. When a chain of
//  connected balls forms a valid equation, the game evaluates it:
//
//    - A '=' ball splits the cluster into left/right regions.
//    - Each region is parsed into an expression tree using rewrite
//      rules (brackets → adjacency → functions → powers → products
//      → sums).
//    - If both sides evaluate equal, the '=' ball turns GREEN.
//      If they disagree, RED. If there's exactly one blank ball in
//      the equation, the solver fills it with the answer.
//    - Balls with no valid role (e.g. two operators touching,
//      a function with two arguments) turn RED.
//
//  The screen is split into two viewports by a draggable gray divider:
//
//    - TOP viewport  = the shared BOARD (networked in multiplayer).
//    - BOTTOM viewport = the local player's HAND (never networked;
//                        only this client sees their own hand).
//
//  Dragging a ball across the divider transfers it between the board
//  and your hand. In multiplayer the transfer round-trips through the
//  server so all clients stay in sync.
//
//  A "bag" system lets players draw random content from a shared pool
//  (server-authoritative when online). Bag balls are visually ringed
//  in blue, cannot be edited or deleted, and return to the pool when
//  dropped on the Bag button.
//
// ---------------------------------------------------------------------
//  SYSTEMS AT A GLANCE
// ---------------------------------------------------------------------
//
//  ▸ BALLS & CONTENT
//      BallNumber          — stores a ball's value (algebra ints/vars
//                            OR raw display text) and drives display.
//      BallAppearance      — picks a color from the content category
//                            and renders the text label. Also tints
//                            hand pieces and '=' state (blue/green/red).
//      BallInputController — keyboard editing for the ball under the
//                            mouse (numbers, letters, backspace, etc).
//      DirectionalOperator — for '-', '/', '^' balls: renders a crescent
//                            and lets the player choose which side is
//                            the "inside" operand by dragging.
//
//  ▸ MATH ENGINE
//      MathSyntax          — character-level helpers, token validation.
//      MathExpression      — classifies text (number / algebra / mixed /
//                            operator / invalid / other) for coloring.
//      MathSimplifier      — combines like terms in add-only expressions.
//      MathEquationSolver  — parser + evaluator (handles functions,
//                            implicit multiplication, equality chains,
//                            variable substitution, complex numbers).
//      BallSyntaxChecker   — per-ball local error pass (e.g. a '+'
//                            with no neighbors is red before rewrite).
//      MathEquationBuilder — the heart of the puzzle logic:
//                            partitions balls into clusters, runs the
//                            rewrite engine, builds ConnectedEquation
//                            objects, drives solving.
//
//  ▸ CONNECTION & DISPLAY
//      BallConnectionRules — AreConnected(): decides when two balls are
//                            "touching" (bounds overlap or small gap).
//      BagRing / LockRing  — LineRenderer circles for bag / locked balls.
//      Spawner             — owns every ball in the scene; per-frame
//                            partitions board & hand clusters, solves
//                            dirty ones, draws tendril lines between
//                            connected balls.
//      EquationListDisplay — left-side UI panel listing solved equations.
//
//  ▸ DRAG & INPUT
//      BallDrag            — mouse grab, drag, throw, hand↔board
//                            crossing, bag-return-on-drop.
//      BallTransferManager — owns the in-flight state for a ball crossing
//                            the divider; shows a fake ghost and spawns
//                            the real ball on release.
//      LassoSelection      — Shift+drag free-form multi-select and group
//                            move.
//      SelectionInputRouter— keyboard edits applied to a whole selection.
//      DualCameraSetup     — creates BoardCamera and HandCamera and
//                            splits the screen.
//      DualCameraController— per-viewport mouse-wheel zoom and pan.
//      BoardHandDivider    — the draggable gray bar between viewports.
//      TestMenuUI          — right-click+Shift dev menu for spawning
//                            hardcoded test setups (TestSpawner).
//      TestSpawner         — hardcoded puzzle arrangements and their
//                            expected results, used to validate rules.
//
//  ▸ BAG
//      BagDefinition        — ScriptableObject: content + count entries.
//      BagLibrary           — ScriptableObject: named list of defs.
//      BagDefinitionFiller  — Editor menu to fill a def with presets
//                             (Algebra, Numbers, Operators, Scrabble, …).
//      BagManager           — server-authoritative NetworkList pool;
//                             draw / return / add / remove / reset.
//                             Also handles the "who is editing" lock.
//      BagButton            — screen rect for drop detection; greys out
//                             when the pool is empty or locked.
//      BagBallMarker        — tags a local (hand) ball as a bag ball.
//      BagMenuUI            — in-game panel for inspecting/editing the
//                             bag contents (host/client with lock).
//      BagCounterDisplay    — IMGUI panel: on-board / per-hand / in-bag
//                             counts (online only).
//      HandBagCountReporter — each client reports its hand bag count
//                             to the server for BagCounterDisplay.
//
//  ▸ NETWORKING
//      NetworkBall          — NetworkBehaviour wrapper: position sync,
//                              grab lock (LockedBy), zone (Hand/Board),
//                              bag flag, despawn/content RPCs.
//      NetworkBallManager   — spawn helpers, board↔hand transfer,
//                              bag-return from board, host migration.
//      NetworkGameSession   — UGS Relay wrapper: host/join/leave.
//      NetworkLobbyUI       — host/join button panel.
//      PlayerManager        — spawns a PlayerData per client; enforces
//                              unique player names.
//      PlayerData           — NetworkBehaviour with PlayerName and
//                              HandBagCount.
//      PlayerNamePrompt     — modal name-entry dialog.
//      PlayerNameSubmitter  — helper to send the name to the server.
//      LocalHandBall        — marks a non-networked hand-owned ball.
//
// ---------------------------------------------------------------------
//  GAMEPLAY WALKTHROUGH
// ---------------------------------------------------------------------
//
//  1. Right-click empty space in either viewport to spawn a blank ball
//     (or Shift+Right-click to open the dev test menu).
//  2. Hover a ball and type to edit: digits become the coefficient,
//     letters add a variable and raise its power, Backspace clears.
//     In display-key mode any character is accepted.
//  3. Drag balls so they touch. The Spawner solves dirty clusters each
//     frame and draws a white tendril between every connected pair.
//  4. Drag a ball across the gray divider to move it between your hand
//     and the shared board.
//  5. Press the Bag button to draw a random ball from the pool. Drop a
//     bag ball on the Bag button to return it.
//  6. When a construction is valid and balanced, its '=' ball turns
//     green and any blank ball is filled with the answer.
//
// ---------------------------------------------------------------------
//  KEY CLASSES / FLOW (offline)
// ---------------------------------------------------------------------
//
//   Spawner.Update
//     ├─ MarkConnectionsChanged while dragging
//     ├─ PartitionClusters() per world
//     ├─ SolveDirtyClusters() per world
//     │    ├─ MathEquationBuilder.SolveCluster
//     │    │    ├─ BallSyntaxChecker.MarkLocalSyntaxErrors
//     │    │    ├─ BuildGraph → PartitionByEquals → SolveRegion
//     │    │    └─ EvaluateEquals → byte state (0 blue /1 green /2 red)
//     │    └─ TrySolveEquation → fill one blank, ReEvaluateAfterFill
//     └─ RefreshTendrils(connectedPairs, …)
//
// ---------------------------------------------------------------------
//  UNITY SETUP NOTES
// ---------------------------------------------------------------------
//
//  Layers required:  "HandPieces", "BoardPieces"
//  Scene object:     one DualCameraSetup + DualCameraController + Spawner
//                    + NetworkBallManager + TestSpawner + TestMenuUI
//                    + NetworkGameSession (DontDestroyOnLoad)
//  Prefabs:          ballPrefab, directionalOperatorPrefab, playerDataPrefab
//  Assets:           BagLibrary containing one or more BagDefinition assets
//
// =====================================================================