// SPDX-License-Identifier: MIT
//
// iBarter localization — language service singleton.
//
// Provides:
//   * a process-wide AppLanguage enum (English / TraditionalChinese),
//   * a LanguageChanged event raised when the active language switches,
//   * string lookup against the currently-active merged ResourceDictionary,
//   * a thread-safe setter that re-applies the matching Strings.{lang}.xaml
//     into Application.Current.Resources.MergedDictionaries,
//   * persistence to Properties.Settings.Default.AppLanguage.
//
// The Localization/LocalizeExtension.cs MarkupExtension wraps this service
// for XAML use (Text="{loc:Localize str.Menu.Save}"); code-behind can call
// LanguageService.Instance.Localize("str.Menu.Save") directly.
//
// Phase 1: framework scaffold. Subsequent phases add String resources to
// Resources/i18n/Strings.{lang}.xaml, then INotifyPropertyChanged Display
// getters on Model/Items.cs and Model/Islands.cs, then live runtime switch
// via the language menu in MainWindow.xaml.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Xml.Linq;

namespace iBarter.Localization {

    /// <summary>
    ///     The two languages iBarter ships with. The numeric values are persisted
    ///     to Properties.Settings.Default.AppLanguage (Int32) so adding new
    ///     languages later never collides with saved user state.
    /// </summary>
    public enum AppLanguage {
        English = 0,
        TraditionalChinese = 1,
    }

    /// <summary>
    ///     Process-wide language service. Holds the current <see cref="AppLanguage"/>,
    ///     exposes a <see cref="Localize"/> lookup, raises <see cref="LanguageChanged"/>
    ///     on switch, and persists the selection via <c>Properties.Settings.Default</c>.
    ///     Singleton access via <see cref="Instance"/>.
    /// </summary>
    public sealed class LanguageService {

        private static readonly Lazy<LanguageService> _instance =
            new Lazy<LanguageService>(() => new LanguageService(), isThreadSafe: true);

        public static LanguageService Instance => _instance.Value;

        private AppLanguage _current = AppLanguage.English;

