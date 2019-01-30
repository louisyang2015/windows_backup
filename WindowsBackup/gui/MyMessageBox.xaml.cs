using System.Windows;

// TODO zz - clean up all the unused using statements in GUIs

namespace WindowsBackup
{
  /// <summary>
  /// Interaction logic for MyMessageBox.xaml
  /// </summary>
  public partial class MyMessageBox : Window
  {
    public MyMessageBox(string message, string caption, int width = 300)
    {
      InitializeComponent();

      Title = caption;
      Message_tb.Text = message;

      Width = width;
    }

    private void OK_btn_Click(object sender, RoutedEventArgs e)
    {
      Close();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
      Message_tb.Focus();
      SizeToContent = SizeToContent.Height;
    }

    public static void show(string message, string caption, int width = 300)
    {
      var message_box = new MyMessageBox(message, caption, width);
      message_box.ShowDialog();
    }
  }
}
