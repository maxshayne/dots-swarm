using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsSwarm.Gameplay
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(WeaponSystem))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct ProjectileMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Projectile>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var commandBuffer = SystemAPI
                .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            state.Dependency = new MoveProjectilesJob
            {
                DeltaTime = SystemAPI.Time.DeltaTime,
                CommandBuffer = commandBuffer.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        private partial struct MoveProjectilesJob : IJobEntity
        {
            public float DeltaTime;
            public EntityCommandBuffer.ParallelWriter CommandBuffer;

            private void Execute(
                [ChunkIndexInQuery] int sortKey,
                Entity entity,
                ref LocalTransform transform,
                ref Projectile projectile)
            {
                var deltaTime = math.max(0f, DeltaTime);
                var movementTime = math.min(deltaTime, math.max(0f, projectile.LifetimeRemaining));
                transform.Position += projectile.Velocity * movementTime;
                projectile.LifetimeRemaining = math.max(0f, projectile.LifetimeRemaining - deltaTime);
                if (projectile.LifetimeRemaining <= 0f)
                {
                    CommandBuffer.DestroyEntity(sortKey, entity);
                }
            }
        }
    }
}
