using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Salud del enemigo con feedback visual (flash rojo al recibir daño).
/// Añade este componente al mismo GameObject que EnemyAIBase.
/// </summary>
public class EnemyHealth : MonoBehaviour
{
    [Header("Salud")]
    [SerializeField] float maxHealth = 100f;
    [SerializeField] float fleeThreshold = 30f;

    [Header("Feedback visual")]
    [SerializeField] Color hitColor = Color.red;
    [SerializeField] float flashDuration = 0.2f;

    [Header("Debug")]
    [SerializeField] float currentHealthDebug;

    [Header("Eventos")]
    public UnityEvent OnDeath;
    public UnityEvent<float> OnDamaged;

    float currentHealth;
    EnemyAIBase aiBase;
    bool hasFledThisLife;

    // Renderers del modelo (busca en hijos para modelos con SkinnedMeshRenderer)
    Renderer[] renderers;
    Color[] originalColors;
    bool isFlashing;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public float HealthPercent => currentHealth / maxHealth * 100f;
    public bool IsDead => currentHealth <= 0f;

    void Awake()
    {
        aiBase = GetComponent<EnemyAIBase>();
        currentHealth = maxHealth;
        currentHealthDebug = currentHealth;

        // Recoge todos los renderers del modelo (incluye hijos)
        renderers = GetComponentsInChildren<Renderer>();

        // Guarda los colores originales de cada material
        originalColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            // Usa sharedMaterial para no crear instancias innecesarias
            // pero al flashear usaremos .material para no afectar al prefab
            originalColors[i] = renderers[i].sharedMaterial != null
                ? renderers[i].sharedMaterial.color
                : Color.white;
        }
    }

    public void TakeDamage(float amount)
    {
        if (IsDead) return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        currentHealthDebug = currentHealth;

        Debug.Log($"[Health] {gameObject.name}: {currentHealth}/{maxHealth} (-{amount})");

        OnDamaged?.Invoke(currentHealth);
        aiBase?.OnDamageReceived();

        // Flash visual
        if (!isFlashing)
            StartCoroutine(FlashRed());

        if (!hasFledThisLife && HealthPercent <= fleeThreshold)
        {
            hasFledThisLife = true;
            aiBase?.OnFleeThresholdReached();
        }

        if (currentHealth <= 0f)
            Die();
    }

    IEnumerator FlashRed()
    {
        isFlashing = true;

        // Cambia al color de impacto
        SetColor(hitColor);

        yield return new WaitForSeconds(flashDuration);

        // Restaura colores originales
        RestoreColors();

        isFlashing = false;
    }

    void SetColor(Color color)
    {
        foreach (var r in renderers)
        {
            if (r == null) continue;
            // .material crea una instancia local, no modifica el asset original
            r.material.color = color;
        }
    }

    void RestoreColors()
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].material.color = originalColors[i];
        }
    }

    void Die()
    {
        Debug.Log($"[Health] {gameObject.name} eliminado.");
        OnDeath?.Invoke();
        gameObject.SetActive(false);
    }
}