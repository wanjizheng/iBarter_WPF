// SPDX-License-Identifier: MIT
//
// iBarter localization — language service singleton.
//
// Phase 9 hotfix 7: completely dropped the ResourceDictionary /
// MergedDictionaries path that has been failing all the previous
// hotfix commits.  The string lookup is now a plain
// Dictionary<string, string> populated once per language swap from
// Resources/i18n/Strings.{lang}.xaml (parsed with XDocument, no
// WPF XAML reader involvement).  Localize() reads from the dict
// directly; the LocalizeExtension's Text getter goes through
// Localize() too.  The Application.FindResource path is kept as a
// last-ditch fallback but is not on the hot path.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace iBarter.Localization {

    /// <summary>
    ///     The two languages iBarter ships with.  Numeric values are
    ///     persisted to Properties.Settings.Default.AppLanguage.
    /// </summary>
    public enum AppLanguage {
        English = 0,
        TraditionalChinese = 1,
    }

    public sealed class LanguageService {

        private static readonly Lazy<LanguageService> _instance =
            new Lazy<LanguageService>(() => new LanguageService(), isThreadSafe: true);
        public static LanguageService Instance => _instance.Value;

        // The single source of truth for localized strings, indexed by key.
        // Populated from Resources/i18n/Strings.{lang}.xaml via XDocument
        // (we don't use WPF's XAML reader because it has been tripping
        // over clr-namespace, assembly, and CLR-name-table issues on
        // .NET 10 throughout Phase 9).  Atomic swap on language change
        // so Localize() never sees a half-populated dict.
        private Dictionary<string, string> _strings =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private AppLanguage _current = AppLanguage.English;

        public AppLanguage Current {
            get => _current;
            set {
                if (_current == value) {
                    return;
                }
                _current = value;
                Persist(value);
                ApplyLanguage(value);
                LanguageChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? LanguageChanged;

        private LanguageService() {
        }

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
            ApplyLanguage(lang);
        }

        public string Localize(string key, params object[] args) {
            string text;
            try {
                if (_strings.TryGetValue(key, out var v)) {
                    text = v;
                }
                else {
                    // Last-ditch: also try Application.FindResource for any
                    // other dictionary that might have the key (e.g. someone
                    // added a WindowStyle-level resource).  Returns null on
                    // miss; we fall back to the key as a visible stub so
                    // missing translations are obvious in QA.
                    text = System.Windows.Application.Current?.FindResource(key) as string ?? key;
                }
            }
            catch {
                text = key;
            }
            if (args is not null && args.Length > 0) {
                try {
                    text = string.Format(System.Globalization.CultureInfo.InvariantCulture, text, args);
                }
                catch {
                    // bad placeholder count - return raw
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
                // best-effort
            }
        }

        private void ApplyLanguage(AppLanguage value) {
            try {
                string fileName = value == AppLanguage.TraditionalChinese
                    ? "Strings.zh-TW.xaml"
                    : "Strings.en-US.xaml";

                string[] candidatePaths = new[] {
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "i18n", fileName),
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName),
                };
                string? resolvedPath = candidatePaths.FirstOrDefault(System.IO.File.Exists);
                if (resolvedPath is null) {
                    System.Diagnostics.Debug.WriteLine(
                        $"[LanguageService] ApplyLanguage({value}): NO {fileName} found under {AppDomain.CurrentDomain.BaseDirectory}");
                    _strings = new Dictionary<string, string>(StringComparer.Ordinal);
                    return;
                }

                var fresh = new Dictionary<string, string>(StringComparer.Ordinal);
                try {
                    var doc = XDocument.Load(resolvedPath, LoadOptions.None);
                    foreach (var el in doc.Descendants()) {
                        if (el.Name.LocalName != "String") continue;
                        var keyAttr = el.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key");
                        if (keyAttr is null) continue;
                        string key = keyAttr.Value;
                        if (string.IsNullOrEmpty(key)) continue;
                        string text = (el.Value ?? string.Empty).Trim();
                        fresh[key] = text;
                    }
                }
                catch (Exception parseEx) {
                    System.Diagnostics.Debug.WriteLine(
                        $"[LanguageService] {fileName} parse error: {parseEx.GetType().Name} {parseEx.Message}");
                }

                // Atomic swap so Localize() never sees a half-populated dict.
                _strings = fresh;
                System.Diagnostics.Debug.WriteLine(
                    $"[LanguageService] ApplyLanguage({value}): loaded {fresh.Count} keys from {resolvedPath}");
            }
            catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine(
                    $"[LanguageService] ApplyLanguage({value}) failed: {ex.Message}");
            }
        }

        // Backwards-compat shim so callers that still reference
        // ApplyMergedDictionary keep compiling.  The path is identical
        // to ApplyLanguage now (the old MergedDictionaries logic was
        // removed because every hotfix attempt around WPF's XAML
        // resource pipeline tripped over a different .NET 10 issue).
        private void ApplyMergedDictionary(AppLanguage value) => ApplyLanguage(value);
    }
}
