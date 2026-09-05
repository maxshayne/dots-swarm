using Unity.Entities;
using Unity.Mathematics;

namespace DotsSwarm.Gameplay
{
    public struct Weapon : IComponentData
    {
        public const float DefaultCooldown = 0.2f;
        public const float MinimumCooldown = 0.01f;
        public const float DefaultRange = 20f;
        public const float DefaultProjectileSpeed = 30f;
        public const float DefaultProjectileLifetime = 2f;

        public Entity ProjectilePrefab;
        public float Cooldown;
        public float Range;
        public float ProjectileSpeed;
        public float ProjectileLifetime;

        public static Weapon Create(
            Entity projectilePrefab,
            float cooldown,
            float range,
            float projectileSpeed,
            float projectileLifetime)
        {
            return new Weapon
            {
                ProjectilePrefab = projectilePrefab,
                Cooldown = math.max(MinimumCooldown, cooldown),
                Range = math.max(0f, range),
                ProjectileSpeed = math.max(0f, projectileSpeed),
                ProjectileLifetime = math.max(0f, projectileLifetime)
            };
        }
    }

    public struct WeaponState : IComponentData
    {
        public float CooldownRemaining;
    }
}
