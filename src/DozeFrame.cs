using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace WheekKeys
{
    internal sealed class DozeFrame
    {
        private static readonly List<DozeFrame> Frames = new List<DozeFrame>();
        private static readonly int Cycle;
        internal readonly int Eyes, Mouth, Nod;
        private readonly int end;

        static DozeFrame()
        {
            using (var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("doze.txt")))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int[] values = Array.ConvertAll(line.Split(' '), int.Parse);
                    Cycle += values[0];
                    Frames.Add(new DozeFrame(Cycle, values[1], values[2], values[3]));
                }
            }
        }

        private DozeFrame(int end, int eyes, int mouth, int nod)
        {
            this.end = end;
            Eyes = eyes;
            Mouth = mouth;
            Nod = nod;
        }

        internal static DozeFrame At(double elapsed)
        {
            double phase = elapsed % Cycle;
            foreach (DozeFrame frame in Frames)
                if (phase < frame.end) return frame;
            return Frames[0];
        }
    }
}
