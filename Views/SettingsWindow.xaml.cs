using System.Windows;
using StaffManagement.ViewModels;

namespace StaffManagement.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is not SettingsViewModel vm) return;
            ApiKeyBox.Password = vm.ApiKey; // PasswordBox.Password is not bindable
            vm.CloseRequested += Close;
        };
    }

    private void OnApiKeyChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.ApiKey = ApiKeyBox.Password;
    }
}
