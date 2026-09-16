using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

namespace DotsSwarm.Gameplay
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DamageSystem))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct DeathSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Health>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var commandBuffer = SystemAPI
                .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            state.Dependency = new DestroyDeadEnemiesJob
            {
                CommandBuffer = commandBuffer.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(Enemy))]
        private partial struct DestroyDeadEnemiesJob : IJobEntity
        {
            public EntityCommandBuffer.ParallelWriter CommandBuffer;

            private void Execute([ChunkIndexInQuery] int sortKey, Entity entity, in Health health)
            {
                if (health.Current <= 0)
                {
                    CommandBuffer.DestroyEntity(sortKey, entity);
                }
            }
        }
    }
}
