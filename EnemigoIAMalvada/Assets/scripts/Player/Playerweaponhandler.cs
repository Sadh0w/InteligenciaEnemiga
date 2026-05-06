using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gestiona el sistema de armas del jugador.
/// Reemplaza al PlayerShooter anterior.
///
/// Setup:
///   1. Añade este script al jugador (quita PlayerShooter si lo tenías).
///   2. Crea un empty child llamado "WeaponHoldPoint" donde se adjuntará el arma visualmente.
///      Colócalo donde quieras que aparezca el arma (delante del jugador, a la altura de la mano...).
///   3. En el Input Actions asset añade:
///        - Acción "Fire"     → Left Button [Mouse]  → conectar a OnFire
///        - Acción "Interact" → tecla F              → conectar a OnInteract
///   4. Coloca prefabs de arma en la escena con el componente Weapon + WeaponData asignado.
/// </summary>
public class PlayerWeaponHandler : MonoBehaviour
{
    #region Variables

    [Header("Referencias")]
    // Punto donde se adjunta el arma visualmente (empty child del jugador)
    [SerializeField] Transform weaponHoldPoint;

    [Header("Detección de armas")]
    [SerializeField] float pickupCheckRadius = 2f; // radio de búsqueda de armas cercanas
    [SerializeField] LayerMask weaponLayer;            // layer asignada a los GameObjects arma

    [Header("Apuntado")]
    [SerializeField] LayerMask groundLayer;

    // Estado
    Weapon currentWeapon;   // arma que lleva el jugador ahora mismo
    float nextFireTime;
    Camera cam;

    #endregion

    #region Propiedades públicas (para UI)

    public Weapon CurrentWeapon => currentWeapon;
    public bool HasWeapon => currentWeapon != null;
    public int CurrentAmmo => currentWeapon != null ? currentWeapon.CurrentAmmo : 0;
    public int MaxAmmo => currentWeapon != null ? currentWeapon.Data.totalAmmo : 0;

    #endregion

    #region Lifecycle

    void Awake()
    {
        cam = Camera.main;

        // Busca WeaponHoldPoint automáticamente si no está asignado
        if (weaponHoldPoint == null)
        {
            Transform found = transform.Find("WeaponHoldPoint");
            if (found != null) weaponHoldPoint = found;
            else Debug.LogWarning("PlayerWeaponHandler: asigna WeaponHoldPoint en el Inspector.");
        }
    }

    #endregion

    #region Input Callbacks

    /// <summary>
    /// Click izquierdo → dispara el arma actual.
    /// Conectar a la acción "Fire" del PlayerInput.
    /// </summary>
    public void OnFire(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        TryShoot();
    }

    /// <summary>
    /// Tecla F → coger arma cercana o soltar la actual.
    /// Conectar a la acción "Interact" del PlayerInput.
    /// </summary>
    public void OnInteract(InputAction.CallbackContext context)
    {
        if (!context.performed) return;

        Weapon nearbyWeapon = FindNearestWeapon();

        if (nearbyWeapon != null)
        {
            // Hay un arma cerca: suelta la actual (si la hay) y coge la nueva
            if (currentWeapon != null)
                DropCurrentWeapon();

            PickupWeapon(nearbyWeapon);
        }
        else if (currentWeapon != null)
        {
            // No hay arma cerca pero llevamos una: la soltamos
            DropCurrentWeapon();
        }
        else
        {
            Debug.Log("[WeaponHandler] No hay armas cerca.");
        }
    }

    #endregion

    #region Pickup / Drop

    void PickupWeapon(Weapon weapon)
    {
        currentWeapon = weapon;
        currentWeapon.Pickup(weaponHoldPoint);
    }

    void DropCurrentWeapon()
    {
        if (currentWeapon == null) return;

        // La lanza en la dirección que mira el jugador
        currentWeapon.Drop(transform.forward);
        currentWeapon = null;
    }

    /// <summary>
    /// Busca el arma más cercana dentro del radio de recogida.
    /// Solo detecta armas que no estén ya cogidas.
    /// </summary>
    Weapon FindNearestWeapon()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, pickupCheckRadius, weaponLayer);

        Weapon nearest = null;
        float minDist = float.MaxValue;

        foreach (var hit in hits)
        {
            Weapon w = hit.GetComponent<Weapon>();
            if (w == null || w.IsPickedUp) continue;

            float dist = Vector3.Distance(transform.position, w.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = w;
            }
        }

        return nearest;
    }

    #endregion

    #region Shoot

    void TryShoot()
    {
        if (!HasWeapon)
        {
            Debug.Log("[WeaponHandler] Sin arma.");
            return;
        }

        if (currentWeapon.IsEmpty)
        {
            Debug.Log($"[WeaponHandler] {currentWeapon.Data.weaponName} vacía. Pulsa F para soltarla.");
            return;
        }

        if (Time.time < nextFireTime) return;

        WeaponData data = currentWeapon.Data;

        if (data.projectilePrefab == null)
        {
            Debug.LogWarning("[WeaponHandler] El WeaponData no tiene projectilePrefab asignado.");
            return;
        }

        // Dirección hacia el cursor
        Vector3 aimPoint = GetCursorWorldPosition();
        Vector3 dir = aimPoint - weaponHoldPoint.position;
        dir.y = 0;
        dir.Normalize();

        if (dir == Vector3.zero) return;

        // Instancia la bala desde el WeaponHoldPoint
        Quaternion shootRot = Quaternion.LookRotation(dir);
        GameObject bullet = Object.Instantiate(
            data.projectilePrefab, weaponHoldPoint.position, shootRot);

        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        if (rb != null)
            rb.AddForce(dir * data.shootForce, ForceMode.Impulse);

        currentWeapon.ConsumeAmmo();
        nextFireTime = Time.time + data.fireRate;
    }

    #endregion

    #region Helpers

    Vector3 GetCursorWorldPosition()
    {
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane groundPlane = new Plane(Vector3.up, transform.position);

        if (groundPlane.Raycast(ray, out float enter))
            return ray.GetPoint(enter);

        return transform.position + transform.forward * 5f;
    }

    #endregion

    #region Gizmos

    void OnDrawGizmosSelected()
    {
        // Radio de búsqueda de armas
        Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, pickupCheckRadius);
    }

    #endregion
}