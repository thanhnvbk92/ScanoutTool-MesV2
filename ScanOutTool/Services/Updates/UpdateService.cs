using AutoUpdate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScanOutTool.Services
{
    public class UpdateService: IUpdateService
    {
        public async Task CheckForUpdatesAsync()
        {
            Update.Updatefile = "Software/Update_ScanOutTool_MESV2.xml";

            Update.Start("10.224.142.245", "admin", "111111");
            await Task.CompletedTask;
        }
    }
}
