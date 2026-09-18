using DotsSwarm.Spatial;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsSwarm.Gameplay
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(WeaponSystem))]
    [UpdateAfter(typeof(EnemySpatialGridSystem))]
    [UpdateBefore(typeof(ProjectileMovementSystem))]
    public partial struct ProjectileHitSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ProjectileCombat>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton<GameSession>(out var session) && !session.CanSimulate)
                return;

            if (SystemAPI.Time.DeltaTime <= 0f)
            {
                return;
            }

            var gridHandle = state.WorldUnmanaged
                .GetExistingUnmanagedSystem<EnemySpatialGridSystem>();
            if (gridHandle == SystemHandle.Null)
            {
                return;
            }

            ref var gridSystem = ref state.WorldUnmanaged
                .GetUnsafeSystemRef<EnemySpatialGridSystem>(gridHandle);
            state.Dependency = new DetectHitsJob
            {
                Grid = gridSystem.Grid,
                Transforms = SystemAPI.GetComponentLookup<LocalTransform>(true),
                Healths = SystemAPI.GetComponentLookup<Health>(true),
                DamageBuffers = SystemAPI.GetBufferLookup<DamageEvent>(true),
                DeltaTime = SystemAPI.Time.DeltaTime
            }.ScheduleParallel(JobHandle.CombineDependencies(
                state.Dependency,
                gridSystem.GridDependency));

            // Return the reader handle, including weapon readers, to the grid owner.
            gridSystem.GridDependency = state.Dependency;
        }

        public static Entity FindFirstEnemy(
            NativeParallelMultiHashMap<int2, Entity>.ReadOnly grid,
            ComponentLookup<LocalTransform> transforms,
            ComponentLookup<Health> healths,
            BufferLookup<DamageEvent> damageBuffers,
            float3 start,
            float3 end,
            float projectileRadius)
        {
            var radius = math.max(0f, projectileRadius) + Enemy.CollisionRadius;
            var cellSize = EnemySpatialGridSystem.CellSize;
            var minimumCell = (int2)math.floor((math.min(start.xz, end.xz) - radius) / cellSize);
            var maximumCell = (int2)math.floor((math.max(start.xz, end.xz) + radius) / cellSize);
            var first = Entity.Null;
            var firstFraction = 1f;

            for (var z = minimumCell.y; z <= maximumCell.y; z++)
            {
                for (var x = minimumCell.x; x <= maximumCell.x; x++)
                {
                    if (!grid.TryGetFirstValue(new int2(x, z), out var enemy, out var iterator))
                    {
                        continue;
                    }

                    do
                    {
                        if (!transforms.TryGetComponent(enemy, out var transform)
                            || !healths.TryGetComponent(enemy, out var health)
                            || health.Current <= 0
                            || !damageBuffers.HasBuffer(enemy)
                            || !TryGetHitFraction(start.xz, end.xz, transform.Position.xz,
                                radius, out var fraction))
                        {
                            continue;
                        }

                        // First contact along the segment wins, with stable identity ties.
                        if (fraction < firstFraction
                            || (fraction == firstFraction
                                && (first == Entity.Null || enemy.Index < first.Index
                                    || (enemy.Index == first.Index && enemy.Version < first.Version))))
                        {
                            first = enemy;
                            firstFraction = fraction;
                        }
                    }
                    while (grid.TryGetNextValue(out enemy, ref iterator));
                }
            }

            return first;
        }

        public static bool TryGetHitFraction(
            float2 start, float2 end, float2 center, float radius, out float fraction)
        {
            var offset = start - center;
            var safeRadius = math.max(0f, radius);
            var c = math.lengthsq(offset) - safeRadius * safeRadius;
            fraction = 0f;
            if (c <= 0f)
            {
                return true;
            }

            var step = end - start;
            var a = math.lengthsq(step);
            var b = math.dot(offset, step);
            var discriminant = b * b - a * c;
            if (a == 0f || b >= 0f || discriminant < 0f)
            {
                return false;
            }

            fraction = (-b - math.sqrt(discriminant)) / a;
            return fraction >= 0f && fraction <= 1f;
        }

        [BurstCompile]
        private partial struct DetectHitsJob : IJobEntity
        {
            [ReadOnly] public NativeParallelMultiHashMap<int2, Entity>.ReadOnly Grid;
            [ReadOnly] public ComponentLookup<LocalTransform> Transforms;
            [ReadOnly] public ComponentLookup<Health> Healths;
            [ReadOnly] public BufferLookup<DamageEvent> DamageBuffers;
            public float DeltaTime;

            private void Execute(
                ref ProjectileCombat combat,
                ref Projectile projectile,
                in LocalTransform transform)
            {
                if (projectile.LifetimeRemaining <= 0f || combat.HitEntity != Entity.Null)
                {
                    return;
                }

                // Sweep the same lifetime-clamped segment that movement would travel.
                var movementTime = math.min(DeltaTime, projectile.LifetimeRemaining);
                var end = transform.Position + projectile.Velocity * movementTime;
                combat.HitEntity = FindFirstEnemy(Grid, Transforms, Healths, DamageBuffers,
                    transform.Position, end, combat.Radius);
                if (combat.HitEntity != Entity.Null)
                {
                    // Movement owns projectile deletion, avoiding duplicate ECB destroys
                    // when impact and expiry happen on the same frame.
                    projectile.LifetimeRemaining = 0f;
                }
            }
        }
    }
}
