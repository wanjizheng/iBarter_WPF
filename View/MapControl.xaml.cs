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
using Esri.ArcGISRuntime.UI;
using iBarter.View;
using static iBarter.EnumLists;
using Grid = System.Windows.Controls.Grid;

namespace iBarter {
    /// <summary>
    /// Interaction logic for MapControl.xaml
    /// </summary>
    public partial class MapControl : UserControl {
        public List<Grid> listGrid_Islands = new List<Grid>();

        public MapControl() {
            InitializeComponent();
            // Islands myIsland = new Islands(Island.Albresser, 1000, 10);
            // Barter myBater = new Barter(myIsland, App.listItems.FirstOrDefault(i => i.ItemName == "Panacea"), App.listItems.First(i => i.ItemName == "Mysterious Rock"), 10, false, 0, 0, 0);
            // IslandsButtonInitialisation(myBater, Brushes.Gold);
        }

        private void Grid_MapMain_SizeChanged(object sender, SizeChangedEventArgs e) {
            IslandsButtonRearrange();
        }

        private void IslandsButtonRearrange() {
            // Grid_Ajir.Margin = new Thickness(0.6325 * Grid_MapMain.ActualWidth, 0.55333 * Grid_MapMain.ActualHeight,
            //     0.355 * Grid_MapMain.ActualWidth, 0.42444 * Grid_MapMain.ActualHeight);
            // Grid_Albresser.Margin = new Thickness(0.30375 * Grid_MapMain.ActualWidth,
            //     0.786666667 * Grid_MapMain.ActualHeight, 0.68375 * Grid_MapMain.ActualWidth,
            //     0.191111111 * Grid_MapMain.ActualHeight);

            foreach (Grid grid in listGrid_Islands) {
                Islands myIslands = App.listIslands.FirstOrDefault(i => i.IslandsName == grid.Name.Substring(14, grid.Name.Length - 14));
                Grid Grid_Image = FindGrid(grid, "GridImage_" + myIslands.IslandsName);
                Label myLabel = FindLabel(grid, "Label_" + myIslands.IslandsName);
                if (myIslands != null) {
                    if (myLabel.ActualWidth != 0 && myLabel.ActualHeight != 0) {
                        myLabel.Width = myLabel.ActualWidth;
                        myLabel.Height = myLabel.ActualHeight;
                    }
                    else {
                        var size = new Size(Double.PositiveInfinity, Double.PositiveInfinity);
                        myLabel.Measure(size);
                        myLabel.Arrange(new Rect(myLabel.DesiredSize));
                        myLabel.Width = myLabel.ActualWidth;
                        myLabel.Height = myLabel.ActualHeight;
                    }

                    // myLabel.Width = 100;
                    // myLabel.Height = 50;
                    Grid_Image.Margin = new Thickness(myIslands.IslandsThickness.Left * Grid_MapMain.ActualWidth, myIslands.IslandsThickness.Top * Grid_MapMain.ActualHeight, myIslands.IslandsThickness.Right * Grid_MapMain.ActualWidth, myIslands.IslandsThickness.Bottom * Grid_MapMain.ActualHeight);
                    //myLabel.Margin = new Thickness(double.Max(0, Grid_Image.Margin.Left - myLabel.ActualWidth), Grid_Image.Margin.Top + Grid_Image.ActualHeight, double.Min(Grid_Image.Margin.Right, Grid_Image.Margin.Right + myLabel.ActualWidth), Grid_Image.Margin.Top + myLabel.ActualHeight);
                    //myLabel.Margin = Grid_Image.Margin;
                    //myLabel.Margin = new Thickness(0);
                    myLabel.Margin = new Thickness(Grid_Image.Margin.Left - myLabel.Width / 2, Grid_Image.Margin.Top, Grid_Image.Margin.Right - myLabel.Width, Grid_Image.Margin.Bottom - myLabel.Height);
                    //App.myCFun.Log(myLabel.Margin.ToString(), Brushes.Red);

                    NewMargin(myLabel);

                }
            }
        }

        private void NewMargin(Label _label) {
            double rightEdge = _label.Margin.Left + _label.ActualWidth;
            double bottomEdge = _label.Margin.Top + _label.ActualHeight;

            double newLeftMargin = _label.Margin.Left;
            double newTopMargin = _label.Margin.Top;

            // 检查并调整右边界
            if (rightEdge > this.ActualWidth) {
                newLeftMargin = this.ActualWidth - _label.ActualWidth;
                newLeftMargin = Math.Max(0, newLeftMargin); // 避免负边距
            }

            // 检查左边界
            if (_label.Margin.Left < 0) {
                newLeftMargin = 0; // 确保 Label 不超出左边界
            }

            // 检查并调整底边界
            if (bottomEdge > this.ActualHeight) {
                newTopMargin = this.ActualHeight - _label.ActualHeight;
                newTopMargin = Math.Max(0, newTopMargin); // 避免负边距
            }

            // 应用新的边距
            _label.Margin = new Thickness(newLeftMargin, newTopMargin, _label.Margin.Right, _label.Margin.Bottom);
        }

