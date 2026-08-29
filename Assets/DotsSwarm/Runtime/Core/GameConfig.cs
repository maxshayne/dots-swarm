using Unity.Entities;
using Unity.Mathematics;

namespace DotsSwarm.Core
{
    public struct GameConfig : IComponentData
    {
        public const float DefaultArenaSize = 100f;
        public const float MinimumArenaSize = 1f;

        public float2 ArenaHalfExtents;

        public static GameConfig FromArenaSize(float2 arenaSize)
        {
            var minimumSize = new float2(MinimumArenaSize);
            return new GameConfig
            {
                ArenaHalfExtents = math.max(arenaSize, minimumSize) * 0.5f
            };
        }
    }
}
