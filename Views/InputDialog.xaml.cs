using System.Windows;

namespace StaffManagement.Views;

public partial class InputDialog : Window
{
    public string Value => ValueBox.Text;

    public InputDialog(string title, string label, string? initial)
    {
        InitializeComponent();
        Title = title;
        LabelText.Text = label;
        ValueBox.Text = initial ?? "";
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
