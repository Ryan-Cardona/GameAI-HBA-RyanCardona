using System;
using UnityEngine;

public class Health : MonoBehaviour
{
    [Header("Health Settings")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private bool destroyOnDeath = false;

    public float MaxHealth => maxHealth;
    public float CurrentHealth { get; private set; }
    public bool IsDead { get; private set; }

    // For UI, debugging, scoring
    public event Action<Health, float> OnDamaged;   // (who, damageAmount)
    public event Action<Health> OnDied;             // (who)

    private void Awake()
    {
        CurrentHealth = maxHealth;
        IsDead = false;
    }

    public void ResetHealth()
    {
        CurrentHealth = maxHealth;
        IsDead = false;
    }

    public void Heal(float amount)
    {
        if (IsDead) return;
        if (amount <= 0f) return;

        CurrentHealth = Mathf.Min(CurrentHealth + amount, maxHealth);
    }

    public void TakeDamage(float amount)
    {
        if (IsDead) return;
        if (amount <= 0f) return;

        CurrentHealth -= amount;
        OnDamaged?.Invoke(this, amount);

        if (CurrentHealth <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        if (IsDead) return;

        IsDead = true;
        CurrentHealth = 0f;

        OnDied?.Invoke(this);

        // ADDED: If this object is a Gladiator ML-Agent, punish its death.
        GladiatorAgent ga = GetComponent<GladiatorAgent>();
        if (ga != null)
        {
            ga.PunishDeath();
        }

        // Disable the agent object.
        // Later we implement respawn or ragdoll or pooling.
        if (destroyOnDeath)
        {
            Destroy(gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
}