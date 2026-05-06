using UnityEngine;

/// <summary>
/// Cámara cenital que sigue al jugador desde arriba.
/// Coloca este script en el GameObject de la cámara.
/// La cámara apunta recto hacia abajo (90° en X).
/// </summary>
public class TopDownCamera : MonoBehaviour
{
    #region Variables

    [Header("Referencias")]
    [SerializeField] Transform target; // el jugador

    [Header("Posición")]
    [SerializeField] float height = 15f;        // altura sobre el jugador
    [SerializeField] Vector2 offset = Vector2.zero; // desplazamiento lateral (X/Z)

    [Header("Suavizado")]
    [SerializeField] float smoothSpeed = 10f;   // qué tan rápido sigue al jugador

    #endregion

    void Awake()
    {
        // Busca al jugador automáticamente si no está asignado
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
            else Debug.LogError("TopDownCamera: no se encontró ningún Player.");
        }

        // Fuerza la rotación cenital
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    // LateUpdate para que la cámara se mueva después de que el jugador ya se haya movido
    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desired = new Vector3(
            target.position.x + offset.x,
            target.position.y + height,
            target.position.z + offset.y
        );

        transform.position = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);
    }
}