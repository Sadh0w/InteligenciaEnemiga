using System.Collections.Generic;
using UnityEngine;

public class EnemyManager : MonoBehaviour
{
    public static EnemyManager Instance { get; private set; }

    readonly List<EnemyAIBase> enemies = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Register(EnemyAIBase enemy) => enemies.Add(enemy);
    public void Unregister(EnemyAIBase enemy) => enemies.Remove(enemy);

    /// <summary>
    /// El enemigo 'sender' avisa de que vio al jugador en 'position'.
    /// Solo alertan los enemigos dentro de 'alertRadius' que estén en Patrol o Investigate.
    /// </summary>
    public void AlertNearby(EnemyAIBase sender, Vector3 position, float alertRadius)
    {
        foreach (var enemy in enemies)
        {
            if (enemy == sender) continue;
            if (enemy.IsCombatActive()) continue; // ya sabe del jugador, no hace falta

            float dist = Vector3.Distance(sender.transform.position, enemy.transform.position);
            if (dist <= alertRadius)
                enemy.ReceiveAlert(position);
        }
    }
}