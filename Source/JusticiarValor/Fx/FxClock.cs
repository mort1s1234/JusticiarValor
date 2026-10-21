using System;
using Verse;

namespace JusticiarValor
{
    // Game time in seconds for the effect shaders, smoothed between ticks.
    // Ticks advance at most 60 times a second at normal speed and stop when the game is paused; the clock advances by
    // the frame time scaled by the game speed and is kept within one tick of TicksGame / 60, so effects animate smoothly
    // at any frame rate, speed up with the game and freeze with it. Doubles keep sub-frame precision in long games.
    public static class FxClock
    {
        private const double OneTick = 1.0 / 60.0;
        private static int lastFrame = -1;
        private static double now;
        private static Game game;

        public static double Now
        {
            get
            {
                Update();
                return now;
            }
        }

        public static void Update()
        {
            Game current = Current.Game;
            if (RealTime.frameCount == lastFrame && current == game)
            {
                return;
            }
            lastFrame = RealTime.frameCount;
            TickManager tickManager = current?.tickManager;
            if (tickManager == null)
            {
                return;
            }
            double target = tickManager.TicksGame * OneTick;
            if (current != game || Math.Abs(now - target) > 0.5)
            {
                game = current;
                now = target;
                return;
            }
            if (!tickManager.Paused)
            {
                now += Math.Min(RealTime.deltaTime, 0.1f) * Math.Min(tickManager.TickRateMultiplier, 15f);
            }
            now = Math.Max(target - OneTick, Math.Min(target + OneTick, now));
        }

        // A shader-friendly time that wraps every hour of game time, for looping animation.
        public static float Wrapped(double time) => (float)(time % 3600.0);
    }
}
