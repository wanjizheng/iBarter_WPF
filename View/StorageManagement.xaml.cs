using Newtonsoft.Json;
using Syncfusion.Windows.Shared;
using System.IO;
using System.Windows.Media;

namespace iBarter.View {
    /// <summary>
    /// Interaction logic for StorageManagement.xaml
    /// </summary>
    public partial class StorageManagement : ChromelessWindow {
        public StorageManagement() {
            InitializeComponent();
            DataContext = App.myStorageVM;
            DataGrid_Storage.ItemsSource = App.myStorageVM.StorageCollection;
            RefreshData();
        }

        public void RefreshData() {
            string strPath_Data = AppDomain.CurrentDomain.BaseDirectory + "Resources\\myStorage_Data.json";
            FileInfo fileInfo = new FileInfo(strPath_Data);

            if (File.Exists(strPath_Data) && fileInfo.Length > 0) {
                try {
                    string readJsonData = File.ReadAllText(strPath_Data);
                    List<Items> dataSource = JsonConvert.DeserializeObject<List<Items>>(readJsonData);

                    DataGrid_Storage.BeginInit();
                    for (int i = 0; i < dataSource.Count; i++) {
                        Items myItem = dataSource[i];
                        if (App.myStorageVM.StorageCollection.FirstOrDefault(i => i.ItemName.Equals(myItem.ItemName)) == null) {
                            App.myStorageVM.StorageCollection.Add(myItem);
                        }
                    }

                    DataGrid_Storage.EndInit();
                    //RefreshDataGrid();
                    App.myCFun.Log("Loaded...", Brushes.Blue);
                }
                catch (Exception exception) {
                    App.myCFun.Log(exception.Message, Brushes.Red);
                }
                //myPlannerControl.DataGrid_Planner.ItemsSource = dataSource;
            }
            else {
                List<string> listItems = new List<string>();
                listItems.Add("Mysterious Rock");
                listItems.Add("Luxury Patterned Fabric");
                listItems.Add("Elixir of Youth");
                listItems.Add("Portrait of the Ancient");
                listItems.Add("102 Year Old Golden Herb");
                listItems.Add("Golden Fish Scale");
                listItems.Add("Taxidermied White Caterpillar");
                listItems.Add("Faded Gold Dragon Figurine");
                listItems.Add("Supreme Gold Candlestick");
                listItems.Add("Statues Tear");
                listItems.Add("Taxidermied Morpho Butterfly");
                listItems.Add("Azure Quartz");
                listItems.Add("37 Year Old Herbal Wine");
                listItems.Add("Octagonal Box");
                listItems.Add("Pirates Key");
                listItems.Add("Bronze Candlestick");
                listItems.Add("Headless Dragon Figurine");
                listItems.Add("Panacea");
                listItems.Add("Seashell Deco");
                listItems.Add("Old Chest with Gold Coins");
                listItems.Add("Boatmans Manual");
                listItems.Add("Green Salt Lump");
                listItems.Add("Solidified Lava");
                listItems.Add("Marine Knights Spear");
                listItems.Add("Amethyst Fragment");
                listItems.Add("Opulent Thread Spool");
                listItems.Add("Stolen Pirate Dagger");
                listItems.Add("Marine Knights Helm");
                listItems.Add("Blue Candle Bundle");
                listItems.Add("Ancient Orders");
                listItems.Add("Lopters Fishnet");
                listItems.Add("Rare Herb Pile");
                listItems.Add("Skull Symbol Carpet");
                listItems.Add("Weasel Leather Coat");
                listItems.Add("Gooey Monster Blood");
                listItems.Add("Round Knife");
                listItems.Add("Skull Decorated Teacup");
                listItems.Add("Stalactite Fragment");
                listItems.Add("Scout Binoculars");
                listItems.Add("Pirates Supply Box");
                listItems.Add("Torn Pirate Treasure Map");
                listItems.Add("Old Hourglass");
                listItems.Add("Urchin Spine");
                listItems.Add("Pirate Gold Coin");
                listItems.Add("Monster Tentacle");
                listItems.Add("Sea Survival Kit");
                listItems.Add("Balanced Stone Pagoda");
                listItems.Add("Narvo Sea Cucumber");
                listItems.Add("Big Stone Slab");
                listItems.Add("Supreme Oyster Box");
                listItems.Add("Conch Shell Ornament");
                listItems.Add("Filtered Drinking Water");
                listItems.Add("Opulent Marble");
                listItems.Add("Pirate Ship Mast");
                listItems.Add("Cron Castle Gold Coin");
                listItems.Add("Islanders Lunchbox");
                listItems.Add("Pirates Gunpowder");
                listItems.Add("Fertile Soil");
                listItems.Add("Rakeflower Seed Pouch");
                listItems.Add("Roa Flower Seed Pouch");
                listItems.Add("Golden Sand");
                listItems.Add("Cherry Tree Seed Pouch");
                listItems.Add("Unidentified Ancient Mural");
                listItems.Add("Ancient Urn Piece");
                listItems.Add("Chewy Raw Gizzard");
                listItems.Add("Raft Toy");
                listItems.Add("Stained Seagull Figurine");
                listItems.Add("Naval Ration");
                listItems.Add("Giant Fish Bone");
                listItems.Add("Dried Blue Rose");

                DataGrid_Storage.BeginInit();
                for (int i = 0; i < listItems.Count; i++) {
                    string strName = listItems[i].Replace("'", "").Replace("(", "").Replace(")", "");
                    Items myItem = App.listItems.FirstOrDefault(i => i.ItemName.Equals(strName));
                    if (myItem != null) {
                        App.myStorageVM.StorageCollection.Add(myItem);
                    }
                    else {
                        App.myCFun.Log("Cannot find the item: " + strName, Brushes.Red);
                    }
                }
                //Items myItems = App.listItems.FirstOrDefault(i => i.ItemName.Equals("Mysterious_Rock"));

                DataGrid_Storage.EndInit();
            }
        }

        private void DataGrid_Storage_CurrentCellEndEdit(object sender, Syncfusion.UI.Xaml.Grid.CurrentCellEndEditEventArgs e) {
            App.myStorageVM.SaveData();
        }

        private void PinWindow_Click(object sender, System.Windows.RoutedEventArgs e) {
            if (this.Topmost) {
                this.Topmost = false;
                PinWindow.IsChecked = false;
            }
            else {
                this.Topmost = true;
                PinWindow.IsChecked = true;
            }
        }
    }
}