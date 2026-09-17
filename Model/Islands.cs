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
        private double? navigationX;
        private double? navigationY;
        private string navigationSource = string.Empty;

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

        /// <summary>
        /// Route-planning destination. During the initial Islands.csv load this is
        /// also the original map-world coordinate. The first finite value is copied
        /// to <see cref="MapAnchorX"/> before IslandBarterLocations can replace the
        /// route destination with the coastal barterer NPC.
        /// </summary>
        public double? NavigationX {
            get { return navigationX; }
            set {
                navigationX = value;
                if (!MapAnchorX.HasValue && value.HasValue && double.IsFinite(value.Value)) {
                    MapAnchorX = value;
                }
            }
        }

        public double? NavigationY {
            get { return navigationY; }
            set {
                navigationY = value;
                if (!MapAnchorY.HasValue && value.HasValue && double.IsFinite(value.Value)) {
                    MapAnchorY = value;
                }
            }
        }

        public string NavigationSource {
            get { return navigationSource; }
            set {
                navigationSource = value ?? string.Empty;
                // Capture the original Islands.csv coordinate-system tag once.
                // Later route-only barterer sources must never replace it.
                if (string.IsNullOrWhiteSpace(MapAnchorSource)
                    && !string.IsNullOrWhiteSpace(navigationSource)
                    && !navigationSource.StartsWith(
                        "bdocodex-barterer-", StringComparison.OrdinalIgnoreCase)) {
                    MapAnchorSource = navigationSource;
                }
                RaisePropertyChanged("NavigationSource");
                RaisePropertyChanged("HasExplicitBarterDestination");
                RaisePropertyChanged("HasVerifiedBarterDestination");
                RaisePropertyChanged("HasUserCalibratedBarterDestination");
                RaisePropertyChanged("UsesCatalogFallbackDestination");
                RaisePropertyChanged("RouteDestinationProvenance");
            }
        }

        /// <summary>
        /// The route destination is backed by an explicit barter-NPC row in
        /// IslandBarterLocations.csv. This includes both source-extracted NPC
        /// coordinates and deliberately labelled user-calibrated NPC coordinates.
        /// </summary>
        public bool HasExplicitBarterDestination =>
            NavigationSource.StartsWith(
                "bdocodex-barterer-npc-", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Olvia Coast's barterer identity (Brio, NPC 58973) is sourced from the
        /// live barter catalog, but the exact X/Y below is currently calibrated
        /// from the user's in-game map observation because the public NPC page does
        /// not expose a spawn coordinate. Keep that distinction visible rather than
        /// claiming the position is source-extracted.
        /// </summary>
        public bool HasUserCalibratedBarterDestination =>
            HasExplicitBarterDestination
            && Island == EnumLists.Island.Olvia
            && StringComparer.OrdinalIgnoreCase.Equals(
                NavigationSource, "bdocodex-barterer-npc-58973");

        /// <summary>
        /// True only when the route destination came from an explicit
        /// IslandBarterLocations catalog row whose NPC coordinate is source-derived,
        /// rather than a node/wharf fallback or a user-calibrated correction.
        /// </summary>
        public bool HasVerifiedBarterDestination =>
            HasExplicitBarterDestination
            && !HasUserCalibratedBarterDestination;

        /// <summary>
        /// True only when no explicit barter-NPC destination is available and the
        /// route is using the best coordinate retained in Islands.csv instead.
        /// A user-calibrated NPC is not a catalog fallback.
        /// </summary>
        public bool UsesCatalogFallbackDestination =>
            !HasExplicitBarterDestination;

        public string RouteDestinationProvenance =>
            HasUserCalibratedBarterDestination
                ? "UserCalibratedNpc"
                : HasVerifiedBarterDestination
                    ? "VerifiedNpc"
                    : "CatalogFallback";

        // The HD map source labels islands at their node anchors, while sailing
        // routes need the coastal barterer. Keep the original node coordinate
        // only as an internal calibration pair; persisted plans continue to
        // expose NavigationX/Y as the actual sailing destination.
        //
        // MapAnchorSource tracks where MapAnchorX/Y came from so the BDF
        // catalog can decide whether this island may participate in the
        // trusted-source affine calibration pool (e.g. anchors whose
        // MapAnchorSource starts with "bdo-world-" are trusted; barterer
        // destinations with "bdocodex-barterer-" are not).
        internal double? MapAnchorX { get; set; }
        internal double? MapAnchorY { get; set; }
        internal string MapAnchorSource { get; set; } = string.Empty;

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
