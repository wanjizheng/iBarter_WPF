using Syncfusion.Windows.Shared;
using System.Collections.ObjectModel;

namespace iBarter.ViewModel {
    public class ShipCargoViewModel : NotificationObject {
        public ShipCargoViewModel() {
            CargoDetails = new ObservableCollection<Barter>();
            CargoDetails.CollectionChanged += CargoDetails_CollectionChanged;
        }

        private void CargoDetails_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
            if (App.myfmMain != null) {
                //SaveData();
            }
        }

        private ObservableCollection<Barter> _cargodetails;

        public ObservableCollection<Barter> CargoDetails {
            get { return _cargodetails; }
            set { _cargodetails = value; }
        }
    }

}