using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ScanOutTool.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool CollapseWhenFalse { get; set; } = true;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (bool)value
                ? Visibility.Visible
                : (CollapseWhenFalse ? Visibility.Collapsed : Visibility.Hidden);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => (Visibility)value == Visibility.Visible;
    }
}
