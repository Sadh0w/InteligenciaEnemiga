using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI; //libreria de navegacion AI

public enum PatrolMode
{
    Random, //Modo de patrulla aleatorio
    Waypoints //Modo de patrulla por puntos a seguir en orden
}

public class EnemyAIBase : MonoBehaviour
{
    #region General Variables
    [Header("AI Configuration")]
    [SerializeField] NavMeshAgent agent; //Referencia al cerebro al sistema de IA NavMeshAgent
    [SerializeField] Transform target; //Referencia al objetivo a seguir
    [SerializeField] LayerMask targetLayer;
    [SerializeField] LayerMask groundLayer;

    [Header("Patrolling Stats")]
    [SerializeField] private PatrolMode patrolMode = PatrolMode.Random; //Modo de patrulla por defecto
    //Variables de estados que son comunes a ambos modos de patrulla
    Vector3 walkPoint; //Destino actual a perseguir
    bool walkPointSet; //¿hay un punto de patrulla establecido? o tenemos que establecer uno nuevo?

    [Header("patrolling - Random")]
    [SerializeField] float walkPointRange = 10f; //Define el radio de deteccion de puntos a perseguir alrededor del agente

    [Header("patrolling - Waypoints")]
    [SerializeField] private List<Transform> waypoints; //Lista de puntos a seguir en orden
    int currentWaypointIndex = 0; //Indice del punto actual a seguir

    [Header("Attack Configuration")]
    public float TimeBetweenAttacks; //Cadencia de disparo del enemigo
    bool alreadyAttacked; //Seguridad ante ataques infinitos
    [SerializeField] GameObject projectile; //Prefab del proyectil a disparar
    [SerializeField] Transform shootPoint; //Punto desde el que se dispara el proyectil
    [SerializeField] float shootSpeedZ;
    [SerializeField] float shootSpeedY;

    [Header("States & Detection")]
    [SerializeField] float sightRange; //Rango de vision del enemigo a partir del cual detecta al player
    [SerializeField] float attackRange; //Rango de vision del enemigo a partir del cual ataca al player
    [SerializeField] bool targetInSightRange; //¿El player esta en rango de vision?3
    [SerializeField] bool targetInAttackRange; //¿El player esta en rango de ataque?

