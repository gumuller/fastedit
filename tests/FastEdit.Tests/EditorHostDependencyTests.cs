using FastEdit.Services.Interfaces;
using FastEdit.Theming;
using FastEdit.ViewModels;
using FastEdit.Views.Controls;

namespace FastEdit.Tests;

public class EditorHostDependencyTests
{
    [Theory]
    [MemberData(nameof(ServiceDependencyProperties))]
    public void ServiceDependency_IsBindableDependencyProperty(
        System.Windows.DependencyProperty property,
        string name,
        Type propertyType)
    {
        Assert.Equal(propertyType, property.PropertyType);
        Assert.Equal(name, property.Name);
        Assert.Equal(typeof(EditorHost), property.OwnerType);
    }

    public static TheoryData<System.Windows.DependencyProperty, string, Type> ServiceDependencyProperties()
    {
        return new TheoryData<System.Windows.DependencyProperty, string, Type>
        {
            { EditorHost.LineFilterServiceProperty, nameof(EditorHost.LineFilterService), typeof(ILineFilterService) },
            { EditorHost.ThemeServiceProperty, nameof(EditorHost.ThemeService), typeof(IThemeService) },
            { EditorHost.SettingsServiceProperty, nameof(EditorHost.SettingsService), typeof(ISettingsService) },
            { EditorHost.TextToolsServiceProperty, nameof(EditorHost.TextToolsService), typeof(ITextToolsService) },
            { EditorHost.MacroServiceProperty, nameof(EditorHost.MacroService), typeof(IMacroService) },
            { EditorHost.DialogServiceProperty, nameof(EditorHost.DialogService), typeof(IDialogService) },
            { EditorHost.FileSystemServiceProperty, nameof(EditorHost.FileSystemService), typeof(IFileSystemService) },
            { EditorHost.MainViewModelProperty, nameof(EditorHost.MainViewModel), typeof(MainViewModel) },
        };
    }
}
