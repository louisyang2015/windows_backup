using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;

using Microsoft.Win32;  // for file open dialog window


namespace WindowsBackup
{
  /// <summary>
  /// Interaction logic for Compare_Window.xaml
  /// </summary>
  public partial class Compare_Window : Window
  {
    public Compare_Window()
    {
      InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
      DirCompareOutput_tb.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Returns true if two files match.
    /// </summary>
    /// <param name="length">Match only the first "length" bytes of two files.
    /// Null means to compare all of two files.</param>
    bool compare_files(string file_path1, string file_path2, long? length = null)
    {
      if (length == null)
      {
        // Require the two files to be the same length
        long length1 = new FileInfo(file_path1).Length;
        long length2 = new FileInfo(file_path2).Length;
        if (length1 != length2) return false;
        length = length1;
      }

      // compare file content
      using (var fs1 = new FileStream(file_path1, FileMode.Open))
      using (var fs2 = new FileStream(file_path2, FileMode.Open))
      {
        long bytes_left = length.Value;

        byte[] buffer1 = new byte[1024 * 100];
        byte[] buffer2 = new byte[1024 * 100];

        do
        {
          int bytes_to_read = buffer1.Length;
          if (bytes_to_read > bytes_left) bytes_to_read = (int)bytes_left;

          fs1.Read(buffer1, 0, bytes_to_read);
          fs2.Read(buffer2, 0, bytes_to_read);

          for (int i = 0; i < bytes_to_read; i++)
          {
            if (buffer1[i] != buffer2[i]) return false;
          }

          bytes_left = bytes_left - bytes_to_read;

        } while (bytes_left > 0);
      }

      return true; // Files are the same if the code manages to get this far.
    }


    /// <summary>
    /// Compare two directories and returns the differences.
    /// </summary>
    string compare_directories(string dir1_path, string dir2_path)
    {
      char sep = Path.DirectorySeparatorChar;
      var sb = new StringBuilder();

      // Check that files in dir1_path matches corresponding files in dir2_path
      var file_names_full_path = Directory.GetFiles(dir1_path);
      foreach (var file_name_full_path in file_names_full_path)
      {
        var file_name = Path.GetFileName(file_name_full_path);
        var dir2_plus_file_name = dir2_path + sep + file_name;

        if (File.Exists(dir2_plus_file_name))
        {
          bool match = compare_files(file_name_full_path, dir2_plus_file_name);
          if (!match)
            sb.AppendLine("File mismatch: " + file_name_full_path + " " + dir2_plus_file_name);
        }
        else
          sb.AppendLine(file_name_full_path + " does not exist inside " + dir2_path);
      }

      // Check that files in dir2_path also exist in dir1_path
      file_names_full_path = Directory.GetFiles(dir2_path);
      foreach (var file_name_full_path in file_names_full_path)
      {
        var file_name = Path.GetFileName(file_name_full_path);
        var dir1_plus_file_name = dir1_path + sep + file_name;

        if (File.Exists(dir1_plus_file_name) == false)
          sb.AppendLine(file_name_full_path + " does not exist inside " + dir1_path);
      }

      // Check subdirectories
      var dir_names_full_path = Directory.GetDirectories(dir1_path);
      foreach (var dir_name_full_path in dir_names_full_path)
      {
        var dir_name = Path.GetFileName(dir_name_full_path);
        var dir2_plus_dir_name = dir2_path + sep + dir_name;
        if (Directory.Exists(dir2_plus_dir_name))
          compare_directories(dir_name_full_path, dir2_plus_dir_name);
        else
          sb.AppendLine(dir_name_full_path + " does not exist inside " + dir2_path);
      }

      return sb.ToString();
    }


    private void File1_btn_Click(object sender, RoutedEventArgs e)
    {
      var dlg = new OpenFileDialog();
      dlg.Filter = "All Files|*.*";

      if (dlg.ShowDialog() == true)
        File1_tb.Text = dlg.FileName;
    }

    private void File2_btn_Click(object sender, RoutedEventArgs e)
    {
      var dlg = new OpenFileDialog();
      dlg.Filter = "All Files|*.*";

      if (dlg.ShowDialog() == true)
        File2_tb.Text = dlg.FileName;
    }

    private void CompareFile_btn_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        Mouse.OverrideCursor = Cursors.Wait;
        bool same = compare_files(File1_tb.Text.Trim(), File2_tb.Text.Trim());
        Mouse.OverrideCursor = null;

        if (same)
          MyMessageBox.show("The two files are the same.", "File Compare Result");
        else
          MyMessageBox.show("The two files are the DIFFERENT.", "File Compare Result");
      }
      catch (Exception ex)
      {
        MyMessageBox.show(ex.Message, "Error");
      }
    }

    private void Dir1_btn_Click(object sender, RoutedEventArgs e)
    {
      string directory = WindowsBackup_App.get_folder(Dir1_tb.Text);
      WindowsBackup_App.remove_ending_slash(ref directory);
      Dir1_tb.Text = directory;
    }

    private void Dir2_btn_Click(object sender, RoutedEventArgs e)
    {
      string directory = WindowsBackup_App.get_folder(Dir2_tb.Text);
      WindowsBackup_App.remove_ending_slash(ref directory);
      Dir2_tb.Text = directory;
    }

    private void CompareDir_btn_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        Mouse.OverrideCursor = Cursors.Wait;
        string result = compare_directories(Dir1_tb.Text.Trim(), Dir2_tb.Text.Trim());
        Mouse.OverrideCursor = null;

        result = result.Trim();
        if (result.Length == 0) result = "No difference found.";
        else
          result += "\n\nDirectory comparison completed.";        

        DirCompareOutput_tb.Visibility = Visibility.Visible;
        DirCompareOutput_tb.Text = result;
      }
      catch(Exception ex)
      {
        MyMessageBox.show(ex.Message, "Error");
      }
    }

    
  }
}
