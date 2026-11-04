using UnityEngine;

/// <summary>
/// Marks a GameObject as a local (non-networked) hand piece. Hand pieces are
/// owned entirely by the local client, live at world Y ≈ 1000, and use the
/// HandCamera for input. They have no NetworkObject and no NetworkBall.
/// </summary>
public class LocalHandBall : MonoBehaviour
{
    /// <summary>The client that owns this hand piece (offline = 0).</summary>
    public ulong OwnerClientId { get; private set; }

    public void Initialize(ulong ownerId)
    {
        OwnerClientId = ownerId;
    }
}