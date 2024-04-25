using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Syncfusion.Windows.PropertyGrid;

namespace iBarter.Model {
    public class ShipCargo {
        private List<Barter> myBarterList;
        private double propExtraLT, propTotalLT, doubCurrentLT;

        public ShipCargo(List<Barter> _list, double _extraLT = 1009, double _totalLT = 21500, double _currentLT = 0) {
            myBarterList = _list;
            propExtraLT = _extraLT;
            propTotalLT = _totalLT;
            doubCurrentLT = _currentLT;
        }

        public List<Barter> BarterList {
            get { return myBarterList; }
            set { myBarterList = value; }
        }

        [Category("CargoProperty"), Description("Extra LT")]
        public double ExtraLT {
            get { return propExtraLT; }
            set { propExtraLT = value; }
        }

        [Category("CargoProperty"), Description("Total LT")]
        public double TotalLT {
            get { return propTotalLT; }
            set { propTotalLT = value; }
        }

        [Category("CargoProperty"), Description("Current LT")]
        public double CurrentLT {
            get { return doubCurrentLT; }
            set { doubCurrentLT = value; }
        }
    }
}