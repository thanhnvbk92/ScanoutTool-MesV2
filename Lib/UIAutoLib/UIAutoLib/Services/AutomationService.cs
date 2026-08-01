using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using UIAutoLib.Helpers;
using UIAutoLib.Interfaces;

namespace UIAutoLib.Services
{
    public class AutomationService : IAutomationService
    {
        #region Win32 API
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, string lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetDlgCtrlID(IntPtr hWnd);


        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        public const int SW_HIDE = 0;
        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_SHOWMAXIMIZED = 3;
        public const int SW_MINIMIZE = 6;
        public const int SW_RESTORE = 9;

        private const int CB_FINDSTRINGEXACT = 0x0158;
        private const int CB_SETCURSEL = 0x014E;
        private const int WM_COMMAND = 0x0111;
        private const int CBN_SELCHANGE = 1;
        #endregion

        private Application _app;
        private AutomationBase _automation;
        private Window _mainWindow;
        private AutomationElement _searchRoot;

        private Dictionary<string, AutomationElement> _elementByAutomationId;
        private Dictionary<string, AutomationElement> _elementByName;
        private AutomationElement[] _allElements;

        public int ProcessId { get; private set; }

        public bool AttachToProcess(string processName)
        {
            var process = Process.GetProcessesByName(processName)
                .OrderByDescending(p => HasMainWindow(p))
                .ThenByDescending(p => SafeGetStartTime(p))
                .FirstOrDefault();
            if (process == null)
                return false;
            return AttachToProcess(process);
        }

        public bool AttachToProcess(int processId)
        {
            return AttachToProcess(Process.GetProcessById(processId));
        }

