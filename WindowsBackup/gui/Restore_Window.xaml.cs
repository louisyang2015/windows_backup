using System.Collections.Generic;
using System.IO;
using System.Windows;

using Microsoft.Win32;  // for file open dialog window


namespace WindowsBackup
{
  /// <summary>
  /// Interaction logic for Restore_Window.xaml
  /// </summary>
  public partial class Restore_Window : Window
  {
    WindowsBackup_App app;

    RestoreInfo restore_info = null;

    // need to ignore some GUI events early on
    bool ignore_gui = true; // set to false in Window_Loaded

    // Embedded prefix -> destination mapping
    List<PrefixMapping> mapping_list;

    class Restore_ListBoxItem
    {
      public string display;
      public int index;

      public Restore_ListBoxItem(string display, int index)
      {
        this.display = display;
        this.index = index;
      }

      public override string ToString()
      {
        return display;
      }
    }

    class PrefixMapping
    {
      public string prefix, destination;

      public PrefixMapping(string prefix, string destination)
      {
        this.prefix = prefix;
        this.destination = destination;
      }

      public string Prefix
      {
        get { return prefix; }
        set { } // don't let user change the prefix
      }

      public string Destination
      {
        get { return destination; }
        set { destination = value; }
      }
    }


    internal Restore_Window(WindowsBackup_App app)
    {
      this.app = app;

      InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
      app.update_restore_GUI(this, RestoreInfo_text, RestoreStatus_text);

      ignore_gui = false;
      hide_most_controls();

      // Display restore archive names.
      RestoreNames_lb.Items.Clear();
      string[] restore_names = app.restore_manager.get_restore_names();

      for (int i = 0; i < restore_names.Length; i++)
        RestoreNames_lb.Items.Add(new Restore_ListBoxItem(restore_names[i], i));
    }


    #region Hiding and showing controls

    void hide_controls(UIElement[] controls)
    {
      foreach (var control in controls)
        control.Visibility = Visibility.Collapsed;
    }

    void show_controls(UIElement[] controls)
    {
      foreach (var control in controls)
        control.Visibility = Visibility.Visible;
    }

    void disable_controls(UIElement[] controls)
    {
      foreach (var control in controls)
        control.IsEnabled = false;
    }

    /// <summary>
    /// Hide controls from "RestoreInfo_text" onward.
    /// </summary>
    void hide_most_controls()
    {
      hide_controls(new UIElement[] { RestoreInfo_text, ShowFileNames_btn,
          ChooseDestination_text, MultiDestination_rbtn, SingleDestination_rbtn,
          EmbeddedPrefix_datagrid, BaseDestination_tb, BaseDestination_btn,
          FNR_Path_text, FNR_Path_tb, FNR_Path_btn, StartRestore_btn,
          RestoreStatus_text });
    }

    void show_or_hide_destination_guis()
    {
      hide_controls(new UIElement[] { EmbeddedPrefix_datagrid, BaseDestination_tb,
              BaseDestination_btn});

      if (MultiDestination_rbtn.IsChecked == true)
        EmbeddedPrefix_datagrid.Visibility = Visibility.Visible;
      else if (SingleDestination_rbtn.IsChecked == true)
        show_controls(new UIElement[] { BaseDestination_tb, BaseDestination_btn });
    }

    #endregion


    #region Get restore information from archives

    /// <summary>
    /// Obtain the "RestoreNames_lb" selected indices.
    /// </summary>
    int[] get_restore_indices()
    {
      var indices = new List<int>();
      foreach (var item in RestoreNames_lb.SelectedItems)
      {
        var item2 = (Restore_ListBoxItem)item;
        indices.Add(item2.index);
      }

      return indices.ToArray();
    }
    
    private void GetInfo_btn_Click(object sender, RoutedEventArgs e)
    {
      // Obtain selected indices
      int[] restore_indices = get_restore_indices();
      if (restore_indices.Length == 0)
      {
        MyMessageBox.show("No archive selected.", "Error");
        return;
      }

      // Update GUI
      disable_controls(new UIElement[] { RestoreNames_lb, GetInfo_btn,
              GetFileNames_cb }); 
      RestoreInfo_text.Visibility = Visibility.Visible;     

      // Call get_restore_info(...) on backup manager thread
      bool skip_file_names = true;
      if (GetFileNames_cb.IsChecked == true) skip_file_names = false;

      var restore_manager = app.restore_manager;
      app.backup_manager.get_restore_info(restore_manager, restore_indices, skip_file_names);
    }

