using UnityEngine;

public class DebugDamageSelf : MonoBehaviour
{
    [SerializeField] private float damagePerPress = 25f;

    private Health health;

    private void Awake()
    {
        health = GetComponent<Health>();
        if (health == null)
            Debug.LogError("DebugDamageSelf requires a Health component on the same GameObject.");
    }

    private void Update()
    {
        if (health == null) return;

        if (Input.GetKeyDown(KeyCode.K))
        {
            health.TakeDamage(damagePerPress);
            Debug.Log($"{name} took {damagePerPress} damage. HP now: {health.CurrentHealth}/{health.MaxHealth}");
        }
    }
}