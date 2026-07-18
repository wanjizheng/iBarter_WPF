#region Copyright Syncfusion Inc. 2001-2024.

// Copyright Syncfusion Inc. 2001-2024. All rights reserved.
// Use of this code is subject to the terms of our license.
// A copy of the current license can be obtained at any time by e-mailing
// licensing@syncfusion.com. Any infringement will be prosecuted under
// applicable laws. 

#endregion

using iBarter.View;
using Syncfusion.SfSkinManager;
using Syncfusion.UI.Xaml.Diagram;
using Syncfusion.Windows.Shared;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;

namespace iBarter.ViewModel {
    public class MainWindowViewModel : NotificationObject {
        public static string DefaultThemeName = "Windows11Light";

        public static System.Globalization.CultureInfo AppCulture;

        #region Member

        /// <summary>
        /// Maintains the selected <see cref="VisualStyles"/>
        /// </summary>
        private string selectedthemename = MainWindowViewModel.DefaultThemeName;
        // Selecting a theme replaces the palette collection and selects its
        // default palette.  Suppress the nested SelectedPalette callback so
        // one user selection produces exactly one theme application.
        private bool synchronizingThemeSelection;

        /// <summary>
        /// Maintains busy status of sample browser while switching between themes.
        /// </summary>
        private bool isProductDemoBusy = false;

        /// <summary>
        /// Property to store busy status of sample browser while launch the show case demo.
        /// </summary>
        private bool isShowCaseDemoBusy = false;

        /// <summary>
        /// Gets or sets the property value indicating whether the products demos launch in separate window
        /// </summary>
        private bool isWindowMode = true;

        /// <summary>
        /// Gets or sets the property value indicating whether the selected sample thememode is inherit or not
        /// </summary>
        private bool isThemeInheritMode = true;


        /// <summary>
        ///  Maintains the command for <see cref="Window"/> loaded event.
        /// </summary>
        private ICommand windowLoadedCommand;

        /// <summary>
        /// Maintain the command to change the visibility of the theme panel
        /// </summary>
        private ICommand themepanelvisibilitycommand;

        /// <summary>
        /// Gets or set the title bar background
        /// </summary>
        private Brush titleBarBackground = new SolidColorBrush(Color.FromRgb(43, 87, 154));

        /// <summary>
        /// Gets or set the title bar foreground
        /// </summary>
        private Brush titleBarForeground = new SolidColorBrush(Color.FromRgb(255, 255, 255));


        /// <summary>
        /// Property to store visibility state of blur layer in sample browser.
        /// </summary>
        private Visibility blurVisibility = Visibility.Collapsed;

        #endregion

        /// <summary>
        /// Constructor
        /// </summary>
        public MainWindowViewModel() {
            AppCulture = System.Threading.Thread.CurrentThread.CurrentUICulture;

            PopulatePaletteList();
        }

        #region Public Properties

        /// <summary>
        /// Gets or sets the property value indicating whether it is store package application or not.
        /// </summary>
        public static bool IsStoreApp = false;

        /// <summary>
        /// Gets or sets the property value indicating whether automate the samples to log bidding error.
        /// </summary>
        public static bool CanAutomate = false;

        /// Property to store busy status of sample browser while switching between themes.
        /// </summary>
        public bool IsProductDemoBusy {
            get { return isProductDemoBusy; }
            set {
                isProductDemoBusy = value;
                RaisePropertyChanged("IsProductDemoBusy");
            }
        }

        /// <summary>
        /// Property to store busy status of sample browser while launch the show case demo.
        /// </summary>
        public bool IsShowCaseDemoBusy {
            get { return isShowCaseDemoBusy; }
            set {
                isShowCaseDemoBusy = value;
                RaisePropertyChanged("IsShowCaseDemoBusy");
            }
        }

        /// <summary>
        /// Gets or sets the property value indicating whether the products demos launch in separate window
        /// </summary>
        public bool IsWindowMode {
            get { return isWindowMode; }
            set {
                isWindowMode = value;
                RaisePropertyChanged("IsWindowMode");
            }
        }


