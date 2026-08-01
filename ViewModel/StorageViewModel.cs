#region Copyright Syncfusion Inc. 2001 - 2023

// Copyright Syncfusion Inc. 2001 - 2023. All rights reserved.
// Use of this code is subject to the terms of our license.
// A copy of the current license can be obtained at any time by e-mailing
// licensing@syncfusion.com. Any infringement will be prosecuted under
// applicable laws.

#endregion

using Newtonsoft.Json;
using Syncfusion.Windows.Shared;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Windows.Media;
using iBarter.Persistence;

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

        // SuppressSave: when true, StorageCollection_CollectionChanged does not call SaveData.
        // Used by LoadData to bulk-populate the collection from JSON + the hardcoded seed
        // list without triggering N writes. The bulk load is wrapped in
        // `using (SuppressAutoSave())`, which restores the flag and runs a single
        // SaveData() at the end so the new state is persisted.
        //
        // Why this matters historically: before this refactor, the seed list (80+
        // hardcoded items) was being added on every StorageManagement window open,
        // each Add firing CollectionChanged -> SaveData -> full JSON write. The first
        // open of the Storage window also acted as a side effect of Barter.InvQuantity's
        // getter, which `new StorageManagement()`-ed the window on first access -
        // meaning clicking the scanner's "Add to Planner" button would silently trigger
        // 80+ storage saves on its first run. See Barter.InvQuantity for the UI fix.
        private int _suppressSaveDepth;
        private bool _storageChangedPending;
        private readonly HashSet<Items> _subscribedStorageItems = new();

        // StorageManager keeps this requested exchange tier at the top of the
        // ledger. IDs (rather than display names) make the order independent of
        // the active UI language and catalog-name refreshes.
        private static readonly string[] PriorityStorageItemIds = {
            "800224", // Sharp Safflower Blade Crate
            "800223", // Brass Bowl Crate
            "800222", // Top-Quality Gamtu Crate
            "800221", // Top-Quality Blue Underglaze Porcelain Crate
            "800220", // Hanji Country Wild Berry Crate
            "800219", // High-quality Ink-scented Box
            "800218", // Nampo Persimmon Crate
            "800217", // Bamboo Sap Crate
            "800216", // Shadow Ornament Mirror
            "800215", // Moonshade Aged Wine
            "800214", // Moonlit Crystal Shard
            "800213", // Black Rose Bouquet
            "800212", // Mossy Silver Log Decoration
            "800211", // Moonlit Crystal Lamp
            "800210", // Kamasylvian Sculpture
            "800209", // Forest Fairy Perfume
            "800208", // Golden Cactus Bouquet
            "800207", // Miniature Arehaza Lighthouse
            "800206", // Traditional Arehazan Tea
            "800205", // Top-Quality Coconut Syrup
            "800204", // Golden Sand Ring
            "800203", // Fancy Camel Hide
            "800202", // Valencian Desert Fine Sword
            "800201", // Valencia Sand Shield
        };

        public event EventHandler? StorageChanged;

        #region Constructor
        /// <summary>
        /// Initializes a new instance of the <see cref="StorViewModel"/> class.
        /// </summary>
        public StorageViewModel() {
            StorageCollection = new ObservableCollection<Items>();
        }

        private void StorageCollection_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            if (e.OldItems is not null)
                foreach (Items item in e.OldItems)
                    UnsubscribeStorageItem(item);
            if (e.NewItems is not null)
                foreach (Items item in e.NewItems)
                    SubscribeStorageItem(item);
            if (e.Action == NotifyCollectionChangedAction.Reset) {
                foreach (Items item in _subscribedStorageItems.ToArray())
                    UnsubscribeStorageItem(item);
                foreach (Items item in StorageCollection)
                    SubscribeStorageItem(item);
            }
            // Skip auto-save during bulk loads; the using-block in LoadData restores
            // the flag and runs a single trailing SaveData() to persist the result.
            if (_suppressSaveDepth > 0) {
                _storageChangedPending = true;
                return;
            }
            SaveData();
            StorageChanged?.Invoke(this, EventArgs.Empty);
        }

        private void StorageItem_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName is not (nameof(Items.StorageVeliaQuantity_Velia)
                or nameof(Items.StorageVeliaQuantity_Iliya)
                or nameof(Items.StorageVeliaQuantity_Epheria)
                or nameof(Items.StorageVeliaQuantity_Ancado))) return;
            if (_suppressSaveDepth > 0) {
                _storageChangedPending = true;
                return;
            }
            SaveData();
            StorageChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SubscribeStorageItem(Items item) {
            if (_subscribedStorageItems.Add(item))
                item.PropertyChanged += StorageItem_PropertyChanged;
        }

        private void UnsubscribeStorageItem(Items item) {
            if (_subscribedStorageItems.Remove(item))
                item.PropertyChanged -= StorageItem_PropertyChanged;
        }

        public void SaveData() {
            _ = TrySaveData();
        }

        public bool TrySaveData() {
            try {
                if (StorageCollection.Count == 0) {
                    return false;
                }

                string path = GetStorageDataPath();
                var snapshot = StorageCollection.ToList();
                string jsonData = JsonConvert.SerializeObject(snapshot);
                AtomicFileStore.WriteValidated(path, jsonData, IsValidStorageJson, HasMeaningfulStorageData);

                App.listStorage.Clear();
                App.listStorage.AddRange(snapshot);
                WorkspaceSnapshotService.CaptureCompletedState("storage-save");
                App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.Storage.Saved"), Brushes.DarkOliveGreen);
                return true;
            }
            catch (Exception exception) {
                App.myCFun.Log(exception.Message, Brushes.Red);
                return false;
            }
        }

        // Loads storage data from disk + the hardcoded fallback list. Safe to call
        // multiple times: the dedup checks inside (FirstOrDefault on ItemName) skip
        // already-present items, so a second call after a partial load is a no-op.
        //
        // Wraps the bulk add in SuppressAutoSave() so the 80+ seed items produce
        // exactly one trailing SaveData() rather than 80+ mid-load writes.
        // Must be called on the UI thread (StorageCollection is an ObservableCollection
        // bound to the Storage window's DataGrid).
        public void LoadData() {
            string path = GetStorageDataPath();
            bool hasPersistedData = File.Exists(path) || File.Exists(path + ".bak");
            List<Items> loadedItems = new();

            if (hasPersistedData) {
                if (!AtomicFileStore.TryReadValidated(path, IsValidStorageJson, out string jsonData, out bool recoveredFromBackup)) {
                    App.myCFun.Log(Localization.LanguageService.Instance.Current == Localization.AppLanguage.TraditionalChinese
                        ? "仓库数据无效；原文件已保留，没有被覆盖。"
                        : "Storage data is invalid. The existing file was preserved and was not overwritten.", Brushes.Red);
                    return;
                }

                try {
                    loadedItems = JsonConvert.DeserializeObject<List<Items>>(jsonData) ?? new List<Items>();
                    loadedItems = loadedItems
                        .Select(HydrateStorageItem)
                        .Where(item => !string.Equals(item.ItemLV, "7", StringComparison.Ordinal))
                        .ToList();
                    if (recoveredFromBackup) {
                        App.myCFun.Log(Localization.LanguageService.Instance.Current == Localization.AppLanguage.TraditionalChinese
                            ? "已从备份文件恢复仓库数据。"
                            : "Storage data was recovered from the backup file.", Brushes.Orange);
                    }
                    App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.Storage.Loaded"), Brushes.Blue);
                }
                catch (Exception exception) {
                    App.myCFun.Log(exception.Message, Brushes.Red);
                    return;
                }
            }

            using (SuppressAutoSave()) {
                StorageCollection.Clear();
                foreach (Items item in loadedItems
                    .GroupBy(item => item.ItemID, StringComparer.Ordinal)
                    .Select(group => group.First())) {
                    StorageCollection.Add(item);
                }

                // Always seed the hardcoded fallback list. The JSON load above
                // already populated StorageCollection with the user's saved items;
                // this pass adds any hardcoded tracked items not yet present, so
                // newly-added items (e.g. the LV6 batch in 2587a5b) become visible
                // to existing users without forcing them to delete
                // myStorage_Data.json. LV7 terminal outputs are intentionally not
                // tracked by StorageManager.
                SeedHardcodedFallback();
                ApplyPreferredDisplayOrder();
            }
        }

        // Bulk-suppress auto-save during batch operations. The returned IDisposable
        // restores the flag and triggers a single SaveData() on Dispose, so a
        // batched add/remove cannot drop changes on the floor. Pair with `using`:
        //   using (App.myStorageVM.SuppressAutoSave()) {
        //       foreach (var item in bigList) StorageCollection.Add(item);
        //   }
        // // <-- one SaveData() fires here, with all changes persisted.
        public IDisposable SuppressAutoSave(bool saveOnDispose = true) {
            _suppressSaveDepth++;
            return new RestoreOnDispose(() => {
                _suppressSaveDepth = Math.Max(0, _suppressSaveDepth - 1);
                if (_suppressSaveDepth == 0 && _storageChangedPending) {
                    _storageChangedPending = false;
                    if (saveOnDispose) {
                        SaveData();
                    }
                    StorageChanged?.Invoke(this, EventArgs.Empty);
                }
            });
        }

        private static string GetStorageDataPath() =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "myStorage_Data.json");

        private static bool IsValidStorageJson(string json) {
            try {
                var items = JsonConvert.DeserializeObject<List<Items>>(json);
                return items is { Count: > 0 }
                    && items.All(item => !string.IsNullOrWhiteSpace(item.ItemID))
                    && items.Select(item => item.ItemID).Distinct(StringComparer.Ordinal).Count() == items.Count;
            }
            catch {
                return false;
            }
        }

        private static bool HasMeaningfulStorageData(string json) {
            try {
                var items = JsonConvert.DeserializeObject<List<Items>>(json);
                return items?.Any(item => item.StorageVeliaQuantity_Velia != 0
                    || item.StorageVeliaQuantity_Iliya != 0
                    || item.StorageVeliaQuantity_Epheria != 0
                    || item.StorageVeliaQuantity_Ancado != 0) == true;
            }
            catch {
                return false;
            }
        }

        private sealed class RestoreOnDispose : IDisposable {
            private Action? _onDispose;
            public RestoreOnDispose(Action onDispose) { _onDispose = onDispose; }
            public void Dispose() {
                _onDispose?.Invoke();
                _onDispose = null;
            }
        }

        #endregion

        #region Properties

        private ObservableCollection<Items> storagecollection;

        public ObservableCollection<Items> StorageCollection {
            get { return storagecollection; }
            set {
                if (storagecollection is not null) {
                    storagecollection.CollectionChanged -= StorageCollection_CollectionChanged;
                    foreach (Items item in _subscribedStorageItems.ToArray())
                        UnsubscribeStorageItem(item);
                }
                storagecollection = value;
                storagecollection.CollectionChanged += StorageCollection_CollectionChanged;
                foreach (Items item in storagecollection)
                    SubscribeStorageItem(item);
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

        #region Methods

        // Re-resolve every Items instance in StorageCollection against the
        // current catalog. Used by the Storage window when the user flips
        // language so the items pick up the new locale's display names.
        // Safe to call multiple times: the underlying items are replaced
        // in-place only when the catalog has fresher metadata.
        //
        // Each in-place replace is a Remove+Add against ObservableCollection,
        // which would normally cascade N SaveData() writes. Wrap in
        // SuppressAutoSave so a language flip with N items does at most one
        // trailing SaveData() (the persistence semantics don't change because
        // the on-disk JSON is the same shape).
        public void RefreshCatalogBindings() {
            using (SuppressAutoSave()) {
                for (int index = 0; index < StorageCollection.Count; index++) {
                    var hydrated = HydrateStorageItem(StorageCollection[index]);
                    if (!ReferenceEquals(hydrated, StorageCollection[index])) {
                        StorageCollection[index] = hydrated;
                    }
                }
            }
        }

        // Resolve an Items instance loaded from disk against the current catalog
        // (App.listItems). If the catalog has a richer version (updated zh-TW name,
        // newer item metadata), swap in the catalog instance while preserving the
        // user-edited storage quantities. This guards against catalog drift between
        // sessions: an old JSON file still produces up-to-date display data.
        private static Items HydrateStorageItem(Items item) {
            if (App.listItems == null) {
                return item;
            }

            var catalog = App.listItems.FirstOrDefault(i =>
                string.Equals(i.ItemID, item.ItemID, StringComparison.Ordinal)
                || string.Equals(i.ItemName, item.ItemName, StringComparison.Ordinal)
                || string.Equals(i.ItemNameZhTw, item.ItemName, StringComparison.Ordinal)
                || string.Equals(i.ItemNameDisplay, item.ItemName, StringComparison.Ordinal));
            if (catalog == null) {
                return item;
            }

            if (string.Equals(item.ItemName, catalog.ItemName, StringComparison.Ordinal)
                && string.Equals(item.ItemID, catalog.ItemID, StringComparison.Ordinal)
                && string.Equals(item.ItemLV, catalog.ItemLV, StringComparison.Ordinal)
                && string.Equals(item.ItemNameZhTw, catalog.ItemNameZhTw, StringComparison.Ordinal)) {
                return item;
            }

            var hydrated = new Items(
                catalog.ItemName,
                catalog.ItemID,
                catalog.ItemLV,
                item.ItemNumber,
                item.StorageVeliaQuantity_Velia,
                item.StorageVeliaQuantity_Iliya,
                item.StorageVeliaQuantity_Epheria,
                item.StorageVeliaQuantity_Ancado);
            hydrated.ItemNameZhTw = catalog.ItemNameZhTw;
            return hydrated;
        }

        private void ApplyPreferredDisplayOrder() {
            var priority = PriorityStorageItemIds
                .Select((itemId, index) => new { itemId, index })
                .ToDictionary(pair => pair.itemId, pair => pair.index, StringComparer.Ordinal);
            var ordered = StorageCollection
                .Where(item => priority.ContainsKey(item.ItemID))
                .OrderBy(item => priority[item.ItemID])
                // Keep every non-priority item in its existing order, so the
                // historic LV5 and lower ordering remains unchanged.
                .Concat(StorageCollection.Where(item => !priority.ContainsKey(item.ItemID)))
                .ToList();

            if (StorageCollection.Select(item => item.ItemID)
                .SequenceEqual(ordered.Select(item => item.ItemID), StringComparer.Ordinal)) {
                return;
            }

            StorageCollection.Clear();
            foreach (Items item in ordered) {
                StorageCollection.Add(item);
            }
        }

        // Hardcoded list of items that should always be visible in the storage
        // grid even on a fresh install. Mirrors the tracked LV5/LV6 barter set
        // the user is most likely to plan around; new items get appended as the
        // catalog grows. The dedup check in the loop is what makes this safe
        // to call on every LoadData - it only adds items the user doesn't
        // already have from a previous JSON.
        private void SeedHardcodedFallback() {
            List<string> listItems = new List<string>();
            listItems.Add("Mysterious Rock");
            listItems.Add("Luxury Patterned Fabric");
            listItems.Add("Elixir of Youth");
            listItems.Add("Portrait of the Ancient");
            listItems.Add("102 Year Old Golden Herb");
            listItems.Add("Golden Fish Scale");
            listItems.Add("Stuffed White Caterpillar");
            listItems.Add("Faded Gold Dragon Figurine");
            listItems.Add("Supreme Gold Candlestick");
            listItems.Add("Statues Tear");
            listItems.Add("Stuffed Morpho Butterfly");
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
            listItems.Add("Bamboo Sap Crate");
            listItems.Add("Black Rose Bouquet");
            listItems.Add("Brass Bowl Crate");
            listItems.Add("Fancy Camel Hide");
            listItems.Add("Forest Fairy Perfume");
            listItems.Add("Golden Cactus Bouquet");
            listItems.Add("Golden Sand Ring");
            listItems.Add("Hanji Country Wild Berry Crate");
            listItems.Add("High-quality Ink-scented Box");
            listItems.Add("Kamasylvian Sculpture");
            listItems.Add("Miniature Arehaza Lighthouse");
            listItems.Add("Moonlit Crystal Lamp");
            listItems.Add("Moonlit Crystal Shard");
            listItems.Add("Moonshade Aged Wine");
            listItems.Add("Mossy Silver Log Decoration");
            listItems.Add("Nampo Persimmon Crate");
            listItems.Add("Shadow Ornament Mirror");
            listItems.Add("Sharp Safflower Blade Crate");
            listItems.Add("Top-Quality Blue Underglaze Porcelain Crate");
            listItems.Add("Top-Quality Coconut Syrup");
            listItems.Add("Top-Quality Gamtu Crate");
            listItems.Add("Traditional Arehazan Tea");
            listItems.Add("Valencia Sand Shield");
            listItems.Add("Valencian Desert Fine Sword");
            listItems.Add("Omar Lava Powder");
            listItems.Add("Artisan Seashell Necklace");
            for (int i = 0; i < listItems.Count; i++) {
                string strName = listItems[i].Replace("'", "").Replace("(", "").Replace(")", "");
                Items myItem = App.listItems.FirstOrDefault(i => i.ItemName.Equals(strName));
                if (myItem != null) {
                    if (string.Equals(myItem.ItemLV, "7", StringComparison.Ordinal)) {
                        continue;
                    }
                    if (StorageCollection.FirstOrDefault(s => string.Equals(s.ItemID, myItem.ItemID, StringComparison.Ordinal)) == null) {
                        StorageCollection.Add(myItem);
                    }
                }
                else {
                    App.myCFun.Log(Localization.LanguageService.Instance.Localize("str.Log.Storage.CannotFindItem", strName), Brushes.Red);
                }
            }
        }

        #endregion
    }
}
