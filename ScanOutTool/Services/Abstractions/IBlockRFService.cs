using ScanOutTool.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public interface IBlockRFService
    {
        Task<RFInfo> IsBlock(string pid);
    }
}
