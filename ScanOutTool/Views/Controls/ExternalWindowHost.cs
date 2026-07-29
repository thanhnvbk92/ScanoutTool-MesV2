using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ScanOutTool.Views.Controls
{
    public class ExternalWindowHost : FrameworkElement
    {
        public static readonly DependencyProperty WindowTitleContainsProperty =
            DependencyProperty.Register(
                nameof(WindowTitleContains),
                typeof(string),
                typeof(ExternalWindowHost),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty ProcessPathProperty =
            DependencyProperty.Register(
                nameof(ProcessPath),
                typeof(string),
                typeof(ExternalWindowHost),
                new PropertyMetadata(string.Empty));

        private const long WS_VISIBLE = 0x10000000;
        private const long WS_CAPTION = 0x00C00000;
        private const long WS_THICKFRAME = 0x00040000;
        private const long WS_SYSMENU = 0x00080000;
        private const long WS_MINIMIZEBOX = 0x00020000;
        private const long WS_MAXIMIZEBOX = 0x00010000;
        private const long WS_POPUP = 0x80000000;
        private const long WS_EX_APPWINDOW = 0x00040000;
        private const long WS_EX_TOOLWINDOW = 0x00000080;
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int GWL_HWNDPARENT = -8;
        private const uint GA_ROOT = 2;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_HIDEWINDOW = 0x0080;
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const int SW_HIDE = 0;
        private static readonly IntPtr HwndTop = IntPtr.Zero;

        private readonly DispatcherTimer _dockTimer;
        private IntPtr _dockedWindowHandle;
        private long? _originalStyle;
        private long? _originalExStyle;
        private IntPtr _originalOwnerHandle;
        private bool _isTemporarilyHidden;

        public ExternalWindowHost()
        {
            _dockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _dockTimer.Tick += (_, _) => DockExternalWindow();

            Loaded += (_, _) => _dockTimer.Start();
            Unloaded += (_, _) =>
            {
                _dockTimer.Stop();
                RestoreExternalWindowStyle();
            };
            IsVisibleChanged += (_, _) => DockExternalWindow();
            SizeChanged += (_, _) => DockExternalWindow();
        }

        public string WindowTitleContains
        {
            get => (string)GetValue(WindowTitleContainsProperty);
            set => SetValue(WindowTitleContainsProperty, value);
        }

        public string ProcessPath
        {
            get => (string)GetValue(ProcessPathProperty);
            set => SetValue(ProcessPathProperty, value);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(25, 25, 25)), null, new Rect(RenderSize));
        }

        private void DockExternalWindow()
        {
            if (!IsLoaded || !IsVisible || ActualWidth < 2 || ActualHeight < 2)
            {
                return;
            }

            if (_dockedWindowHandle != IntPtr.Zero && IsCurrentProcessDialogForeground())
            {
                HideDockedWindow();
                return;
            }

            IntPtr windowHandle = FindWindow(ProcessPath, WindowTitleContains);
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            if (windowHandle != _dockedWindowHandle)
            {
                RestoreExternalWindowStyle();
                _dockedWindowHandle = windowHandle;
                _originalStyle = GetWindowLongPtr(windowHandle, GWL_STYLE).ToInt64();
                _originalExStyle = GetWindowLongPtr(windowHandle, GWL_EXSTYLE).ToInt64();
                _originalOwnerHandle = GetWindowLongPtr(windowHandle, GWL_HWNDPARENT);
            }

            ApplyDockedStyle(windowHandle);
            MoveToDashboardSlot(windowHandle);
            _isTemporarilyHidden = false;
        }

        private void ApplyDockedStyle(IntPtr windowHandle)
        {
            ShowWindow(windowHandle, SW_RESTORE);

            long style = GetWindowLongPtr(windowHandle, GWL_STYLE).ToInt64();
            long dockedStyle = (style | WS_POPUP | WS_VISIBLE)
                & ~WS_CAPTION
                & ~WS_THICKFRAME
                & ~WS_SYSMENU
                & ~WS_MINIMIZEBOX
                & ~WS_MAXIMIZEBOX;

            long exStyle = GetWindowLongPtr(windowHandle, GWL_EXSTYLE).ToInt64();
            long dockedExStyle = (exStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;

            SetWindowLongPtr(windowHandle, GWL_STYLE, ToWindowLongPtr(dockedStyle));
            SetWindowLongPtr(windowHandle, GWL_EXSTYLE, ToWindowLongPtr(dockedExStyle));
            SetWindowLongPtr(windowHandle, GWL_HWNDPARENT, GetOwnerWindowHandle());
            ShowWindow(windowHandle, SW_SHOW);
        }

        private void MoveToDashboardSlot(IntPtr windowHandle)
        {
            Point topLeft = PointToScreen(new Point(0, 0));
            int x = (int)Math.Round(topLeft.X);
            int y = (int)Math.Round(topLeft.Y);
            int width = Math.Max(1, (int)Math.Round(ActualWidth));
            int height = Math.Max(1, (int)Math.Round(ActualHeight));

            SetWindowPos(windowHandle, HwndTop, x, y, width, height, SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            InvalidateRect(windowHandle, IntPtr.Zero, true);
            UpdateWindow(windowHandle);
        }

        private void RestoreExternalWindowStyle()
        {
            if (_dockedWindowHandle == IntPtr.Zero)
            {
                return;
            }

            if (_originalStyle.HasValue)
            {
                SetWindowLongPtr(_dockedWindowHandle, GWL_STYLE, ToWindowLongPtr(_originalStyle.Value));
            }

            if (_originalExStyle.HasValue)
            {
                SetWindowLongPtr(_dockedWindowHandle, GWL_EXSTYLE, ToWindowLongPtr(_originalExStyle.Value));
            }

            SetWindowLongPtr(_dockedWindowHandle, GWL_HWNDPARENT, _originalOwnerHandle);
            SetWindowPos(_dockedWindowHandle, HwndTop, 80, 80, 1280, 800, SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            _dockedWindowHandle = IntPtr.Zero;
            _originalStyle = null;
            _originalExStyle = null;
            _originalOwnerHandle = IntPtr.Zero;
            _isTemporarilyHidden = false;
        }

        private IntPtr GetOwnerWindowHandle()
        {
            Window ownerWindow = Window.GetWindow(this);
            return ownerWindow == null ? IntPtr.Zero : new WindowInteropHelper(ownerWindow).Handle;
        }

        private void HideDockedWindow()
        {
            if (_isTemporarilyHidden)
            {
                return;
            }

            ShowWindow(_dockedWindowHandle, SW_HIDE);
            SetWindowPos(_dockedWindowHandle, HwndTop, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_HIDEWINDOW);
            _isTemporarilyHidden = true;
        }

        private bool IsCurrentProcessDialogForeground()
        {
            IntPtr foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                return false;
            }

            GetWindowThreadProcessId(foregroundWindow, out int processId);
            return processId == Environment.ProcessId && foregroundWindow != GetOwnerWindowHandle();
        }

        private static IntPtr FindWindow(string processPath, string titleContains)
        {
            IntPtr windowHandle = FindWindowByProcessPath(processPath);
            return windowHandle != IntPtr.Zero ? windowHandle : FindWindowByTitle(titleContains);
        }

        private static IntPtr FindWindowByProcessPath(string processPath)
        {
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return IntPtr.Zero;
            }

            string expectedProcessName = Path.GetFileNameWithoutExtension(processPath);
            string expectedFullPath = Path.GetFullPath(processPath);

            foreach (Process process in Process.GetProcesses().Where(p => IsExpectedProcess(p, expectedProcessName, expectedFullPath)))
            {
                IntPtr windowHandle = FindWindowByProcessId(process.Id);
                if (windowHandle != IntPtr.Zero)
                {
                    return windowHandle;
                }
            }

            return IntPtr.Zero;
        }

        private static bool IsExpectedProcess(Process process, string expectedProcessName, string expectedFullPath)
        {
            try
            {
                if (!string.Equals(process.ProcessName, expectedProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string processPath = process.MainModule?.FileName;
                return string.IsNullOrWhiteSpace(processPath)
                    || string.Equals(Path.GetFullPath(processPath), expectedFullPath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(process.ProcessName, expectedProcessName, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static IntPtr FindWindowByTitle(string titleContains)
        {
            if (string.IsNullOrWhiteSpace(titleContains))
            {
                return IntPtr.Zero;
            }

            IntPtr foundHandle = IntPtr.Zero;
            int currentProcessId = Environment.ProcessId;

            EnumWindows((windowHandle, _) =>
            {
                if (!IsAttachableTopLevelWindow(windowHandle))
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

        private static IntPtr FindWindowByProcessId(int expectedProcessId)
        {
            IntPtr foundHandle = IntPtr.Zero;

            EnumWindows((windowHandle, _) =>
            {
                if (!IsAttachableTopLevelWindow(windowHandle))
                {
                    return true;
                }

                GetWindowThreadProcessId(windowHandle, out int processId);
                if (processId != expectedProcessId)
                {
                    return true;
                }

                foundHandle = windowHandle;
                return false;
            }, IntPtr.Zero);

            return foundHandle;
        }

        private static bool IsAttachableTopLevelWindow(IntPtr windowHandle)
        {
            if (!IsWindowVisible(windowHandle))
            {
                return false;
            }

            if (GetAncestor(windowHandle, GA_ROOT) != windowHandle)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(GetWindowTitle(windowHandle));
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
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateWindow(IntPtr hWnd);

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
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

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
