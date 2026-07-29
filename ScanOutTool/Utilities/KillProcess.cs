using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanOutTool.Utilities
{
    public class KillProcess
    {
        public KillProcess() { }

        private static void Kill(string processName)
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(processName);
            foreach (var process in processes)
            {
                try
                {
                    process.Kill();
                }
                catch (Exception ex)
                {
                    // Handle exception if needed
                    Console.WriteLine($"Error killing process {processName}: {ex.Message}");
                }
            }
        }

        public static void KillChromeDriver()
        {
            Kill("chromedriver");
        }

        public static void KillChromeAndDriver()
        {
            Kill("chromedriver");
            Kill("chrome");
        }
    }
}
