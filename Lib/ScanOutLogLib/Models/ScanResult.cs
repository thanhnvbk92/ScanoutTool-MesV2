using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanOutLogLib.Models
{
    public class ScanResult
    {
        public string PID { get; set; }
        public string WorkOrder { get; set; }
        public string Model { get; set; }
        public string Result { get; set; } // OK, NG, UNKNOWN
    }
}
