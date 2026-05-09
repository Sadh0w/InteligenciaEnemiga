using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton que escanea la escena buscando objetos con la layer 'visionBlockerLayer'
/// y genera puntos de cobertura candidatos alrededor de cada uno.
/// Los enemigos lo consultan para encontrar el mejor cover disponible.
///
/// Setup: crea un GameObject vacío en la escena, llámalo "CoverSystem" y asígnale este script.
/// Asigna en el Inspector la misma layer que usas como visionBlockerLayer en los enemigos.
/// </summary>
public class CoverSystem : MonoBehaviour
{
    public static CoverSystem Instance { get; private set; }

    [Header("Detección de obstáculos")]
    [Tooltip("Misma layer que visionBlockerLayer en EnemyAIBase.")]
    [SerializeField] LayerMask obstacleLayer;

    [Header("Generación de puntos")]
    [SerializeField] float pointOffset = 1.5f;  // distancia del punto al borde del obstáculo
    [SerializeField] float validationHeight = 1.0f;  // altura del raycast de validación

    // Cache de puntos generados
    readonly List<Vector3> coverPoints = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        ScanScene();
    }

    /// <summary>
    /// Escanea todos los objetos con obstacleLayer y genera 4 puntos alrededor de cada uno.
    /// Llama a esto si añades obstáculos en runtime.
    /// </summary>
    public void ScanScene()
    {
        coverPoints.Clear();

        // Encuentra todos los colliders en la layer de obstáculos
        Collider[] obstacles = Physics.OverlapSphere(Vector3.zero, 500f, obstacleLayer);

        foreach (var col in obstacles)
        {
            Bounds b = col.bounds;

            // 4 puntos cardinales alrededor del obstáculo
            coverPoints.Add(new Vector3(b.center.x + b.extents.x + pointOffset, b.center.y, b.center.z));
            coverPoints.Add(new Vector3(b.center.x - b.extents.x - pointOffset, b.center.y, b.center.z));
            coverPoints.Add(new Vector3(b.center.x, b.center.y, b.center.z + b.extents.z + pointOffset));
            coverPoints.Add(new Vector3(b.center.x, b.center.y, b.center.z - b.extents.z - pointOffset));
        }
    }

    /// <summary>
    /// Devuelve el mejor punto de cobertura para un enemigo dado su posición y la del jugador.
    /// Un punto es válido si:
    ///   1. Un obstáculo bloquea la línea de visión entre ese punto y el jugador.
    ///   2. El punto es alcanzable (está en el NavMesh).
    ///   3. Es el más cercano al enemigo entre los válidos.
    /// Devuelve Vector3.positiveInfinity si no hay ninguno válido.
    /// </summary>
    public Vector3 GetBestCoverPoint(Vector3 enemyPos, Vector3 playerPos, LayerMask blockerLayer)
    {
        Vector3 best = Vector3.positiveInfinity;
        float bestDist = float.MaxValue;

        foreach (var point in coverPoints)
        {
            // ¿El punto está oculto del jugador?
            if (!IsHiddenFromPlayer(point, playerPos, blockerLayer)) continue;

            // ¿Es más cercano que el mejor hasta ahora?
            float dist = Vector3.Distance(enemyPos, point);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = point;
            }
        }

        return best;
    }

    /// <summary>
    /// Comprueba si un punto está oculto del jugador por un obstáculo.
    /// </summary>
    bool IsHiddenFromPlayer(Vector3 point, Vector3 playerPos, LayerMask blockerLayer)
    {
        Vector3 checkPos = point + Vector3.up * validationHeight;
        Vector3 playerEye = playerPos + Vector3.up * validationHeight;
        Vector3 dir = playerEye - checkPos;

        // Si el primer objeto golpeado es un bloqueador → el punto está oculto
        if (Physics.Raycast(checkPos, dir.normalized, out RaycastHit hit, dir.magnitude, blockerLayer))
            return true;

        return false;
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = new Color(0f, 1f, 0.5f, 0.4f);
        foreach (var p in coverPoints)
            Gizmos.DrawSphere(p, 0.2f);
    }
}