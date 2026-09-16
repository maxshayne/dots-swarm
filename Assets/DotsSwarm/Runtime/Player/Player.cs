using Unity.Entities;

namespace DotsSwarm.Gameplay
{
    public struct Player : IComponentData
    {
        // World-space XZ radius of the MVP player (diameter 1).
        public const float CollisionRadius = 0.5f;

        public float MovementSpeed;
    }
}
