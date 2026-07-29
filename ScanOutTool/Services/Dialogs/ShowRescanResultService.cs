using ScanOutTool.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows;

namespace ScanOutTool.Services
{
    public class ShowRescanResultService : IShowRescanResultService
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOP = new IntPtr(0);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private readonly RescanInfoWindow _rescanInfoWindow;
        private readonly IAutoScanOutUI _autoScanOutUI;

        private DispatcherTimer _timer;

        private IntPtr _ResultHandle;
        public ShowRescanResultService(RescanInfoWindow rescanInfoWindow, IAutoScanOutUI autoScanOutUI)
        {
            _rescanInfoWindow = rescanInfoWindow;
            _autoScanOutUI = autoScanOutUI;
        }
        public void ShowRescanResult(double left, double top, double width, double height)
        {
            _rescanInfoWindow.Left = left;
            _rescanInfoWindow.Top = top;
            _rescanInfoWindow.Height = height;
            _rescanInfoWindow.Width = width;       

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _timer.Tick += (s, e) =>
            {
                KeepOverlayOnTop(_rescanInfoWindow,_autoScanOutUI.GetMainHandle(), left, top, width, height);
            };
            _timer.Start();

            // Implementation to show rescan result window
            // This could involve creating a new window and setting its properties
            // based on the parameters provided.
        }

        private void KeepOverlayOnTop(Window overlay, IntPtr mainAppHandle, double left, double top, double width, double height)
        {

            if (overlay == null || mainAppHandle == IntPtr.Zero)
                return;
            overlay.Show();
            var overlayHandle = new WindowInteropHelper(overlay).Handle;

            SetWindowPos(overlayHandle, HWND_TOP, (int)left, (int)top, (int)width, (int)height,
               SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public void ShowBoxResult(double left, double top, double width, double height)
        {
            // Implementation to show box result window
            // This could involve creating a new window and setting its properties
            // based on the parameters provided.
        }

        public void SetRescanResult(string result, string qty, string message)
        {
            _rescanInfoWindow.txt_Result.Text = result;
            _rescanInfoWindow.txt_Qty.Text = qty;
            _rescanInfoWindow.txt_Message.Text = message;
        }
        public void SetBoxResult(string PartNo, string MagazineNumber, string qty)
        {

        }
    }
}
