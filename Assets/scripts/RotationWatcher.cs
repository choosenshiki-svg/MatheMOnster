using UnityEngine;

public class RotationWatcher : MonoBehaviour
{
    private float _lastZ;
    private string _lastSource = "init";

    public void NoteSource(string source) => _lastSource = source;

    private void Start() => _lastZ = transform.eulerAngles.z;

    private void LateUpdate()
    {
        float z = transform.eulerAngles.z;
        float delta = Mathf.DeltaAngle(_lastZ, z);
        if (Mathf.Abs(delta) > 0.5f)
        {
            Debug.Log($"[WATCH] {name} z {_lastZ:F1} -> {z:F1} (D{delta:F1}) lastSource={_lastSource}");
        }
        _lastZ = z;
    }
}