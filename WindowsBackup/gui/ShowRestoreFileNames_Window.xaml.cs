using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;


namespace WindowsBackup
{
  /// <summary>
  /// Interaction logic for ShowRestoreFileNames_Window.xaml
  /// </summary>
  public partial class ShowRestoreFileNames_Window : Window
  {
    // the key is embedded prefixes, the value is a list of file names
    Dictionary<string, List<string>> file_names;

    public ShowRestoreFileNames_Window(Dictionary<string, List<string>> file_names)
    {
      this.file_names = file_names;
      InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
      foreach (var prefix in file_names.Keys)
        EmbeddedPrefix_cb.Items.Add(prefix);
    }

    private void EmbeddedPrefix_cb_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      var prefix = (string)EmbeddedPrefix_cb.SelectedItem;
      file_names[prefix].Sort();

      var sb = new StringBuilder();
      foreach (var file_name in file_names[prefix])
        sb.AppendLine(file_name);

      Output_tb.Text = sb.ToString();
    }

    
  }
}
