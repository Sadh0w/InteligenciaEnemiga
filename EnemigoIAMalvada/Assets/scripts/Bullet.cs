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
        // Si nadie llamó SetDirection, usa transform.forward como fallback
        if (!directionSet)
            Launch(transform.forward);

        Destroy(gameObject, lifetime);
    }

    /// <summary>
    /// Asigna dirección y lanza la bala. Llamar justo después de Instantiate.
    /// Al llamarlo antes de Start(), garantiza que la velocidad es correcta.
    /// </summary>
    public void SetDirection(Vector3 direction)
    {
        travelDirection = direction.normalized;
        directionSet = true;

        // Si Awake ya corrió, aplica la velocidad ahora mismo
        // Si no, Start() lo hará
        if (rb != null)
            Launch(travelDirection);
    }

    void Launch(Vector3 direction)
    {
        rb.linearVelocity = direction.normalized * speed;
    }

    public void IgnoreCollider(Collider col)
    {
        // Busca en el root y en hijos
        Collider own = GetComponentInChildren<Collider>();
        if (own != null && col != null)
            Physics.IgnoreCollision(own, col);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (ignoreLayer != 0 &&
            ((1 << collision.gameObject.layer) & ignoreLayer) != 0)
            return;

        EnemyHealth health = collision.gameObject.GetComponent<EnemyHealth>();
        if (health != null)
            health.TakeDamage(damage);

        Destroy(gameObject);
    }
}