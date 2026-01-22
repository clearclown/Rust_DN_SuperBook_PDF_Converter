using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WriteTimeStamp
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string str = DateTimeToDtstr(DateTimeOffset.Now);

            Console.WriteLine(str);
        }

        public static string DateTimeToDtstr(DateTimeOffset dt, bool withMSecs = false, bool withNanoSecs = false, string zeroDateTimeStr = "")
        {
            long ticks = dt.Ticks % 10000000;
            if (ticks >= 9999999) ticks = 9999999;

            string msecStr = "";
            if (withNanoSecs)
            {
                msecStr = dt.ToString("fffffff");
            }
            else if (withMSecs)
            {
                msecStr = dt.ToString("ffff").Substring(0, 3);
            }

            string ret = dt.ToString("yyyy/MM/dd HH:mm:ss") + ((withMSecs || withNanoSecs) ? "." + msecStr : "");

            ret += " " + dt.ToString("%K");

            return ret;
        }
    }
}
