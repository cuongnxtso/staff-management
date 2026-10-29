using System.Windows;
using StaffManagement.ViewModels;

namespace StaffManagement.Views;

public partial class StaffEditWindow : Window
{
    public StaffEditWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is StaffEditViewModel vm) vm.CloseRequested += ok => DialogResult = ok;
        };
    }
}
