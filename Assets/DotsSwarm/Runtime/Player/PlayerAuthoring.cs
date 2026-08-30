using Unity.Entities;
using UnityEngine;

namespace DotsSwarm.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class PlayerAuthoring : MonoBehaviour
    {
        public const float DefaultMovementSpeed = 10f;
        public const float MinimumMovementSpeed = 0.1f;

        [SerializeField]
        [Min(MinimumMovementSpeed)]
        private float movementSpeed = DefaultMovementSpeed;

        private void OnValidate()
        {
            movementSpeed = Mathf.Max(MinimumMovementSpeed, movementSpeed);
        }

        private sealed class PlayerBaker : Baker<PlayerAuthoring>
        {
            public override void Bake(PlayerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new Player
                {
                    MovementSpeed = Mathf.Max(MinimumMovementSpeed, authoring.movementSpeed)
                });
            }
        }
    }
}
