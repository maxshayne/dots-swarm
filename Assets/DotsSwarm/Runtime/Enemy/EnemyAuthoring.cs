using Unity.Entities;
using UnityEngine;

namespace DotsSwarm.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class EnemyAuthoring : MonoBehaviour
    {
        [SerializeField]
        [Min(0f)]
        private float movementSpeed = Enemy.DefaultMovementSpeed;

        private void OnValidate()
        {
            movementSpeed = Mathf.Max(0f, movementSpeed);
        }

        private sealed class EnemyBaker : Baker<EnemyAuthoring>
        {
            public override void Bake(EnemyAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, Enemy.Create(authoring.movementSpeed));
            }
        }
    }
}
