using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Bullet : MonoBehaviour
{
    [Header("Movimiento")]
    [SerializeField] float speed = 40f;
    [SerializeField] float lifetime = 2f;

    [Header("Daño")]
    [SerializeField] float damage = 25f;

    [Header("Capas ignoradas")]
    [SerializeField] LayerMask ignoreLayer;

    Rigidbody rb;
    Vector3 travelDirection;
    bool directionSet;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
    }

    void Start()
    {
        if (!directionSet)
            Launch(transform.forward);

        Destroy(gameObject, lifetime);
    }

    public void SetDirection(Vector3 direction)
    {
        travelDirection = direction.normalized;
        directionSet = true;

        if (rb != null)
            Launch(travelDirection);
    }

    void Launch(Vector3 direction)
    {
        rb.linearVelocity = direction.normalized * speed;
    }

    public void IgnoreCollider(Collider col)
    {
        Collider own = GetComponentInChildren<Collider>();
        if (own != null && col != null)
            Physics.IgnoreCollision(own, col);
    }

    void OnCollisionEnter(Collision collision)
    {
        // Ignora layers configuradas (quien disparó)
        if (ignoreLayer != 0 &&
            ((1 << collision.gameObject.layer) & ignoreLayer) != 0)
            return;

        // ¿Es un enemigo?
        EnemyHealth enemyHealth = collision.gameObject.GetComponentInParent<EnemyHealth>();
        if (enemyHealth != null)
        {
            enemyHealth.TakeDamage(damage);
            Destroy(gameObject);
            return;
        }

        // ¿Es el jugador?
        PlayerHealth playerHealth = collision.gameObject.GetComponentInParent<PlayerHealth>();
        if (playerHealth != null)
        {
            playerHealth.TakeDamage();
            Destroy(gameObject);
            return;
        }

        // Cualquier otra cosa (pared, suelo...)
        Destroy(gameObject);
    }
}