        /// <summary>
        /// Gets or sets the property value indicating whether the selected sample thememode  is inherit or not.
        /// </summary>
        public bool IsThemeInheritMode {
            get { return isThemeInheritMode; }
            set {
                isThemeInheritMode = value;
                RaisePropertyChanged("IsThemeInheritMode");
            }
        }


        /// <summary>
        /// Gets or sets the selected <see cref="VisualStyles"/> of application.
        /// </summary>
        public string SelectedThemeName {
            get { return selectedthemename; }
            set {
                if (string.IsNullOrWhiteSpace(value) || selectedthemename == value)
                    return;

                selectedthemename = value;
                if (selectedthemename == "SystemTheme") {
                    ColorPaletteVisibility = false;
                }
                else {
                    ColorPaletteVisibility = true;
                    Palettes = new ObservableCollection<Palette>(PaletteList.Where(x => (x.Theme.Equals(selectedthemename))).ToList<Palette>());
                    synchronizingThemeSelection = true;
                    SelectedPalette = Palettes.FirstOrDefault(x => x.Name.Equals("Default"))
                        ?? Palettes.FirstOrDefault();
                    synchronizingThemeSelection = false;
                }

                ApplySelectedTheme();
                RaisePropertyChanged(nameof(SelectedThemeName));
                RaisePropertyChanged(nameof(SelectedTheme));
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


        public ICommand ThemePanelVisibilityCommand {
            get {
                themepanelvisibilitycommand = new DelegateCommand<object>(ChangeVisibilityofThemepanel);
                return themepanelvisibilitycommand;
            }
        }

        private bool themepanelvisibility = false;

        /// <summary>
        /// Gets or sets the property used to Indicates the visibility of the ThemePanel
        /// </summary>
        public bool ThemePanelVisibility {
            get { return themepanelvisibility; }
            set {
                themepanelvisibility = value;
                RaisePropertyChanged(nameof(ThemePanelVisibility));
            }
        }

        private bool themebuttonvisibility = true;

        public bool ThemeButtonVisibility {
            get { return themebuttonvisibility; }
            set {
                themebuttonvisibility = value;
                RaisePropertyChanged(nameof(ThemeButtonVisibility));
            }
        }


        private void ChangeVisibilityofThemepanel(object obj) {
            ThemePanelVisibility = false;
        }


        // /// <summary>
        // /// Gets the command  <see cref="MainWindow"/> loaded event.
        // /// </summary>
        // public ICommand WindowLoadedCommand {
        //     get {
        //         if (windowLoadedCommand == null) {
        //             windowLoadedCommand = new DelegateCommand<object>(WindowLoaded);
        //         }
        //
        //         return windowLoadedCommand;
        //     }
        // }


        /// <summary>
        /// Gets or sets the visibility state of the blur layer in sample browser.
        /// </summary>
        public Visibility BlurVisibility {
            get { return blurVisibility; }
            set {
                blurVisibility = value;
                RaisePropertyChanged(nameof(BlurVisibility));
            }
        }

        #endregion


        public Action ThemeChanged;

        private List<Palette> PaletteList = new List<Palette>();

        /// <summary>
        /// Method helps to perform product selection change.
        /// </summary>
        public void OnSelectedProductChanged() {
            // Use the same path as the selector.  The previous direct-field
            // initialization skipped SetTheme on startup, then the first UI
            // change had to repair a partially themed visual tree.
            selectedtheme = themelist.FirstOrDefault(theme =>
                theme.ThemeName == MainWindowViewModel.DefaultThemeName);
            selectedthemename = string.Empty;
            SelectedThemeName = MainWindowViewModel.DefaultThemeName;
            RaisePropertyChanged(nameof(SelectedTheme));
        }


        private bool colorpalettevisibility = true;

        /// <summary>
        /// Gets or sets the property value indicating whether the colorpalette combobox should be visible or not
        /// </summary>
        public bool ColorPaletteVisibility {
            get { return colorpalettevisibility; }
            set {
                colorpalettevisibility = value;
                RaisePropertyChanged("ColorPaletteVisibility");
            }
        }

        /// <summary>
        /// Method helps to update the selected <see cref="VisualStyles"/>
        /// </summary>
        /// <param name="selectedTheme">Selected Theme</param>
        private void OnThemeChanged(string selectedTheme) {
            // App.xaml opts into ApplyStylesOnApplication.  In that mode the
            // application-level API is the single source of truth: mixing it
            // with SetTheme(window, ...) leaves the previous theme's resource
            // dictionaries behind (notably Dark -> Light).  ApplicationTheme
            // replaces those dictionaries and updates both current and future
            // windows in one operation.
            SfSkinManager.ApplicationTheme = new Theme { ThemeName = selectedTheme };

            UpdateTitleBarBackgroundandForeground(selectedTheme);
            UpdateApplicationPalette(selectedTheme);

            if (ThemeChanged != null) {
                ThemeChanged();
            }
        }

        private void UpdateTitleBarBackgroundandForeground(string selectedTheme) {
            if (selectedTheme == "SystemTheme") {
                TitleBarBackground = SystemColors.HighlightBrush;
                TitleBarForeground = SystemColors.HighlightTextBrush;

                App.myStorageVM.TitleBarBackground = SystemColors.HighlightBrush;
                App.myStorageVM.TitleBarForeground = SystemColors.HighlightTextBrush;

                App.mySVM.TitleBarBackground = SystemColors.HighlightBrush;
                App.mySVM.TitleBarForeground = SystemColors.HighlightTextBrush;
            }
            else {
                TitleBarBackground = SelectedPalette.PrimaryBackground;
                TitleBarForeground = SelectedPalette.PrimaryForeground;

                App.myStorageVM.TitleBarBackground = SelectedPalette.PrimaryBackground;
                App.myStorageVM.TitleBarForeground = SelectedPalette.PrimaryForeground;

                App.mySVM.TitleBarBackground = SelectedPalette.PrimaryBackground;
                App.mySVM.TitleBarForeground = SelectedPalette.PrimaryForeground;
            }
        }

        private void ApplySelectedTheme() {
            if (selectedthemename != "SystemTheme" && SelectedPalette is not null)
                RegisterSelectedPalette(selectedthemename);

            OnThemeChanged(selectedthemename);
        }

        private static bool IsDarkTheme(string themeName) =>
            themeName.Contains("Dark", StringComparison.OrdinalIgnoreCase)
            || themeName.Contains("Black", StringComparison.OrdinalIgnoreCase)
            || themeName.Equals("Office2019HighContrast", StringComparison.Ordinal);

        private static void UpdateApplicationPalette(string themeName) {
            if (Application.Current is null) return;

            bool dark = IsDarkTheme(themeName);
            var resources = Application.Current.Resources;
            resources["AppSurfaceBrush"] = CreateBrush(dark ? "#1E1E1E" : "#FFFFFF");
            resources["AppRaisedSurfaceBrush"] = CreateBrush(dark ? "#2A2A2A" : "#F7F9FC");
            resources["AppTextBrush"] = CreateBrush(dark ? "#F1F4F6" : "#18212B");
            resources["AppMutedTextBrush"] = CreateBrush(dark ? "#B9C4CC" : "#526170");
            resources["AppBorderBrush"] = CreateBrush(dark ? "#4A5058" : "#D5DCE5");
            resources["AppCardBrush"] = CreateBrush(dark ? "#252D35" : "#FFFFFF");
            resources["AppCardBorderBrush"] = CreateBrush(dark ? "#5A6672" : "#C3CDD8");
            resources["AppAccentBrush"] = CreateBrush(dark ? "#60D6E4" : "#176F7F");
            resources["AppAccentTextBrush"] = CreateBrush(dark ? "#102126" : "#FFFFFF");
        }

        private static SolidColorBrush CreateBrush(string color) =>
            new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(color)!);


        private ObservableCollection<Themes> themelist = new ObservableCollection<Themes>() {
            new Themes { ThemeName = "Windows11Light", DisplayName = "Light", ThemeType = "Windows 11 Themes", EllipseFill = (Brush)new BrushConverter().ConvertFromString("#FFFFFF"), EllipseStroke = (Brush)new BrushConverter().ConvertFromString("#E5E5E5"), PathFill = (Brush)new BrushConverter().ConvertFromString("#005FB8") },
            new Themes { ThemeName = "Windows11Dark", DisplayName = "Dark", ThemeType = "Windows 11 Themes", EllipseFill = (Brush)new BrushConverter().ConvertFromString("#202020"), EllipseStroke = (Brush)new BrushConverter().ConvertFromString("#333333"), PathFill = (Brush)new BrushConverter().ConvertFromString("#60CDFF") },
            new Themes { ThemeName = "MaterialLight", DisplayName = "Light", ThemeType = "Material Themes", EllipseFill = (Brush)new BrushConverter().ConvertFromString("#FFFFFF"), EllipseStroke = (Brush)new BrushConverter().ConvertFromString("#E9E9E9"), PathFill = (Brush)new BrushConverter().ConvertFromString("#0077FF") },
            new Themes { ThemeName = "MaterialDark", DisplayName = "Dark", ThemeType = "Material Themes", EllipseFill = (Brush)new BrushConverter().ConvertFromString("#848484"), EllipseStroke = (Brush)new BrushConverter().ConvertFromString("#5F5F5F"), PathFill = (Brush)new BrushConverter().ConvertFromString("#0077FF") },
            new Themes { ThemeName = "MaterialLightBlue", DisplayName = "Light Blue", ThemeType = "Material Themes", EllipseFill = (Brush)new BrushConverter().ConvertFromString("#BEFFF3"), EllipseStroke = (Brush)new BrushConverter().ConvertFromString("#E9E9E9"), PathFill = (Brush)new BrushConverter().ConvertFromString("#008FA3") },
            new Themes { ThemeName = "MaterialDarkBlue", DisplayName = "Dark Blue", ThemeType = "Material Themes", EllipseFill = (Brush)new BrushConverter().ConvertFromString("#808EA3"), EllipseStroke = (Brush)new BrushConverter().ConvertFromString("#64638E"), PathFill = (Brush)new BrushConverter().ConvertFromString("#08C2CE") },
            new Themes { ThemeName = "Office2019White", DisplayName = "White", ThemeType = "Office 2019 Themes", EllipseFill = (Brush)new BrushConverter().ConvertFromString("#FFFFFF"), EllipseStroke = (Brush)new BrushConverter().ConvertFromString("#E9E9E9"), PathFill = (Brush)new BrushConverter().ConvertFromString("#0077FF") },
            new Themes { ThemeName = "Office2019Black", DisplayName = "Black", ThemeType = "Office 2019 Themes", EllipseFill = (Brush)new BrushConverter().ConvertFromString("#020202"), EllipseStroke = (Brush)new BrushConverter().ConvertFromString("#262626"), PathFill = (Brush)new BrushConverter().ConvertFromString("#008FA3") },
            new Themes { ThemeName = "Office2019Colorful", DisplayName = "Colorful", ThemeType = "Office 2019 Themes", EllipseFill = (Brush)new BrushConverter().ConvertFromString("#FFFFFF"), EllipseStroke = (Brush)new BrushConverter().ConvertFromString("#E9E9E9"), PathFill = (Brush)new BrushConverter().ConvertFromString("#0077FF") },
            new Themes { ThemeName = "Office2019DarkGray", DisplayName = "Dark Gray", ThemeType = "Office 2019 Themes", EllipseFill = (Brush)new BrushConverter().ConvertFromString("#949494"), EllipseStroke = (Brush)new BrushConverter().ConvertFromString("#949494"), PathFill = (Brush)new BrushConverter().ConvertFromString("#0077FF") },
            new Themes { ThemeName = "Office2019HighContrast", DisplayName = "High Contrast Black", ThemeType = "Office 2019 Themes", EllipseFill = (Brush)new BrushConverter().ConvertFromString("#000000"), EllipseStroke = (Brush)new BrushConverter().ConvertFromString("#000000"), PathFill = (Brush)new BrushConverter().ConvertFromString("#FFD600") },
            new Themes { ThemeName = "Office2019HighContrastWhite", DisplayName = "High Contrast White", ThemeType = "Office 2019 Themes", EllipseFill = (Brush)new BrushConverter().ConvertFromString("#FFFFFF"), EllipseStroke = (Brush)new BrushConverter().ConvertFromString("#E9E9E9"), PathFill = (Brush)new BrushConverter().ConvertFromString("#5419B4") },
            new Themes { ThemeName = "FluentLight", DisplayName = "Light", ThemeType = "Fluent Themes", EllipseFill = (Brush)new BrushConverter().ConvertFromString("#FFFFFF"), EllipseStroke = (Brush)new BrushConverter().ConvertFromString("#E9E9E9"), PathFill = (Brush)new BrushConverter().ConvertFromString("#0077FF") },
            new Themes { ThemeName = "FluentDark", DisplayName = "Dark", ThemeType = "Fluent Themes", EllipseFill = (Brush)new BrushConverter().ConvertFromString("#313131"), EllipseStroke = (Brush)new BrushConverter().ConvertFromString("#000000"), PathFill = (Brush)new BrushConverter().ConvertFromString("#0077FF") },
            new Themes { ThemeName = "SystemTheme", DisplayName = "System Theme", ThemeType = "System Theme", EllipseFill = (Brush)new BrushConverter().ConvertFromString("#FFFFFF"), EllipseStroke = (Brush)new BrushConverter().ConvertFromString("#888888"), PathFill = (Brush)new BrushConverter().ConvertFromString("#000000") }
        };

        public ObservableCollection<Themes> ThemeList {
            get { return themelist; }
            set {
                themelist = value;
                RaisePropertyChanged("ThemeList");
            }
        }

        private Themes selectedtheme;

        public Themes SelectedTheme {
            get { return selectedtheme; }
            set {
                selectedtheme = value;
                if (selectedtheme != null) {
                    SelectedThemeName = selectedtheme.ThemeName;
                }

                RaisePropertyChanged(nameof(SelectedTheme));
            }
        }

        //
        //         /// <summary>
        //         /// Method used to excute   <see cref="MainWindow"/> loaded event
        //         /// </summary>
        //         /// <param name="param"></param>
        //         private void WindowLoaded(object param) {
        // #if DEBUG
        //             if (CanAutomate) {
        //                 try {
        //                     BindingErrorAutomation.RunBindingErrorAutomation(this);
        //                 }
        //                 catch (Exception exception) {
        //                     if (this.SelectedProduct != null && this.SelectedSample != null) {
        //                         ErrorLogging.LogError("Product Sample\\" + this.SelectedProduct.Product + "\\" + this.SelectedSample.SampleName + "@@" + exception.Message + " StackTrace: " + exception.StackTrace + " Exception Source: " + exception.Source);
        //                     }
        //                     else if (this.SelectedShowcaseSample != null) {
        //                         ErrorLogging.LogError("Product ShowCase\\" + this.SelectedShowcaseSample.SampleName + "\\" + this.SelectedShowcaseSample.SampleName + "@@" + exception.Message + " StackTrace: " + exception.StackTrace + " Exception Source: " + exception.Source);
        //                     }
        //                 }
        //             }
        // #endif
        //         }

        /// <summary>
        /// Checks if the source string contains a search string (ignoring case and spaces) and returns a boolean result.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="search"></param>
        /// <returns></returns>
        private bool ContainsIgnoreCaseAndSpaceRemoved(string source, string search) {
            if (source == null || search == null) {
                return false;
            }

            return source.Replace(" ", "").ToLower().Contains(search.Replace(" ", "").ToLower());
        }

        private ObservableCollection<Palette> palettes;

        /// <summary>
        /// Gets or sets the property value indicating the list of colorpalette for the selected Theme.
        /// </summary>
        public ObservableCollection<Palette> Palettes {
            get { return palettes; }
            set {
                palettes = value;
                RaisePropertyChanged("Palettes");
            }
        }

        private Palette selectedpalette;

        public Palette SelectedPalette {
            get { return selectedpalette; }
            set {
                if (ReferenceEquals(selectedpalette, value)) return;
                selectedpalette = value;
                if (!synchronizingThemeSelection && SelectedPalette != null && SelectedPalette.Name != null) {
                    ApplySelectedTheme();
                }

                RaisePropertyChanged("SelectedPalette");
            }
        }

        /// <summary>
        /// Method helps to update the selected ColorPalettes
        /// </summary>
        /// <param name="ThemeName">Selected Theme</param>
        public void OnPaletteChanged(string ThemeName) {
            RegisterSelectedPalette(ThemeName);
            OnThemeChanged(ThemeName);
        }

        private void RegisterSelectedPalette(string ThemeName) {
            switch (ThemeName) {
                case "Windows11Light": {
                        changePalette("Syncfusion.Themes.Windows11Light.WPF.Windows11LightThemeSettings, Syncfusion.Themes.Windows11Light.WPF", "Syncfusion.Themes.Windows11Light.WPF.Windows11Palette, Syncfusion.Themes.Windows11Light.WPF", ThemeName);
                        break;
                    }
                case "Windows11Dark": {
                        changePalette("Syncfusion.Themes.Windows11Dark.WPF.Windows11DarkThemeSettings, Syncfusion.Themes.Windows11Dark.WPF", "Syncfusion.Themes.Windows11Dark.WPF.Windows11Palette, Syncfusion.Themes.Windows11Dark.WPF", ThemeName);
                        break;
                    }
                case "FluentLight": {
                        changePalette("Syncfusion.Themes.FluentLight.WPF.FluentLightThemeSettings, Syncfusion.Themes.FluentLight.WPF", "Syncfusion.Themes.FluentLight.WPF.FluentPalette, Syncfusion.Themes.FluentLight.WPF", ThemeName);
                        break;
                    }
                case "FluentDark": {
                        changePalette("Syncfusion.Themes.FluentDark.WPF.FluentDarkThemeSettings, Syncfusion.Themes.FluentDark.WPF", "Syncfusion.Themes.FluentDark.WPF.FluentPalette, Syncfusion.Themes.FluentDark.WPF", ThemeName);
                        break;
                    }
                case "MaterialLight": {
                        changePalette("Syncfusion.Themes.MaterialLight.WPF.MaterialLightThemeSettings, Syncfusion.Themes.MaterialLight.WPF", "Syncfusion.Themes.MaterialLight.WPF.MaterialPalette, Syncfusion.Themes.MaterialLight.WPF", ThemeName);
                        break;
                    }
                case "MaterialLightBlue": {
                        changePalette("Syncfusion.Themes.MaterialLightBlue.WPF.MaterialLightBlueThemeSettings, Syncfusion.Themes.MaterialLightBlue.WPF", "Syncfusion.Themes.MaterialLightBlue.WPF.MaterialPalette, Syncfusion.Themes.MaterialLightBlue.WPF", ThemeName);
                        break;
                    }
                case "MaterialDark": {
                        changePalette("Syncfusion.Themes.MaterialDark.WPF.MaterialDarkThemeSettings, Syncfusion.Themes.MaterialDark.WPF", "Syncfusion.Themes.MaterialDark.WPF.MaterialPalette, Syncfusion.Themes.MaterialDark.WPF", ThemeName);
                        break;
                    }
                case "MaterialDarkBlue": {
                        changePalette("Syncfusion.Themes.MaterialDarkBlue.WPF.MaterialDarkBlueThemeSettings, Syncfusion.Themes.MaterialDarkBlue.WPF", "Syncfusion.Themes.MaterialDarkBlue.WPF.MaterialPalette, Syncfusion.Themes.MaterialDarkBlue.WPF", ThemeName);
                        break;
                    }
                case "Office2019Colorful": {
                        changePalette("Syncfusion.Themes.Office2019Colorful.WPF.Office2019ColorfulThemeSettings, Syncfusion.Themes.Office2019Colorful.WPF", "Syncfusion.Themes.Office2019Colorful.WPF.Office2019Palette, Syncfusion.Themes.Office2019Colorful.WPF", ThemeName);
                        break;
                    }
                case "Office2019Black": {
                        changePalette("Syncfusion.Themes.Office2019Black.WPF.Office2019BlackThemeSettings, Syncfusion.Themes.Office2019Black.WPF", "Syncfusion.Themes.Office2019Black.WPF.Office2019Palette, Syncfusion.Themes.Office2019Black.WPF", ThemeName);
                        break;
                    }
                case "Office2019White": {
                        changePalette("Syncfusion.Themes.Office2019White.WPF.Office2019WhiteThemeSettings, Syncfusion.Themes.Office2019White.WPF", "Syncfusion.Themes.Office2019White.WPF.Office2019Palette, Syncfusion.Themes.Office2019White.WPF", ThemeName);
                        break;
                    }
                case "Office2019DarkGray": {
                        changePalette("Syncfusion.Themes.Office2019DarkGray.WPF.Office2019DarkGrayThemeSettings, Syncfusion.Themes.Office2019DarkGray.WPF", "Syncfusion.Themes.Office2019DarkGray.WPF.Office2019Palette, Syncfusion.Themes.Office2019DarkGray.WPF", ThemeName);
                        break;
                    }
                case "Office2019HighContrast": {
                        changePalette("Syncfusion.Themes.Office2019HighContrast.WPF.Office2019HighContrastThemeSettings, Syncfusion.Themes.Office2019HighContrast.WPF", "Syncfusion.Themes.Office2019HighContrast.WPF.HighContrastPalette, Syncfusion.Themes.Office2019HighContrast.WPF", ThemeName);
                        break;
                    }
                case "Office2019HighContrastWhite": {
                        changePalette("Syncfusion.Themes.Office2019HighContrastWhite.WPF.Office2019HighContrastWhiteThemeSettings, Syncfusion.Themes.Office2019HighContrastWhite.WPF", "Syncfusion.Themes.Office2019HighContrastWhite.WPF.HighContrastPalette, Syncfusion.Themes.Office2019HighContrastWhite.WPF", ThemeName);
                        break;
                    }
            }

        }

        /// <summary>
        /// Method helps to Change the Color palette for the SelectedTheme
        /// </summary>
        /// <param name="themeType">Type of the theme</param>
        /// <param name="theme">Name of the selected theme</param>
        private void changePalette(string themeType, string paletteType, string theme) {
            object themeSettings = Activator.CreateInstance(Type.GetType(themeType));

            themeSettings.GetType().GetRuntimeProperty("Palette").SetValue(themeSettings, Enum.Parse(Type.GetType(paletteType), SelectedPalette.Name));
            SfSkinManager.RegisterThemeSettings(theme, (IThemeSetting)themeSettings);
        }

        /// <summary>
        /// Method helps to populate palette items into Palettelist
        /// </summary>
        void PopulatePaletteList() {
            var paletteDetails = new List<Palette>();
            var xml = File.ReadAllText(@"Model/PaletteList.xml");
            XmlDocument Doc = new XmlDocument();
            Doc.LoadXml(xml);
            XmlNodeList xmlnode = Doc.GetElementsByTagName("Palettes");

            for (int i = 0; i <= xmlnode.Count - 1; i++) {
                foreach (var node in xmlnode[i].ChildNodes) {
                    var element = node as XmlElement;
                    string name = null, theme = null, primaryBackground = null, primaryForeground = null, primaryBackgroundAlt = null, displayname = null;

                    if (element == null || element.Attributes.Count <= 0)
                        continue;

                    name = element.HasAttribute("Name") ? element.Attributes["Name"].Value : string.Empty;
                    theme = element.HasAttribute("Theme") ? element.Attributes["Theme"].Value : string.Empty;

                    primaryBackground = element.HasAttribute("PrimaryBackground") ? element.Attributes["PrimaryBackground"].Value : string.Empty;
                    primaryForeground = element.HasAttribute("PrimaryForeground") ? element.Attributes["PrimaryForeground"].Value : string.Empty;
                    primaryBackgroundAlt = element.HasAttribute("PrimaryBackgroundAlt") ? element.Attributes["PrimaryBackgroundAlt"].Value : string.Empty;
                    displayname = element.HasAttribute("DisplayName") ? element.Attributes["DisplayName"].Value : string.Empty;
                    var palette = new Palette() {
                        Name = name,
                        Theme = theme,
                        DisplayName = displayname,
                        PrimaryBackground = (Brush)new BrushConverter().ConvertFromString(primaryBackground),
                        PrimaryForeground = (Brush)new BrushConverter().ConvertFromString(primaryForeground),
                        PrimaryBackgroundAlt = (Brush)new BrushConverter().ConvertFromString(primaryBackgroundAlt)
                    };
                    paletteDetails.Add(palette);
                }
            }

            PaletteList = paletteDetails;
        }
    }
}
