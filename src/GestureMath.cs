using System;
namespace ZapretDesktop
{
    internal static class GestureMath
    {
        public static bool Commit(double distance, long milliseconds) { return distance >= 44 || distance >= 18 && milliseconds >= 0 && milliseconds < 220; }
        public static double Pull(double movement, double startingOffset) { return Math.Max(0, movement - startingOffset); }
    }
}
