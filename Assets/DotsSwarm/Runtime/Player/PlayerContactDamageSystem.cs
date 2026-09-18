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
    [UpdateAfter(typeof(ProjectileMovementSystem))]
    [UpdateBefore(typeof(DamageSystem))]
    public partial struct PlayerContactDamageSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerContactDamage>();
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

            var gridHandle = state.WorldUnmanaged.GetExistingUnmanagedSystem<EnemySpatialGridSystem>();
            if (gridHandle == SystemHandle.Null)
            {
                return;
            }

            ref var gridSystem = ref state.WorldUnmanaged
                .GetUnsafeSystemRef<EnemySpatialGridSystem>(gridHandle);
            state.Dependency = new ContactJob
            {
                Grid = gridSystem.Grid,
                Transforms = SystemAPI.GetComponentLookup<LocalTransform>(true),
                Healths = SystemAPI.GetComponentLookup<Health>(true),
                DeltaTime = SystemAPI.Time.DeltaTime
            }.ScheduleParallel(JobHandle.CombineDependencies(state.Dependency, gridSystem.GridDependency));

            // The next rebuild must also wait for the contact reader.
            gridSystem.GridDependency = state.Dependency;
        }

        [BurstCompile]
        [WithAll(typeof(Player))]
        private partial struct ContactJob : IJobEntity
        {
            [ReadOnly] public NativeParallelMultiHashMap<int2, Entity>.ReadOnly Grid;
            [ReadOnly] public ComponentLookup<LocalTransform> Transforms;
            [ReadOnly] public ComponentLookup<Health> Healths;
            public float DeltaTime;

            private void Execute(
                ref PlayerContactState contactState,
                ref DynamicBuffer<DamageEvent> events,
                in PlayerContactDamage contact,
                in Health health,
                in LocalTransform transform)
            {
                contactState.CooldownRemaining = math.max(0f, contactState.CooldownRemaining - DeltaTime);
                if (health.Current <= 0 || contact.Damage <= 0 || contactState.CooldownRemaining > 0f)
                {
                    return;
                }

                var radius = Player.CollisionRadius + Enemy.CollisionRadius;
                var cellSize = EnemySpatialGridSystem.CellSize;
                var minimumCell = (int2)math.floor((transform.Position.xz - radius) / cellSize);
                var maximumCell = (int2)math.floor((transform.Position.xz + radius) / cellSize);

                for (var z = minimumCell.y; z <= maximumCell.y; z++)
                {
                    for (var x = minimumCell.x; x <= maximumCell.x; x++)
                    {
                        if (!Grid.TryGetFirstValue(new int2(x, z), out var enemy, out var iterator))
                        {
                            continue;
                        }

                        do
                        {
                            if (!Healths.TryGetComponent(enemy, out var enemyHealth) || enemyHealth.Current <= 0
                                || !Transforms.TryGetComponent(enemy, out var enemyTransform)
                                || math.distancesq(transform.Position.xz, enemyTransform.Position.xz) > radius * radius)
                            {
                                continue;
                            }

                            // One invocation owns this player's buffer and cooldown. Enemy
                            // count and hash-map order cannot multiply the incoming damage.
                            events.Add(new DamageEvent { Amount = contact.Damage });
                            contactState.CooldownRemaining = math.max(PlayerContactDamage.MinimumInterval, contact.Interval);
                            return;
                        }
                        while (Grid.TryGetNextValue(out enemy, ref iterator));
                    }
                }
            }
        }
    }
}
