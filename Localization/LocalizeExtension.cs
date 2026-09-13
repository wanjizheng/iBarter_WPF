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
// Mechanism:
//   * For normal DependencyProperty targets, ProvideValue returns a
//     one-way Binding to this extension's Text property.  When
//     LanguageService raises LanguageChanged, Text raises INPC and
//     already-rendered UI updates in place.
//   * For non-DP targets (for example style Setters / design-time
//     parser paths), ProvideValue falls back to a plain string.  That
//     keeps the extension out of WPF's fragile BAML Setter binding path
//     while still giving the visible UI live language flips.

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
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
                var provideValueTarget =
                    serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
                if (provideValueTarget?.TargetObject is DependencyObject
                    && provideValueTarget.TargetProperty is DependencyProperty) {
                    var binding = new Binding(nameof(Text)) {
                        Source = this,
                        Mode = BindingMode.OneWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    };
                    return binding.ProvideValue(serviceProvider);
                }
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
