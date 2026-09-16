using Unity.Entities;
using Unity.Mathematics;

namespace DotsSwarm.Gameplay
{
    public struct PlayerContactDamage : IComponentData
    {
        public const int DefaultDamage = 1;
        public const float DefaultInterval = 0.5f;
        public const float MinimumInterval = 0.01f;

        public int Damage;
        public float Interval;

        public static PlayerContactDamage Create(int damage, float interval)
        {
            return new PlayerContactDamage
            {
                Damage = math.max(0, damage),
                Interval = math.max(MinimumInterval, interval)
            };
        }
    }

    public struct PlayerContactState : IComponentData
    {
        public float CooldownRemaining;
    }
}
