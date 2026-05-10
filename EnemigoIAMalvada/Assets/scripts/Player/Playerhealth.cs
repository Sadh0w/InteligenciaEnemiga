using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Salud del jugador.
/// De momento muere de un disparo y reinicia el nivel.
/// Las balas enemigas deben llamar a TakeDamage() al impactar.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [Header("Configuración")]
    [SerializeField] float reloadDelay = 1f; // segundos antes de reiniciar

    bool isDead;

    public bool IsDead => isDead;

    public void TakeDamage()
    {
        if (isDead) return;

        isDead = true;
        Debug.Log("[PlayerHealth] Jugador eliminado. Reiniciando nivel...");

        // Desactiva movimiento y disparo
        PlayerController pc = GetComponent<PlayerController>();
        PlayerWeaponHandler ph = GetComponent<PlayerWeaponHandler>();
        if (pc != null) pc.enabled = false;
        if (ph != null) ph.enabled = false;

        Invoke(nameof(ReloadScene), reloadDelay);
    }

    void ReloadScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}