using Unity.Entities;
using UnityEngine;

namespace DotsSwarm.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class ProjectileAuthoring : MonoBehaviour
    {
        private sealed class ProjectileBaker : Baker<ProjectileAuthoring>
        {
            public override void Bake(ProjectileAuthoring authoring)
            {
                AddComponent(GetEntity(TransformUsageFlags.Dynamic), new Projectile());
            }
        }
    }
}
