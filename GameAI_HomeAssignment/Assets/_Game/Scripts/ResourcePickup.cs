using System.Collections;
using UnityEngine;

public enum ResourceType
{
    Health,
    Ammo
}

[RequireComponent(typeof(Collider))]
public class ResourcePickup : MonoBehaviour
{
    [Header("Type")]
    [SerializeField] private ResourceType resourceType = ResourceType.Health;

    [Header("Health Settings")]
    [SerializeField] private float healAmount = 35f;

    [Header("Ammo Settings")]
    [SerializeField] private int ammoAmount = 15;

    [Header("Respawn")]
    [SerializeField] private bool respawns = true;
    [SerializeField] private float respawnTime = 10f;

    [Header("Pickup Animation")]
    [SerializeField] private bool animate = true;
    [SerializeField] private float bobHeight = 0.25f;
    [SerializeField] private float bobSpeed = 2.0f;
    [SerializeField] private float spinSpeedDegreesPerSecond = 45f;

    [Header("Audio")]
    [Tooltip("Sound that plays when the pickup is successfully consumed.")]
    [SerializeField] private AudioClip pickupSfx;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.9f;

    [Header("Debug")]
    [SerializeField] private bool logPickup = true;

    private Collider col;
    private Renderer[] renderers;

    private Vector3 startLocalPos;
    private bool isActive = true;

    private void Awake()
    {
        col = GetComponent<Collider>();
        col.isTrigger = true;

        renderers = GetComponentsInChildren<Renderer>();
        startLocalPos = transform.localPosition;
    }

    private void Update()
    {
        if (!animate || !isActive) return;

        // Bob (sin wave)
        float bobOffset = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.localPosition = startLocalPos + new Vector3(0f, bobOffset, 0f);

        // Spin (Y axis)
        transform.Rotate(0f, spinSpeedDegreesPerSecond * Time.deltaTime, 0f, Space.Self);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isActive) return;

        Health health = other.GetComponentInParent<Health>();
        Weapon weapon = other.GetComponentInParent<Weapon>();

        if (health == null) return; // not an agent
        if (health.IsDead) return;

        bool consumed = false;

        switch (resourceType)
        {
            case ResourceType.Health:
            {
                float before = health.CurrentHealth;
                health.Heal(healAmount);
                consumed = health.CurrentHealth > before; // only consume if it actually healed

                if (consumed && logPickup)
                    Debug.Log($"{other.transform.root.name} picked up HEALTH (+{healAmount}).");
                break;
            }

            case ResourceType.Ammo:
            {
                if (weapon == null) return;
                int beforeAmmo = weapon.Ammo;
                weapon.AddAmmo(ammoAmount);
                consumed = weapon.Ammo > beforeAmmo; // only consume if it actually added ammo

                if (consumed && logPickup)
                    Debug.Log($"{other.transform.root.name} picked up AMMO (+{ammoAmount}).");
                break;
            }
        }

        if (!consumed) return;

        PlaySfx(pickupSfx);

        if (respawns)
            StartCoroutine(RespawnRoutine());
        else
            Destroy(gameObject);
    }

    private IEnumerator RespawnRoutine()
    {
        SetActiveVisual(false);

        yield return new WaitForSeconds(respawnTime);

        // Reset bobbing baseline so it doesn't "jump" after respawn
        startLocalPos = transform.localPosition;

        SetActiveVisual(true);
    }

    private void SetActiveVisual(bool active)
    {
        isActive = active;

        if (col != null) col.enabled = active;

        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = active;
        }
    }

    private void PlaySfx(AudioClip clip)
    {
        if (clip == null) return;

        // Plays even if this pickup disables immediately (important!)
        AudioSource.PlayClipAtPoint(clip, transform.position, sfxVolume);
    }

    public ResourceType GetResourceType() => resourceType;
}
