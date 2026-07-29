using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public interface IShowRescanResultService
    {
        void ShowRescanResult(double left, double top, double width, double height);
        void SetRescanResult(string result, string qty, string message);

        void ShowBoxResult(double left, double top, double width, double height);
        void SetBoxResult(string PartNo, string MagazineNumber, string qty);

    }
}
