using UnityEngine;

public class DebugShoot : MonoBehaviour
{
    private Weapon weapon;

    private void Awake()
    {
        weapon = GetComponent<Weapon>();
        if (weapon == null)
            Debug.LogError("DebugShoot requires a Weapon component on the same GameObject.");
    }

    private void Update()
    {
        if (weapon == null) return;

        if (Input.GetKeyDown(KeyCode.J))
        {
            bool fired = weapon.TryFire();
            if (fired)
                Debug.Log($"{name} fired. Ammo: {weapon.Ammo}/{weapon.MaxAmmo}");
            else
                Debug.Log($"{name} could not fire (cooldown or no ammo). Ammo: {weapon.Ammo}/{weapon.MaxAmmo}");
        }
    }
}