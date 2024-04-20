using iBarter.View;
using PureDM.Logging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace iBarter {
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {
        public MainWindow() {
            InitializeComponent();

            var timer = new DispatcherTimer();

            timer.Interval = TimeSpan.FromMilliseconds(10);
            timer.Tick += Timer_Tick;
            timer.Start();

            //var mapCenterPoint = new MapPoint(14, 21, SpatialReferences.Wgs84);
            //MainMapView.SetViewpoint(new Viewpoint(mapCenterPoint, 52541284));
        }

        // [DllImport("user32.dll")]
        // [return: MarshalAs(UnmanagedType.Bool)]
        // internal static extern bool GetCursorPos(ref Win32Point pt);
        //
        // public static PointPlus GetMousePosition() {
        //     var w32Mouse = new Win32Point();
        //     GetCursorPos(ref w32Mouse);
        //
        //     return new PointPlus(w32Mouse.X, w32Mouse.Y);
        // }

        private void Timer_Tick(object sender, EventArgs e) {
            //statusBarItem_XY.Text = "X: " + GetMousePosition().X + " | Y: " + GetMousePosition().Y;
            int intX, intY;
            App.myPureDM.DM.GetCursorPos(out intX, out intY);
            statusBarItem_XY.Text = "X: " + intX + " | Y: " + intY;


            int intWidth, intHeight;
            App.myPureDM.DM.GetClientSize((int)App.myPureDM.Hwnd, out intWidth, out intHeight);
            if (intWidth > 0 && intHeight > 0) {
                App.myPureDM.WindowWidth = intWidth;
                App.myPureDM.WindowHeight = intHeight;
                double x = (double)intX / App.myPureDM.WindowWidth;
                double y = (double)intY / App.myPureDM.WindowHeight;
                //toolStripStatusLabel_WinPercentage.Text = "X: " + x + " | Y: " + y;
            }
        }


        /// <summary>
        ///     Set the active window
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnActivateWindow(object sender, RoutedEventArgs e) {
            // var name = (sender as MenuItem).Tag as string;
            // dockingManager_Main.ActivateWindow(name);
            App.myBarterScanner = new BarterScanner();
            App.myBarterScanner.Show();
        }

        private void Test_Click(object sender, RoutedEventArgs e) {
            // int intX, intY;
            // App.dmSoft.FindPic(0, 0, 1920, 1080, "\\Images\\Items\\800025.png", "", 0.5, 0, out intX, out intY);
            // App.myCFun.Log("X: "+intX+" | Y: "+intY, Brushes.Green);

            //App.myCFun.downloadMap();
            //App.myCFun.mapMerge();

            //App.myCFun.UpdateItemImages("00800007");

            // App.dmSoft.Capture(0,0 , 1920, 1080, "screen.bmp");
            // ;
            // var m_SourceImage = new Image<Gray, float>(System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)+"\\Resources\\screen.bmp");
            // var m_CornerImage = new Image<Gray, float>("e:\\wanjizheng\\Documents\\MyProject\\iBDO\\iBarter\\bin\\Debug\\Resources\\Images\\anchor.bmp");
            // //CvInvoke.CornerHarris(m_SourceImage,m_CornerImage,3,3,0.01);
            // using (Image<Gray, float> result = m_SourceImage.MatchTemplate(m_CornerImage, TemplateMatchingType.CcoeffNormed))
            // {
            //     double[] minValues, maxValues;
            //     PointPlus[] minLocations, maxLocations;
            //     result.MinMax(out minValues, out maxValues, out minLocations, out maxLocations);
            //     if (maxValues[0] > 0.7)
            //     {
            //         App.myCFun.Log("X: " + maxLocations [0].X + " | Y: " + maxLocations[0].Y, Brushes.Green);
            //     }
            //
            // }


            //App.myCFun.RefreshItems();


            // var myPoint = App.myCFun.App.myPureDM.CV.FindPictures("\\Images\\Items\\800018.bmp", 0.4, 1, 0.5);
            // App.myCFun.Log("X: " + myPoint.X + " | Y: " + myPoint.Y + " | Sim: " + myPoint.Sim, Brushes.Green);


            // Thread myThread_SearchBarter = new Thread(App.myCFun.SearchBarter);
            // myThread_SearchBarter.IsBackground = false;
            // myThread_SearchBarter.Start();
        }


        private void Window_Loaded(object sender, RoutedEventArgs e) {
            try {
                Logging.SaveConsoleLog = false;
                Logging.TextBoxWriter = new TextBoxWriter(richTextBox_Log);
                App.myPureDM = new PureDM.PureDM("wanjizheng1c1f9b855a9f822cbf24afa526dfca3c");
                App.myPureDM.GetProcessName("BlackDesert64");
                App.myPureDM.BindMode = 103;
                App.myPureDM.DisplayMode = "dx.graphic.3d.10plus";
                App.myPureDM.MouseMode = "dx.public.active.api|dx.public.active.message|dx.mouse.position.lock.api|dx.mouse.state.api|dx.mouse.api|dx.mouse.focus.input.api|dx.mouse.focus.input.message|dx.mouse.clip.lock.api|dx.mouse.input.lock.api| dx.mouse.cursor";
                App.myPureDM.KeyboardMode = "dx.keypad.raw.input";
                App.myPureDM.PublicMode = "dx.public.graphic.protect|dx.public.anti.api|dx.public.km.protect|dx.mouse.raw.input|dx.mouse.cursor|dx.public.input.ime|dx.public.focus.message";


                App.myPureDM.DM.SetWindowState((int)App.myPureDM.Hwnd, 1);
                //int bindResult = App.dmSoft.BindWindowEx((int)App.myHwnd, "dx.graphic.3d.10plus", "dx.mouse.cursor|dx.mouse.raw.input", "windows", "dx.mouse.raw.input", 101);

                int bindResult = App.myPureDM.CV.BindWindow((int)App.myPureDM.Hwnd);


                //int bindResult = App.dmSoft.BindWindowEx((int)App.myHwnd, "dx2", "normal", "normal", "dx.public.km.protect|dx.public.anti.api|dx.public.inject.super|", 101);
                if (bindResult == 1) {
                    App.myCFun.Log("绑定游戏窗口成功", Brushes.Blue);
                    App.myPureDM.DM.SetWindowState((int)App.myPureDM.Hwnd, 4);
                }
                else {
                    App.myCFun.Log("绑定游戏窗口失败", Brushes.Red);
                }
            }
            catch (Exception exception) {
                App.myCFun.Log(exception.Message, Brushes.Red);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Win32Point {
            public int X;
            public int Y;
        }

        private void MenuItem_StorageManagement_Click(object sender, RoutedEventArgs e) {
            if (App.myStorageVM != null) {
                App.myStorageVM.StorageCollection.Clear();
            }
            App.myStorageManagement = new StorageManagement();
            App.myStorageManagement.Show();
        }
    }
}