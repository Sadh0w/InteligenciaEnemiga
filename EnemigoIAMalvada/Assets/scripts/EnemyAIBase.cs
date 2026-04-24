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
    [SerializeField] Transform face;

    [Header("Layers")]
    [SerializeField] LayerMask playerLayer;
    [SerializeField] LayerMask obstacleLayer;

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
    [SerializeField] float suppressionTime = 2.5f;

    [Header("Patrol")]
    [SerializeField] PatrolMode patrolMode = PatrolMode.Random;
    [SerializeField] float walkPointRange = 10f;
    [SerializeField] List<Transform> waypoints;

    [Header("Investigate")]
    [SerializeField] float investigateWaitTime = 4f;

    [Header("Communication")]
    [SerializeField] float alertRadius = 20f; // radio al que avisa a otros enemigos

    [Header("Animation")]
    [SerializeField] Animator animator; // null hasta que haya modelo, no rompe nada

    // Hashes son más eficientes que strings en cada frame
    static readonly int HashSpeed = Animator.StringToHash("Speed");
    static readonly int HashShoot = Animator.StringToHash("Shoot");
    static readonly int HashAlert = Animator.StringToHash("Alert"); // para la animación de investigar

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
    bool isSuppressing;
    float suppressionTimer;

    // Investigate
    Vector3 lastKnownPosition;
    bool isLookingAround;

    // Chase grace period
    float chaseGracePeriod;
    const float CHASE_GRACE = 1.5f;

    Rigidbody targetRigidbody;

    #endregion

    // ─────────────────────────────────────────────
    // API PÚBLICA para EnemyManager
    // ─────────────────────────────────────────────

    /// <summary>Devuelve true si el enemigo ya está en combate activo (Chase o Attack).</summary>
    public bool IsCombatActive() =>
        currentState == EnemyState.Chase || currentState == EnemyState.Attack;

    /// <summary>Otro enemigo le pasa la última posición conocida del jugador.</summary>
    public void ReceiveAlert(Vector3 position)
    {
        // Solo reacciona si estaba patrullando o investigando otra cosa
        if (currentState == EnemyState.Patrol || currentState == EnemyState.Investigate)
            EnterInvestigate(position);
    }

    // ─────────────────────────────────────────────
    // LIFECYCLE
    // ─────────────────────────────────────────────

    void Awake()
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

        if (face == null)
        {
            Transform found = transform.Find("Face");
            if (found != null) face = found;
        }
    }

    void OnEnable() => EnemyManager.Instance?.Register(this);
    void OnDisable() => EnemyManager.Instance?.Unregister(this);

    void Start()
    {
        // Registro de seguridad por si el Manager ya existía antes del OnEnable
        EnemyManager.Instance?.Register(this);
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
        Vector3 dir = targetPos - eyePos;
        float dist = dir.magnitude;

        if (dist > sightRange) return false;

        Vector3 fwd = face != null ? face.forward : transform.forward;
        float angle = Vector3.Angle(fwd, dir.normalized);
        if (angle > fieldOfViewAngle * 0.5f) return false;

        LayerMask mask = obstacleLayer | playerLayer;
        if (Physics.Raycast(eyePos, dir.normalized, out RaycastHit hit, sightRange, mask))
            return ((1 << hit.transform.gameObject.layer) & playerLayer) != 0;

        return false;
    }

    bool CanHearTarget()
    {
        float dist = Vector3.Distance(transform.position, target.position);
        if (dist > hearingRange) return false;

        if (targetRigidbody != null)
            return targetRigidbody.linearVelocity.magnitude >= minSpeedToHear;

        return true;
    }

    bool InAttackRange()
    {
        float dist = Vector3.Distance(transform.position, target.position);
        return dist <= attackRange && CanSeeTarget();
    }

    // ─────────────────────────────────────────────
    // MÁQUINA DE ESTADOS
    // ─────────────────────────────────────────────

    void UpdateState()
    {
        bool sees = CanSeeTarget();
        bool hears = CanHearTarget();

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
                if (sees) EnterChase();
                break;

            case EnemyState.Chase:
                if (sees)
                {
                    lastKnownPosition = target.position;
                    chaseGracePeriod = CHASE_GRACE;

                    // Avisa a los enemigos cercanos cada vez que confirma visión
                    EnemyManager.Instance?.AlertNearby(this, lastKnownPosition, alertRadius);

                    if (InAttackRange())
                        currentState = EnemyState.Attack;
                }
                else if (chaseGracePeriod <= 0f)
                {
                    EnterInvestigate(lastKnownPosition);
                }
                break;

            case EnemyState.Attack:
                if (sees)
                {
                    lastKnownPosition = target.position;
                    isSuppressing = false;
                    suppressionTimer = suppressionTime;

                    // También avisa mientras ataca
                    EnemyManager.Instance?.AlertNearby(this, lastKnownPosition, alertRadius);

                    if (!InAttackRange())
                        EnterChase();
                }
                else if (!isSuppressing)
                {
                    // Acaba de perder visión → supresión
                    isSuppressing = true;
                    suppressionTimer = suppressionTime;
                }
                else
                {
                    suppressionTimer -= aiUpdateFrequency;

                    if (suppressionTimer <= 0f)
                    {
                        isSuppressing = false;
                        EnterInvestigate(lastKnownPosition);
                    }
                }
                break;
        }
    }

    void EnterChase()
    {
        currentState = EnemyState.Chase;
        chaseGracePeriod = CHASE_GRACE;
        isSuppressing = false;
        agent.isStopped = false;
        isLookingAround = false;
    }

    void EnterInvestigate(Vector3 position)
    {
        lastKnownPosition = position;
        currentState = EnemyState.Investigate;
        isSuppressing = false;
        agent.isStopped = false;
        isLookingAround = false;
        agent.SetDestination(lastKnownPosition);
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

        UpdateAnimator(); // siempre al final
    }

    #region Animación

    void UpdateAnimator()
    {
        if (animator == null) return; // sin modelo no hace nada, sin errores

        float speed = currentState switch
        {
            EnemyState.Patrol => agent.velocity.magnitude,
            EnemyState.Investigate => agent.velocity.magnitude,
            EnemyState.Chase => agent.velocity.magnitude,
            EnemyState.Attack => 0f,
            _ => 0f
        };

        animator.SetFloat(HashSpeed, speed, 0.1f, Time.deltaTime); // el 0.1f suaviza la transición
        animator.SetBool(HashShoot, currentState == EnemyState.Attack && !isSuppressing);
        animator.SetBool(HashAlert, currentState == EnemyState.Investigate);
    }

    #endregion

    // ─────────────────────────────────────────────
    // PATRULLA
    // ─────────────────────────────────────────────

    void DoPatrol()
    {
        agent.isStopped = false;

        if (!walkPointSet || agent.remainingDistance <= agent.stoppingDistance)
        {
            walkPointSet = false;

            if (patrolMode == PatrolMode.Random) SearchWalkPoint_Random();
            else SearchWalkPoint_Waypoints();
        }
    }

    void SearchWalkPoint_Random()
    {
        float rx = Random.Range(-walkPointRange, walkPointRange);
        float rz = Random.Range(-walkPointRange, walkPointRange);
        Vector3 pt = transform.position + new Vector3(rx, 0, rz);

        if (NavMesh.SamplePosition(pt, out NavMeshHit hit, walkPointRange, NavMesh.AllAreas))
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

        yield return StartCoroutine(RotateTo(leftRot, 0.6f));
        yield return new WaitForSeconds(0.5f);
        if (currentState != EnemyState.Investigate) yield break;

        yield return StartCoroutine(RotateTo(rightRot, 0.8f));
        yield return new WaitForSeconds(0.5f);
        if (currentState != EnemyState.Investigate) yield break;

        yield return StartCoroutine(RotateTo(originalRot, 0.4f));
        if (currentState != EnemyState.Investigate) yield break;

        isLookingAround = false;
        walkPointSet = false;
        currentState = EnemyState.Patrol;
        agent.isStopped = false;
    }

    IEnumerator RotateTo(Quaternion to, float duration)
    {
        Quaternion from = transform.rotation;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (currentState != EnemyState.Investigate) yield break;
            elapsed += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(from, to, elapsed / duration);
            yield return null;
        }
        transform.rotation = to;
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

        // En supresión mira al LKP, si no al jugador
        Vector3 lookTarget = isSuppressing ? lastKnownPosition : target.position;
        Vector3 lookDir = lookTarget - transform.position;
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

        int shots = isSuppressing ? 1 : bulletsPerBurst;
        float cooldown = isSuppressing ? timeBetweenAttacks * 1.8f : timeBetweenAttacks;

        yield return new WaitForSeconds(0.2f);

        for (int i = 0; i < shots; i++)
        {
            if (currentState != EnemyState.Attack) break;

            if (projectile != null && shootPoint != null)
            {
                Quaternion shootRot = isSuppressing
                    ? Quaternion.LookRotation(
                        (lastKnownPosition + Vector3.up - shootPoint.position).normalized)
                        * Quaternion.Euler(
                            Random.Range(-5f, 5f),
                            Random.Range(-8f, 8f), 0)
                    : shootPoint.rotation;

                GameObject bullet = Instantiate(projectile, shootPoint.position, shootRot);
                Rigidbody rb = bullet.GetComponent<Rigidbody>();
                if (rb != null)
                    rb.AddForce(shootRot * Vector3.forward * shootForce, ForceMode.Impulse);
            }

            yield return new WaitForSeconds(timeBetweenShots);
        }

        yield return new WaitForSeconds(cooldown);
        alreadyAttacked = false;
    }

    // ─────────────────────────────────────────────
    // GIZMOS
    // ─────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
        Vector3 fwd = face != null ? face.forward : transform.forward;

        // Cono de visión
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, sightRange);
        Vector3 leftEdge = Quaternion.Euler(0, -fieldOfViewAngle * 0.5f, 0) * fwd * sightRange;
        Vector3 rightEdge = Quaternion.Euler(0, fieldOfViewAngle * 0.5f, 0) * fwd * sightRange;
        Gizmos.color = new Color(1f, 1f, 0f, 0.25f);
        Gizmos.DrawLine(eyePos, eyePos + leftEdge);
        Gizmos.DrawLine(eyePos, eyePos + rightEdge);

        // Oído
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, hearingRange);

        // Ataque
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Radio de alerta a otros enemigos
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, alertRadius);

        // LKP
        if (Application.isPlaying && currentState == EnemyState.Investigate)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(lastKnownPosition, 0.3f);
            Gizmos.DrawLine(transform.position, lastKnownPosition);
        }
    }
}