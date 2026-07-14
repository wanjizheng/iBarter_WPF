using iBarter.Localization;
using Newtonsoft.Json;
using Syncfusion.Windows.Shared;
using System.ComponentModel;
using System.IO;

namespace iBarter {
    public class Items : NotificationObject {
        private string strID;
        private string strLV;
        private string strName;
        // Phase 5 (i18n): Traditional Chinese (zh-TW) name loaded from
        // Resources/Items.zh-TW.csv at AppStartup. Empty when no entry exists
        // in the sidecar; the ItemNameDisplay getter then falls back to the
        // canonical English ItemName so unimproved rows don't blank the UI.
        // Not [JsonIgnore]'d: sidecar is rebuilt on demand and the JSON we
        // persist (myStorage_Data.json) does not contain this field today, so
        // round-tripping leaves it empty for objects loaded from disk.
        private string strNameZhTw = string.Empty;
        private int intNumber;
        private int intStorage_Velia, intStorage_Iliya, intStorage_Epheria, intStorage_Ancado;
        private String icon = null!;

        public Items(string _name, string _id, string _lv, int _number = -1, int _intStorageVelia = 0, int _intStorageIliya = 0, int _intStorageEpheria = 0, int _intStorageAncado = 0) {
            strName = _name;
            strID = _id;
            strLV = _lv;
            intNumber = _number;
            intStorage_Velia = _intStorageVelia;
            intStorage_Iliya = _intStorageIliya;
            intStorage_Epheria = _intStorageEpheria;
            intStorage_Ancado = _intStorageAncado;

            // Phase 5 (i18n): every Items instance re-fires its display
            // PropertyChanged when the active language switches, so any
            // data-bound cell / dropdown auto-refreshes.  The Memory cost
            // is per-row × per-language-change (one event dispatch), which
            // is fine for the catalog sizes iBarter deals with (275 items
            // × a handful of users + a few windows).  Phase 8 may swap this
            // for a WeakEventManager if profiling shows pressure.
            try {
                LanguageService.Instance.LanguageChanged += OnLanguageChanged;
            }
            catch {
                // LanguageService may not be ready in design-time passes.
            }
        }

        private void OnLanguageChanged(object? sender, System.EventArgs e) {
            RaisePropertyChanged("ItemNameDisplay");
            RaisePropertyChanged("ItemTierDisplay");
        }

        public string ItemName {
            get { return strName; }
            set {
                strName = value;
                RaisePropertyChanged("ItemName");
                // ItemNameDisplay depends on ItemName when no zh-TW is available.
                RaisePropertyChanged("ItemNameDisplay");
            }
        }

        public string ItemNameZhTw {
            get { return strNameZhTw; }
            set {
                strNameZhTw = value ?? string.Empty;
                RaisePropertyChanged("ItemNameZhTw");
                RaisePropertyChanged("ItemNameDisplay");
            }
        }

        /// <summary>
        ///     Phase 5 (i18n): UI-bound display name.  Returns the zh-TW name
        ///     when the user has selected Traditional Chinese AND a non-empty
        ///     sidecar entry exists for this ItemID; otherwise returns the
        ///     canonical English ItemName.  Bound by SfDataGrid cells,
        ///     GridMultiColumnDropDownList DisplayMember, and the Storage grid.
        ///     Backing field is read-only; toggling language flips this via
        ///     the LanguageService.LanguageChanged subscription installed in
        ///     ctor.
        /// </summary>
        public string ItemNameDisplay {
            get {
                try {
                    if (LanguageService.Instance?.Current == AppLanguage.TraditionalChinese
                        && !string.IsNullOrWhiteSpace(strNameZhTw)) {
                        return ChineseTextNormalizer.ToSimplifiedChinese(strNameZhTw);
                    }
                }
                catch {
                    // fall through to English
                }
                return strName;
            }
        }

        public string ItemID {
            get { return strID; }
            set {
                strID = value;
                RaisePropertyChanged("ItemID");
            }
        }

