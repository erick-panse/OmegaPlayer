﻿using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmegaPlayer.UI.Converters
{
    /// <summary>
    /// Converter to change Opacity on hover to allow child controls to be visible
    /// Used when dinamically changing IsVisible hides the child controls even on IsVisible="True"
    /// </summary>
    public class BooleanToOpacityConverter : IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null)
                return 0.0;

            if (values.Count >= 2 && values[0] is bool isPointerOver && values[1] is bool isSelected)
            {
                return isPointerOver || isSelected ? 1.0 : 0.0; //Change opacity of the element
            }

            return 0.0; // Default opacity to hidden if conditions aren't met
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}