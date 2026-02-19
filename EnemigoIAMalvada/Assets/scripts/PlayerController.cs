using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    #region General Variables

    [Header("Editor References")]
    [SerializeField] Transform camTransform; //Ref al transform de la camara//

    [Header("Movement Parameters")]
    [SerializeField] float speed = 10f;
    [SerializeField] float rotSpeed = 15f;

    [Header("Jump Parameters")]
    [SerializeField] float jumpForce = 8f; //Potencia de salto
    [SerializeField] Transform groundCheck; //Ref a la posicion desde la que se detecta el suelo
    [SerializeField] float groundCheckRadius = 0.2f; //Rango de deteccion del suelo
    [SerializeField] LayerMask groundLayer; //capa de deteccion del suelo

    //Variables de refencia propias o internas//
    Rigidbody playerRB; // Ref al rigibody, permite movimiento fisico
    Vector2 moveInput; // almacen del input de movimiento
    bool isGrounded; // determina si tocas el suelo (salto)
    #endregion

    private void Awake()
    {
        playerRB = GetComponent<Rigidbody>();
        if (camTransform == null) camTransform = Camera.main.transform;
        playerRB.freezeRotation = true; //Congelamos la rotaciondel rigibody
    }

    // Update is called once per frame
    void Update()
    {
        CheckIfGrounded();
    }

    private void FixedUpdate()
    {
        HandleMovement();
        HandleRotation();
    }

    void HandleMovement()
    {
        //Almacenar la direccion z + x e la camara
        Vector3 cameraForward = camTransform.forward; // Almacena la orientacion frontal de la camara
        Vector3 cameraRight = camTransform.right; // Almacena la orientacion lateralde la camara

        //Anular la orientacion en y antes del calculo de la orientacion de la camara aplicada al movimiento
        cameraForward.y = 0;
        cameraRight.y = 0;
        cameraForward.Normalize();//El calor del float es maximo 1, por lo que no afecta a la multriplicacion de velocidad
        cameraRight.Normalize();//El calor del float es maximo 1, por lo que no afecta a la multriplicacion de velocidad

        //Se calcula y almacena la direccion x/z teniendo en cuenta la camara por el input

        Vector3 moveDirection = (cameraForward * moveInput.y + cameraRight * moveInput.x).normalized;

        //Una vez tenemos las direccion mas el input se lo aplicamos al motor de aceleracion del rigibody
        //Todo esto sin afectar a eje Y porque de eso se encargara el salto
        playerRB.linearVelocity = new Vector3(moveDirection.x * speed, playerRB.linearVelocity.y, moveDirection.z * speed);
    }

    void HandleRotation()
    {
        // si no existe input, cerramos esta accion para ahorrar memoria
        if (moveInput == Vector2.zero) return;

        // hay que revisar la direccion de movimiento actual del rigibody

        Vector3 moveDirection = new Vector3(playerRB.linearVelocity.x, 0, playerRB.linearVelocity.z);
        if (moveDirection == Vector3.zero) return;

        //Almacenar la rotacion del personaje segun la direccion en la que se este moviendo
        Quaternion targetRotation = Quaternion.LookRotation(moveDirection);

        //se aplica el giro
        //suavizar el efecto de la rotacion mediante una interporlacion de angulos
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotSpeed * Time.fixedDeltaTime);
    }

    void CheckIfGrounded()
    {
        if (isGrounded = Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundLayer)) ;

        //elemento de visualizacion en editor OPCIONAL
    }

    void Jump()
    {
        if (isGrounded)
        {
            //reseteamos la aceleracion en Y para que no se solape la velocidad de salto con cada salto
            playerRB.linearVelocity = new Vector3(playerRB.linearVelocity.x, 0, playerRB.linearVelocity.z);
            //realizar el salto en si (modo impulso)
            playerRB.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }

    #region Input Methods
    public void OnMove(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (context.performed) Jump();
    }
    #endregion
}
