using System;
using WheekKeys;
internal static class PenInputSmokeTest
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Main()
    {
        PenInput.MotionState state = new PenInput.MotionState();
        PenInput.Sample first = PenInput.Advance(ref state, true, false, 0.2, 0.3, null, 100, 200);
        Check(first.InRange && !first.TipDown && !first.Pressure.HasValue && first.Dx == 0 && first.Dy == 0, "hover first frame and no-pressure fallback");
        PenInput.Sample contact = PenInput.Advance(ref state, true, true, 0.4, 0.4, 0.75, 100, 200);
        Check(contact.TipDown && contact.Pressure == 0.75 && Math.Abs(contact.Dx - 20) < 0.001 && Math.Abs(contact.Dy - 20) < 0.001, "contact pressure and motion");
        PenInput.Sample leave = PenInput.Advance(ref state, false, false, 0, 0, null, 100, 200);
        Check(!leave.InRange && !state.Active, "leave and removal reset");
        PenInput.Sample again = PenInput.Advance(ref state, true, false, 0.9, 0.9, null, 100, 200);
        Check(again.Dx == 0 && again.Dy == 0, "new first frame after leave");
        PenInput.MotionState other = new PenInput.MotionState();
        Check(PenInput.Advance(ref other, true, false, 0.1, 0.2, null, 100, 200).Dx == 0, "independent device");
        CheckReactions();
        Console.WriteLine("Pen motion and pet reaction simulations passed");
    }
    private static void CheckReactions()
    {
        foreach (bool vertical in new[] { false, true })
        {
            PetReactions reaction = new PetReactions();
            for (int i = 0; i < 4; i++)
            {
                double step = i % 2 == 0 ? 20 : -20;
                reaction.Move(vertical ? 0 : step, vertical ? step : 0, i * 100);
            }
            double angle;
            Check(reaction.Popcorn(500, out angle).Y < 0, "first jump after reversals");
            Check(reaction.Popcorn(970, out angle).Y < 0, "second jump after reversals");
            Check(reaction.Popcorn(1140, out angle).LengthSquared == 0, "jump ends");
            for (int i = 0; i < 4; i++) reaction.Move(i % 2 == 0 ? 20 : -20, 0, 1200 + i * 100);
            Check(reaction.Popcorn(1600, out angle).LengthSquared == 0, "jump cooldown");
            for (int i = 0; i < 4; i++) reaction.Move(i % 2 == 0 ? 20 : -20, 0, 3000 + i * 100);
            Check(reaction.Popcorn(3500, out angle).Y < 0, "jump after cooldown");
        }
        PetReactions small = new PetReactions();
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++) small.Move(i % 2 == 0 ? 5 : -5, 0, i * 100 + j * 15);
        double ignored;
        Check(small.Popcorn(500, out ignored).Y < 0, "small samples accumulate into reversals");
        PetReactions straight = new PetReactions(), jitter = new PetReactions();
        for (int i = 0; i < 20; i++)
        {
            straight.Move(20, 0, i * 30);
            jitter.Move(i % 2 == 0 ? 3 : -3, 0, i * 30);
        }
        Check(straight.Popcorn(600, out ignored).LengthSquared == 0 && jitter.Popcorn(600, out ignored).LengthSquared == 0, "straight movement and jitter do not trigger");
        PetReactions keys = new PetReactions();
        for (int i = 0; i < 3; i++) keys.Press(true, i * 100);
        Check(!keys.Panicked(250), "three backspaces do not panic");
        keys.Press(false, 300);
        keys.Press(true, 400); keys.Press(true, 500); keys.Press(true, 600);
        Check(!keys.Panicked(650), "other key interrupts backspaces");
        keys.Press(true, 700);
        Check(keys.Panicked(700) && keys.Panicked(1949) && !keys.Panicked(1950), "four backspaces and panic duration");
        PetReactions slowKeys = new PetReactions();
        slowKeys.Press(true, 0); slowKeys.Press(true, 100); slowKeys.Press(true, 200);
        slowKeys.Press(true, 601);
        Check(!slowKeys.Panicked(601), "backspace interval resets count");
    }

}
