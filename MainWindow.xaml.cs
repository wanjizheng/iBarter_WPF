using iBarter.View;
using PureDM.Logging;
using Syncfusion.SfSkinManager;
using Syncfusion.Windows.Shared;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace iBarter {
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : ChromelessWindow {
        public MainWindow() {
            InitializeComponent();
            this.DataContext = App.myMainWVM;
            App.myMainWVM.BlurVisibility = Visibility.Visible;
            //SfSkinManager.SetTheme(this, new Theme("Windows11Light"));
            this.WindowState = WindowState.Minimized;

            Thread thread = new Thread(() => {
                App.mySplashScreen = new SplashScreen();
                App.mySplashScreen.Show();
                System.Windows.Threading.Dispatcher.Run();
            });
            thread.SetApartmentState(ApartmentState.STA); // 设置线程为 STA
            thread.IsBackground = true;
            thread.Start();
            App.myMainWVM.OnSelectedProductChanged();

            // Without this, closing the main window leaves SplashScreen
            // open (its Close() is only called inside the game-binding
            // success branch - on bind failure it never closes), and
            // App.ShutdownMode = OnLastWindowClose keeps the process
            // alive for that open SplashScreen. Force-close splash +
            // shut down its background Dispatcher here regardless.
            this.Closing += MainWindow_Closing;
            Localization.LanguageService.Instance.LanguageChanged += (_, _) => ApplyLocalizedChrome();
            //var mapCenterPoint = new MapPoint(14, 21, SpatialReferences.Wgs84);
            //MainMapView.SetViewpoint(new Viewpoint(mapCenterPoint, 52541284));
        }

        // Ensure the splash screen and its background STA Dispatcher shut
        // down when the main window closes - otherwise OnLastWindowClose
        // sees SplashScreen still open and never exits the process.
        private void MainWindow_Closing(object? sender, CancelEventArgs e) {
            try {
                if (App.mySplashScreen != null && App.mySplashScreen.IsLoaded) {
                    var splash = App.mySplashScreen;
                    splash.Dispatcher.Invoke(() => {
                        try { splash.Close(); } catch { }
                        // Stop the background Dispatcher.Run() loop so the
                        // STA thread can unwind.
                        System.Windows.Threading.Dispatcher.ExitAllFrames();
                    });
                }
            }
            catch { }
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
            App.myPureDM.DM.GetClientSize((int)App.myPureDM.WindowHandle, out intWidth, out intHeight);
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
            if (App.myBarterScanner != null && App.myBarterScanner.IsLoaded) {
                App.myBarterScanner.Activate();
                return;
            }
            App.myBarterScanner = new BarterScanner();
            App.myBarterScanner.Closed += (_, _) => App.myBarterScanner = null;
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
            // var m_SourceImage = new Image<Gray, float>(System.IO.AppDomain.CurrentDomain.BaseDirectory+"\\Resources\\screen.bmp");
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
            // Phase 8 (i18n): sync the language menu's IsChecked marks
            // with whatever AppLanguage was loaded by LanguageService
            // InitializeAtStartup (which read it from Properties.Settings
            // in Phase 1).  The click handlers maintain the marks from
            // here on; we only seed the initial state.
            ApplyLocalizedChrome();

            try {
                // App.mySplashScreen.worker.ReportProgress(10);
                Logging.SaveConsoleLog = false;
                Logging.myTextBoxWriter = new TextBoxWriter(richTextBox_Log);
                App.myPureDM = new PureDM.DmAutomation("wanjizheng1c1f9b855a9f822cbf24afa526dfca3c");
                App.myPureDM.AttachToProcessByName("BlackDesert64");
                App.myPureDM.BindMode = 103;
                // App.myPureDM.MouseMode = "dx.public.active.api|dx.public.active.message|dx.mouse.position.lock.api|dx.mouse.state.api|dx.mouse.api|dx.mouse.focus.input.api|dx.mouse.focus.input.message|dx.mouse.clip.lock.api|dx.mouse.input.lock.api| dx.mouse.cursor";




                App.myPureDM.DisplayMode = "dx.graphic.3d.10plus";










                App.myPureDM.MouseMode = "dx.mouse.position.lock.api|dx.mouse.focus.input.api|dx.mouse.focus.input.message|dx.mouse.clip.lock.api|dx.mouse.state.api|dx.mouse.api|dx.mouse.cursor";
                App.myPureDM.KeyboardMode = "dx.keypad.input.lock.api|dx.keypad.state.api|dx.keypad.api";
                App.myPureDM.PublicMode = "dx.public.graphic.protect|dx.public.anti.api|dx.public.km.protect|dx.public.input.ime|dx.public.focus.message";

                // App.myPureDM.DisplayMode = "normal";
                // App.myPureDM.MouseMode = "normal";
                // App.myPureDM.KeyboardMode = "normal";
                // App.myPureDM.PublicMode = "";


                // App.mySplashScreen.worker.ReportProgress(50);
                if ((int)App.myPureDM.WindowHandle > 0) {
                    App.myPureDM.DM.SetWindowState((int)App.myPureDM.WindowHandle, 1);
                    //int bindResult = App.dmSoft.BindWindowEx((int)App.myHwnd, "dx.graphic.3d.10plus", "dx.mouse.cursor|dx.mouse.raw.input", "windows", "dx.mouse.raw.input", 101);

                    int bindResult = App.myPureDM.CV.BindWindow((int)App.myPureDM.WindowHandle);


                    // App.mySplashScreen.worker.ReportProgress(90);
                    //int bindResult = App.dmSoft.BindWindowEx((int)App.myHwnd, "dx2", "normal", "normal", "dx.public.km.protect|dx.public.anti.api|dx.public.inject.super|", 101);
                    if (bindResult == 1) {
                        App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.Binding.Success"), Brushes.Blue);
                        // App.myPureDM.DM.SetWindowState((int)App.myPureDM.Hwnd, 4); //Maximize the window
                    }
                    else {
                        App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.Binding.Failed"), Brushes.Red);
                    }

                    // App.mySplashScreen.worker.ReportProgress(100);
                    var timer = new DispatcherTimer();

                    timer.Interval = TimeSpan.FromMilliseconds(10);
                    timer.Tick += Timer_Tick;
                    timer.Start();
                }
                else {
                    App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.Binding.NoProcess"), Brushes.Red);
                }

                myShipCargo.RefreshData();

                App.mySplashScreen.Dispatcher.Invoke(new Action(() => App.mySplashScreen.Close()));
                //SfSkinManager.ApplyStylesOnApplication = true;
                this.WindowState = WindowState.Normal;
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;
                double windowWidth = this.Width;
                double windowHeight = this.Height;
                this.Left = (screenWidth / 2) - (windowWidth / 2);
                this.Top = (screenHeight / 2) - (windowHeight / 2);
                App.myCFun.DownloadMissingIcon();
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
            if (App.myStorageManagement != null && App.myStorageManagement.IsLoaded) {
                if (App.myStorageVM != null) {
                    App.myStorageVM.StorageCollection.Clear();
                }
                App.myStorageManagement.Activate();
                return;
            }
            if (App.myStorageVM != null) {
                App.myStorageVM.StorageCollection.Clear();
            }
            App.myStorageManagement = new StorageManagement();
            App.myStorageManagement.Closed += (_, _) => App.myStorageManagement = null;
            App.myStorageManagement.Show();
        }

        // Strict sync between Items.csv and Resources/Images/Items/*.bmp.
        // CFunctions.SyncImages handles the actual work; downloads are
        // async so we offload to a worker thread so the click handler
        // returns immediately and the UI log keeps streaming progress.
        private void MenuItem_SyncImages_Click(object sender, RoutedEventArgs e) {
            App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.SyncImages.Starting"), Brushes.Gold);
            System.Threading.Tasks.Task.Run(() => {
                try {
                    App.myCFun.SyncImages();
                    App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.SyncImages.Done"), Brushes.Gold);
                }
                catch (Exception ex) {
                    App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.SyncImages.Failed", ex.Message), Brushes.Red);
                }
            });
        }

        // Phase 4 (i18n): one-shot bdocodex.com/tw/item/{id}/ scraper. Hits
        // every ItemID in App.listItems, extracts the zh-TW H1, and rewrites
        // Resources/Items.zh-TW.csv.  User-controlled; the user keeps this
        // menu item around because they occasionally edit Items.csv and want
        // a re-pull.
        // (Phase 9 hotfix 6: the menu item was removed per user request -
        // the importer is now triggered by re-running
        // 'Tools/scrape_bdocodex_all_names.js' at author time, and the
        // .csv is committed.  The .cs importer is still in the codebase
        // for future use but is no longer wired to a menu entry.)

        // Phase 8 (i18n): live language toggle.  Setting
        // LanguageService.Current = X swaps the active merged dictionary
        // and raises LanguageChanged; every subscriber (Items.ItemNameDisplay
        // / ItemTierDisplay, Islands.IslandsNameDisplay, Barter.*NameDisplay
        // via underlying INPC, and the three View's ApplyLocalizedHeaders
        // for Syncfusion GridColumn.HeaderText) re-renders in place.  The
        // setters also persist the choice via Properties.Settings so the
        // selection survives restart.
        private void MenuItem_LangEnglish_Click(object sender, RoutedEventArgs e) {
            iBarter.Localization.LanguageService.Instance.Current = iBarter.Localization.AppLanguage.English;
            ApplyLocalizedChrome();
        }

        private void MenuItem_LangZhTw_Click(object sender, RoutedEventArgs e) {
            iBarter.Localization.LanguageService.Instance.Current = iBarter.Localization.AppLanguage.TraditionalChinese;
            ApplyLocalizedChrome();
        }

        private void ApplyLocalizedChrome() {
            SyncLangCheckmarks();
            if (statusBarItem_Version != null) {
                statusBarItem_Version.Text =
                    Localization.LanguageService.Instance.Localize("str.StatusBar.VersionLabel") + "Beta_4.4";
            }
        }

        private void SyncLangCheckmarks() {
            var current = iBarter.Localization.LanguageService.Instance.Current;
            if (MenuItem_LangEnglish != null) MenuItem_LangEnglish.IsChecked = current == iBarter.Localization.AppLanguage.English;
            if (MenuItem_LangZhTw != null) MenuItem_LangZhTw.IsChecked = current == iBarter.Localization.AppLanguage.TraditionalChinese;
        }

        private void dockingManager_Main_ActiveWindowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            // if (dockingManager_Main.ActiveWindow.Name == "document_Map") {
            //     myMapControl.IslandsButtonRearrange();
            //     myMapControl.myTimer.Start();
            // }
            // else {
            //     myMapControl.myTimer.Stop();
            // }
        }

        private void scrollviewver_Loaded(object sender, RoutedEventArgs e) {
            ThemeList.AddHandler(MouseWheelEvent, new RoutedEventHandler(mousehandler), true);
            PaletteList.AddHandler(MouseWheelEvent, new RoutedEventHandler(mousehandler), true);
        }

        /// <summary>
        /// This  handler is used for scrolling the Items in themePanel
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void mousehandler(object sender, RoutedEventArgs e) {
            MouseWheelEventArgs eargs = (MouseWheelEventArgs)e;

            double x = (double)eargs.Delta;

            double y = ThemePanelScrollViewer.VerticalOffset;

            ThemePanelScrollViewer.ScrollToVerticalOffset(y - x);
        }
    }
}
