using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Disparo del jugador con sistema de cargador y recarga.
/// - Click izquierdo: dispara si hay balas y no está recargando.
/// - R (acción Reload del Input): recarga manualmente.
/// - Al vaciar el cargador recarga automáticamente.
/// </summary>
public class PlayerShooter : MonoBehaviour
{
    #region Variables

    [Header("Disparo")]
    [SerializeField] GameObject projectile;
    [SerializeField] Transform shootPoint;
    [SerializeField] float shootForce = 25f;
    [SerializeField] float fireRate = 0.2f;   // segundos entre disparos

    [Header("Cargador")]
    [SerializeField] int magazineSize = 12;         // balas por cargador
    [SerializeField] float reloadTime = 1.8f;       // segundos que tarda recargar

    [Header("Apuntado")]
    [SerializeField] LayerMask groundLayer;

    // Internos
    Camera cam;
    float nextFireTime;
    int currentAmmo;
    bool isReloading;

    #endregion

    #region Lifecycle

    void Awake()
    {
        cam = Camera.main;

        if (shootPoint == null)
        {
            Transform found = transform.Find("ShootPoint");
            if (found != null) shootPoint = found;
            else Debug.LogWarning("PlayerShooter: asigna ShootPoint en el Inspector.");
        }
    }

    void Start()
    {
        currentAmmo = magazineSize;
        LogAmmo();
    }

    #endregion

    #region Input Callbacks

    public void OnFire(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        TryShoot();
    }

    public void OnReload(InputAction.CallbackContext context)
    {
        if (!context.performed) return;

        // Recarga manual solo si no estás ya recargando y te faltan balas
        if (!isReloading && currentAmmo < magazineSize)
            StartCoroutine(Reload());
    }

    #endregion

    #region Shoot

    void TryShoot()
    {
        if (isReloading)
        {
            Debug.Log("Recargando...");
            return;
        }

        if (currentAmmo <= 0)
        {
            Debug.Log("Cargador vacío — recargando automáticamente.");
            StartCoroutine(Reload());
            return;
        }

        if (Time.time < nextFireTime) return;
        if (projectile == null || shootPoint == null) return;

        Vector3 aimPoint = GetCursorWorldPosition();
        Vector3 dir = aimPoint - shootPoint.position;
        dir.y = 0;
        dir.Normalize();

        if (dir == Vector3.zero) return;

        Quaternion shootRot = Quaternion.LookRotation(dir);
        GameObject bullet = Instantiate(projectile, shootPoint.position, shootRot);

        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        if (rb != null)
            rb.AddForce(dir * shootForce, ForceMode.Impulse);

        currentAmmo--;
        nextFireTime = Time.time + fireRate;

        LogAmmo();

        // Recarga automática al vaciar el cargador
        if (currentAmmo <= 0)
            StartCoroutine(Reload());
    }

    #endregion

    #region Reload

    IEnumerator Reload()
    {
        isReloading = true;
        Debug.Log($"Recargando... ({reloadTime}s)");

        yield return new WaitForSeconds(reloadTime);

        currentAmmo = magazineSize;
        isReloading = false;
        LogAmmo();
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

    void LogAmmo() => Debug.Log($"Balas: {currentAmmo} / {magazineSize}");

    // Propiedades públicas para cuando añadas UI
    public int CurrentAmmo => currentAmmo;
    public int MagazineSize => magazineSize;
    public bool IsReloading => isReloading;

    #endregion

    #region Gizmos

    void OnDrawGizmosSelected()
    {
        if (shootPoint == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(shootPoint.position, shootPoint.position + transform.forward * 3f);
        Gizmos.DrawWireSphere(shootPoint.position, 0.1f);
    }

    #endregion
}