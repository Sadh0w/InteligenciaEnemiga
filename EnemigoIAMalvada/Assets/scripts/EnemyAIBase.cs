using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum PatrolMode
{
    Random,
    Waypoints
}

public class EnemyAIBase : MonoBehaviour
{
    #region Variables

    [Header("AI Configuration")]
    [SerializeField] NavMeshAgent agent;
    [SerializeField] Transform target;
    [SerializeField] LayerMask targetLayer;
    [SerializeField] LayerMask obstacleLayer;

    [Header("Vision Settings")]
    [SerializeField] float sightRange = 15f;
    [SerializeField] float attackRange = 8f;
    [SerializeField] float eyeHeight = 1.6f;

    [Header("Patrol")]
    [SerializeField] PatrolMode patrolMode = PatrolMode.Random;
    [SerializeField] float walkPointRange = 10f;
    [SerializeField] List<Transform> waypoints;

    int currentWaypointIndex;
    Vector3 walkPoint;
    bool walkPointSet;

    [Header("Attack")]
    [SerializeField] GameObject projectile;
    [SerializeField] Transform shootPoint;
    [SerializeField] float shootForce = 20f;
    [SerializeField] float timeBetweenAttacks = 2f;
    [SerializeField] int bulletsPerBurst = 3;
    [SerializeField] float timeBetweenShots = 0.15f;

    bool alreadyAttacked;

    [Header("Optimization")]
    [SerializeField] float aiUpdateFrequency = 0.2f;

    #endregion

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        if (target == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
                target = playerObj.transform;
            else
            {
                Debug.LogError("No Player found");
                enabled = false;
            }
        }
    }

    private void Start()
    {
        StartCoroutine(AIUpdateRoutine());
    }

    IEnumerator AIUpdateRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(aiUpdateFrequency);

            UpdateDetection();

            if (targetInSight && targetInAttack)
                AttackTarget();
            else if (targetInSight)
                ChaseTarget();
            else
                Patrolling();
        }
    }

    #region Detection

    bool targetInSight;
    bool targetInAttack;

    void UpdateDetection()
    {
        float distance = Vector3.Distance(
            transform.position + Vector3.up * eyeHeight,
            target.position + Vector3.up * eyeHeight
        );

        targetInSight = distance <= sightRange;
        targetInAttack = distance <= attackRange && HasLineOfSight();
    }

    bool HasLineOfSight()
    {
        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 dir = (target.position - origin).normalized;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, sightRange, ~obstacleLayer))
        {
            return hit.transform.CompareTag("Player");
        }

        return false;
    }

    #endregion

    #region Patrol

    void Patrolling()
    {
        if (agent.isStopped) agent.isStopped = false;

        if (!walkPointSet || agent.remainingDistance <= agent.stoppingDistance)
        {
            walkPointSet = false;

            if (patrolMode == PatrolMode.Random)
                SearchWalkPoint_Random();
            else
                SearchWalkPoint_Waypoints();
        }
    }

    void SearchWalkPoint_Random()
    {
        float randomX = Random.Range(-walkPointRange, walkPointRange);
        float randomZ = Random.Range(-walkPointRange, walkPointRange);

        Vector3 randomPoint = new Vector3(
            transform.position.x + randomX,
            transform.position.y,
            transform.position.z + randomZ
        );

        if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, walkPointRange, NavMesh.AllAreas))
        {
            walkPoint = hit.position;
            agent.SetDestination(walkPoint);
            walkPointSet = true;
        }
    }

    void SearchWalkPoint_Waypoints()
    {
        if (waypoints == null || waypoints.Count == 0)
        {
            patrolMode = PatrolMode.Random;
            return;
        }

        walkPoint = waypoints[currentWaypointIndex].position;
        agent.SetDestination(walkPoint);
        walkPointSet = true;

        currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Count;
    }

    #endregion

    #region Combat

    void ChaseTarget()
    {
        if (agent.isStopped) agent.isStopped = false;
        agent.SetDestination(target.position);
    }

    void AttackTarget()
    {
        agent.isStopped = true;

        if (!alreadyAttacked)
            StartCoroutine(BurstFire());
    }

    IEnumerator BurstFire()
    {
        alreadyAttacked = true;

        yield return new WaitForSeconds(0.2f);

        for (int i = 0; i < bulletsPerBurst; i++)
        {
            if (projectile != null && shootPoint != null)
            {
                GameObject bullet = Instantiate(projectile, shootPoint.position, shootPoint.rotation);

                Rigidbody rb = bullet.GetComponent<Rigidbody>();
                if (rb != null)
                    rb.AddForce(shootPoint.forward * shootForce, ForceMode.Impulse);
            }

            yield return new WaitForSeconds(timeBetweenShots);
        }

        yield return new WaitForSeconds(timeBetweenAttacks);
        alreadyAttacked = false;
    }

    #endregion

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, sightRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}