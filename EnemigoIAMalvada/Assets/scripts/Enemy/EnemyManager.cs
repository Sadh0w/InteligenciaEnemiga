using System.Collections.Generic;
using UnityEngine;


[System.Serializable]
public class EnemyGroupConfig
{
    public int groupID = 0;
    public bool canCommunicate = true;

    [Tooltip("Nombre descriptivo solo para identificarlo en el Inspector.")]
    public string groupName = "Grupo";
}

public class EnemyManager : MonoBehaviour
{
    public static EnemyManager Instance { get; private set; }

    // ── Registro de enemigos ─────────────────────────────────────────────────
    readonly List<EnemyAIBase> enemies = new();

    // ── Configuración de grupos (editable en Inspector) ──────────────────────
    [Header("Configuración de Grupos")]
    [Tooltip("Define aquí cada grupo. El groupID debe coincidir con el de cada enemigo.")]
    [SerializeField] List<EnemyGroupConfig> groups = new();

    // Lookup rápido groupID → config (se construye en Awake)
    readonly Dictionary<int, EnemyGroupConfig> groupLookup = new();

    #region Lifecycle

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        RebuildLookup();
    }

    
    public void RebuildLookup()
    {
        groupLookup.Clear();
        foreach (var cfg in groups)
        {
            if (!groupLookup.ContainsKey(cfg.groupID))
                groupLookup[cfg.groupID] = cfg;
            else
                Debug.LogWarning($"EnemyManager: GroupID {cfg.groupID} duplicado en la lista de grupos.");
        }
    }

    #endregion

    #region Registry

    public void Register(EnemyAIBase enemy)
    {
        if (!enemies.Contains(enemy))
            enemies.Add(enemy);
    }

    public void Unregister(EnemyAIBase enemy) => enemies.Remove(enemy);

    #endregion

    #region Group API

    
    public void SetGroup(EnemyAIBase enemy, int newGroupID)
    {
        enemy.GroupID = newGroupID;

        if (!groupLookup.ContainsKey(newGroupID))
        {
            var cfg = new EnemyGroupConfig
            {
                groupID = newGroupID,
                canCommunicate = true,
                groupName = $"Grupo {newGroupID}"
            };
            groups.Add(cfg);
            groupLookup[newGroupID] = cfg;
        }
    }

   
    public void SetGroupCommunication(int groupID, bool canCommunicate)
    {
        if (groupLookup.TryGetValue(groupID, out EnemyGroupConfig cfg))
        {
            cfg.canCommunicate = canCommunicate;
        }
        else
        {
            Debug.LogWarning($"EnemyManager: GroupID {groupID} no encontrado. Créalo primero en la lista de grupos.");
        }
    }

    
    public bool GroupCanCommunicate(int groupID)
    {
        if (groupLookup.TryGetValue(groupID, out EnemyGroupConfig cfg))
            return cfg.canCommunicate;

        return true;
    }

    #endregion

    #region Alert System

   
    public void AlertNearby(EnemyAIBase sender, Vector3 position, float alertRadius, bool combatAlert)
    {
        int senderGroup = sender.GroupID;

        // Si el grupo del emisor no puede comunicarse, no hace nada
        if (!GroupCanCommunicate(senderGroup)) return;

        foreach (var enemy in enemies)
        {
            if (enemy == sender) continue;
            if (enemy.GroupID != senderGroup) continue; // solo mismo grupo
            if (enemy.IsCombatActive()) continue; // ya sabe del jugador

            float dist = Vector3.Distance(sender.transform.position, enemy.transform.position);
            if (dist > alertRadius) continue;

            if (combatAlert)
                enemy.ReceivePlayerSpotted(); // Chase directo
            else
                enemy.ReceiveAlert(position); // Investigate
        }
    }

    #endregion
}