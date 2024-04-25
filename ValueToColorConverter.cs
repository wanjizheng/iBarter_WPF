using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace iBarter {
    public class ValueToColorConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is int intValue && intValue > App.myCargoProperty.TotalLT - App.myCargoProperty.ExtraLT) {
                return Brushes.Red; // 返回红色
            }

            return Brushes.Black; // 默认返回黑色
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}