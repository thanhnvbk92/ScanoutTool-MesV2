using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows.Controls;

namespace ScanOutTool.Services
{
    public class NavigationService : INavigationService
    {
        private readonly IServiceProvider _serviceProvider;
        private Frame _frame;

        public NavigationService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void SetFrame(Frame frame)
        {
            _frame = frame;
        }

        public void NavigateTo<TPage>() where TPage : Page
        {
            var page = _serviceProvider.GetRequiredService<TPage>();
            _frame?.Navigate(page);
        }

        public void GoBack()
        {
            if (_frame?.CanGoBack == true)
                _frame.GoBack();
        }
    }
}
