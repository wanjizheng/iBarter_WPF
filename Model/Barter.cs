using iBarter.View;
using Newtonsoft.Json;
using Syncfusion.Windows.Shared;
using System.ComponentModel;
using System.IO;

namespace iBarter {
    public class Barter : NotificationObject {
        private Islands isLand = null!;
        private Items item1 = null, item2 = null;
        private String icon1 = null, icon2 = null;
        private String item1Name = "", item2Name = "";
        private int exchangeQuantity = 0;
        private bool exchangeDone = false, usingALT = false, calculatedAlready = false;
        private int barterGroup = -1;
        int intChange = 0, intInv = 0;
        private volatile int totalitem1ExchangeQuantity, totalitem2ExchangeQuantity;
        private bool tofGrouped = false;

        public Barter() {
        }

        public Barter(Islands _isLand, Items _item1, Items _item2, int _exchangeQuantity = 0, bool _exchangeDone = false, int _barterGroup = 0, int _intInv = 0, int _intChange = 0, bool _usingALT = false, bool _calculatedAlready = false, int _totalitem1ExchangeQuantity = -1) {
            isLand = _isLand;
            WireUpIsland(isLand);

            item1 = _item1;
            WireUpItem1(item1);
            item2 = _item2;
            WireUpItem2(item2);

            item1Name = item1.ItemName;
            icon1 = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + Item1.ItemID + ".bmp";
            item2Name = item2.ItemName;
            icon2 = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + Item2.ItemID + ".bmp";

            exchangeQuantity = _exchangeQuantity;
            barterGroup = _barterGroup;
            exchangeDone = _exchangeDone;
            usingALT = _usingALT;
            intInv = _intInv;
            intChange = _intChange;
            calculatedAlready = _calculatedAlready;

            totalitem1ExchangeQuantity = _totalitem1ExchangeQuantity;
            if (InvQuantityChange == 0) {
                InvQuantityChange = InvQuantity;
            }
        }

        public bool CalculatedAlready {
            get { return calculatedAlready; }
            set { calculatedAlready = value; }
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
            set {
                barterGroup = value;
                RaisePropertyChanged("BarterGroup");
            }
        }

        public bool Grouped {
            get { return tofGrouped; }
            set {
                tofGrouped = value;
                RaisePropertyChanged("Grouped");
            }
        }

        public bool ExchangeDone {
            get { return exchangeDone; }
            set {
                exchangeDone = value;
                RaisePropertyChanged("ExchangeDone");
            }
        }

        public bool UsingALT {
            get { return usingALT; }
            set {
                usingALT = value;
                RaisePropertyChanged("UsingALT");
            }
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

        public int TotalItem1ExchangeQuantity {
            get {
                if (!calculatedAlready) {
                    totalitem1ExchangeQuantity = ExchangeQuantity * Item1Number;
                }

                return totalitem1ExchangeQuantity;
            }
            set { totalitem1ExchangeQuantity = value; }
        }

        public int TotalItem2ExchangeQuantity {
            get { return ExchangeQuantity * Item2Number; }
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
                int intParley = Parley;
                //Islands myIslands = new Islands(App.myCFun.IslandEnum(value), App.listIslands.Where(land => land.Island == App.myCFun.IslandEnum(value)).Select(land => land.Parley).FirstOrDefault());
                Islands myIslands = App.listIslands.FirstOrDefault(i => i.IslandsName == value);
                IsLand = myIslands;
                IsLand.Parley = intParley;
            }
        }

        public int IslandRemaining {
            get { return IsLand.Remaining; }
            set {
                IsLand.Remaining = value;
                RaisePropertyChanged("IslandRemaining");
            }
        }

        public Items Item1 {
            get { return item1; }
            set {
                if (!ReferenceEquals(item1, value)) {
                    WireUpItem1(value);
                }
                item1 = value;
                RaisePropertyChanged("Item1");
            }
        }

        public Items Item2 {
            get { return item2; }
            set {
                if (!ReferenceEquals(item2, value)) {
                    WireUpItem2(value);
                }
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
                    case "6":
                        strLV = "[Level 6]";
                        break;
                    case "7":
                        strLV = "[Level 7]";
                        break;
                    default:
                        strLV = "[Misc]";
                        break;
                }

                return strLV;
            }
        }

