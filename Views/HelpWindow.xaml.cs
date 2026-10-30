using System.Windows;
using System.Windows.Input;

namespace StaffManagement.Views;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => SearchBox.Focus();
        // A new page starts at its top.
        TopicList.SelectionChanged += (_, _) => PageScroll.ScrollToTop();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            // Up / Down in the search box move through the topics, so the keyboard alone is enough.
            case Key.Down or Key.Up when SearchBox.IsKeyboardFocused && TopicList.Items.Count > 0:
                int i = TopicList.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
                TopicList.SelectedIndex = Math.Clamp(i, 0, TopicList.Items.Count - 1);
                TopicList.ScrollIntoView(TopicList.SelectedItem);
                e.Handled = true;
                break;
        }
    }
}
