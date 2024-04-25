using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using iBarter.Model;
using Syncfusion.Windows.PropertyGrid;
using Syncfusion.Windows.Shared;

namespace iBarter.ViewModel {
    public class ShipCargoViewModel : NotificationObject {
        public ShipCargoViewModel() {
            CargoDetails = new ObservableCollection<ShipCargo>();
            CargoProperty = new ObservableCollection<PropertyGridItem>();
        }

        private ObservableCollection<ShipCargo> _cargodetails;
        private ObservableCollection<Syncfusion.Windows.PropertyGrid.PropertyGridItem> _cargoproperty = null;

        public ObservableCollection<ShipCargo> CargoDetails {
            get { return _cargodetails; }
            set { _cargodetails = value; }
        }

        public ObservableCollection<Syncfusion.Windows.PropertyGrid.PropertyGridItem> CargoProperty {
            get {
                return _cargoproperty;
            }
            set { _cargoproperty = value; }
        }
    }

}