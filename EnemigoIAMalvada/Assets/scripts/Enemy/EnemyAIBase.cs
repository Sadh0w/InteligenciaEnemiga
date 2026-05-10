using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum PatrolMode { Random, Waypoints }
public enum EnemyState { Patrol, Investigate, Chase, Attack, Cover, Flee }

public class EnemyAIBase : MonoBehaviour
{
    #region Variables

    [Header("References")]
    [SerializeField] NavMeshAgent agent;
    [SerializeField] Transform target;
    [SerializeField] Transform face;

    [Header("Movement")]
    [SerializeField] float moveSpeed = 3.5f;

    [Header("Layers")]
    [SerializeField] LayerMask playerLayer;
    [SerializeField] LayerMask visionBlockerLayer;
    [SerializeField] LayerMask bulletLayer;
    [SerializeField] LayerMask enemyLayer;  // layer 'Enemigo', para comprobar fuego amigo

    [Header("Vision")]
    [SerializeField] float sightRange = 15f;
    [SerializeField][Range(10f, 180f)] float fieldOfViewAngle = 90f;
    [SerializeField] float eyeHeight = 1.6f;
    [SerializeField][Range(0.01f, 0.2f)] float visionSphereRadius = 0.05f;

    [Header("Hearing")]
    [SerializeField] float hearingRange = 6f;
    [SerializeField] float minSpeedToHear = 0.5f;

    [Header("Combat")]
    [SerializeField] float attackRange = 8f;
    [SerializeField] GameObject projectile;
    [SerializeField] Transform shootPoint;
    [SerializeField] float shootForce = 0f;
    [SerializeField] float timeBetweenAttacks = 2f;
    [SerializeField] int bulletsPerBurst = 3;
    [SerializeField] float timeBetweenShots = 0.15f;
    [SerializeField] float suppressionTime = 2.5f;

    [Header("Coordination")]
    // Distancia mínima entre enemigos que van al mismo destino
    [SerializeField] float separationRadius = 2f;
    // Offset aleatorio aplicado al destino para que no se apilen
    [SerializeField] float destinationOffset = 1.5f;

    [Header("Cover")]
    [SerializeField] float coverSearchRadius = 20f;
    [SerializeField] float coverDuration = 4f;
    [SerializeField] float flankDistance = 6f;

    [Header("Flee")]
    [SerializeField] bool canFlee = true;
    [SerializeField] bool fleeWhenAlone = true;
    [SerializeField] float reachAllyRadius = 3f;

    [Header("Patrol")]
    [SerializeField] PatrolMode patrolMode = PatrolMode.Random;
    [SerializeField] float walkPointRange = 10f;
    [SerializeField] List<Transform> waypoints;

    [Header("Investigate")]
    [SerializeField] float investigateWaitTime = 4f;

    [Header("Communication")]
    [SerializeField] float alertRadius = 20f;
    [SerializeField] int groupID = 0;

    [Header("Animation")]
    [SerializeField] Animator animator;

    static readonly int HashSpeed = Animator.StringToHash("Speed");
    static readonly int HashShoot = Animator.StringToHash("Shoot");
    static readonly int HashAlert = Animator.StringToHash("Alert");
    static readonly int HashCover = Animator.StringToHash("Cover");
    static readonly int HashFlee = Animator.StringToHash("Flee");

    [Header("Debug")]
    [SerializeField] bool showDebugRays = true;

    [Header("Optimization")]
    [SerializeField] float aiUpdateFrequency = 0.15f;

    // Estado
    EnemyState currentState = EnemyState.Patrol;
    public EnemyState CurrentState => currentState;

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

    // Chase
    float chaseGracePeriod;
    const float CHASE_GRACE = 1.5f;

    // Cover
    Vector3 coverPoint;
    bool coverPointSet;
    float coverTimer;
    bool wasHitRecently;

    // Flee
    EnemyAIBase fleeTarget;

    Rigidbody targetRigidbody;

    #endregion

    #region Public API

    public int GroupID
    {
        get => groupID;
        set => groupID = value;
    }

    public float MoveSpeed => moveSpeed;