    [Header("Optimitzation")]
    [SerializeField] float aiUpdateFrequency = 0.2f; //Frecuencia de actualizacion de la IA
    #endregion

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (target == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                target = playerObject.transform;// Asigna el player al target del enemigo
            }
            else
            {
                Debug.LogError("No se pudo encontrar objetos con tag player. Se requiere revision de tags");
                this.enabled = false; //Desactiva el script si no encuentra Player para evitar errores
            }
        }
    }

    //Corutina de funcionamiento de la IA (CEREBRO DE LA AI)

    private void Start()
    {
        //arranque de la corutina de procesamiento de la IA que sustituye al update 
        StartCoroutine(AIUpdateRoutine());
    }

    private IEnumerator AIUpdateRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(aiUpdateFrequency);
            // Pasi 1: Comprobar la deteccion de target
            targetInSightRange = Physics.CheckSphere(transform.position, sightRange, targetLayer);
            targetInAttackRange = Physics.CheckSphere(transform.position, sightRange, targetLayer);
            // Paso 2: Deteccion y cambio entre estados
            if (targetInSightRange && targetInAttackRange)
            {
                //atacar
                AttackTarget();

            }
            else if (targetInSightRange && !targetInAttackRange)
            {
                //Persecucion
                ChaseTarget();
            }
            else if (!targetInSightRange && !targetInAttackRange)
            {
                //Patrullar
                Patrolling();

            }
        }
    }

    private void Update()
    {
        if (targetInSightRange) transform.LookAt(target);
    }

    void Patrolling()
    {
        //devolvemos la capacidad de moverse al agente
        if (agent.isStopped) agent.isStopped = false;

        //se compreuba si el agente a llegado al punto
        //para ello, se usa un margen de distancia pequeña + stoppingDistance para asegurarnos

        if (!walkPointSet && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f) // si hemos llegado al punto le dec
        {
            walkPointSet = false; //Ya no tenemos punto a patrullar y se genera uno nuevo
        }

        //Se comprueba si no tenemos punto de destino por lo que buscara uno nuevo segun el modo de patrulla (Random/Puntos)
        if (!walkPointSet)
        {
            switch (patrolMode)
            {
                case PatrolMode.Random:
                    SearchWalkPoint_Random();
                    break;
                case PatrolMode.Waypoints:
                    SearchWalkPoint_Waypoints();
                    break;
            }
        }
    }

    void SearchWalkPoint_Random()
    {
        //GENERAR UN PUNTO DE PATRULLA (DESTINATION) ALEATORIO
        //Paso 1: Gnerar posicion aleatoria 
        float randomZ = Random.Range(-walkPointRange, walkPointRange);
        float randomX = Random.Range(-walkPointRange, walkPointRange);
        Vector3 randomPoint = new Vector3(transform.position.x + randomX, transform.position.y, transform.position.z + randomZ);

        //Paso 2: Generar el punto con la posicion determinada den formato NavMesh
        NavMeshHit hit; //almacen de informacion de impacto de rayo solo eb bakeo de NavMesh
        if (NavMesh.SamplePosition(randomPoint, out hit, walkPointRange, NavMesh.AllAreas)) //Si el punto aleatorio esta en la NavMesh
        {
            walkPoint = hit.position; //DEFINE EL PUNTO REAL A PERSEGUIR
            agent.SetDestination(walkPoint);
            walkPointSet = true; //Ya tenemos un punto de patrulla establecido
        }
    }

    void SearchWalkPoint_Waypoints()
    {
        //DETECTAR LOS PUNTOS DE PATRULLA FIJOS EN UNA LISTA Y HACER BUCLE ENTRE ELLOS
        //Paso0: Comprobar si hay puntos en la lista
        if (waypoints == null || waypoints.Count == 0)
        {
            Debug.LogWarning("El agente funciona en modo Waypoints pero la lista es nula o no tiene espacios" + "Cambiando a modo Random.....", this);
            patrolMode = PatrolMode.Random; //Cambia el modo de patrulla a Random si no hay puntos en la lista
            return;
        }
        //Paso1:asignar el siguiente waypoint como destino
        walkPoint = waypoints[currentWaypointIndex].position;
        agent.SetDestination(walkPoint);
        walkPointSet = true; //Ya tenemos un punto de patrulla establecido

        //Paso2: Cambiar el numero de espacio de la lista a perseguir
        //.....Y en caso de llegar al final, pasarlo a 0 en forma PRO
        currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Count;
    }

    void ChaseTarget()
    {
        if (agent.isStopped) agent.isStopped = false;//si el agente esta detenido, lo reanudamos
        agent.SetDestination(target.position); //Cambia el destino del agente a la position del target
    }

    void AttackTarget()
    {
        agent.isStopped = true; //Detiene el movimiento del agente para atacar

        if (!alreadyAttacked)
        {

            Rigidbody rb = Instantiate(projectile, shootPoint.position, Quaternion.identity).GetComponent<Rigidbody>();
            rb.AddForce(transform.forward * shootSpeedZ, ForceMode.Impulse);
            //El siguiente addForce solo se aplica si queremos catapulta
            //rb.AddForce(transform.forward * shootSpeedY, ForceMode.Impulse);

        }
        alreadyAttacked = true;
        StartCoroutine(ResetAttackRoutine());
    }

    IEnumerator ResetAttackRoutine()
    {
        yield return new WaitForSeconds(TimeBetweenAttacks);
        alreadyAttacked = false;//Permitir que el ataque se ejecute de nuevo
    }


    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, sightRange);
    }
}
