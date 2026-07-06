using iBarter.Localization;
using iBarter.Model;
using iBarter.View;
using iBarter.ViewModel;
using Syncfusion.Licensing;
using Syncfusion.SfSkinManager;
using System.Windows;
using System.Windows.Navigation;

namespace iBarter {
    public static class DemosNavigationService {
        public static NavigationService RootNavigationService { get; set; }

        public static NavigationService DemoNavigationService { get; set; }

        public static Window MainWindow { get; set; }
    }

    /// <summary>
    ///     Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {
        public static global::PureDM.DmAutomation myPureDM = null!;
        public static CFunctions myCFun = null!;
        public static MainWindow myfmMain = null!;
        public static SplashScreen mySplashScreen = null!;
        public static BarterScanner myBarterScanner = null!;
        public static StorageManagement myStorageManagement = null!;
        public static List<Islands> listIslands = null!;
        public static List<Items> listItems = null!;
        public static List<Items> listStorage = null;
        public static List<Barter> listCargoItems = null;
        public static List<Barter> listBarterScanner = new List<Barter>();
        public static List<Barter> listBarterPlanner = new List<Barter>();

        public static ScannerViewModel mySVM = null!;
        public static PlannerViewModel myPVM = null!;
        public static MainWindowViewModel myMainWVM = null!;
        public static StorageViewModel myStorageVM = null!;
        public static ShipCargoViewModel myCVM = null!;
        public static CargoProperty myCargoProperty = null;

        // Single lock guarding all App.list* mutations. Children windows / scanner threads
        // take this around any read-then-mutate of the shared lists (e.g.
        // IdentifyBarterAsync on Task.Run worker threads, SaveData on UI threads).
        public static readonly object _listLock = new object();


        public App() {
            SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjHTQxAR8/V1JAaF5cX2pCd1p/TH5YfUNzdUVEY1ZUTXxaS1ZhSXxVdkJjX35edXJRRGhcWEd9XEY=");

            // Global safety net: an unhandled exception on the UI thread
            // (e.g. a bad cell edit in the Planner grid) otherwise tears down
            // the whole process. Log the full stack to crash.log and mark it
            // handled so a single failed action no longer crashes the app.
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            //SfSkinManager.ApplyStylesOnApplication = true;

            // Phase 9 hotfix: install the i18n dictionary FIRST, before
            // MainWindow is constructed below.  The XAML parser evaluates
            // {loc:Localize str.X} markup extensions synchronously at parse
            // time; if the dict is still null the ProvideValue returns the
            // raw key (which is what made the user see 'str.Banner.Title'
            // text instead of the localized value).  Moving this ABOVE
            // the new MainWindow() call fixes the order: dict populated
            // first, then the XAML parses, then ProvideValue finds the
            // key in the dict and returns the localized text.
            iBarter.Localization.LanguageService.Instance.InitializeAtStartup();

            myCFun = new CFunctions();
            listItems = myCFun.LoadItemsCSV();
            listIslands = myCFun.LoadIslandsCSV();
            // Phase 5 (i18n): re-read the zh-TW sidecars so every Item /
            // Islands instance has its display field populated before any
            // view binds against it.  The display getter (ItemNameDisplay
            // / IslandsNameDisplay) returns English or zh-TW based on
            // LanguageService.Current at read time, so the order relative
            // to InitializeAtStartup() does not matter — but doing it
            // here keeps the Log() messages visible in the bottom dock.
            myCFun.LoadItemsZhTw();
            myCFun.LoadIslandsZhTw();

            listStorage = new List<Items>();
            listCargoItems = new List<Barter>();

            mySVM = new ScannerViewModel();
            myPVM = new PlannerViewModel();
            myStorageVM = new StorageViewModel();
            myCVM = new ShipCargoViewModel();
            myMainWVM = new MainWindowViewModel();

            //mySplashScreen = new SplashScreen();
            myfmMain = new MainWindow();

            // Phase 6 (i18n): the "Version: " prefix is now a resource key
            // so the status-bar label flips with the active language; the
            // version number itself ("Beta_4.4") stays as a build-time
            // constant so the localized prefix and the version string can
            // concatenate in any culture.
            myfmMain.statusBarItem_Version.Text = iBarter.Localization.LanguageService.Instance.Localize("str.StatusBar.VersionLabel") + "Beta_4.4";

            // OnMainWindowClose: closing the main window exits the entire process
            // immediately, regardless of whether child windows (BarterScanner,
            // StorageManagement, SplashScreen, docked panels) are still open.
            // WPF auto-closes docked children; SplashScreen's background STA
            // Dispatcher is shut down explicitly in MainWindow_Closing.
            this.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);

            myfmMain.Show();
            //mySplashScreen.Show();
        }

        private void App_DispatcherUnhandledException(object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
            LogCrash("UI-thread", e.Exception);
            // Keep the app alive; the failed action is aborted but the user
            // doesn't lose their whole planning session to one bad edit.
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
            LogCrash("non-UI-thread", e.ExceptionObject as Exception);
        }

        private static void LogCrash(string origin, Exception? ex) {
            try {
                string logPath = AppDomain.CurrentDomain.BaseDirectory + "crash.log";
                string entry = "==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " (" + origin + ") ====\r\n"
                    + (ex?.ToString() ?? "(null exception)") + "\r\n\r\n";
                System.IO.File.AppendAllText(logPath, entry);
            }
            catch {
                // last-resort logger must never throw
            }
            try {
                myCFun?.Log((ex?.GetType().Name ?? "Exception") + ": " + (ex?.Message ?? ""),
                    System.Windows.Media.Brushes.Red);
            }
            catch {
            }
        }
    }
}