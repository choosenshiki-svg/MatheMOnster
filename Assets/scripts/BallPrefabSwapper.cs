using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Performs the despawn+spawn dance when a ball's content changes kind
/// (e.g. '2' → '-'). Called from BallNumber once per frame when a swap
/// is queued.
///
/// Handles three contexts:
///   - Offline (no NetworkManager): local Destroy + Spawner.Spawn*
///   - Server / host with networked board ball: Despawn + SpawnBoard*
///   - Client with networked board ball: RPC to server, which does the above
///
/// Hand pieces are never networked, so they always go through the offline path.
/// </summary>
public class BallPrefabSwapper : MonoBehaviour
{
    public static BallPrefabSwapper Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Swap(BallNumber oldBall)
    {
        if (oldBall == null) return;

        string content = oldBall.GetMathContent().Trim();
        bool shouldBeDirectional  = DirectionalOperator.IsDirectionalOperatorText(content);
        bool currentlyDirectional = oldBall.GetComponent<DirectionalOperator>() != null;
        if (shouldBeDirectional == currentlyDirectional) return;

        var go = oldBall.gameObject;
        var spawner = FindFirstObjectByType<Spawner>();
        if (spawner == null) return;

        // ---- capture state from the old ball ----
        Vector3 pos      = go.transform.position;
        float   rotZ     = go.transform.eulerAngles.z;
        string  key      = oldBall.GetRawDisplayedKey();
        int     number   = oldBall.NumberValue;
        char    variable = oldBall.Variable;
        int     power    = oldBall.VariablePower;
        byte    mode     = (byte)oldBall.InputMode;
        bool    solverO  = oldBall.IsSolverOutput;
        bool    locked   = oldBall.IsLocked;

        var nb = go.GetComponent<NetworkBall>();
        bool isNetworked = nb != null && nb.IsSpawned;
        bool isHand      = go.layer == LayerMask.NameToLayer("HandPieces");
        bool isBag       = go.GetComponent<BagBallMarker>() != null
                           || (nb != null && nb.IsBagBall.Value);

        var nm = NetworkManager.Singleton;
        bool online   = nm != null && nm.IsListening;
        bool isServer = nm != null && nm.IsServer;

        // ==========================================================
        // Case 1: networked board ball
        // ==========================================================
        if (isNetworked && !isHand)
        {
            if (online && !isServer)
            {
                // Client: ask server to swap.
                if (NetworkBallManager.Instance != null)
                {
                    NetworkBallManager.Instance.RequestSwapBallServerRpc(
                        nb.NetworkObject, shouldBeDirectional,
                        key, number, variable, power, mode, solverO, locked, isBag, rotZ);
                }
                return;
            }

            // Server / host: do it directly.
            nb.NetworkObject.Despawn(true);

            if (shouldBeDirectional)
                NetworkBallManager.Instance.SpawnBoardOperator(pos, key, rotZ);
            else
                NetworkBallManager.Instance.SpawnBoardBall(pos, key, rotZ);

            RestoreContentOnNewestBoard(
                shouldBeDirectional, key, number, variable, power,
                mode, solverO, locked, isBag);
            return;
        }

        // ==========================================================
        // Case 2: hand piece (local, never networked)
        // ==========================================================
        if (isHand)
        {
            spawner.DestroyHandBall(go);

            GameObject newBall = shouldBeDirectional
                ? spawner.SpawnLocalHandOperator(pos, key, rotZ)
                : spawner.SpawnLocalHandBallReturning(pos, key, number, variable, power, rotZ);

            if (newBall != null)
            {
                var bn = newBall.GetComponent<BallNumber>();
                if (bn != null)
                {
                    bn.SetInputModeRaw((BallInputMode)mode);
                    bn.SetRawDisplayedKey(key, solverO);
                    bn.SetLockedRaw(locked);
                    bn.RefreshDisplay();
                }
                if (isBag && newBall.GetComponent<BagBallMarker>() == null)
                {
                    newBall.AddComponent<BagBallMarker>();
                    if (newBall.GetComponent<BagRing>() == null)
                        newBall.AddComponent<BagRing>();
                }
            }
            return;
        }

        // ==========================================================
        // Case 3: offline board ball (no NetworkBall, not on hand layer)
        // ==========================================================
        spawner.DestroyBoardBall(go);

        GameObject newLocal = shouldBeDirectional
            ? spawner.SpawnLocalBoardOperator(pos, key, rotZ)
            : spawner.SpawnBallAtPublicReturning(pos, key, rotZ);

        if (newLocal != null)
        {
            var bn = newLocal.GetComponent<BallNumber>();
            if (bn != null)
            {
                bn.SetInputModeRaw((BallInputMode)mode);
                bn.SetRawDisplayedKey(key, solverO);
                bn.SetLockedRaw(locked);
                bn.RefreshDisplay();
            }
            if (isBag && newLocal.GetComponent<BagBallMarker>() == null)
            {
                newLocal.AddComponent<BagBallMarker>();
                if (newLocal.GetComponent<BagRing>() == null)
                    newLocal.AddComponent<BagRing>();
            }
        }
    }

    /// <summary>
    /// After a server-side networked swap, find the most recently spawned
    /// board ball (the one we just spawned) and restore its content, bag
    /// flag, and lock. Board balls are appended to the Spawner's
    /// boardBalls list, so "last in the list" is the one we want.
    /// </summary>
    private void RestoreContentOnNewestBoard(
        bool wasDirectional, string key, int number, char variable, int power,
        byte mode, bool solverO, bool locked, bool isBag)
    {
        var spawner = FindFirstObjectByType<Spawner>();
        if (spawner == null) return;

        var all = spawner.GetAllBoardPieces();
        if (all == null || all.Count == 0) return;

        var newest = all[all.Count - 1];
        if (newest == null) return;

        var bn = newest.GetComponent<BallNumber>();
        if (bn != null)
        {
            bn.SetInputModeRaw((BallInputMode)mode);
            bn.SetRawDisplayedKey(key, solverO);
            bn.SetLockedRaw(locked);
            bn.RefreshDisplay();
        }

        var nb = newest.GetComponent<NetworkBall>();
        if (nb != null)
        {
            if (isBag) nb.IsBagBall.Value = true;
            nb.PushImmediate();
        }
    }
}