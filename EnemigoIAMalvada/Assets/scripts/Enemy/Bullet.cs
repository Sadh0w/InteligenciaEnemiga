using UnityEngine;


public class Bullet : MonoBehaviour
{
    [Tooltip("Segundos antes de autodestruirse si no impacta nada.")]
    [SerializeField] float lifetime = 4f;

    // Layer del enemigo: la bala ignorará colisiones con objetos de esta layer
    // para evitar que se destruya nada más salir del shootPoint.
    [SerializeField] LayerMask ignoreLayer;

    void Start()
    {
        Destroy(gameObject, lifetime);
    }

    void OnCollisionEnter(Collision collision)
    {
        // Ignora colisiones con objetos en ignoreLayer (ej: la layer del enemigo)
        if (ignoreLayer != 0 && ((1 << collision.gameObject.layer) & ignoreLayer) != 0)
            return;

        // Aquí puedes añadir efectos de impacto, daño, partículas, etc.
        Destroy(gameObject);
    }
}