        private Grid FindGrid(Grid _grid, string _name) {
            foreach (var child in _grid.Children) {
                if (child is Grid && ((Grid)child).Name == _name) {
                    return (Grid)child;
                }
            }

            return null;
        }

        private Label FindLabel(Grid _grid, string _name) {
            foreach (var child in _grid.Children) {
                if (child is Label && ((Label)child).Name == _name) {
                    return (Label)child;
                }
            }

            return null;
        }

        public void IslandsButtonInitialisation(Barter _barter, Brush _brush) {
            Grid myGrid_Container = new Grid();
            myGrid_Container.Name = "GridContainer_" + _barter.IsLandName;
            Grid myGrid_Image = new Grid();
            myGrid_Image.Name = "GridImage_" + _barter.IsLandName;
            myGrid_Image.Margin = new Thickness(_barter.IsLand.IslandsThickness.Left * Grid_MapMain.ActualWidth, _barter.IsLand.IslandsThickness.Top * Grid_MapMain.ActualHeight, _barter.IsLand.IslandsThickness.Right * Grid_MapMain.ActualWidth, _barter.IsLand.IslandsThickness.Bottom * Grid_MapMain.ActualHeight);
            myGrid_Image.MouseLeftButtonDown += IslandsImage_MouseLeftButtonDown;
            myGrid_Image.Width = 10;
            myGrid_Image.Height = 10;

            Rectangle myRectangle = new Rectangle();
            myRectangle.Fill = _brush;

            Image myImage = new Image();
            myImage.Name = "Image_" + _barter.IsLand.IslandsName;
            myImage.HorizontalAlignment = HorizontalAlignment.Center;
            myImage.VerticalAlignment = VerticalAlignment.Center;
            myImage.Width = 10;
            myImage.Height = 10;
            myImage.Margin = new Thickness(0);

            Label myLabel = new Label();
            myLabel.Name = "Label_" + _barter.IsLand.IslandsName;
            myLabel.Foreground = _brush;
            myLabel.HorizontalAlignment = HorizontalAlignment.Left;
            myLabel.Content = "[" + _barter.Item1Number * _barter.ExchangeQuantity + "] " + _barter.Item1Name + " => " + _barter.Item2Name + " [" + _barter.Item2Number * _barter.ExchangeQuantity + "]";
            //myLabel.Margin = new Thickness(double.Max(0, Grid_MapMain.Margin.Left - myLabel.ActualWidth), Grid_MapMain.Margin.Top + myGrid_Image.ActualHeight, double.Min(Grid_MapMain.Margin.Right, Grid_MapMain.Margin.Right + myLabel.ActualWidth), Grid_MapMain.Margin.Top + myLabel.ActualHeight);

            if (myLabel.ActualWidth != 0 && myLabel.ActualHeight != 0) {
                myLabel.Width = myLabel.ActualWidth;
                myLabel.Height = myLabel.ActualHeight;
            }
            else {
                var size = new Size(Double.PositiveInfinity, Double.PositiveInfinity);
                myLabel.Measure(size);
                myLabel.Arrange(new Rect(myLabel.DesiredSize));
                myLabel.Width = myLabel.ActualWidth;
                myLabel.Height = myLabel.ActualHeight;
            }

            myLabel.Margin = new Thickness(myGrid_Image.Margin.Left - myLabel.Width / 2, myGrid_Image.Margin.Top, myGrid_Image.Margin.Right - myLabel.Width, myGrid_Image.Margin.Bottom - myLabel.Height);

            NewMargin(myLabel);


            myGrid_Container.Children.Add(myGrid_Image);
            myGrid_Image.Children.Add(myRectangle);
            myGrid_Container.Children.Add(myLabel);

            Grid_MapMain.Children.Add(myGrid_Container);

            if (listGrid_Islands.FirstOrDefault(b => b.Name == "GridImage_" + _barter.IsLand.IslandsName) == null) {
                listGrid_Islands.Add(myGrid_Container);
            }

            IslandsButtonRearrange();
        }

        private void Image_Albresser_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (e.ClickCount == 2) {
                App.myCFun.Log("Double click", Brushes.Blue);
            }
            else if (e.ClickCount == 1) {
                //ToolTipControl myTTC = new ToolTipControl();
                //Grid_MapMain.Children.Add(myTTC);
                //Grid_Albresser.Children.Add(myTTC);
            }
        }

        private void IslandsImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (e.ClickCount == 2) {
                App.myCFun.Log("Double click", Brushes.Blue);
            }
            else if (e.ClickCount == 1) {
                ToolTipControl myTTC = new ToolTipControl();
                //Grid_MapMain.Children.Add(myTTC);
                //Grid_Albresser.Children.Add(myTTC);
            }
        }
    }
}