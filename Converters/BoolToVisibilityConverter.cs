using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace GnuSorter;

/// <summary>Converts a bool to a <see cref="Visibility"/> for XAML bindings.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is bool flag && flag ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility visibility && visibility == Visibility.Visible;
}
