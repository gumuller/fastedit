using System.Windows;

namespace FastEdit.Infrastructure;

/// <summary>
/// A Freezable proxy that allows DataTemplate bindings to reach
/// a DataContext outside the visual tree (e.g., Window's DataContext).
/// </summary>
public class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));

    public object Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
