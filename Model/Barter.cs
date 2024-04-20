using iBarter.View;
using Syncfusion.Windows.Shared;
using System.IO;
using System.Reflection;

namespace iBarter {
    public class Barter : NotificationObject {
        private Islands isLand = null!;
        private Items item1 = null, item2 = null;
        private String icon1 = null, icon2 = null;
        private String item1Name = "", item2Name = "";
        private int exchangeQuantity = 0;
        private bool exchangeDone = false;
        private int barterGroup = -1;
        int intChange = 0, intInv = 0;

        public Barter() {
        }

        public Barter(Islands _isLand, Items _item1, Items _item2, int _exchangeQuantity = 0, bool _exchangeDone = false, int _barterGroup = -1, int _intInv = 0, int _intChange = 0) {
            isLand = _isLand;

            item1 = _item1;
            item2 = _item2;

            item1Name = item1.ItemName;
            icon1 = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\Images\\Items\\" + Item1.ItemID + ".bmp";
            item2Name = item2.ItemName;
            icon2 = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\Images\\Items\\" + Item2.ItemID + ".bmp";

            exchangeQuantity = _exchangeQuantity;
            barterGroup = _barterGroup;
            exchangeDone = _exchangeDone;
            intInv = _intInv;
            intChange = _intChange;
            if (InvQuantityChange == 0) {
                InvQuantityChange = InvQuantity;
            }
        }

        public Islands IsLand {
            get { return isLand; }
            set {
                isLand = value;
                RaisePropertyChanged("IsLand");
            }
        }

        public int BarterGroup {
            get { return barterGroup; }
            set { barterGroup = value; }
        }

        public bool ExchangeDone {
            get { return exchangeDone; }
            set { exchangeDone = value; }
        }

        public int ExchangeQuantity {
            get { return exchangeQuantity; }
            set {
                if (value > IslandRemaining) {
                    value = IslandRemaining;
                }
                else if (value < 0) {
                    value = 0;
                }

                exchangeQuantity = value;
                // if (Item1 != null && Item2 != null) {
                //     Barter myBarter = App.myPVM.BarterCollection.FirstOrDefault(b => b.BarterGroup == this.BarterGroup && b.Item1Name.Equals(Item2Name));
                //     if (myBarter != null) {
                //         myBarter.InvQuantityChange = myBarter.InvQuantityChange + ExchangeQuantity * Item2Number;
                //         if (myBarter.Item1.ItemLV == "5" && myBarter.InvQuantityChange > App.myfmMain.myPlannerControl.ComboBox_LV5Max.SelectedIndex + 1) {
                //             myBarter.InvQuantityChange = App.myfmMain.myPlannerControl.ComboBox_LV5Max.SelectedIndex + 1;
                //         }
                //     }
                //     //
                //     // intChange = 0;
                //     // intChange += (InvQuantity + ExchangeQuantity * Item2Number);
                //
                //     InvQuantityChange = Math.Max(0, InvQuantityChange - ExchangeQuantity * Item1Number);
                // }
            }
        }

        public int Parley {
            get { return IsLand.Parley; }
            set {
                IsLand.Parley = value;
                RaisePropertyChanged("Parley");
            }
        }

        public string IsLandName {
            get { return IsLand.IslandsName; }
            set {
                Islands myIslands = new Islands(App.myCFun.IslandEnum(value), App.listIslands.Where(land => land.Island == App.myCFun.IslandEnum(value)).Select(land => land.Parley).FirstOrDefault());
                IsLand = myIslands;
            }
        }

        public int IslandRemaining {
            get { return IsLand.Remaining; }
            set {
                IsLand.Remaining = value;
                RaisePropertyChanged("remainingChange");
            }
        }

        public Items Item1 {
            get { return item1; }
            set {
                item1 = value;
                RaisePropertyChanged("Item1");
            }
        }

        public Items Item2 {
            get { return item2; }
            set {
                item2 = value;
                RaisePropertyChanged("Item2");
            }
        }

        public int Item1Number {
            get { return Item1.ItemNumber; }
            set {
                Item1.ItemNumber = value;
                RaisePropertyChanged("Item1NumberChange");
            }
        }

        public string Item1Name {
            get {
                if (item1Name.Equals("") && Item1 != null)
                    item1Name = Item1.ItemName;
                return item1Name;
            }
            set {
                item1Name = value;
                UpdateItem();
                RaisePropertyChanged("Item1NameChange");
            }
        }

