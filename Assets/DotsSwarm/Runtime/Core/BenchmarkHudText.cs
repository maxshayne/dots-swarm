using Unity.Mathematics;

namespace DotsSwarm.Gameplay
{
    // Caller owns the buffer; no boxing, ToString or intermediate strings on
    // refresh. The maximum output fits in Capacity even for int.MaxValue counts.
    public static class BenchmarkHudText
    {
        public const int Capacity = 192;

        public static int Write(char[] buffer, int enemies, int projectiles, float milliseconds,
            float p95Milliseconds, int playerHealth, in GameSession session, in BenchmarkState benchmark)
        {
            var index = 0;
            Append(buffer, ref index, benchmark.IsStress ? "STRESS " : "SURVIVAL");
            if (benchmark.IsStress)
            {
                Number(buffer, ref index, benchmark.TargetCount);
                Append(buffer, ref index, "  |  Seed 1");
            }
            else
            {
                var remaining = (int)math.ceil(math.max(0f, GameSession.Duration - session.Elapsed));
                Append(buffer, ref index, "  |  ");
                Number(buffer, ref index, remaining / 60);
                buffer[index++] = ':';
                buffer[index++] = (char)('0' + remaining % 60 / 10);
                buffer[index++] = (char)('0' + remaining % 10);
                Append(buffer, ref index, "  |  HP ");
                Number(buffer, ref index, playerHealth);
            }
            Append(buffer, ref index, "\nEnemies: ");
            Number(buffer, ref index, enemies);
            Append(buffer, ref index, "   Projectiles: ");
            Number(buffer, ref index, projectiles);
            Append(buffer, ref index, "\nFrame avg: ");
            Milliseconds(buffer, ref index, milliseconds);
            Append(buffer, ref index, " ms   p95: ");
            Milliseconds(buffer, ref index, p95Milliseconds);
            Append(buffer, ref index, " ms  |  R: restart");
            return index;
        }

        private static void Milliseconds(char[] buffer, ref int index, float value)
        {
            var tenths = math.isfinite(value) ? (int)math.round(math.clamp(value, 0f, 99999f) * 10f) : 0;
            Number(buffer, ref index, tenths / 10);
            buffer[index++] = '.';
            buffer[index++] = (char)('0' + tenths % 10);
        }

        private static void Append(char[] buffer, ref int index, string value)
        {
            for (var i = 0; i < value.Length; i++)
                buffer[index++] = value[i];
        }

        private static void Number(char[] buffer, ref int index, int value)
        {
            value = math.max(0, value);
            var start = index;
            do
            {
                buffer[index++] = (char)('0' + value % 10);
                value /= 10;
            } while (value > 0);
            for (int left = start, right = index - 1; left < right; left++, right--)
            {
                var digit = buffer[left];
                buffer[left] = buffer[right];
                buffer[right] = digit;
            }
        }
    }
}
