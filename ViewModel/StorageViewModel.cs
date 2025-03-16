#region Copyright Syncfusion Inc. 2001 - 2023

// Copyright Syncfusion Inc. 2001 - 2023. All rights reserved.
// Use of this code is subject to the terms of our license.
// A copy of the current license can be obtained at any time by e-mailing
// licensing@syncfusion.com. Any infringement will be prosecuted under
// applicable laws. 

#endregion

using Newtonsoft.Json;
using Syncfusion.Windows.Shared;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;

namespace iBarter.ViewModel {
    public class StorageViewModel : NotificationObject {
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
        /// Initializes a new instance of the <see cref="StorViewModel"/> class.
        /// </summary>
        public StorageViewModel() {
            StorageCollection = new ObservableCollection<Items>();
            StorageCollection.CollectionChanged += StorageCollection_CollectionChanged;
        }

        private void StorageCollection_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
            SaveData();
        }

        public void SaveData() {
            try {
                if (App.myStorageVM.StorageCollection.Count > 0) {
                    string strPath_Data = AppDomain.CurrentDomain.BaseDirectory + "Resources\\myStorage_Data.json";

                    using (FileStream streamData = new FileStream(strPath_Data, FileMode.OpenOrCreate, FileAccess.Write)) {
                        streamData.SetLength(0);
                        App.listStorage.Clear();
                        for (int i = 0; i < App.myStorageVM.StorageCollection.Count; i++) {
                            Items myItem = App.myStorageVM.StorageCollection[i];
                            if (!App.listStorage.Contains(myItem)) {
                                App.listStorage.Add(myItem);
                            }
                        }

                        string jsonData = JsonConvert.SerializeObject(App.listStorage);
                        //File.WriteAllText(strPath_Data, jsonData);
                        byte[] byteArray = System.Text.Encoding.UTF8.GetBytes(jsonData);
                        streamData.Write(byteArray, 0, byteArray.Length);
                    }

                    App.myCFun.Log("Saved data.", Brushes.DarkOliveGreen);
                }
            }
            catch (Exception exception) {
                App.myCFun.Log(exception.Message, Brushes.Red);
            }
        }

        #endregion

        #region Properties

        private ObservableCollection<Items> storagecollection;

        public ObservableCollection<Items> StorageCollection {
            get { return storagecollection; }
            set {
                storagecollection = value;
                RaisePropertyChanged("StorageCollectionChange");
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

        private ObservableCollection<Items> PopulateStorage() {
            ObservableCollection<Items> itemsList = new ObservableCollection<Items>();
            List<Items> myList = App.myCFun.LoadItemsCSV();
            foreach (Items item in myList) {
                itemsList.Add(item);
            }

            return itemsList;
        }

        #endregion
    }
}