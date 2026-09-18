namespace DotsSwarm.Gameplay
{
    // Arithmetic mean of real frame durations, independent of timeScale and the
    // clamped simulation delta. Startup/preset changes reset the sample window.
    public struct BenchmarkFrameTime
    {
        public const double RefreshInterval = 0.25;
        private double totalSeconds;
        private int frames;
        public float Milliseconds { get; private set; }

        public bool AddFrame(double seconds)
        {
            if (seconds <= 0d || double.IsNaN(seconds) || double.IsInfinity(seconds))
                return false;

            totalSeconds += seconds;
            frames++;
            if (totalSeconds < RefreshInterval)
                return false;

            Milliseconds = (float)(totalSeconds * 1000d / frames);
            totalSeconds = 0d;
            frames = 0;
            return true;
        }
    }
}
