using UnityEngine;
using UnityEngine.Events;

public class EnemyHealth : MonoBehaviour
{
    [Header("Salud")]
    [SerializeField] float maxHealth = 100f;
    [SerializeField] float fleeThreshold = 30f;

    [Header("Debug — visible durante Play")]
    [SerializeField] float currentHealthDebug; // solo para ver en Inspector

    [Header("Eventos")]
    public UnityEvent OnDeath;
    public UnityEvent<float> OnDamaged;

    float currentHealth;
    EnemyAIBase aiBase;
    bool hasFledThisLife;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public float HealthPercent => currentHealth / maxHealth * 100f;
    public bool IsDead => currentHealth <= 0f;

    void Awake()
    {
        aiBase = GetComponent<EnemyAIBase>();
        currentHealth = maxHealth;
        currentHealthDebug = currentHealth;
    }

    public void TakeDamage(float amount)
    {
        if (IsDead) return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        currentHealthDebug = currentHealth; // actualiza el debug visible

        Debug.Log($"[Health] {gameObject.name}: {currentHealth}/{maxHealth} (-{amount})");

        OnDamaged?.Invoke(currentHealth);
        aiBase?.OnDamageReceived();

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
        Debug.Log($"[Health] {gameObject.name} eliminado.");
        OnDeath?.Invoke();
        gameObject.SetActive(false);
    }
}