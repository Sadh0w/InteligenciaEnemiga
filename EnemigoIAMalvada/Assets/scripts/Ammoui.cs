using TMPro;
using UnityEngine;

/// <summary>
/// Actualiza el texto de munición en el canvas.
/// Muestra "12 / 12" cuando hay arma, "- / -" cuando no.
/// 
/// Setup:
///   - Asigna este script al GameObject del canvas o a un empty hijo.
///   - Asigna el TextMeshProUGUI del contador en 'ammoText'.
///   - Asigna el PlayerWeaponHandler del jugador en 'weaponHandler'.
/// </summary>
public class AmmoUI : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] TMP_Text ammoText;
    [SerializeField] PlayerWeaponHandler weaponHandler;

    void Awake()
    {
        // Busca el PlayerWeaponHandler automáticamente si no está asignado
        if (weaponHandler == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                weaponHandler = player.GetComponent<PlayerWeaponHandler>();
            else
                Debug.LogWarning("AmmoUI: no se encontró ningún Player con PlayerWeaponHandler.");
        }
    }

    void Update()
    {
        if (ammoText == null || weaponHandler == null) return;

        if (weaponHandler.HasWeapon)
            ammoText.text = $"{weaponHandler.CurrentAmmo} / {weaponHandler.MaxAmmo}";
        else
            ammoText.text = "- / -";
    }
}