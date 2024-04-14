using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;

namespace iBarter.View {
    /// <summary>
    /// Interaction logic for StorageManagement.xaml
    /// </summary>
    public partial class StorageManagement : Window {
        public StorageManagement() {
            InitializeComponent();
            DataContext = App.myStorageVM;
            DataGrid_Storage.ItemsSource = App.myStorageVM.StorageCollection;
            RefreshData();
        }

        public void RefreshData() {
            string strPath_Data = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\myStorage_Data.json";

            if (File.Exists(strPath_Data)) {
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
                listItems.Add("Mysterious_Rock");
                listItems.Add("Luxury_Patterned_Fabric");
                listItems.Add("Elixir_of_Youth");
                listItems.Add("Portrait_of_the_Ancient");
                listItems.Add("102_Year_Old_Golden_Herb");
                listItems.Add("Golden_Fish_Scale");
                listItems.Add("Taxidermied_White_Caterpillar");
                listItems.Add("Faded_Gold_Dragon_Figurine");
                listItems.Add("Supreme_Gold_Candlestick");
                listItems.Add("Statue's_Tear");
                listItems.Add("Taxidermied_Morpho_Butterfly");
                listItems.Add("Azure_Quartz");
                listItems.Add("37_Year_Old_Herbal_Wine");
                listItems.Add("Octagonal_Box");
                listItems.Add("Pirate's_Key");
                listItems.Add("Bronze_Candlestick");
                listItems.Add("Headless_Dragon_Figurine");
                listItems.Add("Panacea");
                listItems.Add("Seashell_Deco");
                listItems.Add("Old_Chest_with_Gold_Coins");
                listItems.Add("Boatman's_Manual");
                listItems.Add("Green_Salt_Lump");
                listItems.Add("Solidified_Lava");
                listItems.Add("Marine_Knights'_Spear");
                listItems.Add("Amethyst_Fragment");
                listItems.Add("Opulent_Thread_Spool");
                listItems.Add("Stolen_Pirate_Dagger");
                listItems.Add("Marine_Knights'_Helm");
                listItems.Add("Blue_Candle_Bundle");
                listItems.Add("Ancient_Orders");
                listItems.Add("Lopters_Fishnet");
                listItems.Add("Rare_Herb_Pile");
                listItems.Add("Skull_Symbol_Carpet");
                listItems.Add("Weasel_Leather_Coat");
                listItems.Add("Gooey_Monster_Blood");
                listItems.Add("Round_Knife");
                listItems.Add("Skull_Decorated_Teacup");
                listItems.Add("Stalactite_Fragment");
                listItems.Add("Scout_Binoculars");
                listItems.Add("Pirates'_Supply_Box");
                listItems.Add("Torn_Pirate_Treasure_Map");
                listItems.Add("Old_Hourglass");
                listItems.Add("Urchin_Spine");
                listItems.Add("Pirate_Gold_Coin");
                listItems.Add("Monster_Tentacle");
                listItems.Add("Sea_Survival_Kit");
                listItems.Add("Balanced_Stone_Pagoda");
                listItems.Add("Narvo_Sea_Cucumber");
                listItems.Add("Big_Stone_Slab");
                listItems.Add("Supreme_Oyster_Box");
                listItems.Add("Conch_Shell_Ornament");
                listItems.Add("Filtered_Drinking_Water");
                listItems.Add("Opulent_Marble");
                listItems.Add("Pirate_Ship_Mast");
                listItems.Add("Cron_Castle_Gold_Coin");
                listItems.Add("Islanders'_Lunchbox");
                listItems.Add("Pirates'_Gunpowder");
                listItems.Add("Fertile_Soil");
                listItems.Add("Rakeflower_Seed_Pouch");
                listItems.Add("Roa_Flower_Seed_Pouch");
                listItems.Add("Golden_Sand");
                listItems.Add("Cherry_Tree_Seed_Pouch");
                listItems.Add("Unidentified_Ancient_Mural");
                listItems.Add("Ancient_Urn_Piece");
                listItems.Add("Chewy_Raw_Gizzard");
                listItems.Add("Raft_Toy");
                listItems.Add("Stained_Seagull_Figurine");
                listItems.Add("Naval_Ration");
                listItems.Add("Giant_Fish_Bone");
                listItems.Add("Dried_Blue_Rose");

                DataGrid_Storage.BeginInit();
                for (int i = 0; i < listItems.Count; i++) {
                    string strName = listItems[i].Replace(" ", "_").Replace("'", "").Replace("(", "_").Replace(")", "");
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
    }
}