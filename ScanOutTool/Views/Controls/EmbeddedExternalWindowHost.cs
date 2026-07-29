using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ScanOutTool.Views.Controls
{
    public class EmbeddedExternalWindowHost : HwndHost
    {
        public static readonly DependencyProperty WindowTitleContainsProperty =
            DependencyProperty.Register(
                nameof(WindowTitleContains),
                typeof(string),
                typeof(EmbeddedExternalWindowHost),
                new PropertyMetadata(string.Empty));

        private const long WS_CHILD = 0x40000000;
        private const long WS_VISIBLE = 0x10000000;
        private const long WS_CAPTION = 0x00C00000;
        private const long WS_THICKFRAME = 0x00040000;
        private const long WS_SYSMENU = 0x00080000;
        private const long WS_MINIMIZEBOX = 0x00020000;
        private const long WS_MAXIMIZEBOX = 0x00010000;
        private const long WS_POPUP = 0x80000000;
        private const int WS_CLIPCHILDREN = 0x02000000;
        private const int WS_CLIPSIBLINGS = 0x04000000;
        private const int GWL_STYLE = -16;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private static readonly IntPtr HwndTop = IntPtr.Zero;

        private readonly DispatcherTimer _attachTimer;
        private IntPtr _hostHandle;
        private IntPtr _embeddedWindowHandle;

        public EmbeddedExternalWindowHost()
        {
            _attachTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _attachTimer.Tick += (_, _) => TryAttachExternalWindow();
            SizeChanged += (_, _) => ResizeEmbeddedWindow();
        }

        public string WindowTitleContains
        {
            get => (string)GetValue(WindowTitleContainsProperty);
            set => SetValue(WindowTitleContainsProperty, value);
        }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            _hostHandle = CreateWindowEx(
                0,
                "static",
                string.Empty,
                (int)(WS_CHILD | WS_VISIBLE) | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
                0,
                0,
                Math.Max(1, (int)ActualWidth),
                Math.Max(1, (int)ActualHeight),
                hwndParent.Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            _attachTimer.Start();
            return new HandleRef(this, _hostHandle);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            _attachTimer.Stop();
            if (_embeddedWindowHandle != IntPtr.Zero)
            {
                SetParent(_embeddedWindowHandle, IntPtr.Zero);
                _embeddedWindowHandle = IntPtr.Zero;
            }

            DestroyWindow(hwnd.Handle);
            _hostHandle = IntPtr.Zero;
        }

        private void TryAttachExternalWindow()
        {
            if (_hostHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(WindowTitleContains))
            {
                return;
            }

            IntPtr externalWindowHandle = FindWindowByTitle(WindowTitleContains);
            if (externalWindowHandle == IntPtr.Zero || externalWindowHandle == _embeddedWindowHandle)
            {
                return;
            }

            ShowWindow(externalWindowHandle, SW_RESTORE);

            long style = GetWindowLongPtr(externalWindowHandle, GWL_STYLE).ToInt64();
            long childStyle = (style | WS_CHILD | WS_VISIBLE)
                & ~WS_POPUP
                & ~WS_CAPTION
                & ~WS_THICKFRAME
                & ~WS_SYSMENU
                & ~WS_MINIMIZEBOX
                & ~WS_MAXIMIZEBOX;

            SetParent(externalWindowHandle, _hostHandle);
            SetWindowLongPtr(externalWindowHandle, GWL_STYLE, ToWindowLongPtr(childStyle));

            _embeddedWindowHandle = externalWindowHandle;
            ShowWindow(_embeddedWindowHandle, SW_SHOW);
            ResizeEmbeddedWindow();
        }

        private void ResizeEmbeddedWindow()
        {
            if (_embeddedWindowHandle == IntPtr.Zero)
            {
                return;
            }

            int width = Math.Max(1, (int)ActualWidth);
            int height = Math.Max(1, (int)ActualHeight);
            SetWindowPos(_embeddedWindowHandle, HwndTop, 0, 0, width, height, SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        }

        private static IntPtr FindWindowByTitle(string titleContains)
        {
            IntPtr foundHandle = IntPtr.Zero;
            int currentProcessId = Environment.ProcessId;

            EnumWindows((windowHandle, _) =>
            {
                if (!IsWindowVisible(windowHandle))
                {
                    return true;
                }

                GetWindowThreadProcessId(windowHandle, out int processId);
                if (processId == currentProcessId)
                {
                    return true;
                }

                string title = GetWindowTitle(windowHandle);
                if (title.IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    foundHandle = windowHandle;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return foundHandle;
        }

        private static string GetWindowTitle(IntPtr windowHandle)
        {
            int length = GetWindowTextLength(windowHandle);
            if (length == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(length + 1);
            GetWindowText(windowHandle, builder, builder.Capacity);
            return builder.ToString();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
        }

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);
        }

        private static IntPtr ToWindowLongPtr(long value)
        {
            return IntPtr.Size == 8
                ? new IntPtr(value)
                : new IntPtr(unchecked((int)value));
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    }
}
