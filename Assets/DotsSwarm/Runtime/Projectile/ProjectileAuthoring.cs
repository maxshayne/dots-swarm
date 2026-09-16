using Unity.Entities;
using UnityEngine;

namespace DotsSwarm.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class ProjectileAuthoring : MonoBehaviour
    {
        [SerializeField, Min(0)]
        private int damage = ProjectileCombat.DefaultDamage;

        [SerializeField, Min(0f), Tooltip("Projectile collision radius in world units on the XZ plane.")]
        private float radius = ProjectileCombat.DefaultRadius;

        private void OnValidate()
        {
            damage = Mathf.Max(0, damage);
            radius = Mathf.Max(0f, radius);
        }

        private sealed class ProjectileBaker : Baker<ProjectileAuthoring>
        {
            public override void Bake(ProjectileAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new Projectile());
                AddComponent(entity, ProjectileCombat.Create(authoring.damage, authoring.radius));
            }
        }
    }
}
