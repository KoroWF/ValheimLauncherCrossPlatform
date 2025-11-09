using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace ValheimLauncherCrossPlatform.Models.Utils
{
    public class ProgressToClipConverter : IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Count == 3 && values[0] is double value && values[1] is double maximum && values[2] is double width)
            {
                if (maximum > 0)
                {
                    double progressWidth = value / maximum * width;
                    // For Avalonia, return a Rect
                    return new Rect(0, 0, progressWidth, 1000); // Height can be large, it will be clipped by the control
                }
            }
            return new Rect(0, 0, 0, 1000); // Fallback
        }
    }
}