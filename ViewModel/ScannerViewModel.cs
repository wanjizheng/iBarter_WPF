#region Copyright Syncfusion Inc. 2001 - 2023

// Copyright Syncfusion Inc. 2001 - 2023. All rights reserved.
// Use of this code is subject to the terms of our license.
// A copy of the current license can be obtained at any time by e-mailing
// licensing@syncfusion.com. Any infringement will be prosecuted under
// applicable laws. 

#endregion

using Syncfusion.Windows.Shared;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace iBarter.ViewModel {
    public class ScannerViewModel : NotificationObject {
        /// <summary>
        /// Gets or set the title bar background
        /// </summary>
        private Brush titleBarBackground = new SolidColorBrush(Color.FromRgb(43, 87, 154));

        /// <summary>
        /// Gets or set the title bar foreground
        /// </summary>
        private Brush titleBarForeground = new SolidColorBrush(Color.FromRgb(255, 255, 255));

        #region Constructor
        /// <summary>
        /// Initializes a new instance of the <see cref="ScannerViewModel"/> class.
        /// </summary>
        public ScannerViewModel() {
            BarterDetails = new ObservableCollection<Barter>();

            Status = new List<string>();
            Status.Add("Active");
            Status.Add("Inactive");

            Trustworthiness = new List<string>();
            Trustworthiness.Add("Sufficient");
            Trustworthiness.Add("Insufficient");
            Trustworthiness.Add("Perfect");
            itemscollection = PopulateItems();
            islandscollection = PopulateIslands();
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

        public List<string> Status { get; set; }

        public List<string> Trustworthiness { get; set; }

        private ObservableCollection<Items> itemscollection;

        public ObservableCollection<Items> ItemsCollection {
            get { return itemscollection; }
            set {
                itemscollection = value;
                RaisePropertyChanged("ItemsCollectionChange");
            }
        }

        private ObservableCollection<Islands> islandscollection;

        public ObservableCollection<Islands> IslandsCollection {
            get { return islandscollection; }
            set {
                islandscollection = value;
                RaisePropertyChanged("IslandsCollectionChange");
            }
        }

        /// <summary>
        /// Gets or set the title bar background
        /// </summary>
        public Brush TitleBarBackground {
            get { return titleBarBackground; }
            set {
                titleBarBackground = value;
                this.RaisePropertyChanged("TitleBarBackground");
            }
        }

        /// <summary>
        /// Gets or set the title bar foreground
        /// </summary>
        public Brush TitleBarForeground {
            get { return titleBarForeground; }
            set {
                titleBarForeground = value;
                this.RaisePropertyChanged("TitleBarForeground");
            }
        }
        #endregion

        #region Method

        private ObservableCollection<Items> PopulateItems() {
            ObservableCollection<Items> itemsList = new ObservableCollection<Items>();
            List<Items> myList = App.myCFun.LoadItemsCSV();
            foreach (Items item in myList) {
                itemsList.Add(item);
            }

            return itemsList;
        }

        private ObservableCollection<Islands> PopulateIslands() {
            ObservableCollection<Islands> islandsList = new ObservableCollection<Islands>();
            List<Islands> myList = App.myCFun.LoadIslandsCSV();
            foreach (Islands item in myList) {
                islandsList.Add(item);
            }

            return islandsList;
        }

        #endregion
    }
}