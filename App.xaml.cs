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

            myCFun = new CFunctions();
            listItems = myCFun.LoadItemsCSV();
            listIslands = myCFun.LoadIslandsCSV();
            listStorage = new List<Items>();
            listCargoItems = new List<Barter>();

            mySVM = new ScannerViewModel();
            myPVM = new PlannerViewModel();
            myStorageVM = new StorageViewModel();
            myCVM = new ShipCargoViewModel();
            myMainWVM = new MainWindowViewModel();

            //mySplashScreen = new SplashScreen();
            myfmMain = new MainWindow();

            myfmMain.statusBarItem_Version.Text = "Version: Beta_4.4";

            // OnLastWindowClose so closing MainWindow plus child windows actually exits
            // the process; OnMainWindowClose used to leave child dialogs running.
            this.ShutdownMode = ShutdownMode.OnLastWindowClose;
        }

        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);

            myfmMain.Show();
            //mySplashScreen.Show();
        }
    }
}