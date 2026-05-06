using UnityEngine;

/// <summary>
/// Componente del GameObject arma en el mundo.
/// Gestiona su estado (en suelo o en mano), munición,
/// y las transiciones entre ambos estados.
/// 
/// Setup del prefab:
///   - MeshRenderer + MeshFilter (la geometría del arma)
///   - Rigidbody (se desactiva al cogerla)
///   - Collider normal para físicas en suelo
///   - Collider adicional con Is Trigger = true para detección de proximidad
///   - Este script
///   - Referencia a un WeaponData en el Inspector
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Weapon : MonoBehaviour
{
    #region Variables

    [Header("Datos del arma")]
    [SerializeField] WeaponData data;

    // Estado
    bool isPickedUp;
    int currentAmmo;

    // Componentes
    Rigidbody rb;
    Collider[] colliders;  // todos los colliders del arma

    #endregion

    #region Propiedades públicas

    public WeaponData Data => data;
    public int CurrentAmmo => currentAmmo;
    public bool IsPickedUp => isPickedUp;
    public bool IsEmpty => currentAmmo <= 0;

    #endregion

    #region Lifecycle

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        colliders = GetComponents<Collider>();
        currentAmmo = data != null ? data.totalAmmo : 0;
    }

    #endregion

    #region Pickup / Drop

    /// <summary>
    /// El jugador recoge el arma. Se adjunta al holdPoint como hijo.
    /// </summary>
    public void Pickup(Transform holdPoint)
    {
        isPickedUp = true;

        // Desactiva físicas para que el arma no interfiera
        rb.isKinematic = true;
        rb.linearVelocity = Vector3.zero;

        // Desactiva colliders físicos (los trigger los dejamos activos
        // solo si los necesitas para otro propósito; aquí los desactivamos todos)
        SetCollidersEnabled(false);

        // Adjunta el arma al punto de sujeción del jugador
        transform.SetParent(holdPoint);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        Debug.Log($"[Weapon] {data.weaponName} recogida — {currentAmmo}/{data.totalAmmo} balas");
    }

    /// <summary>
    /// El jugador suelta el arma. Vuelve al mundo con física y una pequeña fuerza.
    /// </summary>
    public void Drop(Vector3 throwDirection)
    {
        isPickedUp = false;

        // Despareja del jugador
        transform.SetParent(null);

        // Reactiva físicas
        rb.isKinematic = false;
        SetCollidersEnabled(true);

        // Pequeño impulso en la dirección que mira el jugador
        rb.AddForce(throwDirection * data.throwForce, ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * 2f, ForceMode.Impulse);

        Debug.Log($"[Weapon] {data.weaponName} soltada — {currentAmmo}/{data.totalAmmo} balas restantes");
    }

    #endregion

    #region Ammo

    /// <summary>
    /// Consume una bala. Devuelve true si el disparo es válido.
    /// </summary>
    public bool ConsumeAmmo()
    {
        if (currentAmmo <= 0) return false;
        currentAmmo--;
        Debug.Log($"[Weapon] {data.weaponName} — {currentAmmo}/{data.totalAmmo}");
        return true;
    }

    #endregion

    #region Helpers

    void SetCollidersEnabled(bool enabled)
    {
        foreach (var col in colliders)
            col.enabled = enabled;
    }

    #endregion

    #region Gizmos

    void OnDrawGizmosSelected()
    {
        if (data == null) return;
        Gizmos.color = isPickedUp
            ? new Color(0f, 1f, 0f, 0.3f)
            : new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, data.pickupRange);
    }

    #endregion
}