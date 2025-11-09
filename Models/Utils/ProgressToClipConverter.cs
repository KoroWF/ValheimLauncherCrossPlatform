using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace ValheimLauncherCrossPlatform.Models.Utils
{
    /// <summary>
    /// Converts progress values to a clipping rectangle for UI progress bars.
    /// </summary>
    public class ProgressToClipConverter : IMultiValueConverter
    {
        /// <summary>
        /// Converts progress, maximum, and width values to a <see cref="Rect"/> representing the visible progress area.
        /// </summary>
        /// <param name="values">An array containing the current value, maximum value, and total width.</param>
        /// <param name="targetType">The target type of the binding (should be <see cref="Rect"/>).</param>
        /// <param name="parameter">An optional parameter (not used).</param>
        /// <param name="culture">The culture to use in the converter.</param>
        /// <returns>A <see cref="Rect"/> representing the clipping area for the progress bar.</returns>
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Count == 3 && values[0] is double value && values[1] is double maximum && values[2] is double width)
            {
                if (maximum > 0)
                {
                    double progressWidth = value / maximum * width;
                    return new Rect(0, 0, progressWidth, 1000);
                }
            }
            return new Rect(0, 0, 0, 1000);
        }
    }
}