    internal void get_restore_info_done(RestoreInfo restore_info)
    {
      this.restore_info = restore_info;

      double total_file_size_mb = restore_info.total_file_size / (1024.0 * 1024.0);
      RestoreInfo_text.Text += "\nTotal file size: " + total_file_size_mb.ToString("F1") + " MB";

      // show more GUI
      if (GetFileNames_cb.IsChecked == true && restore_info.file_names != null)
        ShowFileNames_btn.Visibility = Visibility.Visible;

      show_or_hide_destination_guis();

      show_controls(new UIElement[] { ChooseDestination_text,
              MultiDestination_rbtn, SingleDestination_rbtn,
              FNR_Path_text,
              FNR_Path_tb, FNR_Path_btn, StartRestore_btn });

      // Allocate mapping_list and bind to data grid
      mapping_list = new List<PrefixMapping>();
      foreach(var prefix in restore_info.embedded_prefixes)
      {
        string destination = "";
        if (app.restore_manager.default_destinations.ContainsKey(prefix))
          destination = app.restore_manager.default_destinations[prefix];

        mapping_list.Add(new PrefixMapping(prefix, destination));
      }
      
      EmbeddedPrefix_datagrid.ItemsSource = mapping_list;
    }
    
    private void ShowFileNames_btn_Click(object sender, RoutedEventArgs e)
    {
      var window = new ShowRestoreFileNames_Window(restore_info.file_names);
      window.ShowDialog();
    }

    #endregion


    #region Get restore information from user
            
    private void Destination_rbtn_Checked(object sender, RoutedEventArgs e)
    {
      if (ignore_gui) return;
      show_or_hide_destination_guis();
    }

    private void BaseDestination_btn_Click(object sender, RoutedEventArgs e)
    {
      string directory = WindowsBackup_App.get_folder(BaseDestination_tb.Text);
      WindowsBackup_App.remove_ending_slash(ref directory);
      BaseDestination_tb.Text = directory;
    }

    private void FNR_Path_btn_Click(object sender, RoutedEventArgs e)
    {
      var dlg = new SaveFileDialog();
      dlg.Filter = "Tab Delimited Document|*.tsv";

      if (dlg.ShowDialog() == true)
        FNR_Path_tb.Text = dlg.FileName;
    }

    private void StartRestore_btn_Click(object sender, RoutedEventArgs e)
    {
      var settings = new RestoreSettings();

      if (MultiDestination_rbtn.IsChecked == true)
      {
        // check mapping_list
        if (mapping_list.Count == 0)
        {
          MyMessageBox.show("The \"destination\" options section is incorrect. "
            + "The user did not provide any restore destination information.",
            "Error");
          return;
        }

        // check that each destination directory exists
        foreach(var mapping in mapping_list)
        {
          mapping.destination = mapping.destination.Trim();
          WindowsBackup_App.remove_ending_slash(ref mapping.destination);

          if (mapping.destination.Length > 0
              && Directory.Exists(mapping.destination) == false)
          {
            MyMessageBox.show("The embedded prefix \"" + mapping.prefix
              + "\" is mapped to a destination \"" + mapping.destination
              + "\" that does not exist on disk.", "Error");
            return;
          }
        }

        // Build up embedded_prefix --> destination lookup
        var destination_lookup = new Dictionary<string, string>();
        foreach (var mapping in mapping_list)
        {
          if (mapping.destination.Length > 0)
            destination_lookup.Add(mapping.prefix, mapping.destination);
        }

        settings.restore_destination_lookup = destination_lookup;
      }
      else if (SingleDestination_rbtn.IsChecked == true)
      {
        // check that destination directory exists
        BaseDestination_tb.Text = BaseDestination_tb.Text.Trim();
        string destination_base = BaseDestination_tb.Text;
        WindowsBackup_App.remove_ending_slash(ref destination_base);

        if (Directory.Exists(destination_base) == false)
        {
          MyMessageBox.show("The restore destination directory \""
            + destination_base + "\" does not exist on disk.", "Error");
          return;
        }

        settings.restore_destination_base = destination_base;
      }

      // The file name registration field needs to be filled out
      FNR_Path_tb.Text = FNR_Path_tb.Text.Trim();
      if (FNR_Path_tb.Text.Length == 0)
      {
        MyMessageBox.show("The file name registration field is not filled out. "
          + "Restoring encrypted files result in a new file name registration record.",
          "Error");
        return;
      }

      //  The file name registration field should not reference an existing file
      if (File.Exists(FNR_Path_tb.Text))
      {
        MyMessageBox.show("The file name registration field is referencing a file \""
          + FNR_Path_tb.Text + "\" that currently exists.", "Error");
        return;
      }

      // The file name registration file should not reference an existing directory
      if (Directory.Exists(FNR_Path_tb.Text))
      {
        MyMessageBox.show("The file name registration field is referencing a location \""
          + FNR_Path_tb.Text + "\" that currently exists as a directory.", "Error");
        return;
      }

      // disable previous controls, show RestoreStatus_text
      disable_controls(new UIElement[] { MultiDestination_rbtn, SingleDestination_rbtn,
              EmbeddedPrefix_datagrid, BaseDestination_tb, BaseDestination_btn,
              FNR_Path_tb, FNR_Path_btn, StartRestore_btn });
      RestoreStatus_text.Visibility = Visibility.Visible;

      // start restore operation on the backup manager thread
      settings.indices = get_restore_indices();
      settings.file_name_reg = new FileNameRegistration(FNR_Path_tb.Text);
      
      var restore_manager = app.restore_manager;
      app.backup_manager.restore(restore_manager, settings);
    }

    #endregion


  }
}
