using System;
using System.Globalization;
using System.Windows.Data;

namespace ScanOutTool.Converters
{
    public class BoolToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isOpen = (bool)value;
            return isOpen ? 200 : 60; // Mở rộng 200px, thu gọn 60px
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
