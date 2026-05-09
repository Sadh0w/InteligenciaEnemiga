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

    [Header("Layers")]
    [SerializeField] LayerMask playerLayer;
    [SerializeField] LayerMask visionBlockerLayer;
    [SerializeField] LayerMask bulletLayer;   // layer de los proyectiles enemigos del jugador

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

    [Header("Cover")]
    [SerializeField] float coverSearchRadius = 20f;    // radio máximo para buscar cover
    [SerializeField] float coverDuration = 4f;     // segundos máx en cobertura antes de flanquear
    [SerializeField] float flankDistance = 6f;     // distancia del punto de flanqueo lateral

    [Header("Flee")]
    [SerializeField] bool canFlee = true;
    // Si está solo (sin aliados en el grupo) también huye
    [SerializeField] bool fleeWhenAlone = true;
    // Radio dentro del cual el aliado se considera "alcanzado"
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

    // ── Estado ────────────────────────────────────────────────────────────────
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
    bool wasHitRecently;    // flag: el jugador nos disparó

    // Flee
    EnemyAIBase fleeTarget;   // aliado hacia el que huimos

    Rigidbody targetRigidbody;

    #endregion

    #region Public API

    public int GroupID
    {
        get => groupID;
        set => groupID = value;
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

    // Llamado por EnemyHealth cuando recibe daño
    public void OnDamageReceived()
    {
        wasHitRecently = true;
    }

    // Llamado por EnemyHealth cuando la vida baja del umbral
    public void OnFleeThresholdReached()
    {
        if (canFlee)
            EnterFlee();
    }

    #endregion

    #region Lifecycle

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

    // ── Detección de proyectiles del jugador ─────────────────────────────────
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

    // ¿El jugador está en cobertura respecto a este enemigo?
    bool PlayerIsInCover()
    {
        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPos = target.position + Vector3.up * eyeHeight;
        Vector3 dir = targetPos - origin;
        LayerMask mask = visionBlockerLayer | playerLayer;

        if (Physics.SphereCast(origin, visionSphereRadius, dir.normalized,
                               out RaycastHit hit, dir.magnitude, mask))
        {
            // Si el primer objeto es un bloqueador (no el jugador), está en cobertura
            return ((1 << hit.transform.gameObject.layer) & playerLayer) == 0;
        }
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
                // ── FIX: en cuanto pierde visión sale del estado de ataque ────
                bool losNow = HasLineOfSight();

                if (sees)
                {
                    lastKnownPosition = target.position;
                    isSuppressing = false;
                    suppressionTimer = suppressionTime;
                    EnemyManager.Instance?.AlertNearby(this, lastKnownPosition, alertRadius, combatAlert: true);

                    // El jugador está cubierto y nos dispararon → buscar cobertura
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
                    // Tiene LOS pero salió del cono → perseguir
                    isSuppressing = false;
                    EnterChase();
                }
                else
                {
                    // ── Perdió visión completamente: sale inmediatamente ──────
                    // No hay supresión aquí: si otro enemigo ve al jugador,
                    // este ya habrá recibido ReceivePlayerSpotted y entrado en Chase.
                    // Si nadie lo ve, va a investigar el LKP.
                    isSuppressing = false;
                    EnterInvestigate(lastKnownPosition);
                }
                break;

            case EnemyState.Cover:
                coverTimer -= aiUpdateFrequency;

                if (sees && !PlayerIsInCover())
                {
                    // El jugador salió de cobertura → atacar
                    EnterAttackFromCover();
                }
                else if (coverTimer <= 0f)
                {
                    // Tiempo en cover agotado → flanquear
                    EnterFlank();
                }
                else if (sees && wasHitRecently)
                {
                    // Nos siguen disparando → buscar mejor cover
                    wasHitRecently = false;
                    EnterCover();
                }
                break;

            case EnemyState.Flee:
                // Si el aliado ya fue eliminado, busca otro
                if (fleeTarget == null || !fleeTarget.isActiveAndEnabled)
                {
                    fleeTarget = EnemyManager.Instance?.GetNearestAlly(this);
                    if (fleeTarget == null)
                    {
                        // No hay más aliados → volver a patrullar
                        EnterPatrol();
                        break;
                    }
                    agent.SetDestination(fleeTarget.transform.position);
                }

                // ¿Llegamos al aliado?
                float distToAlly = Vector3.Distance(transform.position, fleeTarget.transform.position);
                if (distToAlly <= reachAllyRadius)
                {
                    // Nos unimos al grupo del aliado
                    groupID = fleeTarget.GroupID;
                    EnterChase(); // retomamos combate con el contexto del aliado
                }
                break;
        }

        // Resetea flag de hit después de procesarlo
        if (currentState != EnemyState.Cover)
            wasHitRecently = false;
    }

    // ── Transiciones ──────────────────────────────────────────────────────────

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

        if (best == Vector3.positiveInfinity)
        {
            // No hay cover disponible → flanquear directamente
            EnterFlank();
            return;
        }

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
        // Calcula un punto lateral al jugador
        Vector3 toPlayer = (target.position - transform.position).normalized;
        Vector3 lateral = Vector3.Cross(toPlayer, Vector3.up).normalized;

        // Alterna izquierda/derecha aleatoriamente
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
            EnterChase(); // si no hay punto navegable, persigue directamente
        }
    }

    void EnterFlee()
    {
        // Comprueba si debe huir o flanquear según condiciones
        int allies = EnemyManager.Instance?.GetActiveAlliesInGroup(this) ?? 0;

        if (!canFlee) return;

        fleeTarget = EnemyManager.Instance?.GetNearestAlly(this);

        if (fleeTarget == null && !fleeWhenAlone)
        {
            // No hay aliados y no está configurado para huir solo → flanquea
            EnterFlank();
            return;
        }

        currentState = EnemyState.Flee;
        agent.isStopped = false;

        if (fleeTarget != null)
            agent.SetDestination(fleeTarget.transform.position);
        else
        {
            // Sin aliados: huye en dirección contraria al jugador
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

        //UpdateAnimator();
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

    void DoChase()
    {
        agent.isStopped = false;
        agent.SetDestination(target.position);
    }

    #endregion

    #region Attack

    void DoAttack()
    {
        agent.isStopped = true;

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

        bool wasSupressing = isSuppressing;
        int shots = wasSupressing ? 1 : bulletsPerBurst;
        float cooldown = wasSupressing ? timeBetweenAttacks * 1.8f : timeBetweenAttacks;

        yield return new WaitForSeconds(0.2f);

        for (int i = 0; i < shots; i++)
        {
            // Sale si el estado cambió (fix: no dispara si ya no ve al jugador)
            if (currentState != EnemyState.Attack) break;

            bool canShoot = HasLineOfSight();
            if (!canShoot) break;

            if (projectile != null && shootPoint != null)
            {
                Quaternion meshOffset = Quaternion.Euler(-90f, 0f, 0f);
                Quaternion shootRot = shootPoint.rotation * meshOffset;

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

        yield return new WaitForSeconds(cooldown);
        alreadyAttacked = false;
    }

    #endregion

    #region Cover

    void DoCover()
    {
        agent.isStopped = false;

        // Mientras se mueve al cover, mira al jugador si lo ve
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
        // Actualiza destino hacia el aliado en cada tick
        if (fleeTarget != null && fleeTarget.isActiveAndEnabled)
            agent.SetDestination(fleeTarget.transform.position);
    }

    #endregion

    #region Animation
    /*
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
    */
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

        // Cover point activo
        if (Application.isPlaying && currentState == EnemyState.Cover && coverPointSet)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(coverPoint, 0.3f);
            Gizmos.DrawLine(transform.position, coverPoint);
        }

        // LKP
        if (Application.isPlaying && currentState == EnemyState.Investigate)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(lastKnownPosition, 0.3f);
            Gizmos.DrawLine(transform.position, lastKnownPosition);
        }

        // Aliado en huida
        if (Application.isPlaying && currentState == EnemyState.Flee && fleeTarget != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(transform.position, fleeTarget.transform.position);
        }
    }

    #endregion
}