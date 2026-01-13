using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WpfMapApp2
{
    public class NullToVisibilityConverter : IValueConverter
    {
        private static NullToVisibilityConverter _instance;
        public static NullToVisibilityConverter Instance => _instance ??= new NullToVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter?.ToString() == "inverse")
            {
                return value == null ? Visibility.Visible : Visibility.Collapsed;
            }
            return value == null ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}