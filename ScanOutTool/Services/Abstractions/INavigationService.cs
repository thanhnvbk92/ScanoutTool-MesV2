using System.Windows.Controls;

namespace ScanOutTool.Services
{
    public interface INavigationService
    {
        void SetFrame(Frame frame);
        void NavigateTo<TPage>() where TPage : Page;
        void GoBack();
    }
}