    public void SetMoveSpeed(float speed)
    {
        moveSpeed = Mathf.Max(0f, speed);
        if (agent != null)
            agent.speed = moveSpeed;
    }

    public bool IsCombatActive() =>
        currentState == EnemyState.Chase ||
        currentState == EnemyState.Attack ||
        currentState == EnemyState.Cover;

    public void ReceiveAlert(Vector3 position)
    {
        if (currentState == EnemyState.Patrol || currentState == EnemyState.Investigate)
            EnterInvestigate(position);
    }

    public void ReceivePlayerSpotted()
    {
        if (currentState != EnemyState.Attack && currentState != EnemyState.Cover)
            EnterChase();
    }

    public void OnDamageReceived()
    {
        wasHitRecently = true;
    }

    public void OnFleeThresholdReached()
    {
        if (canFlee) EnterFlee();
    }

    #endregion

    #region Lifecycle

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        if (agent != null)
            agent.speed = moveSpeed;

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
                Debug.LogError("EnemyAIBase: no se encontró ningún Player.");
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

    void OnTriggerEnter(Collider other)
    {
        if (bulletLayer == 0) return;
        if (((1 << other.gameObject.layer) & bulletLayer) != 0)
            wasHitRecently = true;
    }

    #endregion

    #region Detection

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