        public bool AttachToProcessWindowByText(int processId, string automationId, string expectedText)
        {
            var process = Process.GetProcessById(processId);
            if (!AttachToProcessCore(process))
            {
                return false;
            }

            var windows = _app.GetAllTopLevelWindows(_automation);
            foreach (var window in windows.OrderByDescending(w => w.Title?.IndexOf("GMES ShopFloorControl", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                _mainWindow = window;
                ResetElementCache();

                string text = ReadTextByAutomationId(automationId);
                if (!string.IsNullOrWhiteSpace(text)
                    && text.IndexOf(expectedText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            _mainWindow = _app.GetMainWindow(_automation);
            ResetElementCache();
            return false;
        }

        private bool AttachToProcess(Process process)
        {
            return AttachToProcessCore(process);
        }

        private bool AttachToProcessCore(Process process)
        {
            ProcessId = process.Id;
            try
            {
                process.WaitForInputIdle(500);
            }
            catch
            {
                // Some legacy apps do not support WaitForInputIdle. Attaching can still continue.
            }

            _app = Application.Attach(process);
            _automation = new UIA3Automation();
            _mainWindow = _app.GetMainWindow(_automation);

            if (_mainWindow == null) return false;

            _searchRoot = _mainWindow;
            ResetElementCache(keepSearchRoot: true);
            return true;
        }

        private void ResetElementCache(bool keepSearchRoot = false)
        {
            _elementByAutomationId = new Dictionary<string, AutomationElement>();
            _elementByName = new Dictionary<string, AutomationElement>();
            _allElements = null;
            if (!keepSearchRoot)
            {
                _searchRoot = _mainWindow;
            }
        }

        public void ClearCache()
        {
            ResetElementCache(keepSearchRoot: true);
        }

        private static bool HasMainWindow(Process process)
        {
            try
            {
                return process.MainWindowHandle != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        private static DateTime SafeGetStartTime(Process process)
        {
            try
            {
                return process.StartTime;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private void CacheAllElements()
        {
            _allElements = SearchRoot.FindAllDescendants();

            _elementByAutomationId = _allElements
                .Select(e => new { Element = e, Id = SafeGetAutomationId(e) })
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .GroupBy(x => x.Id)
                .ToDictionary(g => g.Key, g => g.First().Element);

            _elementByName = _allElements
                .Select(e => new { Element = e, Name = SafeGetName(e) })
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Name)
                .ToDictionary(g => g.Key, g => g.First().Element);
        }

        private string SafeGetAutomationId(AutomationElement e)
        {
            try
            {
                return e.Properties.AutomationId?.Value ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string SafeGetName(AutomationElement e)
        {
            try
            {
                return e.Properties.Name?.Value ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }


        public string ReadTextByAutomationId(string automationId)
        {
            var element = FindByAutomationId(automationId);
            if (element != null)
            {
                // TextBox
                if (element.ControlType == FlaUI.Core.Definitions.ControlType.Edit)
                    return element.AsTextBox()?.Text ?? string.Empty;

                // Label / StaticText
                if (element.ControlType == FlaUI.Core.Definitions.ControlType.Text)
                    return element.AsLabel()?.Text ?? element.Name;

                // Nếu có ValuePattern
                if (element.Patterns.Value.IsSupported)
                    return element.Patterns.Value.Pattern.Value.Value ?? string.Empty;

                // Nếu có LegacyIAccessiblePattern (WinForms cũ)
                if (element.Patterns.LegacyIAccessible.IsSupported)
                    return element.Patterns.LegacyIAccessible.Pattern.Value ?? string.Empty;

                // Fallback
                return element.Name;
            }

            return string.Empty;
        }

        public string ReadTextByName(string name)
        {
            var element = FindByName(name);
            if (element != null)
                return element?.AsLabel()?.Text ?? element?.Name ?? string.Empty;

            return string.Empty;
        }

        public bool ClickByAutomationId(string automationId)
        {
            var element = FindByAutomationId(automationId);
            if (element != null)
            {
                var button = element.AsButton();
                if (button != null)
                {
                    button.Invoke();
                    return true;
                }
            }
            return false;
        }

        public bool ClickByAutomationIdPreserveForeground(string automationId)
        {
            IntPtr previousForeground = GetForegroundWindow();
            bool clicked = ClickByAutomationId(automationId);
            if (clicked && previousForeground != IntPtr.Zero)
            {
                System.Threading.Thread.Sleep(100);
                SetForegroundWindow(previousForeground);
            }

            return clicked;
        }

        public (double X, double Y, double Width, double Height) GetElementBounds(string automationId)
        {
            var element = FindByAutomationId(automationId);
            if (element != null)
            {
                var rect = element.BoundingRectangle;
                return (rect.X, rect.Y, rect.Width, rect.Height);
            }
            return (0, 0, 0, 0);
        }

        public (double X, double Y, double Width, double Height) GetElementBounds(AutomationElement element)
        {
            if (element != null)
            {
                var rect = element.BoundingRectangle;
                return (rect.X, rect.Y, rect.Width, rect.Height);
            }
            return (0, 0, 0, 0);
        }


        public bool SelectComboBoxItem(string automationId, string itemText)
        {
                EnsureAllElementsCached();
                if (_allElements == null || _allElements.Length == 0)
                {
                    return false;
                }

                // ✅ Tự động fallback nếu không có combo con
                var (parentCombo, innerCombo) = GetComboBoxPair(automationId);
                if (parentCombo == null || innerCombo == null)
                {
                    return false;
                }


                try
                {
                    if (SelectWpfComboBox(innerCombo, itemText)) return true;
                    if (SelectLegacyComboBox(innerCombo, itemText)) return true;
                    return false;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }

        #region 🧩 1️⃣ Lấy combo cha & combo con thật
        private (AutomationElement parent, AutomationElement inner) GetComboBoxPair(string automationId)
        {
            var parent = _allElements.FirstOrDefault(e =>
            SafeGetAutomationId(e) == automationId);

            if (parent == null)
            {
                Console.WriteLine($"❌ Không tìm thấy ComboBox '{automationId}'.");
                return (null, null);
            }

            var rect = parent.BoundingRectangle;
            var inner = _allElements.FirstOrDefault(e =>
                SafeGetControlType(e) == ControlType.ComboBox &&
                e.BoundingRectangle.IntersectsWith(rect) &&
                e != parent);

            // ✅ Nếu không có combo con, dùng luôn combo cha
            if (inner == null)
            {
                Console.WriteLine($"⚠️ ComboBox '{automationId}' không có combo con. Sẽ thao tác trực tiếp lên control chính.");
                inner = parent;
            }

            return (parent, inner);
        }
        #endregion


        /// <summary>
        /// Lấy ControlType một cách an toàn (không throw exception)
        /// </summary>
        private ControlType SafeGetControlType(AutomationElement e)
        {
            try
            {
                return e.Properties.ControlType?.Value ?? ControlType.Custom;
            }
            catch
            {
                return ControlType.Custom;
            }
        }

        #region 🧩 2️⃣ WPF ComboBox handler
        private bool SelectWpfComboBox(AutomationElement combo, string itemText)
        {
            try
            {
                // 0️⃣ Kiểm tra null hoặc không hỗ trợ WPF
                if (combo == null)
                {
                    Console.WriteLine("❌ ComboBox null.");
                    return false;
                }

                var framework = combo.Properties.FrameworkId.ValueOrDefault;
                if (!string.Equals(framework, "WPF", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"⚠️ ComboBox '{combo.Name}' không phải WPF (FrameworkId={framework}).");
                    return false;
                }

                Console.WriteLine($"🪟 Đang chọn '{itemText}' trong WPF ComboBox '{combo.Name}' (background-safe).");

                // 1️⃣ Nếu ComboBox hỗ trợ ValuePattern (editable ComboBox)
                if (combo.Patterns.Value.IsSupported)
                {
                    combo.Patterns.Value.Pattern.SetValue(itemText);
                    Console.WriteLine($"✅ Đặt text '{itemText}' bằng ValuePattern (editable mode).");
                    return true;
                }

                // 2️⃣ Thử chọn logic item (không Expand)
                var listItems = combo.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
                if (listItems?.Length > 0)
                {
                    var target = listItems.FirstOrDefault(i =>
                        i.Name.Equals(itemText, StringComparison.OrdinalIgnoreCase));

                    if (target?.Patterns.SelectionItem.IsSupported == true)
                    {
                        target.Patterns.SelectionItem.Pattern.Select();
                        Console.WriteLine($"✅ Chọn item '{itemText}' bằng SelectionItemPattern (logic-only).");
                        return true;
                    }
                }

                // 3️⃣ Nếu chưa có item trong logical tree → Expand tạm (vẫn background-safe)
                if (combo.Patterns.ExpandCollapse.IsSupported)
                {
                    Console.WriteLine("⚙️ Logical tree rỗng → Expand tạm để load item (có thể bring-front).");

                    // Gọi Expand tạm nhưng minimize window để tránh bring-front
                    var hwnd = (IntPtr)combo.Properties.NativeWindowHandle.Value;
                    bool minimized = false;
                    if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd))
                    {
                        minimized = true;
                        ShowWindow(hwnd, SW_MINIMIZE); // ⚠️ tránh bring front
                    }

                    var expandCollapse = combo.Patterns.ExpandCollapse.Pattern;
                    expandCollapse.Expand();
                    Thread.Sleep(300);

                    var target = combo.FindFirstDescendant(cf =>
                        cf.ByControlType(ControlType.ListItem).And(cf.ByName(itemText)));

                    if (target?.Patterns.SelectionItem.IsSupported == true)
                    {
                        target.Patterns.SelectionItem.Pattern.Select();
                        Console.WriteLine($"✅ Đã chọn '{itemText}' sau khi Expand.");
                    }
                    else
                    {
                        Console.WriteLine($"⚠️ Không tìm thấy '{itemText}' sau khi Expand.");
                    }

                    expandCollapse.Collapse();

                    if (minimized)
                        ShowWindow(hwnd, SW_RESTORE);

                    return true;
                }

                Console.WriteLine("⚠️ Không pattern nào phù hợp trong ComboBox này.");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [SelectWpfComboBox] Lỗi: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 🧩 4️⃣ Legacy ComboBox (MSAA)
        private bool SelectLegacyComboBox(AutomationElement combo, string itemText)
        {
            if (!combo.Patterns.LegacyIAccessible.IsSupported)
                return false;

            try
            {
                if (!combo.Patterns.LegacyIAccessible.IsSupported)
                {
                    Console.WriteLine($"⚠️ Control '{combo.Name}' không hỗ trợ LegacyIAccessiblePattern.");
                    return false;
                }

                var legacy = combo.Patterns.LegacyIAccessible.Pattern;

                // Đặt giá trị trực tiếp bằng MSAA API
                legacy.SetValue(itemText);

                // Gửi event SelectionChanged cho parent
                TriggerSelectionChanged(combo);

                Console.WriteLine($"✅ Đã chọn '{itemText}' qua MSAA (background-safe, không bring front)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ SelectMsaaComboBox_Background lỗi: {ex.Message}");
                return false;
            }
        }
        #endregion


        #region 🧩 6️⃣ Trigger event (WinForms/Legacy)
        private void TriggerSelectionChanged(AutomationElement element)
        {
            try
            {
                IntPtr hwnd = (IntPtr)element.Properties.NativeWindowHandle.Value;
                if (hwnd == IntPtr.Zero) return;

                SendMessage(GetParent(hwnd), WM_COMMAND,
                    (CBN_SELCHANGE << 16) | GetDlgCtrlID(hwnd), hwnd.ToInt32());
            }
            catch { }
        }
        #endregion


        public AutomationElement GetParrentElement(string automationId,int index)
        {
            EnsureAllElementsCached();
            if (_elementByAutomationId.TryGetValue(automationId, out var element))
            {
                var parent = AutomationExtensions.FindVisualParent(element, _allElements,1);               
                
                return parent;
            }
            return null;
        }

        public IntPtr GetMainHandle()
        {
            return _mainWindow.Properties.NativeWindowHandle;
        }

        public string GetMainWindowTitle()
        {
            return _mainWindow.Title;
        }

        public bool IsHaveDialogWindow()
        {
            var dialog = _mainWindow.FindFirstChild(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Window));
            return dialog != null;
        }

        public bool ShowWindowByTitle(string title)
        {
            if (_app == null || _automation == null || string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            var window = _app.GetAllTopLevelWindows(_automation)
                .FirstOrDefault(w => w.Title.IndexOf(title, System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (window == null)
            {
                return false;
            }

            IntPtr hwnd = (IntPtr)window.Properties.NativeWindowHandle.Value;
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
            return true;
        }

        private AutomationElement SearchRoot => _searchRoot ?? _mainWindow;

        private AutomationElement FindByAutomationId(string automationId)
        {
            if (string.IsNullOrWhiteSpace(automationId) || SearchRoot == null)
            {
                return null;
            }

            if (_elementByAutomationId != null && _elementByAutomationId.TryGetValue(automationId, out var cachedElement))
            {
                return cachedElement;
            }

            var element = SearchRoot.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
            if (element != null)
            {
                _elementByAutomationId ??= new Dictionary<string, AutomationElement>();
                _elementByAutomationId[automationId] = element;
            }

            return element;
        }

        private AutomationElement FindByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || SearchRoot == null)
            {
                return null;
            }

            if (_elementByName != null && _elementByName.TryGetValue(name, out var cachedElement))
            {
                return cachedElement;
            }

            var element = SearchRoot.FindFirstDescendant(cf => cf.ByName(name));
            if (element != null)
            {
                _elementByName ??= new Dictionary<string, AutomationElement>();
                _elementByName[name] = element;
            }

            return element;
        }

        private void EnsureAllElementsCached()
        {
            if (_allElements != null)
            {
                return;
            }

            CacheAllElements();
        }

        private string ReadElementText(AutomationElement element)
        {
            if (element == null)
            {
                return string.Empty;
            }

            if (element.ControlType == FlaUI.Core.Definitions.ControlType.Edit)
                return element.AsTextBox()?.Text ?? string.Empty;

            if (element.ControlType == FlaUI.Core.Definitions.ControlType.Text)
                return element.AsLabel()?.Text ?? element.Name;

            if (element.Patterns.Value.IsSupported)
                return element.Patterns.Value.Pattern.Value.Value ?? string.Empty;

            if (element.Patterns.LegacyIAccessible.IsSupported)
                return element.Patterns.LegacyIAccessible.Pattern.Value ?? string.Empty;

            return element.Name;
        }
    }
}
