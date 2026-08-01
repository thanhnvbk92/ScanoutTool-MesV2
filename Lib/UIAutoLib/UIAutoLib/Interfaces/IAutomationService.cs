using FlaUI.Core.AutomationElements;

namespace UIAutoLib.Interfaces
{
    public interface IAutomationService
    {
        bool AttachToProcess(string processName);
        string ReadTextByAutomationId(string automationId);
        bool ClickByAutomationId(string automationId);
        bool ShowWindowByTitle(string title);
        string ReadTextByName(string elementName);

        (double X, double Y, double Width, double Height) GetElementBounds(string automationId);
        (double X, double Y, double Width, double Height) GetElementBounds(AutomationElement element);
        AutomationElement GetParrentElement(string automationId,int index);

        IntPtr GetMainHandle();
    }
}
