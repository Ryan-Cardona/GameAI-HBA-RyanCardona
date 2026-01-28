using UnityEngine;

public class Weapon : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform muzzle; // where the ray comes from (use Eyes)
    [SerializeField] private LayerMask hitLayers = ~0;

    [Header("Weapon Stats")]
    [SerializeField] private float damage = 20f;
    [SerializeField] private float range = 25f;
    [SerializeField] private float fireCooldown = 0.35f;

    [Header("Ammo")]
    [SerializeField] private int maxAmmo = 30;
    [SerializeField] private int ammo = 30;
    [SerializeField] private bool infiniteAmmo = false;

    [Header("Debug")]
    [SerializeField] private bool logHits = true;

    public int Ammo => ammo;
    public int MaxAmmo => maxAmmo;
    public bool CanFire => Time.time >= nextFireTime && (infiniteAmmo || ammo > 0);

    private float nextFireTime;

    private void Awake()
    {
        if (muzzle == null)
        {
            // Try to find Eyes for convenience
            Transform eyes = transform.Find("Eyes");
            if (eyes != null) muzzle = eyes;
        }

        if (muzzle == null)
            Debug.LogError($"Weapon on {name} needs a Muzzle/Eyes transform assigned.");

        ammo = Mathf.Clamp(ammo, 0, maxAmmo);
    }

    public void RefillAmmoToMax() => ammo = maxAmmo;

    public void AddAmmo(int amount)
    {
        if (infiniteAmmo) return;
        ammo = Mathf.Clamp(ammo + amount, 0, maxAmmo);
    }

    public bool TryFire()
    {
        if (!CanFire) return false;

        nextFireTime = Time.time + fireCooldown;

        if (!infiniteAmmo)
            ammo--;

        FireRaycast();
        return true;
    }

    private void FireRaycast()
    {
        if (muzzle == null) return;

        Vector3 origin = muzzle.position;
        Vector3 dir = muzzle.forward;

        Debug.DrawRay(origin, dir * range, Color.red, 0.25f);

        // RaycastAll so we can ignore hitting ourselves first.
        RaycastHit[] hits = Physics.RaycastAll(origin, dir, range, hitLayers, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            if (logHits) Debug.Log($"{name} shot and hit nothing.");
            return;
        }

        // Sort by distance so we process nearest hits first
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];

            // Ignore our own colliders
            if (hit.transform.root == transform.root)
                continue;

            if (logHits) Debug.Log($"{name} shot hit: {hit.collider.name} (root: {hit.transform.root.name})");

            DamageableHitbox hitbox = hit.collider.GetComponentInParent<DamageableHitbox>();
            if (hitbox != null)
            {
                hitbox.ApplyDamage(damage);
            }

            // Stop at the first non-self thing we hit (wall or enemy)
            return;
        }

        if (logHits) Debug.Log($"{name} shot only hit itself (ignored).");
    }
}
