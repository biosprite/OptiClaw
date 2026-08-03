using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using OptiClaw.Core.Models;

namespace OptiClaw;

public sealed class GameStatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var resourceKey = value switch
        {
            GameStatusKind.Installed => "ClawXeSSBlueBrush",
            GameStatusKind.Ready => "ClawGreenBrush",
            _ => "ClawRedBrush"
        };

        return Application.Current.Resources[resourceKey];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
