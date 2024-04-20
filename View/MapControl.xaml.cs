using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using static iBarter.EnumLists;
using Grid = System.Windows.Controls.Grid;
using RowColumnIndex = Syncfusion.UI.Xaml.ScrollAxis.RowColumnIndex;


namespace iBarter.View {
    /// <summary>
    /// Interaction logic for MapControl.xaml
    /// </summary>
    public partial class MapControl : UserControl {
        public List<Grid> listGrid_Islands = new List<Grid>();

        public MapControl() {
            InitializeComponent();
            InitTempGrid();
        }

        public void InitTempGrid() {
            Islands myIsland = new Islands(Island.Ancient, 1000, 10);
            Barter myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Cox_Pirates, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Rickun, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Cholace, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Haran, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Carrack, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Unfinished, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Lantinia, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Pakio, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Wandering, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Crow, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Halmad, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Kashuma, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Derko, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);

            myIsland = new Islands(Island.Hakoven, 0, 0);
            myBater = new Barter(myIsland, new Items("", "000", "0"), new Items("", "000", "0"), 0, false, 0, 0, 0);
            IslandsButtonInitialisation(myBater, Brushes.DarkSlateGray);
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
                Grid Grid_Image = null;
                Label myLabel = null;
                if (myIslands != null) {
                    Grid_Image = FindGrid(grid, "GridImage_" + myIslands.IslandsName);
                    myLabel = FindLabel(grid, "Label_" + myIslands.IslandsName);
                }
                else {
                    myIslands = App.listIslands.FirstOrDefault(i => i.IslandsName + "Temp" == grid.Name.Substring(14, grid.Name.Length - 14));
                    Grid_Image = FindGrid(grid, "GridImage_" + myIslands.IslandsName + "Temp");
                    myLabel = FindLabel(grid, "Label_" + myIslands.IslandsName + "Temp");
                }

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


                    Grid_Image.Margin = new Thickness(myIslands.IslandsThickness.Left * Grid_MapMain.ActualWidth, myIslands.IslandsThickness.Top * Grid_MapMain.ActualHeight, myIslands.IslandsThickness.Right * Grid_MapMain.ActualWidth, myIslands.IslandsThickness.Bottom * Grid_MapMain.ActualHeight);

                    myLabel.Margin = new Thickness(Grid_Image.Margin.Left - myLabel.Width / 2, Grid_Image.Margin.Top, Grid_Image.Margin.Right - myLabel.Width, Grid_Image.Margin.Bottom - myLabel.Height);
                    ;

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

            if (_barter.Item1Name != "" && _barter.Item2Name != null) {
                myGrid_Container.Name = "GridContainer_" + _barter.IsLandName;
                myGrid_Container.MouseLeftButtonDown += Islands_MouseLeftButtonDown;
                myGrid_Container.MouseRightButtonDown += Islands_MouseRightButtonDown;
            }
            else {
                myGrid_Container.Name = "GridContainer_" + _barter.IsLandName + "Temp";
            }

            Grid myGrid_Image = new Grid();
            if (_barter.Item1Name != "" && _barter.Item2Name != null) {
                myGrid_Image.Name = "GridImage_" + _barter.IsLandName;
            }
            else {
                myGrid_Image.Name = "GridImage_" + _barter.IsLandName + "Temp";
            }


            myGrid_Image.Margin = new Thickness(_barter.IsLand.IslandsThickness.Left * Grid_MapMain.ActualWidth, _barter.IsLand.IslandsThickness.Top * Grid_MapMain.ActualHeight, _barter.IsLand.IslandsThickness.Right * Grid_MapMain.ActualWidth, _barter.IsLand.IslandsThickness.Bottom * Grid_MapMain.ActualHeight);
            myGrid_Image.Width = 10;
            myGrid_Image.Height = 10;

            Rectangle myRectangle = new Rectangle();
            myRectangle.Fill = _brush;

            Image myImage = new Image();
            if (_barter.Item1Name != "" && _barter.Item2Name != null) {
                myImage.Name = "Image_" + _barter.IsLand.IslandsName;
            }
            else {
                myImage.Name = "Image_" + _barter.IsLand.IslandsName + "Temp";
            }

            myImage.HorizontalAlignment = HorizontalAlignment.Center;
            myImage.VerticalAlignment = VerticalAlignment.Center;
            myImage.Width = 10;
            myImage.Height = 10;
            myImage.Margin = new Thickness(0);

            Label myLabel = new Label();
            if (_barter.Item1Name != "" && _barter.Item2Name != null) {
                myLabel.Name = "Label_" + _barter.IsLand.IslandsName;
            }
            else {
                myLabel.Name = "Label_" + _barter.IsLand.IslandsName + "Temp";
            }

            myLabel.Foreground = _brush;
            myLabel.HorizontalAlignment = HorizontalAlignment.Left;
            if (_barter.Item1Name != "" && _barter.Item2Name != "") {
                myLabel.Content = "[" + _barter.Item1Number * _barter.ExchangeQuantity + "] " + _barter.Item1Name + " => " + _barter.Item2Name + " [" + _barter.Item2Number * _barter.ExchangeQuantity + "]";
            }
            else {
                myLabel.Content = "";
            }
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

        private void Islands_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (e.ClickCount == 2) {
                var clickedGrid = sender as Grid;
                if (clickedGrid != null) {
                    App.myPVM.BarterCollection.FirstOrDefault(b => b.IsLandName == clickedGrid.Name.Substring(14, clickedGrid.Name.Length - 14))!.ExchangeDone = true;
                    App.myfmMain.myPlannerControl.Grouping();
                }
            }
            else if (e.ClickCount == 1) {
                //Grid_MapMain.Children.Add(myTTC);
                //Grid_Albresser.Children.Add(myTTC);
            }
        }

        private void Islands_MouseRightButtonDown(object sender, MouseButtonEventArgs e) {
            var clickedGrid = sender as Grid;
            if (clickedGrid != null) {
                Barter? myBarter = App.myPVM.BarterCollection.FirstOrDefault(b => b.IsLandName == clickedGrid.Name.Substring(14, clickedGrid.Name.Length - 14));
                if (myBarter != null) {
                    App.myfmMain.dockingManager_Main.ActiveWindow = App.myfmMain.document_Planner;
                    App.myfmMain.myPlannerControl.DataGrid_Planner.SelectedItem = myBarter;

                    App.myfmMain.myPlannerControl.DataGrid_Planner.ScrollInView(new RowColumnIndex(App.myfmMain.myPlannerControl.DataGrid_Planner.SelectedIndex, 0));
                }
            }
        }
    }
}