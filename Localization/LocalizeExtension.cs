// SPDX-License-Identifier: MIT
//
// iBarter localization — custom MarkupExtension equivalent of
// WPFLocalizationExtension's `{lex:Loc Key="..."}` markup.
//
// XAML usage:
//   xmlns:loc="clr-namespace:iBarter.Localization"
//   <TextBlock Text="{loc:Localize str.Menu.Save}" />
//
//   <Setter Property="Header" Value="{loc:Localize str.Planner.Save}" />
//
// Mechanism (simplified for the Phase 9 hotfix chain):
//   * ProvideValue returns a plain string (not a Binding).  WPF's
//     BAML compiler on .NET 10 throws "Value cannot be null.
//     (Parameter 'key')" inside Binding's ProvideValue path when
//     the markup extension is used in property setters during BAML
//     compilation, regardless of our catch block.  Returning a
//     plain string sidesteps that error entirely.
//   * Live language flips are still observable through the rest of
//     the i18n stack:
//       - Items.ItemNameDisplay / ItemTierDisplay re-fire INPC on
//         every Items instance when LanguageChanged is raised;
//       - Islands.IslandsNameDisplay does the same on Islands;
//       - Barter.Item1NameDisplay / Item2NameDisplay / IsLandNameDisplay
//         re-fire because Items/Islands they reference re-fired
//         (Phase 5 WireUpItem1/2 / WireUpIsland subscribe chain);
//       - The per-View ApplyLocalizedHeaders re-walks SfDataGrid
//         column headers on every LanguageChanged (Phase 2 wiring).
//     So the chrome that doesn't auto-refresh is purely the
//     {loc:Localize} XAML string substitution; everything data-bound
//     to *Display getters does refresh in place.
//   * When the user re-opens a View (or restarts the app), the
//     XAML re-evaluates ProvideValue and the new language's text
//     appears.  This is an acceptable user flow for a menu-driven
//     language switch: "switch language, close + reopen the view".

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Markup;

namespace iBarter.Localization {

    [ContentProperty(nameof(Key))]
    [MarkupExtensionReturnType(typeof(object))]
    public class LocalizeExtension : MarkupExtension, INotifyPropertyChanged {

        public LocalizeExtension() {
            HookLanguageChanged();
        }

        public LocalizeExtension(string key) : this() {
            Key = key;
        }

        private string _key = string.Empty;

        public string Key {
            get => _key;
            set {
                if (_key == value) {
                    return;
                }
                _key = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Text));
            }
        }

        /// <summary>
        ///     Resolved localized string.  Named "Text" to avoid a WPF BAML
        ///     name-table collision on .NET 10 - a property literally named
        ///     "Value" makes the BAML compiler throw "Value cannot be
        ///     null. (Parameter 'key')" during markup extension
        ///     serialisation.
        /// </summary>
        public object Text {
            get {
                try {
                    var svc = LanguageService.Instance;
                    if (svc is null || string.IsNullOrEmpty(_key)) {
                        return _key;
                    }
                    return svc.Localize(_key);
                }
                catch {
                    return _key;
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool _hooked;

        private void HookLanguageChanged() {
            if (_hooked) {
                return;
            }
            _hooked = true;
            try {
                LanguageService.Instance.LanguageChanged -= OnLanguageServiceChanged;
                LanguageService.Instance.LanguageChanged += OnLanguageServiceChanged;
            }
            catch {
                // design-time pass; safe to ignore
            }
        }

        private void OnLanguageServiceChanged(object? sender, EventArgs e) {
            OnPropertyChanged(nameof(Text));
        }

        public override object ProvideValue(IServiceProvider serviceProvider) {
            try {
                HookLanguageChanged();
                return Text;
            }
            catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine(
                    $"[LocalizeExtension] ProvideValue failed for key '{_key}': {ex.Message}");
                return _key;
            }
        }
    }
}
