using System.Windows;
using StaffManagement.ViewModels;

namespace StaffManagement.Views;

public partial class DecisionImportWindow : Window
{
    public DecisionImportWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is DecisionImportViewModel vm) vm.CloseRequested += ok => DialogResult = ok;
        };
    }
}
