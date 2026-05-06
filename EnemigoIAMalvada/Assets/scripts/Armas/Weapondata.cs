using UnityEngine;

/// <summary>
/// ScriptableObject con las estadísticas de un tipo de arma.
/// Crea uno desde Assets → Create → Weapons → WeaponData.
/// Cada arma en el mundo referencia su WeaponData correspondiente.
/// </summary>
[CreateAssetMenu(fileName = "WeaponData", menuName = "Weapons/WeaponData")]
public class WeaponData : ScriptableObject
{
    [Header("Identificación")]
    public string weaponName = "Pistola";

    [Header("Disparo")]
    public GameObject projectilePrefab;
    public float shootForce = 25f;
    public float fireRate = 0.2f;  // segundos entre disparos
    public int totalAmmo = 12;    // balas con las que viene el arma, sin recarga extra

    [Header("Recogida")]
    public float pickupRange = 2f;         // distancia máxima para poder cogerla con F

    [Header("Físicas al soltar")]
    public float throwForce = 4f;          // fuerza con la que sale volando al soltarla
}