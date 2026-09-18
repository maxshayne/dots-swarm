using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsSwarm.Gameplay
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileHitSystem))]
    [UpdateAfter(typeof(ProjectileMovementSystem))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct DamageSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton<GameSession>(out var session) && !session.CanSimulate)
                return;

            // One producer job owns random buffer writes: simultaneous hits on the
            // same enemy cannot race. Detection and per-enemy damage remain parallel.
            var transferDependency = new TransferHitsJob
            {
                DamageBuffers = SystemAPI.GetBufferLookup<DamageEvent>()
            }.Schedule(state.Dependency);

            state.Dependency = new ApplyDamageJob
            {
                ProtectPlayer = SystemAPI.TryGetSingleton<BenchmarkState>(out var benchmark) && benchmark.IsStress,
                Players = SystemAPI.GetComponentLookup<Player>(true)
            }.ScheduleParallel(transferDependency);
        }

        [BurstCompile]
        private partial struct TransferHitsJob : IJobEntity
        {
            public BufferLookup<DamageEvent> DamageBuffers;

            private void Execute(ref ProjectileCombat combat)
            {
                if (combat.HitEntity == Entity.Null)
                {
                    return;
                }

                if (DamageBuffers.HasBuffer(combat.HitEntity))
                {
                    DamageBuffers[combat.HitEntity].Add(new DamageEvent
                    {
                        Amount = math.max(0, combat.Damage)
                    });
                }

                combat.HitEntity = Entity.Null;
            }
        }

        [BurstCompile]
        private partial struct ApplyDamageJob : IJobEntity
        {
            public bool ProtectPlayer;
            [ReadOnly] public ComponentLookup<Player> Players;

            private void Execute(Entity entity, ref Health health, ref DynamicBuffer<DamageEvent> events)
            {
                if (ProtectPlayer && Players.HasComponent(entity))
                {
                    events.Clear();
                    return;
                }
                var remaining = math.max(0, health.Current);
                foreach (var damage in events)
                {
                    // Saturate each subtraction to avoid integer overflow on overkill.
                    remaining -= math.min(remaining, math.max(0, damage.Amount));
                }

                health.Current = remaining;
                events.Clear();
            }
        }
    }
}
