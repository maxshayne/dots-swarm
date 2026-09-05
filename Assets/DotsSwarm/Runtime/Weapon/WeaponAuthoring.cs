using Unity.Entities;
using UnityEngine;

namespace DotsSwarm.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerAuthoring))]
    public sealed class WeaponAuthoring : MonoBehaviour
    {
        [SerializeField]
        private ProjectileAuthoring projectilePrefab;

        [SerializeField, Min(Weapon.MinimumCooldown)]
        private float cooldown = Weapon.DefaultCooldown;

        [SerializeField, Min(0f)]
        private float range = Weapon.DefaultRange;

        [SerializeField, Min(0f)]
        private float projectileSpeed = Weapon.DefaultProjectileSpeed;

        [SerializeField, Min(0f)]
        private float projectileLifetime = Weapon.DefaultProjectileLifetime;

        private void OnValidate()
        {
            cooldown = Mathf.Max(Weapon.MinimumCooldown, cooldown);
            range = Mathf.Max(0f, range);
            projectileSpeed = Mathf.Max(0f, projectileSpeed);
            projectileLifetime = Mathf.Max(0f, projectileLifetime);
        }

        private sealed class WeaponBaker : Baker<WeaponAuthoring>
        {
            public override void Bake(WeaponAuthoring authoring)
            {
                if (authoring.projectilePrefab == null)
                {
                    return;
                }

                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, Weapon.Create(
                    GetEntity(authoring.projectilePrefab.gameObject, TransformUsageFlags.Dynamic),
                    authoring.cooldown,
                    authoring.range,
                    authoring.projectileSpeed,
                    authoring.projectileLifetime));
                AddComponent(entity, new WeaponState());
            }
        }
    }
}
