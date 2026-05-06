//using UnityEngine;
//using UnityEngine.InputSystem;

//public class PlayerController : MonoBehaviour
//{
//    #region General Variables

//    [Header("Editor References")]
//    [SerializeField] Transform camTransform; //Ref al transform de la camara//

//    [Header("Movement Parameters")]
//    [SerializeField] float speed = 10f;
//    [SerializeField] float rotSpeed = 15f;

//    [Header("Jump Parameters")]
//    [SerializeField] float jumpForce = 8f; //Potencia de salto
//    [SerializeField] Transform groundCheck; //Ref a la posicion desde la que se detecta el suelo
//    [SerializeField] float groundCheckRadius = 0.2f; //Rango de deteccion del suelo
//    [SerializeField] LayerMask groundLayer; //capa de deteccion del suelo

//    //Variables de refencia propias o internas//
//    Rigidbody playerRB; // Ref al rigibody, permite movimiento fisico
//    Vector2 moveInput; // almacen del input de movimiento
//    bool isGrounded; // determina si tocas el suelo (salto)
//    #endregion

//    private void Awake()
//    {
//        playerRB = GetComponent<Rigidbody>();
//        if (camTransform == null) camTransform = Camera.main.transform;
//        playerRB.freezeRotation = true; //Congelamos la rotaciondel rigibody
//    }

//    // Update is called once per frame
//    void Update()
//    {
//        CheckIfGrounded();
//    }

//    private void FixedUpdate()
//    {
//        HandleMovement();
//        HandleRotation();
//    }

//    void HandleMovement()
//    {
//        Vector3 cameraForward = camTransform.forward;
//        Vector3 cameraRight = camTransform.right;

//        cameraForward.y = 0;
//        cameraRight.y = 0;
//        cameraForward.Normalize();
//        cameraRight.Normalize();

//        Vector3 moveDirection = (cameraForward * moveInput.y + cameraRight * moveInput.x).normalized;

//        playerRB.linearVelocity = new Vector3(
//            moveDirection.x * speed,
//            playerRB.linearVelocity.y,
//            moveDirection.z * speed
//        );
//    }

//    void HandleRotation()
//    {
//        if (moveInput == Vector2.zero) return;

//        Vector3 moveDirection = new Vector3(playerRB.linearVelocity.x, 0, playerRB.linearVelocity.z);
//        if (moveDirection == Vector3.zero) return;

//        Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
//        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotSpeed * Time.fixedDeltaTime);
//    }

//    void CheckIfGrounded()
//    {
//        isGrounded = Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundLayer);

//        if (isGrounded)
//            Debug.Log("TOCANDO SUELO");
//    }


//    void Jump()
//    {
//        if (isGrounded)
//        {
//            playerRB.linearVelocity = new Vector3(playerRB.linearVelocity.x, 0, playerRB.linearVelocity.z);
//            playerRB.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
//        }
//    }



//    private void OnDrawGizmosSelected()
//    {
//        if (groundCheck != null)
//        {
//            Gizmos.color = Color.yellow;
//            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
//        }
//    }

//    #region Input Methods
//    public void OnMove(InputAction.CallbackContext context)
//    {
//        moveInput = context.ReadValue<Vector2>();
//    }

//    public void OnJump(InputAction.CallbackContext context)
//    {
//        if (context.performed) Jump();
//    }
//    #endregion
//}

using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    #region General Variables

    [Header("Editor References")]
    [SerializeField] Transform camTransform;

    [Header("Movement Parameters")]
    [SerializeField] float speed = 10f;
    [SerializeField] float rotSpeed = 15f;

    [Header("Jump Parameters")]
    [SerializeField] float jumpForce = 8f;
    [SerializeField] Transform groundCheck;
    [SerializeField] float groundCheckRadius = 0.2f;
    [SerializeField] LayerMask groundLayer;

    Rigidbody playerRB;
    Vector2 moveInput;
    bool isGrounded;
    Camera cam; // añadido para el raycast del cursor

    #endregion

    private void Awake()
    {
        playerRB = GetComponent<Rigidbody>();
        cam = Camera.main;
        if (camTransform == null) camTransform = cam.transform;
        playerRB.freezeRotation = true;
    }

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
        Vector3 cameraForward = camTransform.up;   // ← era camTransform.forward
        Vector3 cameraRight = camTransform.right; // este no cambia

        cameraForward.y = 0;
        cameraRight.y = 0;
        cameraForward.Normalize();
        cameraRight.Normalize();

        Vector3 moveDirection = (cameraForward * moveInput.y + cameraRight * moveInput.x).normalized;

        playerRB.linearVelocity = new Vector3(
            moveDirection.x * speed,
            playerRB.linearVelocity.y,
            moveDirection.z * speed
        );
    }

    
    void HandleRotation()
    {
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane groundPlane = new Plane(Vector3.up, transform.position);

        if (groundPlane.Raycast(ray, out float enter))
        {
            Vector3 worldPoint = ray.GetPoint(enter);
            Vector3 dir = worldPoint - transform.position;
            dir.y = 0;

            if (dir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, targetRotation, rotSpeed * Time.fixedDeltaTime);
            }
        }
    }


    void CheckIfGrounded()
    {
        isGrounded = Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundLayer);
    }

    void Jump()
    {
        if (isGrounded)
        {
            playerRB.linearVelocity = new Vector3(playerRB.linearVelocity.x, 0, playerRB.linearVelocity.z);
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