        // See Items.ItemIcon for why this is [JsonIgnore]d. Persisting the
        // absolute path in myShipCargoItems_Data.json bakes whichever
        // BaseDirectory the program happened to be running from at save-time
        // into the JSON; on the next run from a different directory the stale
        // path fails File.Exists, which makes the getter call RefreshItems,
        // which spams 'Download icon for: ...' and runs a bdocodex fetch
        // even though the bmp is sitting on disk at the new BaseDirectory.
        [JsonIgnore]
        public string Item1Icon {
            get {
                if (icon1 == null || !icon1.Contains(Item1.ItemID)) {
                    icon1 = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + Item1.ItemID + ".bmp";
                }

                if (!File.Exists(icon1) && item1 != null && int.TryParse(Item1.ItemID, out int id1) && id1 > 0) {
                    try {
                        App.myCFun.RefreshItems(item1.ItemID);
                    }
                    catch (Exception ex) {
                        // One bad item shouldn't take down the whole
                        // getter - the previous int.Parse(...) would throw
                        // FormatException out of a property accessor and
                        // blank the Barter UI.
                        System.Diagnostics.Debug.WriteLine("Item1Icon refresh fail " + item1.ItemID + ": " + ex.Message);
                    }
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

        // See Item1Icon / Items.ItemIcon for why this is [JsonIgnore]d.
        [JsonIgnore]
        public string Item2Icon {
            get {
                if (icon2 == null || !icon2.Contains(Item2.ItemID)) {
                    icon2 = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + Item2.ItemID + ".bmp";
                }

                if (!File.Exists(icon2) && item2 != null && int.TryParse(Item2.ItemID, out int id2) && id2 > 0) {
                    try {
                        App.myCFun.RefreshItems(item2.ItemID);
                    }
                    catch (Exception ex) {
                        System.Diagnostics.Debug.WriteLine("Item2Icon refresh fail " + item2.ItemID + ": " + ex.Message);
                    }
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
            set {
                intInv = value;
                RaisePropertyChanged("InvQuantity");
            }
        }

        public int InvQuantityChange {
            get { return intChange; }
            set {
                intChange = value;
                RaisePropertyChanged("InvQuantityChange");
            }
        }

        private void UpdateItem() {
            if (item1Name != "" && !item1Name.Equals(Item1.ItemName)) {
                Items item1 = new Items(App.listItems.FirstOrDefault(i => i.ItemName.Equals(item1Name)).ItemName, App.listItems.FirstOrDefault(i => i.ItemName.Equals(item1Name)).ItemID, App.listItems.FirstOrDefault(i => i.ItemName.Equals(item1Name)).ItemLV);
                Item1 = item1;
                icon1 = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + Item1.ItemID + ".bmp";
            }

            if (item2Name != "" && !item2Name.Equals(Item2.ItemName)) {
                Items item2 = new Items(App.listItems.FirstOrDefault(i => i.ItemName.Equals(item2Name)).ItemName, App.listItems.FirstOrDefault(i => i.ItemName.Equals(item2Name)).ItemID, App.listItems.FirstOrDefault(i => i.ItemName.Equals(item2Name)).ItemLV);
                Item2 = item2;
                icon2 = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + Item2.ItemID + ".bmp";
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

        // ====================================================================
        // Phase 5 (i18n): display getters + PropertyChanged relay
        // ====================================================================
        // Barter stores canonical English names (Item1Name, IsLandName) so
        // the JSON persisted to myShipCargoItems_Data.json stays stable
        // across language switches.  UI bindings now read these *Display
        // variants instead; they read the language-aware getter on the
        // underlying Items/Islands instance and re-fire whenever the
        // underlying object raises PropertyChanged (which is how Items /
        // Islands broadcast language flips).  Underlying-INPC subscription
        // is wired in the ctor + setter so a re-assigned item still
        // refreshes the cell.

        public string Item1NameDisplay =>
            item1?.ItemNameDisplay ?? Item1Name;

        public string Item2NameDisplay =>
            item2?.ItemNameDisplay ?? Item2Name;

        public string IsLandNameDisplay =>
            isLand?.IslandsNameDisplay ?? IsLandName;

        private void WireUpItem1(Items? newItem) {
            if (item1 != null) {
                item1.PropertyChanged -= OnItem1PropertyChanged;
            }
            if (newItem != null) {
                newItem.PropertyChanged += OnItem1PropertyChanged;
            }
        }

        private void WireUpItem2(Items? newItem) {
            if (item2 != null) {
                item2.PropertyChanged -= OnItem2PropertyChanged;
            }
            if (newItem != null) {
                newItem.PropertyChanged += OnItem2PropertyChanged;
            }
        }

        private void WireUpIsland(Islands? newIsland) {
            if (isLand != null) {
                isLand.PropertyChanged -= OnIslandPropertyChanged;
            }
            if (newIsland != null) {
                newIsland.PropertyChanged += OnIslandPropertyChanged;
            }
        }

        private void OnItem1PropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(Items.ItemNameDisplay)
                || e.PropertyName == nameof(Items.ItemName)) {
                RaisePropertyChanged(nameof(Item1NameDisplay));
            }
        }

        private void OnItem2PropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(Items.ItemNameDisplay)
                || e.PropertyName == nameof(Items.ItemName)) {
                RaisePropertyChanged(nameof(Item2NameDisplay));
            }
        }

        private void OnIslandPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(Islands.IslandsNameDisplay)
                || e.PropertyName == nameof(Islands.IslandsName)) {
                RaisePropertyChanged(nameof(IsLandNameDisplay));
            }
        }
    }
}