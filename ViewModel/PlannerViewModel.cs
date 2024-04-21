using Syncfusion.Windows.Shared;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Media;
using iBarter.Model;

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
            altcollection = PopulateALT();
        }

        private void BarterCollectionCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            try {
                if (App.myPVM.BarterCollection.Count > 0) {
                    //App.myfmMain.myPlannerControl.RefreshDataGrid();
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

        private ObservableCollection<AltLevel> altcollection;

        public ObservableCollection<AltLevel> AltCollection {
            get { return altcollection; }
            set {
                altcollection = value;
                RaisePropertyChanged("AltCollectionChange");
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

        private ObservableCollection<AltLevel> PopulateALT() {
            ObservableCollection<AltLevel> altList = new ObservableCollection<AltLevel> {
                new AltLevel { Level = "Beginner 1", Value = 1 },
                new AltLevel { Level = "Beginner 2", Value = 0.9727 },
                new AltLevel { Level = "Beginner 3", Value = 0.9614 },
                new AltLevel { Level = "Beginner 4", Value = 0.9529 },
                new AltLevel { Level = "Beginner 5", Value = 0.9457 },
                new AltLevel { Level = "Beginner 6", Value = 0.9394 },
                new AltLevel { Level = "Beginner 7", Value = 0.9338 },
                new AltLevel { Level = "Beginner 8", Value = 0.9286 },
                new AltLevel { Level = "Beginner 9", Value = 0.9238 },
                new AltLevel { Level = "Beginner 10", Value = 0.9194 },
                new AltLevel { Level = "Apprentice 1", Value = 0.9152 },
                new AltLevel { Level = "Apprentice 2", Value = 0.9113 },
                new AltLevel { Level = "Apprentice 3", Value = 0.9075 },
                new AltLevel { Level = "Apprentice 4", Value = 0.904 },
                new AltLevel { Level = "Apprentice 5", Value = 0.9006 },
                new AltLevel { Level = "Apprentice 6", Value = 0.8973 },
                new AltLevel { Level = "Apprentice 7", Value = 0.8942 },
                new AltLevel { Level = "Apprentice 8", Value = 0.8912 },
                new AltLevel { Level = "Apprentice 9", Value = 0.8883 },
                new AltLevel { Level = "Apprentice 10", Value = 0.8855 },
                new AltLevel { Level = "Skilled 1", Value = 0.8827 },
                new AltLevel { Level = "Skilled 2", Value = 0.8801 },
                new AltLevel { Level = "Skilled 3", Value = 0.8776 },
                new AltLevel { Level = "Skilled 4", Value = 0.8751 },
                new AltLevel { Level = "Skilled 5", Value = 0.8727 },
                new AltLevel { Level = "Skilled 6", Value = 0.8704 },
                new AltLevel { Level = "Skilled 7", Value = 0.8681 },
                new AltLevel { Level = "Skilled 8", Value = 0.8659 },
                new AltLevel { Level = "Skilled 9", Value = 0.8638 },
                new AltLevel { Level = "Skilled 10", Value = 0.8617 },
                new AltLevel { Level = "Professional 1", Value = 0.8597 },
                new AltLevel { Level = "Professional 2", Value = 0.8577 },
                new AltLevel { Level = "Professional 3", Value = 0.8558 },
                new AltLevel { Level = "Professional 4", Value = 0.8539 },
                new AltLevel { Level = "Professional 5", Value = 0.8521 },
                new AltLevel { Level = "Professional 6", Value = 0.8503 },
                new AltLevel { Level = "Professional 7", Value = 0.8485 },
                new AltLevel { Level = "Professional 8", Value = 0.8468 },
                new AltLevel { Level = "Professional 9", Value = 0.8451 },
                new AltLevel { Level = "Professional 10", Value = 0.8435 },
                new AltLevel { Level = "Artisan 1", Value = 0.8419 },
                new AltLevel { Level = "Artisan 2", Value = 0.8403 },
                new AltLevel { Level = "Artisan 3", Value = 0.8388 },
                new AltLevel { Level = "Artisan 4", Value = 0.8373 },
                new AltLevel { Level = "Artisan 5", Value = 0.8358 },
                new AltLevel { Level = "Artisan 6", Value = 0.8344 },
                new AltLevel { Level = "Artisan 7", Value = 0.833 },
                new AltLevel { Level = "Artisan 8", Value = 0.8316 },
                new AltLevel { Level = "Artisan 9", Value = 0.8303 },
                new AltLevel { Level = "Artisan 10", Value = 0.829 },
                new AltLevel { Level = "Master 1", Value = 0.8277 },
                new AltLevel { Level = "Master 2", Value = 0.8264 },
                new AltLevel { Level = "Master 3", Value = 0.8252 },
                new AltLevel { Level = "Master 4", Value = 0.824 },
                new AltLevel { Level = "Master 5", Value = 0.8228 },
                new AltLevel { Level = "Master 6", Value = 0.8217 },
                new AltLevel { Level = "Master 7", Value = 0.8206 },
                new AltLevel { Level = "Master 8", Value = 0.8195 },
                new AltLevel { Level = "Master 9", Value = 0.8184 },
                new AltLevel { Level = "Master 10", Value = 0.8173 },
                new AltLevel { Level = "Master 11", Value = 0.8163 },
                new AltLevel { Level = "Master 12", Value = 0.8153 },
                new AltLevel { Level = "Master 13", Value = 0.8143 },
                new AltLevel { Level = "Master 14", Value = 0.8133 },
                new AltLevel { Level = "Master 15", Value = 0.8124 },
                new AltLevel { Level = "Master 16", Value = 0.8115 },
                new AltLevel { Level = "Master 17", Value = 0.8106 },
                new AltLevel { Level = "Master 18", Value = 0.8097 },
                new AltLevel { Level = "Master 19", Value = 0.8088 },
                new AltLevel { Level = "Master 20", Value = 0.808 },
                new AltLevel { Level = "Master 21", Value = 0.8072 },
                new AltLevel { Level = "Master 22", Value = 0.8064 },
                new AltLevel { Level = "Master 23", Value = 0.8056 },
                new AltLevel { Level = "Master 24", Value = 0.8048 },
                new AltLevel { Level = "Master 25", Value = 0.8041 },
                new AltLevel { Level = "Master 26", Value = 0.8033 },
                new AltLevel { Level = "Master 27", Value = 0.8026 },
                new AltLevel { Level = "Master 28", Value = 0.802 },
                new AltLevel { Level = "Master 29", Value = 0.8013 },
                new AltLevel { Level = "Master 30", Value = 0.8006 },
                new AltLevel { Level = "Guru 1", Value = 0.8 },
                new AltLevel { Level = "Guru 2", Value = 0.7994 },
                new AltLevel { Level = "Guru 3", Value = 0.7988 },
                new AltLevel { Level = "Guru 4", Value = 0.7982 },
                new AltLevel { Level = "Guru 5", Value = 0.7976 },
                new AltLevel { Level = "Guru 6", Value = 0.7971 },
                new AltLevel { Level = "Guru 7", Value = 0.7966 },
                new AltLevel { Level = "Guru 8", Value = 0.796 },
                new AltLevel { Level = "Guru 9", Value = 0.7956 },
                new AltLevel { Level = "Guru 10", Value = 0.7951 },
                new AltLevel { Level = "Guru 11", Value = 0.7946 },
                new AltLevel { Level = "Guru 12", Value = 0.7942 },
                new AltLevel { Level = "Guru 13", Value = 0.7937 },
                new AltLevel { Level = "Guru 14", Value = 0.7933 },
                new AltLevel { Level = "Guru 15", Value = 0.7929 },
                new AltLevel { Level = "Guru 16", Value = 0.7925 },
                new AltLevel { Level = "Guru 17", Value = 0.7922 },
                new AltLevel { Level = "Guru 18", Value = 0.7918 },
                new AltLevel { Level = "Guru 19", Value = 0.7915 },
                new AltLevel { Level = "Guru 20", Value = 0.7911 },
                new AltLevel { Level = "Guru 21", Value = 0.7908 },
                new AltLevel { Level = "Guru 22", Value = 0.7905 },
                new AltLevel { Level = "Guru 23", Value = 0.7903 },
                new AltLevel { Level = "Guru 24", Value = 0.79 },
                new AltLevel { Level = "Guru 25", Value = 0.7898 },
                new AltLevel { Level = "Guru 26", Value = 0.7895 },
                new AltLevel { Level = "Guru 27", Value = 0.7893 },
                new AltLevel { Level = "Guru 28", Value = 0.7891 },
                new AltLevel { Level = "Guru 29", Value = 0.7889 },
                new AltLevel { Level = "Guru 30", Value = 0.7888 },
                new AltLevel { Level = "Guru 31", Value = 0.7886 },
                new AltLevel { Level = "Guru 32", Value = 0.7885 },
                new AltLevel { Level = "Guru 33", Value = 0.7883 },
                new AltLevel { Level = "Guru 34", Value = 0.7882 },
                new AltLevel { Level = "Guru 35", Value = 0.7881 },
                new AltLevel { Level = "Guru 36", Value = 0.7881 },
                new AltLevel { Level = "Guru 37", Value = 0.788 },
                new AltLevel { Level = "Guru 38", Value = 0.7879 },
                new AltLevel { Level = "Guru 39", Value = 0.7879 },
                new AltLevel { Level = "Guru 40", Value = 0.7879 }
            };


            return altList;
        }

        #endregion
    }
}