using Unity.Entities;
using Unity.Mathematics;

namespace DotsSwarm.Gameplay
{
    public struct Projectile : IComponentData
    {
        public float3 Velocity;
        public float LifetimeRemaining;
    }
}