        public string Item1LV {
            get {
                string strLV = "";
                switch (Item1.ItemLV) {
                    case "0":
                        strLV = "[Basic Item]";
                        break;
                    case "1":
                        strLV = "[Level 1]";
                        break;
                    case "2":
                        strLV = "[Level 2]";
                        break;
                    case "3":
                        strLV = "[Level 3]";
                        break;
                    case "4":
                        strLV = "[Level 4]";
                        break;
                    case "5":
                        strLV = "[Level 5]";
                        break;
                    default:
                        strLV = "[Misc]";
                        break;
                }

                return strLV;
            }
        }

        public string Item1Icon {
            get {
                if (icon1 == null || !icon1.Contains(Item1.ItemID)) {
                    icon1 = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\Images\\Items\\" + Item1.ItemID + ".bmp";
                }

                return icon1;
            }
            set {
                icon1 = value;
                RaisePropertyChanged("Icon1Change");
            }
        }

        public int Item2Number {
            get { return Item2.ItemNumber; }
            set {
                Item2.ItemNumber = value;
                RaisePropertyChanged("Item2NumberChange");
            }
        }

        public string Item2Name {
            get {
                if (item2Name.Equals("") && Item2 != null)
                    item2Name = Item2.ItemName;
                return item2Name;
            }
            set {
                item2Name = value;
                UpdateItem();
                RaisePropertyChanged("Item2NameChange");
            }
        }

        public string Item2Icon {
            get {
                if (icon2 == null || !icon2.Contains(Item2.ItemID)) {
                    icon2 = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\Images\\Items\\" + Item2.ItemID + ".bmp";
                }

                return icon2;
            }
            set {
                icon2 = value;
                RaisePropertyChanged("Icon2Change");
            }
        }

        public int InvQuantity {
            get {
                if (App.myStorageManagement == null) {
                    App.myStorageManagement = new StorageManagement();
                }

                Items myItem = App.myStorageVM.StorageCollection.FirstOrDefault(i => i.ItemName.Equals(Item1Name));
                if (myItem != null) {
                    intInv = (myItem.StorageVeliaQuantity_Iliya + myItem.StorageVeliaQuantity_Velia + myItem.StorageVeliaQuantity_Epheria + myItem.StorageVeliaQuantity_Ancado);
                }

                return intInv;
            }
            set { intInv = value; }
        }

        public int InvQuantityChange {
            get { return intChange; }
            set { intChange = value; }
        }

        private void UpdateItem() {
            if (item1Name != "" && !item1Name.Equals(Item1.ItemName)) {
                Items item1 = new Items(App.listItems.FirstOrDefault(i => i.ItemName.Equals(item1Name)).ItemName, App.listItems.FirstOrDefault(i => i.ItemName.Equals(item1Name)).ItemID, App.listItems.FirstOrDefault(i => i.ItemName.Equals(item1Name)).ItemLV);
                Item1 = item1;
                icon1 = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\Images\\Items\\" + Item1.ItemID + ".bmp";
            }

            if (item2Name != "" && !item2Name.Equals(Item2.ItemName)) {
                Items item2 = new Items(App.listItems.FirstOrDefault(i => i.ItemName.Equals(item2Name)).ItemName, App.listItems.FirstOrDefault(i => i.ItemName.Equals(item2Name)).ItemID, App.listItems.FirstOrDefault(i => i.ItemName.Equals(item2Name)).ItemLV);
                Item2 = item2;
                icon2 = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "\\Resources\\Images\\Items\\" + Item2.ItemID + ".bmp";
            }

            //RaisePropertyChanged("ItemChange");
            // if (App.myBarterScanner != null) {
            //     App.myBarterScanner.RefreshDataGrid();
            // }
        }


        // public void SetIsland(Islands _island) {
        //     _isLand = _island;
        // }
        //
        // public Islands GetIsland() {
        //     return _isLand;
        // }

        // public Items GetItem1() {
        //     return item1;
        // }
        //
        // public Items GetItem2() {
        //     return item2;
        // }
        //
        // public void SetItem1(Items _item) {
        //     item1 = _item;
        // }
        //
        // public void SetItem2(Items _item) {
        //     item2 = _item;
        // }
    }
}