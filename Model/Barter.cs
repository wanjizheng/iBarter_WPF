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
            isLand = ResolveCatalogIsland(_isLand, _isLand?.IslandsName);
            WireUpIsland(isLand);

            item1 = ResolveCatalogItem(_item1, _item1?.ItemName);
            WireUpItem1(item1);
            item2 = ResolveCatalogItem(_item2, _item2?.ItemName);
            WireUpItem2(item2);

            item1Name = item1?.ItemName ?? "";
            icon1 = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + Item1.ItemID + ".bmp";
            item2Name = item2?.ItemName ?? "";
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
                var resolved = ResolveCatalogIsland(value, value?.IslandsName);
                if (!ReferenceEquals(isLand, resolved)) {
                    WireUpIsland(resolved);
                }
                isLand = resolved;
                RaisePropertyChanged("IsLand");
                RaisePropertyChanged(nameof(IsLandName));
                RaisePropertyChanged(nameof(IsLandNameDisplay));
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
            get { return IsLand?.Parley ?? 0; }
            set {
                if (IsLand == null) {
                    return;
                }
                IsLand.Parley = value;
                RaisePropertyChanged("Parley");
            }
        }

        public string IsLandName {
            get { return IsLand?.IslandsName ?? ""; }
            set {
                int intParley = IsLand?.Parley ?? 0;
                Islands? myIslands = FindCatalogIsland(value);
                if (myIslands != null) {
                    IsLand = myIslands;
                    IsLand.Parley = intParley;
                }
                RaisePropertyChanged(nameof(IsLandName));
                RaisePropertyChanged(nameof(IsLandNameDisplay));
            }
        }

        public int IslandRemaining {
            get { return IsLand?.Remaining ?? 0; }
            set {
                if (IsLand == null) {
                    return;
                }
                IsLand.Remaining = value;
                RaisePropertyChanged("IslandRemaining");
            }
        }

        public Items Item1 {
            get { return item1; }
            set {
                var resolved = ResolveCatalogItem(value, value?.ItemName ?? item1Name);
                if (!ReferenceEquals(item1, resolved)) {
                    WireUpItem1(resolved);
                }
                item1 = resolved;
                item1Name = item1?.ItemName ?? item1Name;
                RaisePropertyChanged("Item1");
                RaisePropertyChanged(nameof(Item1Name));
                RaisePropertyChanged(nameof(Item1NameDisplay));
                RaisePropertyChanged(nameof(Item1LV));
                RaisePropertyChanged(nameof(Item1Icon));
            }
        }

        public Items Item2 {
            get { return item2; }
            set {
                var resolved = ResolveCatalogItem(value, value?.ItemName ?? item2Name);
                if (!ReferenceEquals(item2, resolved)) {
                    WireUpItem2(resolved);
                }
                item2 = resolved;
                item2Name = item2?.ItemName ?? item2Name;
                RaisePropertyChanged("Item2");
                RaisePropertyChanged(nameof(Item2Name));
                RaisePropertyChanged(nameof(Item2NameDisplay));
                RaisePropertyChanged(nameof(Item2Icon));
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
            // Pure read with on-read fallback. The legacy version wrote
            // `item1Name = Item1.ItemName` from inside the getter, which
            // (a) broke WPF TwoWay binding invariants by mutating the
            // backing field without raising PropertyChanged, and
            // (b) caused "clear has no effect" in the Scanner grid:
            //   user types Chinese -> UpdateItem() resolves Item1 ->
            //   user clears the cell -> binding writes "" via setter ->
            //   WPF re-reads for any consumer -> getter rewrites the
            //   backing field to Item1.ItemName -> cell shows old name
            //   -> user perceives "delete did nothing" / "frozen".
            // The Item1 setter (line ~188) and UpdateItem() (line ~398)
            // are the only legitimate writers to item1Name; the getter
            // computes the display value lazily and never mutates state.
            get { return item1?.ItemName ?? item1Name; }
            set {
                item1Name = value;
                UpdateItem();
                RaisePropertyChanged("Item1NameChange");
                RaisePropertyChanged(nameof(Item1Name));
                RaisePropertyChanged(nameof(Item1NameDisplay));
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
            // See Item1Name above for why the getter must not mutate
            // the backing field. Item2 setter + UpdateItem() own the
            // write-side; this getter only computes the display value.
            get { return item2?.ItemName ?? item2Name; }
            set {
                item2Name = value;
                UpdateItem();
                RaisePropertyChanged("Item2NameChange");
                RaisePropertyChanged(nameof(Item2Name));
                RaisePropertyChanged(nameof(Item2NameDisplay));
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
                // Pure read: query the shared App.myStorageVM.StorageCollection
                // without side effects. The previous version lazily `new`-ed a
                // StorageManagement window on first access to guarantee the
                // collection was loaded, but that had two bad consequences:
                //   1. WPF data-binding fires this getter during render, so the
                //      first time a Barter was bound (e.g. after a scan + "Add to
                //      Planner" click) it would silently create and show a
                //      Storage window, which then ran SeedHardcodedFallback and
                //      triggered 80+ CollectionChanged -> SaveData cascades.
                //   2. InvQuantity became coupled to a UI window existing,
                //      making the data layer untestable on its own.
                // The load now happens once at App.OnStartup via
                // App.myStorageVM.LoadData(), so this getter can be a pure read.
                var storage = App.myStorageVM?.StorageCollection;
                if (storage == null) {
                    return intInv;
                }

                Items myItem = storage.FirstOrDefault(i => i.ItemName.Equals(Item1Name));
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
            if (item1Name != "" && (Item1 == null || !item1Name.Equals(Item1.ItemName))) {
                Items? resolvedItem1 = FindCatalogItem(item1Name);
                if (resolvedItem1 != null) {
                    item1Name = resolvedItem1.ItemName;
                    Item1 = CreateItemFromCatalog(resolvedItem1, item1);
                }
                if (Item1 != null) {
                    icon1 = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + Item1.ItemID + ".bmp";
                }
            }

            if (item2Name != "" && (Item2 == null || !item2Name.Equals(Item2.ItemName))) {
                Items? resolvedItem2 = FindCatalogItem(item2Name);
                if (resolvedItem2 != null) {
                    item2Name = resolvedItem2.ItemName;
                    Item2 = CreateItemFromCatalog(resolvedItem2, item2);
                }
                if (Item2 != null) {
                    icon2 = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + Item2.ItemID + ".bmp";
                }
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

        [JsonIgnore]
        public string Item1NameDisplay {
            get { return item1?.ItemNameDisplay ?? Item1Name; }
            set { Item1Name = value; }
        }

        [JsonIgnore]
        public string Item2NameDisplay {
            get { return item2?.ItemNameDisplay ?? Item2Name; }
            set { Item2Name = value; }
        }

        [JsonIgnore]
        public string IsLandNameDisplay {
            get { return isLand?.IslandsNameDisplay ?? IsLandName; }
            set { IsLandName = value; }
        }

        private static Items ResolveCatalogItem(Items? candidate, string? name) {
            if (App.listItems != null) {
                Items? byName = FindCatalogItem(name)
                    ?? FindCatalogItem(candidate?.ItemName)
                    ?? FindCatalogItem(candidate?.ItemNameZhTw);
                if (byName != null) {
                    return CreateItemFromCatalog(byName, candidate);
                }

                if (!string.IsNullOrWhiteSpace(candidate?.ItemID)) {
                    Items? byId = App.listItems.FirstOrDefault(i => i.ItemID == candidate.ItemID);
                    if (byId != null) {
                        return CreateItemFromCatalog(byId, candidate);
                    }
                }
            }

            return candidate ?? new Items(name ?? string.Empty, "0", "0");
        }

        private static Items CreateItemFromCatalog(Items catalog, Items? candidate) {
            var item = new Items(
                catalog.ItemName,
                catalog.ItemID,
                catalog.ItemLV,
                candidate?.ItemNumber ?? catalog.ItemNumber,
                candidate?.StorageVeliaQuantity_Velia ?? catalog.StorageVeliaQuantity_Velia,
                candidate?.StorageVeliaQuantity_Iliya ?? catalog.StorageVeliaQuantity_Iliya,
                candidate?.StorageVeliaQuantity_Epheria ?? catalog.StorageVeliaQuantity_Epheria,
                candidate?.StorageVeliaQuantity_Ancado ?? catalog.StorageVeliaQuantity_Ancado);
            item.ItemNameZhTw = catalog.ItemNameZhTw;
            return item;
        }

        private static Items? FindCatalogItem(string? name) {
            if (string.IsNullOrWhiteSpace(name) || App.listItems == null) {
                return null;
            }

            return App.listItems.FirstOrDefault(i =>
                string.Equals(i.ItemName, name, StringComparison.Ordinal) ||
                string.Equals(i.ItemNameDisplay, name, StringComparison.Ordinal) ||
                string.Equals(i.ItemNameZhTw, name, StringComparison.Ordinal));
        }

        private static Islands ResolveCatalogIsland(Islands? candidate, string? name) {
            if (App.listIslands != null) {
                Islands? byName = FindCatalogIsland(name)
                    ?? FindCatalogIsland(candidate?.IslandsName)
                    ?? FindCatalogIsland(candidate?.IslandsNameZhTw);
                if (byName != null) {
                    return CreateIslandFromCatalog(byName, candidate);
                }

                if (candidate != null) {
                    Islands? byEnum = App.listIslands.FirstOrDefault(i => i.Island == candidate.Island);
                    if (byEnum != null) {
                        return CreateIslandFromCatalog(byEnum, candidate);
                    }
                }
            }

            return candidate ?? new Islands(EnumLists.Island.Unfinished, 0);
        }

        private static Islands CreateIslandFromCatalog(Islands catalog, Islands? candidate) {
            var island = new Islands(
                catalog.Island,
                candidate?.Parley ?? catalog.Parley,
                candidate?.Remaining ?? catalog.Remaining);
            island.IslandsNameZhTw = catalog.IslandsNameZhTw;
            island.NavigationX = catalog.NavigationX;
            island.NavigationY = catalog.NavigationY;
            island.NavigationSource = catalog.NavigationSource;
            return island;
        }

        private static Islands? FindCatalogIsland(string? name) {
            if (string.IsNullOrWhiteSpace(name) || App.listIslands == null) {
                return null;
            }

            return App.listIslands.FirstOrDefault(i =>
                string.Equals(i.IslandsName, name, StringComparison.Ordinal) ||
                string.Equals(i.IslandsNameDisplay, name, StringComparison.Ordinal) ||
                string.Equals(i.IslandsNameZhTw, name, StringComparison.Ordinal));
        }

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