        return CheckLineOfSight(eyePos, dir.normalized, dist);
    }

    bool HasLineOfSight()
    {
        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPos = target.position + Vector3.up * eyeHeight;
        Vector3 dir = targetPos - origin;
        return CheckLineOfSight(origin, dir.normalized, dir.magnitude);
    }

    /// <summary>
    /// Comprueba si hay un aliado interpuesto entre este enemigo y el jugador.
    /// Si lo hay, no debe disparar para evitar fuego amigo.
    /// </summary>
    bool AllyInLineOfFire()
    {
        if (enemyLayer == 0) return false;

        Vector3 origin = shootPoint != null ? shootPoint.position : transform.position + Vector3.up * eyeHeight;
        Vector3 targetPos = target.position + Vector3.up * eyeHeight;
        Vector3 dir = (targetPos - origin).normalized;
        float dist = Vector3.Distance(origin, targetPos);

        // Máscara: solo comprueba contra otros enemigos
        if (Physics.SphereCast(origin, 0.3f, dir, out RaycastHit hit, dist, enemyLayer))
        {
            // Si el objeto golpeado no es este mismo enemigo, hay un aliado en medio
            if (hit.transform != transform && hit.transform.root != transform)
                return true;
        }

        return false;
    }

    bool CheckLineOfSight(Vector3 origin, Vector3 direction, float distance)
    {
        LayerMask mask = visionBlockerLayer | playerLayer;
        bool result = false;

        if (Physics.SphereCast(origin, visionSphereRadius, direction,
                               out RaycastHit hit, distance, mask))
        {
            result = ((1 << hit.transform.gameObject.layer) & playerLayer) != 0;

            if (showDebugRays)
            {
                Debug.DrawLine(origin, hit.point,
                    result ? Color.green : Color.red, aiUpdateFrequency);
                if (!result)
                    Debug.DrawLine(hit.point, origin + direction * distance,
                        new Color(1f, 0.5f, 0f), aiUpdateFrequency);
            }
        }
        else
        {
            if (showDebugRays)
                Debug.DrawLine(origin, origin + direction * distance,
                    Color.gray, aiUpdateFrequency);
        }

        return result;
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

    bool PlayerIsInCover()
    {
        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPos = target.position + Vector3.up * eyeHeight;
        Vector3 dir = targetPos - origin;
        LayerMask mask = visionBlockerLayer | playerLayer;

        if (Physics.SphereCast(origin, visionSphereRadius, dir.normalized,
                               out RaycastHit hit, dir.magnitude, mask))
            return ((1 << hit.transform.gameObject.layer) & playerLayer) == 0;

        return false;
    }

    #endregion

    #region State Machine

    void UpdateState()
    {
        bool sees = CanSeeTarget();
        bool hears = CanHearTarget();

        if (currentState == EnemyState.Chase && chaseGracePeriod > 0f)
            chaseGracePeriod -= aiUpdateFrequency;

        switch (currentState)
        {
            case EnemyState.Patrol:
                if (sees) EnterChase();
                else if (hears) EnterInvestigate(target.position);
                break;

            case EnemyState.Investigate:
                if (sees) EnterChase();
                break;

            case EnemyState.Chase:
                if (sees)
                {
                    lastKnownPosition = target.position;
                    chaseGracePeriod = CHASE_GRACE;
                    EnemyManager.Instance?.AlertNearby(this, lastKnownPosition, alertRadius, combatAlert: true);

                    if (InAttackRange())
                        currentState = EnemyState.Attack;
                }
                else if (chaseGracePeriod <= 0f)
                {
                    EnterInvestigate(lastKnownPosition);
                }
                break;

            case EnemyState.Attack:
                bool losNow = HasLineOfSight();

                if (sees)
                {
                    lastKnownPosition = target.position;
                    isSuppressing = false;
                    suppressionTimer = suppressionTime;
                    EnemyManager.Instance?.AlertNearby(this, lastKnownPosition, alertRadius, combatAlert: true);

                    if (wasHitRecently && PlayerIsInCover())
                    {
                        wasHitRecently = false;
                        EnterCover();
                        break;
                    }

                    if (!InAttackRange()) EnterChase();
                }
                else if (losNow)
                {
                    isSuppressing = false;
                    EnterChase();
                }
                else
                {
                    isSuppressing = false;
                    EnterInvestigate(lastKnownPosition);
                }
                break;

            case EnemyState.Cover:
                coverTimer -= aiUpdateFrequency;

                if (sees && !PlayerIsInCover())
                    EnterAttackFromCover();
                else if (coverTimer <= 0f)
                    EnterFlank();
                else if (sees && wasHitRecently)
                {
                    wasHitRecently = false;
                    EnterCover();
                }
                break;

            case EnemyState.Flee:
                if (fleeTarget == null || !fleeTarget.isActiveAndEnabled)
                {
                    fleeTarget = EnemyManager.Instance?.GetNearestAlly(this);
                    if (fleeTarget == null) { EnterPatrol(); break; }
                    agent.SetDestination(fleeTarget.transform.position);
                }

                float distToAlly = Vector3.Distance(transform.position, fleeTarget.transform.position);
                if (distToAlly <= reachAllyRadius)
                {
                    groupID = fleeTarget.GroupID;
                    EnterChase();
                }
                break;
        }

        if (currentState != EnemyState.Cover)
            wasHitRecently = false;
    }

    void EnterChase()
    {
        currentState = EnemyState.Chase;
        chaseGracePeriod = CHASE_GRACE;
        isSuppressing = false;
        agent.isStopped = false;
        isLookingAround = false;
        StopCoroutine(nameof(LookAroundRoutine));
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

    void EnterCover()
    {
        if (CoverSystem.Instance == null) { EnterChase(); return; }

        Vector3 best = CoverSystem.Instance.GetBestCoverPoint(
            transform.position, target.position, visionBlockerLayer);

        if (best == Vector3.positiveInfinity) { EnterFlank(); return; }

        coverPoint = best;
        coverPointSet = true;
        coverTimer = coverDuration;
        currentState = EnemyState.Cover;
        agent.isStopped = false;
        agent.SetDestination(coverPoint);
    }

    void EnterAttackFromCover()
    {
        currentState = EnemyState.Attack;
        agent.isStopped = true;
    }

    void EnterFlank()
    {
        Vector3 toPlayer = (target.position - transform.position).normalized;
        Vector3 lateral = Vector3.Cross(toPlayer, Vector3.up).normalized;
        if (Random.value > 0.5f) lateral = -lateral;

        Vector3 flankPos = target.position + lateral * flankDistance;

        if (NavMesh.SamplePosition(flankPos, out NavMeshHit hit, flankDistance, NavMesh.AllAreas))
        {
            currentState = EnemyState.Chase;
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }
        else
        {
            EnterChase();
        }
    }

    void EnterFlee()
    {
        if (!canFlee) return;

        fleeTarget = EnemyManager.Instance?.GetNearestAlly(this);

        currentState = EnemyState.Flee;
        agent.isStopped = false;

        if (fleeTarget != null)
            agent.SetDestination(fleeTarget.transform.position);
        else
        {
            Vector3 awayDir = (transform.position - target.position).normalized;
            Vector3 fleePos = transform.position + awayDir * 15f;
            if (NavMesh.SamplePosition(fleePos, out NavMeshHit hit, 15f, NavMesh.AllAreas))
                agent.SetDestination(hit.position);
        }
    }

    void EnterPatrol()
    {
        currentState = EnemyState.Patrol;
        agent.isStopped = false;
        walkPointSet = false;
    }

    #endregion

    #region State Execution

    void ExecuteState()
    {
        switch (currentState)
        {
            case EnemyState.Patrol: DoPatrol(); break;
            case EnemyState.Investigate: DoInvestigate(); break;
            case EnemyState.Chase: DoChase(); break;
            case EnemyState.Attack: DoAttack(); break;
            case EnemyState.Cover: DoCover(); break;
            case EnemyState.Flee: DoFlee(); break;
        }

        UpdateAnimator();
    }

    #endregion

    #region Patrol

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

    #endregion

    #region Investigate

    void DoInvestigate()
    {
        if (agent.remainingDistance <= agent.stoppingDistance && !isLookingAround)
        {
            isLookingAround = true;
            StartCoroutine(nameof(LookAroundRoutine));
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

    #endregion

    #region Chase

    /// <summary>
    /// Persigue al jugador con un offset para que los enemigos no se apilen
    /// todos en el mismo punto exacto.
    /// </summary>
    void DoChase()
    {
        agent.isStopped = false;

        // Comprueba si hay otro aliado muy cerca yendo al mismo destino
        Vector3 destination = target.position;

        if (separationRadius > 0f)
        {
            Collider[] nearby = Physics.OverlapSphere(transform.position, separationRadius, enemyLayer);
            foreach (var col in nearby)
            {
                if (col.transform == transform || col.transform.root == transform) continue;

                // Si hay un aliado en frente hacia el jugador, desplazamos el destino lateralmente
                Vector3 toAlly = col.transform.position - transform.position;
                Vector3 toPlayer = target.position - transform.position;

                if (Vector3.Dot(toAlly.normalized, toPlayer.normalized) > 0.7f)
                {
                    Vector3 lateral = Vector3.Cross(toPlayer.normalized, Vector3.up).normalized;
                    lateral *= (Random.value > 0.5f ? 1f : -1f);
                    destination = target.position + lateral * destinationOffset;
                    break;
                }
            }
        }

        if (NavMesh.SamplePosition(destination, out NavMeshHit hit, destinationOffset + 1f, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
        else
            agent.SetDestination(target.position);
    }

    #endregion

    #region Attack

    void DoAttack()
    {
        agent.isStopped = true;

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
            if (!HasLineOfSight()) break;

            // No dispara si hay un aliado en la línea de fuego
            if (AllyInLineOfFire())
            {
                Debug.Log($"[AI] {gameObject.name}: aliado en línea de fuego, esperando...");
                yield return new WaitForSeconds(timeBetweenShots);
                continue;
            }

            if (projectile != null && shootPoint != null)
            {
                GameObject bullet = Instantiate(projectile, shootPoint.position, Quaternion.identity);
                Bullet b = bullet.GetComponent<Bullet>();
                if (b != null)
                {
                    b.SetDirection(shootPoint.forward);
                    Collider enemyCol = GetComponent<Collider>();
                    if (enemyCol != null) b.IgnoreCollider(enemyCol);
                }
                else if (shootForce > 0f)
                {
                    Rigidbody rb = bullet.GetComponent<Rigidbody>();
                    if (rb != null)
                        rb.AddForce(shootPoint.forward * shootForce, ForceMode.Impulse);
                }
            }

            yield return new WaitForSeconds(timeBetweenShots);
        }

        yield return new WaitForSeconds(timeBetweenAttacks);
        alreadyAttacked = false;
    }

    #endregion

    #region Cover

    void DoCover()
    {
        agent.isStopped = false;

        if (CanSeeTarget())
        {
            Vector3 lookDir = target.position - transform.position;
            lookDir.y = 0;
            if (lookDir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    Quaternion.LookRotation(lookDir),
                    6f * aiUpdateFrequency
                );
        }
    }

    #endregion

    #region Flee

    void DoFlee()
    {
        if (fleeTarget != null && fleeTarget.isActiveAndEnabled)
            agent.SetDestination(fleeTarget.transform.position);
    }

    #endregion

    #region Animation

    void UpdateAnimator()
    {
        if (animator == null) return;

        float speed = currentState switch
        {
            EnemyState.Patrol => agent.velocity.magnitude,
            EnemyState.Investigate => agent.velocity.magnitude,
            EnemyState.Chase => agent.velocity.magnitude,
            EnemyState.Cover => agent.velocity.magnitude,
            EnemyState.Flee => agent.velocity.magnitude,
            EnemyState.Attack => 0f,
            _ => 0f
        };

        animator.SetFloat(HashSpeed, speed, 0.1f, Time.deltaTime);
        animator.SetBool(HashShoot, currentState == EnemyState.Attack);
        animator.SetBool(HashAlert, currentState == EnemyState.Investigate);
        animator.SetBool(HashCover, currentState == EnemyState.Cover);
        animator.SetBool(HashFlee, currentState == EnemyState.Flee);
    }

    #endregion

    #region Gizmos

    void OnDrawGizmosSelected()
    {
        Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
        Vector3 fwd = face != null ? face.forward : transform.forward;
        LayerMask mask = visionBlockerLayer | playerLayer;

        int rayCount = 24;
        Vector3 prevPoint = Vector3.zero;
        bool hasPrev = false;

        for (int i = 0; i <= rayCount; i++)
        {
            float t = (float)i / rayCount;
            float angle = Mathf.Lerp(-fieldOfViewAngle * 0.5f, fieldOfViewAngle * 0.5f, t);
            Vector3 dir = Quaternion.Euler(0, angle, 0) * fwd;

            float rayDist = sightRange;
            bool blocked = false;

            if (Physics.SphereCast(eyePos, visionSphereRadius, dir,
                                   out RaycastHit hit, sightRange, mask))
            {
                rayDist = hit.distance;
                blocked = ((1 << hit.transform.gameObject.layer) & playerLayer) == 0;
            }

            Vector3 endPoint = eyePos + dir * rayDist;

            Gizmos.color = blocked
                ? new Color(1f, 0.2f, 0.2f, 0.8f)
                : new Color(1f, 1f, 0.2f, 0.5f);
            Gizmos.DrawLine(eyePos, endPoint);

            if (hasPrev)
            {
                Gizmos.color = new Color(1f, 1f, 0f, 0.1f);
                Gizmos.DrawLine(prevPoint, endPoint);
            }

            prevPoint = endPoint;
            hasPrev = true;
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(eyePos, eyePos + Quaternion.Euler(0, -fieldOfViewAngle * 0.5f, 0) * fwd * sightRange);
        Gizmos.DrawLine(eyePos, eyePos + Quaternion.Euler(0, fieldOfViewAngle * 0.5f, 0) * fwd * sightRange);

        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, hearingRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = new Color(1f, 0.4f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, alertRadius);

        // Separación entre aliados
        Gizmos.color = new Color(1f, 1f, 1f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, separationRadius);

        if (Application.isPlaying && currentState == EnemyState.Cover && coverPointSet)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(coverPoint, 0.3f);
            Gizmos.DrawLine(transform.position, coverPoint);
        }

        if (Application.isPlaying && currentState == EnemyState.Investigate)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(lastKnownPosition, 0.3f);
            Gizmos.DrawLine(transform.position, lastKnownPosition);
        }

        if (Application.isPlaying && currentState == EnemyState.Flee && fleeTarget != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(transform.position, fleeTarget.transform.position);
        }
    }

    #endregion
}