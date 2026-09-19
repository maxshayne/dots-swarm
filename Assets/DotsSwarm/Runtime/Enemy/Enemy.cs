using Unity.Entities;
using Unity.Mathematics;

namespace DotsSwarm.Gameplay
{
    public struct Enemy : IComponentData
    {
        public const float DefaultMovementSpeed = 2.25f;
        // World-space XZ radius of the MVP enemy prefab (diameter 0.8).
        public const float CollisionRadius = 0.4f;

        public float MovementSpeed;

        public static Enemy Create(float movementSpeed)
        {
            return new Enemy
            {
                MovementSpeed = math.max(0f, movementSpeed)
            };
        }
    }
}
