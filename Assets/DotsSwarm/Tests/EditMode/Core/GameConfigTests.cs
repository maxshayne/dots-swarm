using DotsSwarm.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace DotsSwarm.Tests.Core
{
    public sealed class GameConfigTests
    {
        [Test]
        public void FromArenaSize_ConvertsDimensionsToHalfExtents()
        {
            var config = GameConfig.FromArenaSize(new float2(80f, 120f));

            Assert.That(config.ArenaHalfExtents.x, Is.EqualTo(40f));
            Assert.That(config.ArenaHalfExtents.y, Is.EqualTo(60f));
        }

        [Test]
        public void FromArenaSize_ClampsNonPositiveDimensions()
        {
            var config = GameConfig.FromArenaSize(new float2(0f, -10f));

            Assert.That(config.ArenaHalfExtents.x, Is.EqualTo(0.5f));
            Assert.That(config.ArenaHalfExtents.y, Is.EqualTo(0.5f));
        }
    }
}
