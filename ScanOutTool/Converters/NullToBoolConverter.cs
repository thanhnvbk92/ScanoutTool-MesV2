using System;
using System.Globalization;
using System.Windows.Data;

namespace ScanOutTool.Converters
{
    public class NullToBoolConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => Invert ? value != null : value == null;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
