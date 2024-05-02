#region Copyright Syncfusion Inc. 2001-2024.
// Copyright Syncfusion Inc. 2001-2024. All rights reserved.
// Use of this code is subject to the terms of our license.
// A copy of the current license can be obtained at any time by e-mailing
// licensing@syncfusion.com. Any infringement will be prosecuted under
// applicable laws. 
#endregion
using System.Windows;

namespace iBarter {
    /// <summary>
    /// Convert between boolean and visibility
    /// </summary>
    public sealed class BooleanToVisibilityConverter : BoolToObjectConverter {
        public BooleanToVisibilityConverter() {
            TrueValue = Visibility.Visible;
            FalseValue = Visibility.Collapsed;
        }
    }
}