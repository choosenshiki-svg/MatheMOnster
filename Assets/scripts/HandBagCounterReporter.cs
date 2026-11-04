using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Each client counts its own hand bag balls each frame and reports changes
/// to the server. Server stores the count in PlayerData.HandBagCount, which
/// all clients read for the counter display.
/// </summary>
public class HandBagCountReporter : MonoBehaviour
{
    private int _lastReported = -1;
    private float _nextReportTime;
    private const float ReportInterval = 0.2f;

    private void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsClient) return;

        if (Time.unscaledTime < _nextReportTime) return;
        _nextReportTime = Time.unscaledTime + ReportInterval;

        var spawner = FindFirstObjectByType<Spawner>();
        if (spawner == null) return;

        int count = spawner.GetHandBagBallCount();
        if (count == _lastReported) return;

        if (PlayerManager.Instance != null)
            PlayerManager.Instance.ReportHandBagCountServerRpc(count);

        _lastReported = count;
    }
}