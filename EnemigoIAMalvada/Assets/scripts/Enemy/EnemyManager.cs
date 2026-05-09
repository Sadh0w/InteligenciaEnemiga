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

    readonly List<EnemyAIBase> enemies = new();

    [Header("Configuración de Grupos")]
    [SerializeField] List<EnemyGroupConfig> groups = new();

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
                Debug.LogWarning($"EnemyManager: GroupID {cfg.groupID} duplicado.");
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
            cfg.canCommunicate = canCommunicate;
        else
            Debug.LogWarning($"EnemyManager: GroupID {groupID} no encontrado.");
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
        if (!GroupCanCommunicate(senderGroup)) return;

        foreach (var enemy in enemies)
        {
            if (enemy == sender) continue;
            if (enemy.GroupID != senderGroup) continue;
            if (enemy.IsCombatActive()) continue;

            float dist = Vector3.Distance(sender.transform.position, enemy.transform.position);
            if (dist > alertRadius) continue;

            if (combatAlert) enemy.ReceivePlayerSpotted();
            else enemy.ReceiveAlert(position);
        }
    }

    #endregion

    #region Flee Support

    /// <summary>
    /// Busca el aliado activo más cercano al enemigo en huida.
    /// Prioriza el mismo grupo; si no hay, busca en cualquier grupo.
    /// Devuelve null si no hay ninguno disponible.
    /// </summary>
    public EnemyAIBase GetNearestAlly(EnemyAIBase fleeing)
    {
        EnemyAIBase bestSameGroup = null;
        EnemyAIBase bestAnyGroup = null;
        float distSame = float.MaxValue;
        float distAny = float.MaxValue;

        foreach (var enemy in enemies)
        {
            if (enemy == fleeing) continue;
            // No contar aliados que también estén huyendo
            if (enemy.CurrentState == EnemyState.Flee) continue;

            float d = Vector3.Distance(fleeing.transform.position, enemy.transform.position);

            if (enemy.GroupID == fleeing.GroupID)
            {
                if (d < distSame) { distSame = d; bestSameGroup = enemy; }
            }
            else
            {
                if (d < distAny) { distAny = d; bestAnyGroup = enemy; }
            }
        }

        // Prioridad: mismo grupo → cualquier grupo → null
        return bestSameGroup ?? bestAnyGroup;
    }

    /// <summary>
    /// Devuelve cuántos aliados activos tiene el enemigo en su mismo grupo (sin contarse a sí mismo).
    /// </summary>
    public int GetActiveAlliesInGroup(EnemyAIBase enemy)
    {
        int count = 0;
        foreach (var e in enemies)
        {
            if (e == enemy) continue;
            if (e.GroupID == enemy.GroupID) count++;
        }
        return count;
    }

    #endregion
}