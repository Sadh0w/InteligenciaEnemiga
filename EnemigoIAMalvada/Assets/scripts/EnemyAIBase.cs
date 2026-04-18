using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum PatrolMode { Random, Waypoints }
public enum EnemyState { Patrol, Investigate, Chase, Attack }

public class EnemyAIBase : MonoBehaviour
{
    #region Variables

    [Header("References")]
    [SerializeField] NavMeshAgent agent;
    [SerializeField] Transform target;
    [SerializeField] Transform face; // el objeto Face del enemigo

    [Header("Layers")]
    [SerializeField] LayerMask playerLayer;     // solo la capa del Player
    [SerializeField] LayerMask obstacleLayer;   // solo obstáculos que bloquean visión

    [Header("Vision")]
    [SerializeField] float sightRange = 15f;
    [SerializeField][Range(10f, 180f)] float fieldOfViewAngle = 90f;
    [SerializeField] float eyeHeight = 1.6f;

    [Header("Hearing")]
    [SerializeField] float hearingRange = 6f;
    [SerializeField] float minSpeedToHear = 0.5f;

    [Header("Combat")]
    [SerializeField] float attackRange = 8f;
    [SerializeField] GameObject projectile;
    [SerializeField] Transform shootPoint;
    [SerializeField] float shootForce = 20f;
    [SerializeField] float timeBetweenAttacks = 2f;
    [SerializeField] int bulletsPerBurst = 3;
    [SerializeField] float timeBetweenShots = 0.15f;

    [Header("Patrol")]
    [SerializeField] PatrolMode patrolMode = PatrolMode.Random;
    [SerializeField] float walkPointRange = 10f;
    [SerializeField] List<Transform> waypoints;

    [Header("Investigate")]
    [SerializeField] float investigateWaitTime = 4f;

    [Header("Optimization")]
    [SerializeField] float aiUpdateFrequency = 0.15f;

    // Estado
    EnemyState currentState = EnemyState.Patrol;

    // Patrol
    int currentWaypointIndex;
    Vector3 walkPoint;
    bool walkPointSet;

    // Combat
    bool alreadyAttacked;

    // Investigate
    Vector3 lastKnownPosition;
    bool isLookingAround;

    // Caché del Rigidbody del jugador para oído
    Rigidbody targetRigidbody;

    // Para evitar que el cono falle mientras el agente gira al empezar a perseguir
    float chaseGracePeriod = 0f;
    const float CHASE_GRACE = 1.5f; // segundos que persigue aunque pierda el cono

