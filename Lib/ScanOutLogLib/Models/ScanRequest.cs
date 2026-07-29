using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanOutLogLib.Models
{
    public class ScanRequest
    {
        public string PID { get; set; }
        public DateTime SentTime { get; set; }
    }
}
