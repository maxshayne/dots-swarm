using System;

namespace DotsSwarm.Gameplay
{
    public struct FrameTimeSummary
    {
        public int Frames;
        public float AverageMilliseconds;
        public float P50Milliseconds;
        public float P95Milliseconds;
        public float P99Milliseconds;
        public float MaxMilliseconds;
    }

    // The latest real frame durations in a fixed ring. Percentiles use the
    // nearest-rank method: the smallest sample with at least p% of the samples at
    // or below it. Both arrays are allocated once; summaries sort a scratch copy.
    public sealed class FrameTimeHistory
    {
        private readonly float[] samples;
        private readonly float[] sorted;
        private int next;

        public FrameTimeHistory(int capacity)
        {
            capacity = Math.Max(1, capacity);
            samples = new float[capacity];
            sorted = new float[capacity];
        }

        public int Capacity => samples.Length;
        public int Count { get; private set; }

        public bool Add(double seconds)
        {
            if (!(seconds > 0d) || double.IsInfinity(seconds))
                return false;

            samples[next] = (float)(seconds * 1000d);
            next = (next + 1) % samples.Length;
            if (Count < samples.Length) Count++;
            return true;
        }

        public void Clear()
        {
            next = 0;
            Count = 0;
        }

        public FrameTimeSummary Summarize()
        {
            if (Count == 0)
                return default;

            // Until the ring wraps, the samples occupy the front of the array.
            Array.Copy(samples, sorted, Count);
            Array.Sort(sorted, 0, Count);
            var total = 0d;
            for (var i = 0; i < Count; i++) total += sorted[i];
            return new FrameTimeSummary
            {
                Frames = Count,
                AverageMilliseconds = (float)(total / Count),
                P50Milliseconds = Percentile(50),
                P95Milliseconds = Percentile(95),
                P99Milliseconds = Percentile(99),
                MaxMilliseconds = sorted[Count - 1]
            };
        }

        // Integer ceiling keeps ranks exact: 95% of 20 samples is rank 19.
        private float Percentile(int percent) => sorted[(percent * Count + 99) / 100 - 1];
    }
}
