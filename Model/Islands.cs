using iBarter.Localization;
using Syncfusion.Windows.Shared;
using System.ComponentModel;
using System.Windows;
using iBarter.Navigation;

namespace iBarter {
    public class Islands : NotificationObject {
        private EnumLists.Island enumIsland;
        private int intParley = 0;
        private int intRemaining;
        private Thickness myThickness;

        // Phase 5 (i18n): Traditional Chinese name loaded from
        // Resources/Islands.zh-TW.csv at AppStartup.  Empty when the
        // hand-curated sidecar has no row for this Island enum (Confidence
        // = low / blank); the display getter then falls back to the
        // canonical English IslandsName so unimproved rows don't blank
        // the UI.  No setter — populated only at ctor via the loader.
        private string strNameZhTw = string.Empty;

        public Islands(EnumLists.Island _name, int _parley, int _remaining = 0) {
            intParley = _parley;
            enumIsland = _name;
            intRemaining = _remaining;
            myThickness = new Thickness(0, 0, 0, 0);

            // Phase 5: every Islands instance re-fires its display
            // PropertyChanged when the active language switches so the
            // planner / scanner / map labels refresh without reload.
            try {
                LanguageService.Instance.LanguageChanged += OnLanguageChanged;
            }
            catch {
                // design-time pass; nothing to localize
            }
        }

        public string IslandsNameZhTw {
            get { return strNameZhTw; }
            set {
                strNameZhTw = value ?? string.Empty;
                RaisePropertyChanged("IslandsNameZhTw");
                RaisePropertyChanged("IslandsNameDisplay");
            }
        }

        private void OnLanguageChanged(object? sender, System.EventArgs e) {
            RaisePropertyChanged("IslandsNameDisplay");
        }

        public Thickness IslandsThickness {
            get {
                if (myThickness.Left + myThickness.Right + myThickness.Top + myThickness.Bottom == 0) {
                    myThickness = App.listIslands.FirstOrDefault(i => i.IslandsName == IslandsName).IslandsThickness;
                }

                return myThickness;
            }
            set {
                myThickness = value;
                RaisePropertyChanged("IslandsThickness");
            }
        }

        public EnumLists.Island Island {
            get { return enumIsland; }
            set { enumIsland = value; }
        }

        public string IslandsName {
            get { return Island.ToString(); }
        }

        /// <summary>
        ///     Phase 5 (i18n): UI-bound display name.  zh-TW when the user
        ///     has selected Traditional Chinese AND a non-empty sidecar entry
        ///     exists for this Island enum; otherwise the canonical English
        ///     <see cref="IslandsName"/>.
        /// </summary>
        public string IslandsNameDisplay {
            get {
                try {
                    if (LanguageService.Instance?.Current == AppLanguage.TraditionalChinese
                        && !string.IsNullOrWhiteSpace(strNameZhTw)) {
                        // Keep the Taiwan game terminology while presenting simplified
                        // glyphs. The old sidecar shipped several Velia spellings;
                        // normalize that one known game term consistently.
                        if (IslandsName == nameof(EnumLists.Island.Velia)) return "贝尔利亚村庄";
                        return ChineseTextNormalizer.ToSimplifiedChinese(strNameZhTw);
                    }
                }
                catch {
                    // fall through to English
                }
                return IslandsName;
            }
        }

        public double? NavigationX { get; set; }
        public double? NavigationY { get; set; }
        public string NavigationSource { get; set; } = string.Empty;

        // The HD map source labels islands at their node anchors, while sailing
        // routes need the coastal barterer. Keep the original node coordinate
        // only as an internal calibration pair; persisted plans continue to
        // expose NavigationX/Y as the actual sailing destination.
        internal double? MapAnchorX { get; set; }
        internal double? MapAnchorY { get; set; }

        public bool HasNavigationCoordinates =>
            NavigationX.HasValue && NavigationY.HasValue &&
            double.IsFinite(NavigationX.Value) && double.IsFinite(NavigationY.Value);

        public NavigationPoint NavigationPoint => HasNavigationCoordinates
            ? new NavigationPoint(NavigationX!.Value, NavigationY!.Value)
            : throw new InvalidOperationException($"Missing navigation coordinates for {IslandsName}");

        public int Parley {
            get { return intParley; }
            set {
                intParley = value;
                RaisePropertyChanged("Parley");
            }
        }

        public int Remaining {
            get { return intRemaining; }
            set {
                intRemaining = value;
                RaisePropertyChanged("Remaining");
            }
        }


        // public EnumLists.Island GetIslandEnum() {
        //     return enumIsland;
        // }

        // public void SetIslandEnum(EnumLists.Island _island) {
        //     enumIsland = _island;
        // }


        // public int GetParley() {
        //     return intParley;
        // }
        //
        // public void SetParley(int _parley) {
        //     intParley = _parley;
        // }

        // public void SetRemaining(int _remaining) {
        //     intRemaining = _remaining;
        // }
        //
        // public int GetRemaining() {
        //     return intRemaining;
        // }
    }
}
