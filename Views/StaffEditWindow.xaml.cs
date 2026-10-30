using System.Windows;
using StaffManagement.ViewModels;

namespace StaffManagement.Views;

public partial class StaffEditWindow : Window
{
    public StaffEditWindow()
    {
        InitializeComponent();
        // 820 wide = two 340-px fields per row (with the scrollbar); 680 high fits the longest tab without scrolling.
        // Small or scaled screens: keep Lưu / Hủy on screen.
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is StaffEditViewModel vm) vm.CloseRequested += ok => DialogResult = ok;
        };
    }
}
