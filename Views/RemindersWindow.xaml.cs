using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StaffManagement.ViewModels;

namespace StaffManagement.Views;

public partial class RemindersWindow : Window
{
    public RemindersWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is RemindersViewModel vm) vm.CloseRequested += Close;
        };
    }

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is DataGridRow { Item: ReminderRow row })
            {
                ((RemindersViewModel)DataContext).EditCommand.Execute(row);
                return;
            }
        }
    }
}
