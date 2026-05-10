using UnityEngine;

/// <summary>
/// Suelta un prefab de loot en la posición exacta donde murió el enemigo.
/// 
/// Setup:
///   1. Añade este script al mismo GameObject que EnemyHealth.
///   2. Asigna el prefab del loot en 'lootPrefab'.
///   3. En EnemyHealth → On Death () → pulsa + → arrastra este GameObject
///      → selecciona EnemyLootDrop → DropLoot.
/// </summary>
public class EnemyLootDrop : MonoBehaviour
{
    [Header("Loot")]
    [SerializeField] GameObject lootPrefab;

    [Tooltip("Offset respecto a la posición del enemigo. (0,0,0) = exactamente donde murió.")]
    [SerializeField] Vector3 spawnOffset = Vector3.zero;

    /// <summary>
    /// Llamado desde el evento On Death () de EnemyHealth.
    /// </summary>
    public void DropLoot()
    {
        if (lootPrefab == null)
        {
            Debug.LogWarning($"[LootDrop] {gameObject.name}: no hay lootPrefab asignado.");
            return;
        }

        Vector3 spawnPos = transform.position + spawnOffset;
        Instantiate(lootPrefab, spawnPos, Quaternion.identity);
    }
}