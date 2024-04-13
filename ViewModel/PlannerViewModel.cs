using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using PureDM.Logging;
using Syncfusion.Windows.Shared;
using Newtonsoft.Json;
using System.Windows.Controls;

namespace iBarter.ViewModel {
    public class PlannerViewModel : NotificationObject {
        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="PlannerViewModel"/> class.
        /// </summary>
        public PlannerViewModel() {
            BarterDetails = new ObservableCollection<Barter>();
        }

        #endregion

        #region Properties

        private ObservableCollection<Barter> _barterDetails;

        /// <summary>
        /// Gets or sets the employee details.
        /// </summary>
        /// <value>The employee details.</value>
        public ObservableCollection<Barter> BarterDetails {
            get { return _barterDetails; }
            set { _barterDetails = value; }
        }

        private ObservableCollection<Barter> _barterCollection = new ObservableCollection<Barter>();

        /// <summary>
        /// Gets or sets the orders details.
        /// </summary>
        /// <value>The orders details.</value>
        public ObservableCollection<Barter> BarterCollection {
            get { return _barterCollection; }
            set { _barterCollection = value; }
        }

        #endregion

        #region Method
        
        #endregion
    }
}