    #endregion

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        if (target == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                target = playerObj.transform;
                targetRigidbody = playerObj.GetComponent<Rigidbody>();
            }
            else
            {
                Debug.LogError("No Player found");
                enabled = false;
                return;
            }
        }

        // Busca Face automáticamente si no está asignado
        if (face == null)
        {
            Transform found = transform.Find("Face");
            if (found != null) face = found;
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
            UpdateState();
            ExecuteState();
        }
    }

    // ─────────────────────────────────────────────
    // DETECCIÓN
    // ─────────────────────────────────────────────

    bool CanSeeTarget()
    {
        Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPos = target.position + Vector3.up * eyeHeight;
        Vector3 dirToTarget = targetPos - eyePos;
        float distance = dirToTarget.magnitude;

        if (distance > sightRange) return false;

        // Usa Face.forward si está disponible, si no transform.forward
        Vector3 forwardDir = face != null ? face.forward : transform.forward;
        float angle = Vector3.Angle(forwardDir, dirToTarget.normalized);
        if (angle > fieldOfViewAngle * 0.5f) return false;

        // Raycast contra obstáculos + player: si llega al player, ve
        // Primero: ¿hay un obstáculo antes que el jugador?
        LayerMask combinedMask = obstacleLayer | playerLayer;
        if (Physics.Raycast(eyePos, dirToTarget.normalized, out RaycastHit hit, sightRange, combinedMask))
        {
            // Si lo primero que toca es el jugador, hay visión
            if (((1 << hit.transform.gameObject.layer) & playerLayer) != 0)
                return true;
        }

        return false;
    }

    bool CanHearTarget()
    {
        float distance = Vector3.Distance(transform.position, target.position);
        if (distance > hearingRange) return false;

        if (targetRigidbody != null)
            return targetRigidbody.linearVelocity.magnitude >= minSpeedToHear;

        return true; // sin Rigidbody, detecta siempre si está en rango
    }

    bool InAttackRange()
    {
        float distance = Vector3.Distance(transform.position, target.position);
        return distance <= attackRange && CanSeeTarget();
    }

    // ─────────────────────────────────────────────
    // MÁQUINA DE ESTADOS
    // ─────────────────────────────────────────────

    void UpdateState()
    {
        bool sees = CanSeeTarget();
        bool hears = CanHearTarget();

        // Reduce el grace period en Chase
        if (currentState == EnemyState.Chase && chaseGracePeriod > 0f)
            chaseGracePeriod -= aiUpdateFrequency;

        switch (currentState)
        {
            case EnemyState.Patrol:
                if (sees)
                    EnterChase();
                else if (hears)
                    EnterInvestigate(target.position);
                break;

            case EnemyState.Investigate:
                if (sees)
                    EnterChase();
                break;

            case EnemyState.Chase:
                if (sees)
                {
                    lastKnownPosition = target.position;
                    chaseGracePeriod = CHASE_GRACE; // resetea el timer mientras ve al jugador

                    if (InAttackRange())
                        currentState = EnemyState.Attack;
                }
                else if (chaseGracePeriod <= 0f)
                {
                    // Solo pierde al jugador cuando expira el grace period
                    EnterInvestigate(lastKnownPosition);
                }
                break;

            case EnemyState.Attack:
                if (!sees)
                {
                    if (chaseGracePeriod > 0f)
                        EnterChase();
                    else
                        EnterInvestigate(lastKnownPosition);
                }
                else if (!InAttackRange())
                {
                    EnterChase();
                }
                break;
        }
    }

    void EnterChase()
    {
        currentState = EnemyState.Chase;
        chaseGracePeriod = CHASE_GRACE;
        agent.isStopped = false;
        isLookingAround = false;
    }

    void EnterInvestigate(Vector3 position)
    {
        lastKnownPosition = position;
        currentState = EnemyState.Investigate;
        agent.isStopped = false;
        agent.SetDestination(lastKnownPosition);
        isLookingAround = false;
    }

    // ─────────────────────────────────────────────
    // EJECUCIÓN DE ESTADOS
    // ─────────────────────────────────────────────

    void ExecuteState()
    {
        switch (currentState)
        {
            case EnemyState.Patrol: DoPatrol(); break;
            case EnemyState.Investigate: DoInvestigate(); break;
            case EnemyState.Chase: DoChase(); break;
            case EnemyState.Attack: DoAttack(); break;
        }
    }

    // ─────────────────────────────────────────────
    // PATRULLA
    // ─────────────────────────────────────────────

    void DoPatrol()
    {
        agent.isStopped = false;

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
        Vector3 randomPoint = transform.position + new Vector3(randomX, 0, randomZ);

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

    // ─────────────────────────────────────────────
    // INVESTIGAR
    // ─────────────────────────────────────────────

    void DoInvestigate()
    {
        if (agent.remainingDistance <= agent.stoppingDistance && !isLookingAround)
        {
            isLookingAround = true;
            StartCoroutine(LookAroundRoutine());
        }
    }

    IEnumerator LookAroundRoutine()
    {
        agent.isStopped = true;

        Quaternion originalRot = transform.rotation;
        Quaternion leftRot = originalRot * Quaternion.Euler(0, -60f, 0);
        Quaternion rightRot = originalRot * Quaternion.Euler(0, 60f, 0);

        // Mira a la izquierda
        yield return StartCoroutine(RotateTo(leftRot, 0.6f));
        yield return new WaitForSeconds(0.5f);

        if (currentState != EnemyState.Investigate) yield break;

        // Mira a la derecha
        yield return StartCoroutine(RotateTo(rightRot, 0.8f));
        yield return new WaitForSeconds(0.5f);

        if (currentState != EnemyState.Investigate) yield break;

        // Vuelve al centro
        yield return StartCoroutine(RotateTo(originalRot, 0.4f));

        if (currentState != EnemyState.Investigate) yield break;

        // No encontró nada, vuelve a patrullar
        isLookingAround = false;
        walkPointSet = false;
        currentState = EnemyState.Patrol;
        agent.isStopped = false;
    }

    IEnumerator RotateTo(Quaternion target, float duration)
    {
        Quaternion start = transform.rotation;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            // Sale si el estado cambió
            if (currentState != EnemyState.Investigate) yield break;

            elapsed += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(start, target, elapsed / duration);
            yield return null;
        }

        transform.rotation = target;
    }

    // ─────────────────────────────────────────────
    // PERSEGUIR
    // ─────────────────────────────────────────────

    void DoChase()
    {
        agent.isStopped = false;
        agent.SetDestination(target.position);
    }

    // ─────────────────────────────────────────────
    // ATACAR
    // ─────────────────────────────────────────────

    void DoAttack()
    {
        agent.isStopped = true;

        // Mira al jugador suavemente
        Vector3 lookDir = target.position - transform.position;
        lookDir.y = 0;
        if (lookDir != Vector3.zero)
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(lookDir),
                8f * aiUpdateFrequency
            );

        if (!alreadyAttacked)
            StartCoroutine(BurstFire());
    }

    IEnumerator BurstFire()
    {
        alreadyAttacked = true;

        yield return new WaitForSeconds(0.2f);

        for (int i = 0; i < bulletsPerBurst; i++)
        {
            if (currentState != EnemyState.Attack) break;

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

    // ─────────────────────────────────────────────
    // GIZMOS — visibles en Scene view
    // ─────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
        Vector3 fwd = face != null ? face.forward : transform.forward;

        // Cono de visión
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, sightRange);
        Vector3 left = Quaternion.Euler(0, -fieldOfViewAngle * 0.5f, 0) * fwd * sightRange;
        Vector3 right = Quaternion.Euler(0, fieldOfViewAngle * 0.5f, 0) * fwd * sightRange;
        Gizmos.color = new Color(1f, 1f, 0f, 0.25f);
        Gizmos.DrawLine(eyePos, eyePos + left);
        Gizmos.DrawLine(eyePos, eyePos + right);

        // Radio de oído
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, hearingRange);

        // Rango de ataque
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // LKP
        if (Application.isPlaying && currentState == EnemyState.Investigate)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(lastKnownPosition, 0.3f);
            Gizmos.DrawLine(transform.position, lastKnownPosition);
        }
    }
}