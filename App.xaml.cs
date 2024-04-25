using iBarter.Model;
using iBarter.View;
using iBarter.ViewModel;
using Syncfusion.Licensing;
using System.Windows;

namespace iBarter {
    /// <summary>
    ///     Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {
        public static global::PureDM.PureDM myPureDM = null!;
        public static CFunctions myCFun = null!;
        public static MainWindow myfmMain = null!;
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
        public static StorageViewModel myStorageVM = null!;
        public static ShipCargoViewModel myCVM = null!;
        public static CargoProperty myCargoProperty = null;


        public App() {
            SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjHTQxAR8/V1NBaF5cXmtCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdnWXtec3RcRmRYUUdxWUM=");

            myCFun = new CFunctions();
            listItems = myCFun.LoadItemsCSV();
            listIslands = myCFun.LoadIslandsCSV();
            listStorage = new List<Items>();
            listCargoItems = new List<Barter>();

            mySVM = new ScannerViewModel();
            myPVM = new PlannerViewModel();
            myStorageVM = new StorageViewModel();
            myCVM = new ShipCargoViewModel();


            myfmMain = new MainWindow();

            this.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);

            myfmMain.Show();
        }
    }
}