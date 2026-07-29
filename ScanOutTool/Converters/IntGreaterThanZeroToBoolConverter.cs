using System;
using System.Globalization;
using System.Windows.Data;

namespace ScanOutTool.Converters
{
    public class IntGreaterThanZeroToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is int intValue && intValue > 0;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
