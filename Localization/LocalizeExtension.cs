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
// Mechanism (mirrors WPFLocalizationExtension 3.10.0 LocExtension):
//   * ProvideValue asks the IProvideValueTarget service for the host element.
//     When the target is a Setter (style/trigger) we cannot host a Binding,
//     so we fall back to the literal resolved string.
//   * Otherwise we return a one-way Binding whose Source is this extension
//     instance and whose Path is "Value".  WPF's binding engine subscribes
//     to INotifyPropertyChanged on this instance.
//   * On construction we subscribe to LanguageService.Instance.LanguageChanged
//     and re-fire PropertyChanged("Value") whenever the language flips.  The
//     open Binding then re-pulls Value → re-resolves the string → updates
//     the target DependencyObject, with no restart and no visual-tree rebuild.
//
// Phase 1: bare minimum (Key + Value); Phase 7 adds Args / StringFormat;
// later phases may add a ForceCulture parameter.

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;

namespace iBarter.Localization {

    /// <summary>
    ///     Markup extension that resolves the active <see cref="LanguageService"/>
    ///     string for the supplied <see cref="Key"/> at the time the host
    ///     DependencyProperty is materialised, then re-resolves it whenever
    ///     the language flips.
    /// </summary>
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

        /// <summary>
        ///     Resource key under the active merged dictionary
        ///     (Resources/i18n/Strings.{lang}.xaml). Defaults to empty string
        ///     so designers never NRE before <c>Key</c> is bound.
        /// </summary>
        public string Key {
            get => _key;
            set {
                if (_key == value) {
                    return;
                }
                _key = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Value));
            }
        }

        /// <summary>
        ///     Resolved localized string. Re-evaluated every time WPF re-pulls
        ///     it (which happens when <see cref="INotifyPropertyChanged"/> fires
        ///     on this instance, including after a language switch).
        /// </summary>
        public object Value {
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
                // The Application initializes LanguageService.Instance before any UI loads;
                // if a markup extension is evaluated at design time the Instance still exists.
                LanguageService.Instance.LanguageChanged -= OnLanguageServiceChanged;
                LanguageService.Instance.LanguageChanged += OnLanguageServiceChanged;
            }
            catch {
                // Singleton not yet built (very early parse pass). The next ProvideValue
                // call will retry the hook; the meantime we just don't auto-refresh.
            }
        }

        private void OnLanguageServiceChanged(object? sender, EventArgs e) {
            OnPropertyChanged(nameof(Value));
        }

        /// <summary>
        ///     Returns either a literal <see cref="Value"/> (for Setter / style hosts
        ///     which can't accept a <see cref="Binding"/>) or a one-way
        ///     <c>Binding("Value") { Source = this }</c> that listens to
        ///     <see cref="INotifyPropertyChanged"/> on this extension.
        /// </summary>
        public override object ProvideValue(IServiceProvider serviceProvider) {
            try {
                if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget pvt) {
                    // Setter / trigger / style values cannot host a live Binding;
                    // return a plain string instead. The set only re-evaluates on
                    // next ProvideValue call (page reload, etc.).
                    if (pvt.TargetObject is Setter) {
                        return Value;
                    }
                }

                HookLanguageChanged(); // belt-and-braces; harmless if already done

                var binding = new Binding(nameof(Value)) {
                    Source = this,
                    Mode = BindingMode.OneWay,
                };
                // Delegate to Binding.ProvideValue so that target-specific quirks
                // (e.g. ToolTipService vs DependencyProperty) are handled uniformly.
                return binding.ProvideValue(serviceProvider);
            }
            catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine(
                    $"[LocalizeExtension] ProvideValue failed for key '{_key}': {ex.Message}");
                return _key;
            }
        }
    }
}
