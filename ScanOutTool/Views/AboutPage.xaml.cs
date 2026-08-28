using System;
using System.IO;
using System.Windows.Controls;

namespace ScanOutTool.Views
{
    public partial class AboutPage : Page
    {
        public AboutPage()
        {
            InitializeComponent();

            string guidePath = Path.Combine(AppContext.BaseDirectory, "Assets", "UserGuide.html");
            GuideBrowser.Navigate(new Uri(guidePath));
        }
    }
}
