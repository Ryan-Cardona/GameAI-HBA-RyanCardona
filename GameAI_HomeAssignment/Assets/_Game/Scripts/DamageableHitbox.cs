using UnityEngine;

public class DamageableHitbox : MonoBehaviour
{
    [Tooltip("If not set, will search in parents.")]
    [SerializeField] private Health health;

    private void Awake()
    {
        if (health == null)
            health = GetComponentInParent<Health>();

        if (health == null)
            Debug.LogError($"DamageableHitbox on {name} could not find a Health component in parents.");
    }

    public void ApplyDamage(float amount)
    {
        if (health == null) return;
        health.TakeDamage(amount);
    }

    public Health GetHealth() => health;
}