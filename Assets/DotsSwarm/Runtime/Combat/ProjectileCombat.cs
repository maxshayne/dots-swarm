using Unity.Entities;
using Unity.Mathematics;

namespace DotsSwarm.Gameplay
{
    // Separate from flight state so firing preserves the prefab's combat settings.
    public struct ProjectileCombat : IComponentData
    {
        public const int DefaultDamage = 1;
        public const float DefaultRadius = 0.125f;

        public int Damage;
        public float Radius;
        public Entity HitEntity;

        public static ProjectileCombat Create(int damage, float radius)
        {
            return new ProjectileCombat
            {
                Damage = math.max(0, damage),
                Radius = math.max(0f, radius)
            };
        }
    }
}
