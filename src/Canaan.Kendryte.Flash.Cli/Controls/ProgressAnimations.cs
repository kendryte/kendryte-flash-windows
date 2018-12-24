namespace AaronLuna.ConsoleProgressBar
{
    using System;
    using System.Linq;

    using Microsoft.VisualBasic;

    public static class ProgressAnimations
    {
        // These characters render correctly on Windows and Mac
        public const string Default = @"|/-\-";
        public const string BouncingBall = ".oO\u00b0Oo.";
        public const string Explosion = ".oO@*";
        public const string RotatingTriangle = "\u25b2\u25ba\u25bc\u25c4";
        public const string RotatingArrow = "\u2190\u2191\u2192\u2193";
        public const string PulsingLine = "\u2212\u003d\u2261\u039e\u2261\u003d\u2212";
        public const string Circles = "\u25cb\u263c\u00a4\u2219";

        // These characters render correctly only on Mac
        public const string RotatingDot = "\u25dc\u25dd\u25de\u25df";
        public const string GrowingBarVertical = "\u2581\u2582\u2583\u2584\u2585\u2586\u2587\u2588\u2587\u2586\u2585\u2584\u2583\u2581";
        public const string RotatingPipe = "\u2524\u2518\u2534\u2514\u251c\u250c\u252c\u2510";
        public const string RotatingCircle = "\u25d0\u25d3\u25d1\u25d2";
        public const string GrowingBarHorizontal = "\u2589\u258a\u258b\u258c\u258d\u258e\u258f\u258e\u258d\u258c\u258b\u258a\u2589";

        public static string RandomBrailleSequence()
        {
            var rand = new Random();
            var sequence = string.Empty;
            foreach (int i in Enumerable.Range(0, 40))
            {
                var charIndex = rand.Next(10241, 10496);
                var randChar = Strings.ChrW(charIndex);
                sequence += randChar;
            }

            return sequence;
        }
    }
}
