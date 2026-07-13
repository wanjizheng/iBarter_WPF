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
                    });
                    // InvokeShutdown() must be called from outside the Invoke
                    // callback - it terminates Dispatcher.Run() and lets the
                    // STA thread unwind cleanly.
                    splash.Dispatcher.InvokeShutdown();
                }
            }
            catch { }

            // Release the DM COM object on the same STA thread that created it,
            // then stop that thread. Stopping first would leave DmAutomation's
            // finalizer to run on an arbitrary GC thread, outside its COM apartment.
            try {
                if (PureDmWorker.IsRunning && App.myPureDM != null) {
                    PureDmWorker.Call(() => App.myPureDM.Dispose());
                }
            }
            catch { }
            finally {
                try { PureDmWorker.Stop(); } catch { }
            }
        }

        // [DllImport("user32.dll")]
        // [return: MarshalAs(UnmanagedType.Bool)]
        // internal static extern bool GetCursorPos(ref Win32Point pt);
        //
        // public static PointPlus GetMousePosition() {
        //     var w32Mouse = new Win32Point();
        //     GetCursorPos(ref w32Mouse);
        //
        // 2026-07-10: Timer_Tick method removed - it was the 200 calls/sec
        // culprit calling DM.GetCursorPos + DM.GetClientSize on the UI
        // thread. PureDmWorker.Call funnels the equivalent reads through
        // the dedicated STA worker instead.


        /// <summary>
        ///     Set the active window
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnActivateWindow(object sender, RoutedEventArgs e) {
            if (App.myBarterScanner != null && App.myBarterScanner.IsLoaded) {
                App.myBarterScanner.InitializeScannerState();
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

            // Phase 9 hotfix 8: the dropdown is empty when clicked and the
            // user has been unable to see the diagnostic from PlannerControl
            // ctor because Syncfusion's DockingManager lazily creates
            // document content only when the panel is first activated.  Print
            // the data counts here so the user can confirm App.listItems /
            // ItemsCollection actually have rows when the UI starts up.
            try {
                int appList = App.listItems?.Count ?? -1;
                int itemsCol = App.myPVM?.ItemsCollection?.Count ?? -1;
                int islandsCol = App.myPVM?.IslandsCollection?.Count ?? -1;
                int storageItems = App.listStorage?.Count ?? -1;
                App.myCFun?.Log(
                    $"[Diagnostic] App.listItems={appList}, myPVM.ItemsCollection={itemsCol}, " +
                    $"IslandsCollection={islandsCol}, listStorage={storageItems}",
                    System.Windows.Media.Brushes.Gray);
            }
            catch (Exception ex) {
                App.myCFun?.Log("[Diagnostic] failed: " + ex.Message, System.Windows.Media.Brushes.Red);
            }

            try {
                // App.mySplashScreen.worker.ReportProgress(10);
                // 2026-07-10: turn ON file logging so that if init hangs
                // before any UI log line is written we still have something
                // on disk to diagnose from.
                Logging.SaveConsoleLog = true;
                Logging.myTextBoxWriter = new TextBoxWriter(richTextBox_Log);
                App.myCFun.Log("[INIT] 0 before PureDmWorker.Start", Brushes.Gray);
                // Start the owner STA first, then construct DmAutomation on it.
                // Its constructor creates the dm.dmsoft COM object immediately;
                // constructing it on the WPF UI STA and merely calling it from
                // this worker would still cross COM apartment boundaries.
                PureDmWorker.Start();
                App.myCFun.Log("[INIT] 1 after PureDmWorker.Start", Brushes.Gray);
                App.myCFun.Log("[INIT] 2 before new DmAutomation", Brushes.Gray);
                App.myPureDM = PureDmWorker.Call(() =>
                    new PureDM.DmAutomation("wanjizheng1c1f9b855a9f822cbf24afa526dfca3c"));
                App.myCFun.Log("[INIT] 3 after new DmAutomation", Brushes.Gray);

                // All initialization and binding stays on the owner STA.
                PureDmWorker.Call(() => {
                    App.myPureDM.AttachToProcessByName("BlackDesert64");
                    App.myCFun.Log("[INIT] 4 after AttachToProcessByName hwnd=" + App.myPureDM.WindowHandle, Brushes.Gray);
                    App.myPureDM.BindMode = 103;
                    App.myPureDM.DisplayMode = "dx.graphic.3d.10plus";
                    App.myPureDM.MouseMode = "dx.mouse.position.lock.api|dx.mouse.focus.input.api|dx.mouse.focus.input.message|dx.mouse.clip.lock.api|dx.mouse.state.api|dx.mouse.api|dx.mouse.cursor";
                    App.myPureDM.KeyboardMode = "dx.keypad.input.lock.api|dx.keypad.state.api|dx.keypad.api";
                    App.myPureDM.PublicMode = "dx.public.graphic.protect|dx.public.anti.api|dx.public.km.protect|dx.public.input.ime|dx.public.focus.message";
                });

                // App.mySplashScreen.worker.ReportProgress(50);
                if ((int)App.myPureDM.WindowHandle > 0) {
                    App.myCFun.Log("[INIT] 5 before BindWindow hwnd=" + App.myPureDM.WindowHandle, Brushes.Gray);
                    int bindResult = PureDmWorker.Call(() => {
                        App.myPureDM.DM.SetWindowState((int)App.myPureDM.WindowHandle, 1);
                        return App.myPureDM.CV.BindWindow((int)App.myPureDM.WindowHandle);
                    });
                    App.myCFun.Log("[INIT] 6 after BindWindow bindResult=" + bindResult, Brushes.Gray);

                    // App.mySplashScreen.worker.ReportProgress(90);
                    if (bindResult == 1) {
                        App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.Binding.Success"), Brushes.Blue);
                    }
                    else {
                        App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.Binding.Failed"), Brushes.Red);
                    }

                    // 2026-07-10: REMOVED the 10ms DispatcherTimer that
                    // called DM.GetCursorPos + DM.GetClientSize on the
                    // UI thread ~200 times/sec. Those calls are now
                    // funnelled through the STA worker via the scan
                    // path's TryRefreshGameWindowSize, which is plenty
                    // for keeping WindowWidth/WindowHeight current.
                    //
                    // The status-bar XY readout is also removed - it
                    // served no functional purpose and was the main
                    // source of COM cross-thread contention.
                }
                else {
                    App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.Binding.NoProcess"), Brushes.Red);
                    // 2026-07-10: BDO not found - the previous code path
                    // fell through to myShipCargo.RefreshData() (which then
                    // misbehaves / throws) and the splash was never closed.
                    // Close + shutdown splash here so the user gets a usable
                    // main window instead of a stuck splash forever.
                    App.myCFun.Log("[INIT] NoProcess - closing splash + InvokeShutdown", Brushes.Red);
                    try {
                        App.mySplashScreen.Dispatcher.Invoke(new Action(() => App.mySplashScreen.Close()));
                        App.mySplashScreen.Dispatcher.InvokeShutdown();
                    } catch (Exception splashEx) {
                        App.myCFun.Log("[INIT] splash shutdown fail: " + splashEx.Message, Brushes.Red);
                    }
                    return;
                }

                App.myCFun.Log("[INIT] 7 before splash close", Brushes.Gray);
                myShipCargo.RefreshData();

                App.mySplashScreen.Dispatcher.Invoke(new Action(() => App.mySplashScreen.Close()));
                // Shut down the background STA Dispatcher so Dispatcher.Run() exits
                // and the SplashScreen thread terminates. Without this the thread
                // stays alive as a "zombie" Dispatcher, holding WPF/DirectWrite
                // resources that cause 0x80070008 (ERROR_NOT_ENOUGH_MEMORY) during
                // subsequent DirectWrite calls on the main thread (e.g. during Scan).
                App.mySplashScreen.Dispatcher.InvokeShutdown();
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
                App.myCFun.Log("[INIT] EXCEPTION: " + exception.Message, Brushes.Red);
                // 2026-07-10: even on init exception, close splash so the
                // user is not left looking at a stuck splash.
                try {
                    App.mySplashScreen?.Dispatcher.Invoke(new Action(() => App.mySplashScreen?.Close()));
                    App.mySplashScreen?.Dispatcher.InvokeShutdown();
                } catch { /* best effort */ }
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

        // 2026-07-08: manual re-bind menu item removed per user request.
        // Rebinding the game window from the UI thread crashes the game
        // client on the user's dx.graphic.3d.10plus build (see commit
        // df859cc + the [DIAG-capture-down] message that says "DO NOT
        // manually rebind via the menu"). The auto-unbind in the scan
        // path is the only safe recovery - the user just waits 5-15 s
        // and re-clicks Scan.

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
