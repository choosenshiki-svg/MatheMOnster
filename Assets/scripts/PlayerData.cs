using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerData : NetworkBehaviour
{
    public readonly NetworkVariable<FixedString32Bytes> PlayerName = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public readonly NetworkVariable<int> HandBagCount = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public bool HasSetName => PlayerName.Value.Length > 0;
}