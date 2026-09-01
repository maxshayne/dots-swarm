using Unity.Entities;
using Unity.Mathematics;

namespace DotsSwarm.Gameplay
{
    public struct Enemy : IComponentData
    {
        public const float DefaultMovementSpeed = 3f;

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
