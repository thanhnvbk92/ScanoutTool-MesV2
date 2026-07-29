using ControlzEx.Standard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public interface IScanoutService
    {
        void Start();
        void Stop();
        void RequestResult(string pid);
        event Action<string , string , string , string > OnResultReady;
    }
}
