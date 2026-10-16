using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace TicketSplitter.App.Converters;

/// <summary>bool -&gt; Visibility。经典 {Binding} 没有 x:Bind 那种内建 bool 转换，必须自己提供。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>为 true 时取反。</summary>
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is bool b && b;
        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>非空 -&gt; Visibility。</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is not null;
        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
