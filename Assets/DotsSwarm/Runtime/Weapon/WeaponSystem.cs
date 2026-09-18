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
    [UpdateAfter(typeof(PlayerMovementSystem))]
    [UpdateAfter(typeof(EnemySpatialGridSystem))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct WeaponSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Weapon>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton<GameSession>(out var session) && !session.CanSimulate)
                return;

            var gridHandle = state.WorldUnmanaged
                .GetExistingUnmanagedSystem<EnemySpatialGridSystem>();
            if (gridHandle == SystemHandle.Null)
            {
                return;
            }

            ref var gridSystem = ref state.WorldUnmanaged
                .GetUnsafeSystemRef<EnemySpatialGridSystem>(gridHandle);
            var commandBuffer = SystemAPI
                .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            state.Dependency = new FireJob
            {
                Grid = gridSystem.Grid,
                Transforms = SystemAPI.GetComponentLookup<LocalTransform>(true),
                CommandBuffer = commandBuffer.AsParallelWriter(),
                DeltaTime = SystemAPI.Time.DeltaTime
            }.ScheduleParallel(JobHandle.CombineDependencies(
                state.Dependency,
                gridSystem.GridDependency));

            // The next rebuild must wait until target selection has finished reading.
            gridSystem.GridDependency = state.Dependency;
        }

        public static Entity FindNearestEnemy(
            NativeParallelMultiHashMap<int2, Entity>.ReadOnly grid,
            ComponentLookup<LocalTransform> transforms,
            float3 origin,
            float range)
        {
            var safeRange = math.max(0f, range);
            var cellSize = EnemySpatialGridSystem.CellSize;
            var minimumCell = (int2)math.floor((origin.xz - safeRange) / cellSize);
            var maximumCell = (int2)math.floor((origin.xz + safeRange) / cellSize);
            var bestDistanceSquared = safeRange * safeRange;
            var nearest = Entity.Null;

            for (var z = minimumCell.y; z <= maximumCell.y; z++)
            {
                for (var x = minimumCell.x; x <= maximumCell.x; x++)
                {
                    var cell = new int2(x, z);
                    var cellMinimum = (float2)cell * cellSize;
                    var closestPoint = math.clamp(origin.xz, cellMinimum, cellMinimum + cellSize);
                    if (math.distancesq(origin.xz, closestPoint) > bestDistanceSquared
                        || !grid.TryGetFirstValue(cell, out var enemy, out var iterator))
                    {
                        continue;
                    }

                    do
                    {
                        if (!transforms.TryGetComponent(enemy, out var transform))
                        {
                            continue;
                        }

                        var distanceSquared = math.distancesq(origin.xz, transform.Position.xz);
                        // Hash-map iteration order is unspecified; ties use entity identity.
                        if (distanceSquared < bestDistanceSquared
                            || (distanceSquared == bestDistanceSquared
                                && (nearest == Entity.Null || enemy.Index < nearest.Index
                                    || (enemy.Index == nearest.Index && enemy.Version < nearest.Version))))
                        {
                            nearest = enemy;
                            bestDistanceSquared = distanceSquared;
                        }
                    }
                    while (grid.TryGetNextValue(out enemy, ref iterator));
                }
            }

            return nearest;
        }

        [BurstCompile]
        [WithAll(typeof(Player))]
        private partial struct FireJob : IJobEntity
        {
            [ReadOnly]
            public NativeParallelMultiHashMap<int2, Entity>.ReadOnly Grid;

            [ReadOnly]
            public ComponentLookup<LocalTransform> Transforms;

            public EntityCommandBuffer.ParallelWriter CommandBuffer;
            public float DeltaTime;

            private void Execute(
                [ChunkIndexInQuery] int sortKey,
                ref WeaponState weaponState,
                in Weapon weapon,
                in LocalTransform transform)
            {
                weaponState.CooldownRemaining = math.max(
                    0f,
                    weaponState.CooldownRemaining - math.max(0f, DeltaTime));
                if (weaponState.CooldownRemaining > 0f
                    || !Transforms.TryGetComponent(weapon.ProjectilePrefab, out var projectileTransform))
                {
                    return;
                }

                var target = FindNearestEnemy(Grid, Transforms, transform.Position, weapon.Range);
                if (target == Entity.Null)
                {
                    return;
                }

                var offset = Transforms[target].Position.xz - transform.Position.xz;
                var direction = math.normalizesafe(offset, new float2(0f, 1f));
                var velocity = new float3(direction.x, 0f, direction.y)
                               * weapon.ProjectileSpeed;
                projectileTransform.Position = transform.Position;
                projectileTransform.Rotation = quaternion.LookRotationSafe(
                    new float3(direction.x, 0f, direction.y),
                    math.up());

                var projectile = CommandBuffer.Instantiate(sortKey, weapon.ProjectilePrefab);
                CommandBuffer.SetComponent(sortKey, projectile, projectileTransform);
                CommandBuffer.SetComponent(sortKey, projectile, new Projectile
                {
                    Velocity = velocity,
                    LifetimeRemaining = weapon.ProjectileLifetime
                });

                // At most one shot per update: idle time and long frames do not bank bursts.
                weaponState.CooldownRemaining = weapon.Cooldown;
            }
        }
    }
}