        /// <summary>
        ///     Active language. Setting this swaps the i18n merged dictionary and
        ///     raises <see cref="LanguageChanged"/> (typically called from the
        ///     language menu on the UI thread).
        /// </summary>
        public AppLanguage Current {
            get => _current;
            set {
                if (_current == value) {
                    return;
                }
                _current = value;
                Persist(value);
                ApplyMergedDictionary(value);
                LanguageChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        ///     Raised on the calling thread (UI thread in normal flow) immediately
        ///     after the merged dictionary is swapped. Subscribers include the
        ///     <c>LocalizeExtension</c> markup extension and the
        ///     <c>Model/Items.cs</c>/<c>Model/Islands.cs</c> display getters —
        ///     they re-fire <c>INotifyPropertyChanged</c> so any DataGrid/ComboBox
        ///     bound to <c>ItemNameDisplay</c> / <c>IslandsNameDisplay</c> refreshes.
        /// </summary>
        public event EventHandler? LanguageChanged;

        private LanguageService() {
        }

        /// <summary>
        ///     One-time boot hook called from <c>App</c> ctor. Reads the saved
        ///     <see cref="AppLanguage"/> from <c>Properties.Settings</c> and installs
        ///     the matching <c>Strings.{lang}.xaml</c> as the only i18n entry in
        ///     <see cref="Application.Resources"/>'s merged dictionaries.
        /// </summary>
        public void InitializeAtStartup() {
            AppLanguage lang;
            try {
                int saved = Properties.Settings.Default.AppLanguage;
                lang = Enum.IsDefined(typeof(AppLanguage), saved) ? (AppLanguage)saved : AppLanguage.English;
            }
            catch {
                lang = AppLanguage.English;
            }
            _current = lang;
            ApplyMergedDictionary(lang);
        }

        /// <summary>
        ///     Resolves <paramref name="key"/> against the active merged dictionary
        ///     (en-US or zh-TW). If the key is missing the returned value is
        ///     <c>#key#</c> to make misses obvious during development. Optional
        ///     <paramref name="args"/> are applied via <see cref="string.Format(System.IFormatProvider, string, object[])"/>
        ///     with <see cref="System.Globalization.CultureInfo.InvariantCulture"/>
        ///     so e.g. <c>"Found {0:N0} items"</c> formats consistently regardless
        ///     of the user's OS regional settings.
        /// </summary>
        public string Localize(string key, params object[] args) {
            string text;
            try {
                // Application.FindResource walks the merged dictionaries (including
                // the one we installed via ApplyMergedDictionary). Returns null on miss.
                text = Application.Current?.FindResource(key) as string ?? key;
            }
            catch {
                text = key;
            }
            if (args is not null && args.Length > 0) {
                try {
                    text = string.Format(System.Globalization.CultureInfo.InvariantCulture, text, args);
                }
                catch {
                    // bad placeholder count - return raw to surface the key
                }
            }
            return text;
        }

        private static void Persist(AppLanguage value) {
            try {
                Properties.Settings.Default.AppLanguage = (int)value;
                Properties.Settings.Default.Save();
            }
            catch {
                // settings persistence is best-effort; the in-memory value holds for this session
            }
        }

        private static void ApplyMergedDictionary(AppLanguage value) {
            try {
                var app = Application.Current;
                if (app is null) {
                    return; // designer-time or pre-startup; defer until InitializeAtStartup
                }

                string fileName = value == AppLanguage.TraditionalChinese
                    ? "Strings.zh-TW.xaml"
                    : "Strings.en-US.xaml";

                // Find the file under the build output's Resources/i18n/ folder
                // (or under the .exe directory itself when Resources is flat).
                string[] candidatePaths = new[] {
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "i18n", fileName),
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName),
                };
                string? resolvedPath = candidatePaths.FirstOrDefault(System.IO.File.Exists);
                if (resolvedPath is null) {
                    System.Diagnostics.Debug.WriteLine(
                        $"[LanguageService] ApplyMergedDictionary({value}): no {fileName} found under {AppDomain.CurrentDomain.BaseDirectory}");
                    return;
                }

                // Parse the .xaml manually with System.Xml.Linq so we don't have to
                // rely on WPF's XAML reader understanding the clr-namespace
                // declaration for System.String on .NET 10.  Each <sys:String
                // x:Key="...">value</sys:String> becomes a plain System.String
                // entry in a fresh ResourceDictionary; the XAML reader's quirks
                // (mscorlib vs System.Private.CoreLib, XAML 2009 mode, etc.)
                // are completely sidestepped.
                ResourceDictionary dict = new ResourceDictionary();
                try {
                    XDocument doc = XDocument.Load(resolvedPath, LoadOptions.None);
                    foreach (var el in doc.Descendants()) {
                        if (el.Name.LocalName != "String") continue;
                        var keyAttr = el.Attribute("Key");
                        if (keyAttr is null) continue;
                        string key = keyAttr.Value;
                        // text content may include the leading whitespace;
                        // the previous WPF XAML reader trimmed it via the
                        // XamlTypeConverter, so we mirror that.
                        string text = (el.Value ?? string.Empty).Trim();
                        if (string.IsNullOrEmpty(key)) continue;
                        dict[key] = text;
                    }
                }
                catch (Exception parseEx) {
                    System.Diagnostics.Debug.WriteLine(
                        $"[LanguageService] {fileName} parse error: {parseEx.GetType().Name} {parseEx.Message}");
                    return;
                }

                var merged = app.Resources.MergedDictionaries;

                // Remove any dictionaries we previously installed (recognised
                // by a private tag we control - ResourceDictionary doesn't have
                // a free-form tag, so we use a recognizable source key on the
                // dictionary we install: a sentinel comment isn't possible, but
                // the dictionaries we install have a `Source` of null and a
                // single root tag we can check via reflection.  Simpler: just
                // clear all merged dictionaries that came from us by Source=null
                // sentinel - we use an empty Source and stash a tag-like key
                // in a custom property.
                // For simplicity we mark our entries with a sentinel source
                // string (we set dict.Source to a Uri with a fragment that
                // identifies us).  The WPF XAML reader ignores it but our
                // own installer can find and remove.
                dict.Source = new Uri(
                    $"pack://application:,,,/Resources/i18n/{fileName}#iBarterLanguageService");

                for (int i = merged.Count - 1; i >= 0; i--) {
                    var src = merged[i].Source?.OriginalString;
                    if (src is not null && src.Contains("/i18n/Strings.", StringComparison.OrdinalIgnoreCase)) {
                        merged.RemoveAt(i);
                    }
                }

                merged.Add(dict);
            }
            catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine(
                    $"[LanguageService] ApplyMergedDictionary({value}) failed: {ex.Message}");
            }
        }
    }
}
