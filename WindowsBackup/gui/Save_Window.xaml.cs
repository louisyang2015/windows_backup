using System.Windows;


namespace WindowsBackup
{
  /// <summary>
  /// Interaction logic for Save_Window.xaml
  /// </summary>
  public partial class Save_Window : Window
  {
    bool save = false;
    public bool Save { get { return save; } }

    public Save_Window()
    {
      InitializeComponent();
    }
        
    private void DoNotSave_Click(object sender, RoutedEventArgs e)
    {
      Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
      save = true;
      Close();
    }
  }
}
