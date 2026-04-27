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
    // Asigna aquí la layer "Wall" o la que uses para geometría que tapa la vista.
    [SerializeField] LayerMask visionBlockerLayer;

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
    [SerializeField] float alertRadius = 20f;
    // Grupo al que pertenece. Enemigos del mismo grupo se comunican entre sí
    // si el grupo tiene comunicación activa en EnemyManager.
    [SerializeField] int groupID = 0;

    [Header("Animation")]
    [SerializeField] Animator animator;

    static readonly int HashSpeed = Animator.StringToHash("Speed");
    static readonly int HashShoot = Animator.StringToHash("Shoot");
    static readonly int HashAlert = Animator.StringToHash("Alert");

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

    // Chase
    float chaseGracePeriod;
    const float CHASE_GRACE = 1.5f;

    Rigidbody targetRigidbody;

    #endregion

    #region Public API

    /// <summary>GroupID expuesto para que EnemyManager pueda leerlo y asignarlo.</summary>
    public int GroupID
    {
        get => groupID;
        set => groupID = value;
    }

    /// <summary>Devuelve true si el enemigo ya está en combate activo (Chase o Attack).</summary>
    public bool IsCombatActive() =>
        currentState == EnemyState.Chase || currentState == EnemyState.Attack;

    /// <summary>
    /// Alerta de posición: otro enemigo oyó algo o perdió al jugador en ese punto.
    /// El receptor va a investigar si no estaba ya en combate.
    /// </summary>
    public void ReceiveAlert(Vector3 position)
    {
        if (currentState == EnemyState.Patrol || currentState == EnemyState.Investigate)
            EnterInvestigate(position);
    }

    /// <summary>
    /// Alerta de combate: otro enemigo del grupo tiene al jugador en visión ahora mismo.
    /// Cancela cualquier investigación en curso y entra directamente en Chase.
    /// </summary>
    public void ReceivePlayerSpotted()
    {
        if (currentState != EnemyState.Attack)
            EnterChase();
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

    #endregion

    #region Detection

    /// <summary>
    /// Visión completa: comprueba distancia, ángulo del cono y línea de visión.
    /// Los objetos en visionBlockerLayer tapan la vista aunque el jugador esté en rango.
    /// El raycast evalúa primero lo que toca: si es pared → no ve; si es jugador → ve.
    /// </summary>
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

        LayerMask mask = visionBlockerLayer | playerLayer;
        if (Physics.Raycast(eyePos, dir.normalized, out RaycastHit hit, sightRange, mask))
            return ((1 << hit.transform.gameObject.layer) & playerLayer) != 0;

        return false;
    }

    /// <summary>
    /// Línea de visión pura sin cono. Solo comprueba si hay pared entre el
    /// enemigo y el jugador. Se usa en combate para validar cada disparo.
    /// </summary>
    bool HasLineOfSight()
    {
        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPos = target.position + Vector3.up * eyeHeight;
        Vector3 dir = targetPos - origin;

        LayerMask mask = visionBlockerLayer | playerLayer;
        if (Physics.Raycast(origin, dir.normalized, out RaycastHit hit, sightRange, mask))
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

                    // combatAlert = true: los receptores van directo a Chase
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

                    if (!InAttackRange())
                        EnterChase();
                }
                else if (losNow) // LOS pero fuera del cono (movimiento lateral) → perseguir
                {
                    isSuppressing = false;
                    EnterChase();
                }
                else if (!isSuppressing) // perdió LOS por pared → supresión
                {
                    isSuppressing = true;
                    suppressionTimer = suppressionTime;

                    // Avisa de posición (no combate activo) para que otros investiguen
                    EnemyManager.Instance?.AlertNearby(this, lastKnownPosition, alertRadius, combatAlert: false);
                }
                else // suprimiendo: cuenta atrás
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

        // Cancela el LookAroundRoutine si estaba activo
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
            if (currentState != EnemyState.Attack) break;

            bool canShoot = wasSupressing || HasLineOfSight();
            if (!canShoot) break;

            if (projectile != null && shootPoint != null)
            {
                Quaternion shootRot = wasSupressing
                    ? Quaternion.LookRotation(
                        (lastKnownPosition + Vector3.up - shootPoint.position).normalized)
                        * Quaternion.Euler(Random.Range(-5f, 5f), Random.Range(-8f, 8f), 0)
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
            EnemyState.Attack => 0f,
            _ => 0f
        };

        animator.SetFloat(HashSpeed, speed, 0.1f, Time.deltaTime);
        animator.SetBool(HashShoot, currentState == EnemyState.Attack && !isSuppressing);
        animator.SetBool(HashAlert, currentState == EnemyState.Investigate);
    }

    #endregion

    #region Gizmos

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

        // Rango de ataque
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // Radio de alerta
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, alertRadius);

        // LKP solo en Play
        if (Application.isPlaying && currentState == EnemyState.Investigate)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(lastKnownPosition, 0.3f);
            Gizmos.DrawLine(transform.position, lastKnownPosition);
        }
    }

    #endregion
}