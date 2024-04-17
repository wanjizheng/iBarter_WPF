using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows;

namespace iBarter {
    public class ColorConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            try {
                if (value != null && (value as Barter) != null) {
                    var input = (value as Barter).BarterGroup;

                    //custom condition is checked based on data.
                    switch (input) {
                        case -1:
                            return Brushes.Bisque;
                            break;
                        case 0:
                            return Brushes.Aquamarine;
                            break;
                        case 1:
                            return Brushes.CornflowerBlue;
                            break;
                        case 2:
                            return Brushes.DarkCyan;
                            break;
                        case 3:
                            return Brushes.CadetBlue;
                            break;
                        case 4:
                            return Brushes.Chocolate;
                            break;
                        case 5:
                            return Brushes.BurlyWood;
                            break;
                        case 6:
                            return Brushes.DarkTurquoise;
                            break;
                        case 7:
                            return Brushes.Ivory;
                            break;
                        case 8:
                            return Brushes.DeepSkyBlue;
                            break;
                        case 9:
                            return Brushes.DarkSalmon;
                            break;
                        case 10:
                            return Brushes.LawnGreen;
                            break;
                        case 11:
                            return Brushes.Orchid;
                            break;
                        case 12:
                            return Brushes.Olive;
                            break;
                        case 13:
                            return Brushes.Orange;
                            break;
                        case 14:
                            return Brushes.Plum;
                            break;
                        case 15:
                            return Brushes.DarkViolet;
                            break;
                        case 16:
                            return Brushes.Yellow;
                            break;
                        case 17:
                            return Brushes.Tomato;
                            break;
                        case 18:
                            return Brushes.MediumSlateBlue;
                            break;
                        case 19:
                            return Brushes.SpringGreen;
                            break;
                        case 20:
                            return Brushes.SandyBrown;
                            break;
                        default:
                            return Brushes.RoyalBlue;
                            break;
                    }
                }
                else {
                    return DependencyProperty.UnsetValue;
                }
            }
            catch (Exception e) {
                App.myCFun.Log(e.Message, Brushes.Red);
                return DependencyProperty.UnsetValue;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}