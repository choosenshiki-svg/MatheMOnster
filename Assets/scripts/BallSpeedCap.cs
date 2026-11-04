using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class BallSpeedCap : MonoBehaviour
{
    [SerializeField] private float maxSpeed = 2f;

    private Rigidbody2D body;

    private void Awake() => body = GetComponent<Rigidbody2D>();

    private void FixedUpdate()
    {
        if (body == null) return;
        if (body.linearVelocity.sqrMagnitude > maxSpeed * maxSpeed)
            body.linearVelocity = body.linearVelocity.normalized * maxSpeed;
    }
}