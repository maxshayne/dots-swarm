using Unity.Entities;
using Unity.Mathematics;

namespace DotsSwarm.Gameplay
{
    public struct Health : IComponentData
    {
        public const int DefaultEnemyHealth = 2;
        public const int DefaultPlayerHealth = 12;

        public int Current;

        public static Health Create(int amount)
        {
            return new Health { Current = math.max(1, amount) };
        }
    }
}
