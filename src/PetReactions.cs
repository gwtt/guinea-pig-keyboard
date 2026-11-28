using System;
using System.Windows;

namespace WheekKeys
{
    internal sealed class PetReactions
    {
        private double lastMotion = double.NegativeInfinity, turnStarted;
        private Vector distance, direction;
        private int turns, combo, backspaces;
        private double lastKey = double.NegativeInfinity, glowStarted, panicUntil;
        private double jumpStarted = double.NegativeInfinity;
        internal bool GlowEnabled;

        internal void Move(double dx, double dy, double now)
        {
            if (now - jumpStarted < 2500) return;
            if (now - lastMotion > 250 || now - turnStarted > 650)
            {
                direction = distance = new Vector();
                turns = 0;
                turnStarted = now;
            }
            lastMotion = now;
            distance += new Vector(dx, dy);
            if (distance.LengthSquared < 18 * 18) return;
            distance.Normalize();
            if (Vector.Multiply(direction, distance) < -0.55 && ++turns >= 3)
                jumpStarted = now;
            direction = distance;
            distance = new Vector();
        }

        internal void Press(bool backspace, double now)
        {
            combo = now - lastKey <= 220 ? Math.Min(5, combo + 1) : 1;
            if (combo == 4) glowStarted = now;
            backspaces = backspace ? (now - lastKey <= 400 ? backspaces + 1 : 1) : 0;
            if (backspaces == 4) { panicUntil = now + 1250; backspaces = 0; }
            lastKey = now;
        }

        internal bool Panicked(double now) { return now < panicUntil; }
        internal double GlowStrength(double now)
        {
            return GlowEnabled && combo >= 4 ? Math.Max(0, Math.Min(1, (900 - (now - lastKey)) / 400)) : 0;
        }
        internal double GlowPhase(double now) { return ((now - glowStarted) % 720) / 720; }
        internal bool Animating(double now) { return now - jumpStarted < 840 || Panicked(now) || GlowStrength(now) > 0; }

        internal Vector Popcorn(double now, out double angle)
        {
            double age = now - jumpStarted;
            angle = 0;
            if (age < 0 || age >= 840) return new Vector();
            if (age < 80) return new Vector(0, 4 * Math.Sin(Math.PI * age / 80));
            bool second = age >= 490;
            double p = second ? (age - 490) / 350 : (age - 80) / 410;
            double lift = Math.Sin(Math.PI * p);
            angle = (second ? -1 : 1) * 12 * Math.Sin(2 * Math.PI * p) * lift;
            return new Vector((second ? -1 : 1) * 5 * lift, -(second ? 16 : 21) * lift);
        }
    }
}
