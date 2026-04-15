using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace PRPR.BooruViewer.Converters
{
    public class IntToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int intVal && parameter is string paramStr && int.TryParse(paramStr, out int target))
                return intVal == target;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolVal && boolVal && parameter is string paramStr && int.TryParse(paramStr, out int target))
                return target;
            return DependencyProperty.UnsetValue;
        }
    }
}
