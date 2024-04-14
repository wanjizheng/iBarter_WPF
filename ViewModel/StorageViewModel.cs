#region Copyright Syncfusion Inc. 2001 - 2023

// Copyright Syncfusion Inc. 2001 - 2023. All rights reserved.
// Use of this code is subject to the terms of our license.
// A copy of the current license can be obtained at any time by e-mailing
// licensing@syncfusion.com. Any infringement will be prosecuted under
// applicable laws. 

#endregion

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Media;
using Esri.ArcGISRuntime.Portal;
using Newtonsoft.Json;
using Syncfusion.Windows.Shared;

namespace iBarter.ViewModel {
    public class StorageViewModel : NotificationObject {
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
                    string strPath_Data = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\myStorage_Data.json";

                    using (FileStream streamData = new FileStream(strPath_Data, FileMode.Create, FileAccess.Write)) {
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