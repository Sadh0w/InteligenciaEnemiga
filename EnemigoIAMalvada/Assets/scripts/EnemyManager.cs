using System.Collections.Generic;
using UnityEngine;

public class EnemyManager : MonoBehaviour
{
    public static EnemyManager Instance { get; private set; }

    // Todos los enemigos registrados
    readonly List<EnemyAIBase> enemies = new();

    // groupID → ¿puede comunicarse ese grupo internamente?
    readonly Dictionary<int, bool> groupCommunication = new();

    #region Lifecycle

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    #endregion

    #region Registry

    public void Register(EnemyAIBase enemy) => enemies.Add(enemy);
    public void Unregister(EnemyAIBase enemy) => enemies.Remove(enemy);

    #endregion

    #region Group API

    /// <summary>
    /// Asigna un enemigo a un grupo. Los grupos se crean automáticamente
    /// la primera vez que se usan. Por defecto la comunicación está activa.
    /// </summary>
    public void SetGroup(EnemyAIBase enemy, int groupID)
    {
        enemy.GroupID = groupID;

        if (!groupCommunication.ContainsKey(groupID))
            groupCommunication[groupID] = true; // comunicación activa por defecto
    }

    /// <summary>
    /// Activa o desactiva la comunicación entre los miembros de un grupo.
    /// false = los enemigos de ese grupo actúan solos, sin avisarse.
    /// </summary>
    public void SetGroupCommunication(int groupID, bool canCommunicate)
    {
        groupCommunication[groupID] = canCommunicate;
    }

    /// <summary>
    /// Devuelve si un grupo tiene comunicación activa.
    /// </summary>
    public bool GroupCanCommunicate(int groupID)
    {
        if (groupCommunication.TryGetValue(groupID, out bool value))
            return value;

        return true; // si el grupo no existe aún, por defecto puede comunicarse
    }

    #endregion

    #region Alert System

    /// <summary>
    /// El enemigo 'sender' avisa a los cercanos del mismo grupo.
    /// 'combatAlert' = true significa que el jugador fue visto en combate → los receptores van a Chase.
    /// 'combatAlert' = false → van a Investigate (posición de sonido o LKP).
    /// </summary>
    public void AlertNearby(EnemyAIBase sender, Vector3 position, float alertRadius, bool combatAlert)
    {
        int senderGroup = sender.GroupID;

        // Si el grupo del emisor no puede comunicarse, no hace nada
        if (!GroupCanCommunicate(senderGroup)) return;

        foreach (var enemy in enemies)
        {
            if (enemy == sender) continue;
            if (enemy.GroupID != senderGroup) continue; // solo mismo grupo
            if (enemy.IsCombatActive()) continue;       // ya sabe del jugador

            float dist = Vector3.Distance(sender.transform.position, enemy.transform.position);
            if (dist > alertRadius) continue;

            if (combatAlert)
                enemy.ReceivePlayerSpotted();  // el jugador está siendo visto ahora → Chase directo
            else
                enemy.ReceiveAlert(position);  // posición de interés → Investigate
        }
    }

    #endregion
}