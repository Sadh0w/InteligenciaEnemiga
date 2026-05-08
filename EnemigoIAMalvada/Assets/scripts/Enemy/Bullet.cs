using UnityEngine;

/// <summary>
/// Proyectil unificado para jugador y enemigos.
/// 
/// Rotación del mesh vs dirección de viaje:
///   El prefab puede tener el mesh rotado (ej: cápsula en Y necesita 90° en X
///   para apuntar hacia adelante). Ajusta 'meshRotationOffset' para compensarlo
///   sin tocar la dirección de disparo.
///
/// Velocidad:
///   La bala se mueve por su propia velocidad ('speed'). El que la instancia
///   NO debe añadir fuerza adicional (pon shootForce = 0 en WeaponData y en
///   el Inspector del enemigo).
///
/// Trail Renderer:
///   Configúralo directamente en el prefab como componente.
///   Este script no lo toca, solo asegura que la bala se mueva correctamente.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Bullet : MonoBehaviour
{
    [Header("Movimiento")]
    [SerializeField] float speed = 40f;
    [SerializeField] float lifetime = 2f;

    [Header("Corrección de rotación del mesh")]
    // Si el mesh del prefab está rotado respecto al eje de avance, compensa aquí.
    // Cápsula en Y (por defecto Unity): offset = (90, 0, 0)
    // Esfera u objetos ya alineados en Z: offset = (0, 0, 0)
    [SerializeField] Vector3 meshRotationOffset = new Vector3(90f, 0f, 0f);

    [Header("Capas ignoradas")]
    [SerializeField] LayerMask ignoreLayer;

    Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
    }

    void Start()
    {
        // Aplica la corrección visual del mesh sin afectar la dirección de viaje.
        // transform.forward es la dirección real de disparo (asignada al instanciar).
        // El mesh hijo rota localmente para alinearse con esa dirección.
        if (meshRotationOffset != Vector3.zero)
        {
            // Si hay un hijo (el mesh), lo rotamos a él; si no, rotamos el objeto entero.
            if (transform.childCount > 0)
                transform.GetChild(0).localRotation = Quaternion.Euler(meshRotationOffset);
            // Si no hay hijo separado, la rotación ya viene compensada desde quien instancia.
        }

        // Velocidad en la dirección que apunta el proyectil
        rb.linearVelocity = transform.forward * speed;

        Destroy(gameObject, lifetime);
    }



    void OnCollisionEnter(Collision collision)
    {
        if (ignoreLayer != 0 &&
            ((1 << collision.gameObject.layer) & ignoreLayer) != 0)
            return;

        // Aquí puedes añadir daño, partículas de impacto, etc.
        Destroy(gameObject);
    }
}