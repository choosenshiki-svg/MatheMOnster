using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class BallImpactLimiter : MonoBehaviour
{
    [Tooltip("Max speed the ball can have immediately after any collision.")]
    [SerializeField] private float maxPostCollisionSpeed = 1.0f;

    private Rigidbody2D body;

    private void Awake() => body = GetComponent<Rigidbody2D>();

    private void OnCollisionEnter2D(Collision2D _)
    {
        ClampNow();
    }

    private void OnCollisionStay2D(Collision2D _)
    {
        ClampNow();
    }

    private void ClampNow()
    {
        if (body == null) return;
        float cap = maxPostCollisionSpeed;
        if (body.linearVelocity.sqrMagnitude > cap * cap)
            body.linearVelocity = body.linearVelocity.normalized * cap;
    }
}