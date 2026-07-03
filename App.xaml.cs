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
            SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjHTQxAR8/V1JHaF5cWWdCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdlWXlceXVdR2BZVEJ3W0FWYEo=");

            //SfSkinManager.ApplyStylesOnApplication = true;

            // Phase 1 (i18n): install the active language merged dictionary
            // (Strings.en-US.xaml or Strings.zh-TW.xaml) BEFORE any UI loads so
            // every DynamicResource lookup during InitializeComponent sees the
            // correct dictionary. Phase 8 adds the user-facing "Language" menu
            // that lets the user switch live.
            LanguageService.Instance.InitializeAtStartup();

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
    }
}