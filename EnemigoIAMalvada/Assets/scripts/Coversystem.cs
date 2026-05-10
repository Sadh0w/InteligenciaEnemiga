using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton que escanea la escena buscando objetos con obstacleLayer
/// y genera puntos de cobertura candidatos alrededor de cada uno.
///
/// Setup: crea un GameObject vacío llamado "CoverSystem" y asígnale este script.
/// Asigna la misma layer que visionBlockerLayer en los enemigos.
/// </summary>
public class CoverSystem : MonoBehaviour
{
    public static CoverSystem Instance { get; private set; }

    [Header("Detección de obstáculos")]
    [Tooltip("Misma layer que visionBlockerLayer en EnemyAIBase.")]
    [SerializeField] LayerMask obstacleLayer;

    [Header("Generación de puntos")]
    [SerializeField] float pointOffset = 1.5f;  // distancia del punto al borde
    [SerializeField] float validationHeight = 1.0f;  // altura del raycast de validación

    [Header("Debug")]
    [SerializeField] bool showGizmos = true;
    [SerializeField] Color gizmoColor = new Color(0f, 1f, 0.5f, 0.6f);
    [SerializeField] float gizmoSphereSize = 0.25f;

    readonly List<Vector3> coverPoints = new();

    // Expuesto para que los gizmos funcionen en Edit mode también
    public IReadOnlyList<Vector3> CoverPoints => coverPoints;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        ScanScene();
    }

    // Escanea también en Edit mode para ver los puntos sin Play
    void OnValidate() => ScanScene();

    /// <summary>
    /// Escanea todos los colliders con obstacleLayer y genera puntos alrededor.
    /// </summary>
    public void ScanScene()
    {
        coverPoints.Clear();

        if (obstacleLayer == 0)
        {
            Debug.LogWarning("[CoverSystem] Obstacle Layer no asignada.");
            return;
        }

        Collider[] obstacles = Physics.OverlapSphere(Vector3.zero, 500f, obstacleLayer);

        foreach (var col in obstacles)
        {
            Bounds b = col.bounds;

            // 4 puntos cardinales
            TryAddPoint(new Vector3(b.center.x + b.extents.x + pointOffset, b.min.y + 0.1f, b.center.z));
            TryAddPoint(new Vector3(b.center.x - b.extents.x - pointOffset, b.min.y + 0.1f, b.center.z));
            TryAddPoint(new Vector3(b.center.x, b.min.y + 0.1f, b.center.z + b.extents.z + pointOffset));
            TryAddPoint(new Vector3(b.center.x, b.min.y + 0.1f, b.center.z - b.extents.z - pointOffset));
        }

        Debug.Log($"[CoverSystem] {coverPoints.Count} puntos de cobertura generados desde {obstacles.Length} obstáculos.");
    }

    void TryAddPoint(Vector3 point)
    {
        // Solo añade el punto si está sobre el NavMesh
        if (UnityEngine.AI.NavMesh.SamplePosition(point, out UnityEngine.AI.NavMeshHit hit, 1f, UnityEngine.AI.NavMesh.AllAreas))
            coverPoints.Add(hit.position);
    }

    /// <summary>
    /// Devuelve el mejor punto de cobertura para un enemigo.
    /// Un punto es válido si un obstáculo bloquea la línea de visión hacia el jugador.
    /// </summary>
    public Vector3 GetBestCoverPoint(Vector3 enemyPos, Vector3 playerPos, LayerMask blockerLayer)
    {
        Vector3 best = Vector3.positiveInfinity;
        float bestDist = float.MaxValue;

        foreach (var point in coverPoints)
        {
            if (!IsHiddenFromPlayer(point, playerPos, blockerLayer)) continue;

            float dist = Vector3.Distance(enemyPos, point);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = point;
            }
        }

        if (best == Vector3.positiveInfinity)
            Debug.Log("[CoverSystem] No se encontró ningún punto de cobertura válido.");

        return best;
    }

    bool IsHiddenFromPlayer(Vector3 point, Vector3 playerPos, LayerMask blockerLayer)
    {
        Vector3 checkPos = point + Vector3.up * validationHeight;
        Vector3 playerEye = playerPos + Vector3.up * validationHeight;
        Vector3 dir = playerEye - checkPos;

        return Physics.Raycast(checkPos, dir.normalized, dir.magnitude, blockerLayer);
    }

    void OnDrawGizmos()
    {
        if (!showGizmos) return;

        // Escanea en Edit mode para mostrar puntos sin necesidad de Play
        if (!Application.isPlaying)
            ScanScene();

        Gizmos.color = gizmoColor;
        foreach (var p in coverPoints)
        {
            Gizmos.DrawSphere(p, gizmoSphereSize);
            // Dibuja una línea vertical para verlos mejor desde arriba (vista cenital)
            Gizmos.DrawLine(p, p + Vector3.up * 1.5f);
        }
    }
}