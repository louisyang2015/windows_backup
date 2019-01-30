using System.Text;
using System.Windows;


namespace WindowsBackup
{
  /// <summary>
  /// Interaction logic for FNR_Window.xaml
  /// </summary>
  public partial class FNR_Window : Window
  {
    FileNameRegistration file_name_reg;

    internal FNR_Window(FileNameRegistration file_name_reg)
    {
      this.file_name_reg = file_name_reg;

      InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
      Info_text.Visibility = Visibility.Collapsed;
      Delete_btn.Visibility = Visibility.Collapsed;
    }

    private void GetInfo_btn_Click(object sender, RoutedEventArgs e)
    {
      var path = Path_tb.Text.Trim();
      WindowsBackup_App.remove_ending_slash(ref path);
      Path_tb.Text = path;

      var path_status = file_name_reg.get_path_status(path);

      if (path_status == null)
      {
        Info_text.Text = "No info";
        Info_text.Visibility = Visibility.Visible;
        Delete_btn.Visibility = Visibility.Collapsed;
      }
      else if (path_status.is_file == false)
      {
        Info_text.Text = "Path is a directory";
        Info_text.Visibility = Visibility.Visible;
        Delete_btn.Visibility = Visibility.Collapsed;
      }
      else if (path_status.is_file)
      {
        var sb = new StringBuilder();
        sb.AppendLine("Encrypted file name: " + path_status.alt_file_name);
        if (path_status.modified_time != null)
          sb.AppendLine("File modified time (UTC): " + path_status.modified_time.Value);
        else
          sb.AppendLine("File modified time unknown");

        Info_text.Text = sb.ToString();
        Info_text.Visibility = Visibility.Visible;
        Delete_btn.Visibility = Visibility.Visible;
      }
    }

    private void Delete_btn_Click(object sender, RoutedEventArgs e)
    {
      file_name_reg.delete(Path_tb.Text.Trim());

      Info_text.Text = "Entry deleted from the file name registration table.";
      Info_text.Visibility = Visibility.Visible;
      Delete_btn.Visibility = Visibility.Collapsed;
    }
    
  }
}
