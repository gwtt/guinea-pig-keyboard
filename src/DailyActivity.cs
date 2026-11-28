using System;
using System.Collections.Generic;
using System.Globalization;

namespace WheekKeys
{
    internal sealed class DailyActivity
    {
        private readonly Queue<double> recent = new Queue<double>(10);
        private double nextBurst;
        internal DateTime Day { get; private set; }
        internal long Keys { get; private set; }
        internal long Clicks { get; private set; }

        internal DailyActivity() { Day = DateTime.Today; }

        internal bool StartDay(DateTime date)
        {
            if (Day == date.Date) return false;
            Day = date.Date;
            Keys = Clicks = 0;
            recent.Clear();
            nextBurst = 0;
            return true;
        }

        internal void Restore(string day, string keys, string clicks)
        {
            StartDay(DateTime.Today);
            if (day != Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) return;
            long count;
            if (LocalPeers.TryCount(keys, out count)) Keys = count;
            if (LocalPeers.TryCount(clicks, out count)) Clicks = count;
        }

        internal void RecordClick() { if (Clicks < long.MaxValue) Clicks++; }

        internal string RecordKey(double now)
        {
            if (Keys < long.MaxValue) Keys++;
            if (Keys % 1000 == 0)
            {
                recent.Clear();
                nextBurst = now + 30;
                return "今天已敲 " + Keys + " 键 ♡";
            }
            if (now < nextBurst) return null;
            while (recent.Count > 0 && now - recent.Peek() > 1) recent.Dequeue();
            recent.Enqueue(now);
            if (recent.Count < 10) return null;
            recent.Clear();
            nextBurst = now + 30;
            return "小爪跟上啦！";
        }
    }
}
