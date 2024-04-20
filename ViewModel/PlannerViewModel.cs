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
            BarterCollection = new ObservableCollection<Barter>();
            BarterCollection.CollectionChanged += BarterCollectionCollectionChanged;
            itemscollection = PopulateItems();
            islandscollection = PopulateIslands();
        }

        private void BarterCollectionCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            try {
                if (App.myPVM.BarterCollection.Count > 0) {
                    App.myfmMain.myPlannerControl.RefreshDataGrid();
                    //App.myCFun.Log("Saved data.", Brushes.DarkOliveGreen);
                }
            }
            catch (Exception exception) {
                App.myCFun.Log(exception.Message, Brushes.Red);
            }
        }

        #endregion

        #region Properties

        private ObservableCollection<Barter> _barterCollection;

        /// <summary>
        /// Gets or sets the employee details.
        /// </summary>
        /// <value>The employee details.</value>
        public ObservableCollection<Barter> BarterCollection {
            get { return _barterCollection; }
            set { _barterCollection = value; }
        }


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