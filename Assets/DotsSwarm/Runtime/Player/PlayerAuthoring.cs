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

        [SerializeField, Min(1)]
        private int health = Health.DefaultPlayerHealth;

        [SerializeField, Min(0), Tooltip("Damage received per contact interval, regardless of enemy count.")]
        private int contactDamage = PlayerContactDamage.DefaultDamage;

        [SerializeField, Min(PlayerContactDamage.MinimumInterval)]
        private float contactInterval = PlayerContactDamage.DefaultInterval;

        private void OnValidate()
        {
            movementSpeed = Mathf.Max(MinimumMovementSpeed, movementSpeed);
            health = Mathf.Max(1, health);
            contactDamage = Mathf.Max(0, contactDamage);
            contactInterval = Mathf.Max(PlayerContactDamage.MinimumInterval, contactInterval);
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
                AddComponent(entity, Health.Create(authoring.health));
                AddBuffer<DamageEvent>(entity);
                AddComponent(entity, PlayerContactDamage.Create(authoring.contactDamage, authoring.contactInterval));
                AddComponent(entity, new PlayerContactState());
            }
        }
    }
}