        // Don't persist the absolute path - the icon location is always
        // BaseDirectory/Resources/Items/<id>.bmp, so the path is rebuilt by
        // the getter on the next access. Persisting it (a) hard-codes
        // whichever directory iBarter happened to run from at save-time into
        // the JSON, breaking icon resolution when the program is later run
        // from a different path (e.g. C:\Users\...\OneDrive\Desktop\iBarter\
        // one day and E:\wanjizheng\MyProject\iBarter\ the next), and
        // (b) made File.Exists return false on the stale path, which
        // cascaded into spurious bdocodex downloads at startup.
        [JsonIgnore]
        public string ItemIcon {
            get {
                if (icon == null || !icon.Contains(ItemID)) {
                    icon = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + ItemID + ".bmp";
                }

                if (!File.Exists(icon) && int.TryParse(ItemID, out int idNum) && idNum > 0) {
                    try {
                        // Defensive parity with Barter.Item1Icon (Model/Barter.cs:287-298):
                        // RefreshItems dispatches an HTTP fetch through Task.Run, which
                        // can throw on dispatcher races or bdocodex hiccups. Without
                        // the try/catch the exception propagates out of the property
                        // getter and breaks the WPF binding pipeline - same failure
                        // mode that previously nuked Barter.Item1Icon before its fix.
                        App.myCFun.RefreshItems(ItemID);
                    }
                    catch (Exception ex) {
                        System.Diagnostics.Debug.WriteLine("ItemIcon refresh fail " + ItemID + ": " + ex.Message);
                    }
                    icon = AppDomain.CurrentDomain.BaseDirectory + "Resources\\Images\\Items\\" + ItemID + ".bmp";
                }

                return icon;
            }
            set {
                icon = value;
                RaisePropertyChanged("ItemIcon");
            }
        }

        public string ItemLV {
            get { return strLV; }
            set {
                strLV = value;
                RaisePropertyChanged("ItemLV");
            }
        }

        public string ItemTier {
            get {
                string strLV = "";
                switch (ItemLV) {
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

        /// <summary>
        ///     Phase 6 (i18n): localized variant of <see cref="ItemTier"/>.
        ///     Returns the resource-string equivalent of the English tier
        ///     marker (e.g. en-US '[Level 5]' or zh-TW '[等級 5]').  Falls
        ///     back to <see cref="ItemTier"/> if the LanguageService lookup
        ///     is unavailable (design-time) or the matching key is missing
        ///     (developer error).  The Storage grid binds to this via
        ///     <c>MappingName="ItemTierDisplay"</c> so its Tier column flips
        ///     with the rest of the UI on language switch.
        /// </summary>
        public string ItemTierDisplay {
            get {
                string key = ItemLV switch {
                    "0" => "str.ItemTier.Basic",
                    "1" => "str.ItemTier.Lv1",
                    "2" => "str.ItemTier.Lv2",
                    "3" => "str.ItemTier.Lv3",
                    "4" => "str.ItemTier.Lv4",
                    "5" => "str.ItemTier.Lv5",
                    "6" => "str.ItemTier.Lv6",
                    "7" => "str.ItemTier.Lv7",
                    _  => "str.ItemTier.Misc",
                };
                try {
                    var svc = Localization.LanguageService.Instance;
                    if (svc != null) {
                        return svc.Localize(key);
                    }
                }
                catch {
                    // fall through
                }
                return ItemTier;
            }
        }

        public int ItemNumber {
            get { return intNumber; }
            set {
                intNumber = value;
                RaisePropertyChanged("ItemNumber");
            }
        }

        public int StorageVeliaQuantity_Velia {
            get { return intStorage_Velia; }
            set {
                intStorage_Velia = value;
                RaisePropertyChanged("StorageVeliaQuantity_Velia");
            }
        }

        public int StorageVeliaQuantity_Iliya {
            get { return intStorage_Iliya; }
            set {
                intStorage_Iliya = value;
                RaisePropertyChanged("StorageVeliaQuantity_Iliya");
            }
        }

        public int StorageVeliaQuantity_Epheria {
            get { return intStorage_Epheria; }
            set {
                intStorage_Epheria = value;
                RaisePropertyChanged("StorageVeliaQuantity_Epheria");
            }
        }

        public int StorageVeliaQuantity_Ancado {
            get { return intStorage_Ancado; }
            set {
                intStorage_Ancado = value;
                RaisePropertyChanged("StorageVeliaQuantity_Ancado");
            }
        }
    }
}
