using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace iBarter {
    /// <summary>
    /// Interaction logic for MapControl.xaml
    /// </summary>
    public partial class MapControl : UserControl {
        public MapControl() {
            InitializeComponent();
        }

        private void Grid_MapMain_SizeChanged(object sender, SizeChangedEventArgs e) {
            IslandsButtonRearrange();
            int a = 0;
        }

        public void IslandsButtonRearrange() {
            Grid_Ajir.Margin = new Thickness(0.6325 * Grid_MapMain.ActualWidth, 0.55333 * Grid_MapMain.ActualHeight, 0.355 * Grid_MapMain.ActualWidth, 0.42444 * Grid_MapMain.ActualHeight);
            Grid_Albresser.Margin = new Thickness(0.30375 * Grid_MapMain.ActualWidth, 0.786666667 * Grid_MapMain.ActualHeight, 0.68375 * Grid_MapMain.ActualWidth, 0.191111111 * Grid_MapMain.ActualHeight);
        }

        public void IslandsButtonInitialisation(Islands _islands, Brush _brush) {

        }
    }
}