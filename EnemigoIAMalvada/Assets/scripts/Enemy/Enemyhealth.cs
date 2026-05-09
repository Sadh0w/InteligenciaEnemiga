using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Sistema de salud del enemigo.
/// Notifica al EnemyAIBase cuando recibe daño o muere.
///
/// Setup: añade este componente al mismo GameObject que EnemyAIBase.
/// Las balas deben llamar a TakeDamage() al impactar.
/// </summary>
public class EnemyHealth : MonoBehaviour
{
    [Header("Salud")]
    [SerializeField] float maxHealth = 100f;
    [SerializeField] float fleeThreshold = 30f;  // % de vida al que empieza a huir (0-100)

    [Header("Eventos")]
    public UnityEvent OnDeath;
    public UnityEvent<float> OnDamaged;  // pasa la vida actual

    float currentHealth;
    EnemyAIBase aiBase;
    bool hasFledThisLife; // evita activar la huida múltiples veces

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public float HealthPercent => currentHealth / maxHealth * 100f;
    public bool IsDead => currentHealth <= 0f;

    void Awake()
    {
        aiBase = GetComponent<EnemyAIBase>();
        currentHealth = maxHealth;
    }

    /// <summary>
    /// Aplica daño al enemigo. Llamar desde el script de la bala al impactar.
    /// </summary>
    public void TakeDamage(float amount)
    {
        if (IsDead) return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        OnDamaged?.Invoke(currentHealth);

        // Avisa a la IA del daño recibido
        aiBase?.OnDamageReceived();

        // Comprueba umbral de huida
        if (!hasFledThisLife && HealthPercent <= fleeThreshold)
        {
            hasFledThisLife = true;
            aiBase?.OnFleeThresholdReached();
        }

        if (currentHealth <= 0f)
            Die();
    }

    void Die()
    {
        OnDeath?.Invoke();
        // La IA se desactiva, el EnemyManager lo desregistra automáticamente via OnDisable
        gameObject.SetActive(false);
    }
}