using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;


namespace WindowsBackup
{
  interface CanBeModified
  {
    bool Modified { get; }
  }

  delegate void AddBackup_Callback(Backup backup);
  delegate void Basic_Callback();


  /// <summary>
  /// Interaction logic for SetupWindow.xaml
  /// </summary>
  public partial class SetupWindow : Window
  {
    WindowsBackup_App app;
    List<CanBeModified> pages = new List<CanBeModified>();

    // If false, the "Window_Closing(...)" doesn't check for modifications.
    bool check_for_mods = true;

    // The root node for the backup objects.
    TreeViewItem backup_root_node = null;

    // One page is used for editing all backup objects.
    EditBackup_Page edit_backup_page = null;

    // The "AddBackup_Page" might gets re-created when a new backup
    // is added. So it needs an external modified flag.
    bool new_backup_added = false;

    internal SetupWindow(WindowsBackup_App app)
    {
      InitializeComponent();

      this.app = app;

      // Create the tree nodes.
      // Key node
      var key_node = new TreeViewItem();
      key_node.Header = "Keys                "; // spaces for easier clicking
      NavPane_tv.Items.Add(key_node);

      // Cloud node
      var cloud_node = new TreeViewItem();
      cloud_node.Header = "Cloud Accounts";
      NavPane_tv.Items.Add(cloud_node);

      // Backup root node
      backup_root_node = new TreeViewItem();
      backup_root_node.IsExpanded = true;
      backup_root_node.Header = "Backups";
      NavPane_tv.Items.Add(backup_root_node);
           
      // Create pages and attach them to the nodes.
      var key_page = new Key_Page(app.key_manager);
      associate_tv_item_to_page(key_node, key_page);
      pages.Add(key_page);

      var cloud_page = new Cloud_Page(app.cloud_backup_services);
      associate_tv_item_to_page(cloud_node, cloud_page);
      pages.Add(cloud_page);

      // The edit_backup_page is not re-created when the backup nodes are
      // re-created. So adding it to the list of "CanBeModified" here, so
      // that it's a one time add.
      edit_backup_page = new EditBackup_Page(update_backup_node_headers);
      pages.Add(edit_backup_page);

      init_backup_nodes();
    }

    /// <summary>
    /// Standard TreeView item handler --- navigate to the
    /// "Tag" object if it is not null.
    /// </summary>
    void standard_tv_item_handler(object sender, RoutedEventArgs e)
    {
      e.Handled = true;
      TreeViewItem tv_item = (TreeViewItem)sender;
      if (tv_item.Tag == null) return;

      Page page = (Page)tv_item.Tag;
      Output_frame.Navigate(page);
    }

    /// <summary>
    /// TreeView item handler for editing backup nodes --- navigate 
    /// to the edit_backup_page and initialize it with the backup
    /// object that is stored inside the "Tag" parameter.
    /// </summary>
    void backup_tv_item_handler(object sender, RoutedEventArgs e)
    {
      e.Handled = true;
      TreeViewItem tv_item = (TreeViewItem)sender;
      if (tv_item.Tag == null) return;

      Backup backup = (Backup)tv_item.Tag;
      edit_backup_page.load_backup(backup);
      Output_frame.Navigate(edit_backup_page);
    }

    /// <summary>
    /// Standard procedure to associate a TreeView item 
    /// to a Page object.
    /// </summary>
    void associate_tv_item_to_page(TreeViewItem tv_item, Page page)
    {
      tv_item.Tag = page;
      tv_item.Selected += standard_tv_item_handler;
    }

    void add_backup(Backup backup)
    {
      app.backup_manager.backups.Add(backup);
      new_backup_added = true;

      // Add restoration data structure for encrypted backups
      if (backup is EncryptedBackup)
      {
        var encrypted_backup = (EncryptedBackup)backup;

        // Check to see if this restore already exists
        if (app.restore_manager.does_restore_exist(encrypted_backup.destination_base,
                                    encrypted_backup.destination_name) == false)
        {
          var restore = new Restore(encrypted_backup.destination_name,
                encrypted_backup.destination_base, app.file_name_reg.default_prefix);
          app.restore_manager.add_restore(restore);
        }

        app.restore_manager.set_default_destination(encrypted_backup.embedded_prefix,
              encrypted_backup.source_base);
      }

      init_backup_nodes();
      Output_frame.Content = null;
    }

    void init_backup_nodes()
    {
      backup_root_node.Items.Clear();

      // Add a node for each backup object.
      foreach (var backup in app.backup_manager.backups)
      {
        var backup_node = new TreeViewItem();
        backup_node.Header = backup.Name;
        backup_root_node.Items.Add(backup_node);

        backup_node.Tag = backup;
        backup_node.Selected += backup_tv_item_handler;
      }

      // Add one last node for adding new backups.
      var new_backup_node = new TreeViewItem();
      new_backup_node.Header = "Add New Backup";
      backup_root_node.Items.Add(new_backup_node);

      var add_backup_page = new AddBackup_Page(app.backup_manager,
        app.cloud_backup_services, app.key_manager, add_backup);
      associate_tv_item_to_page(new_backup_node, add_backup_page);
    }

    /// <summary>
    /// Updates the headers of nodes under "backup_root_node" to
    /// reflect the latest name change. This is triggered via the 
    /// "unload" event of "edit_backup_page".
    /// </summary>
    void update_backup_node_headers()
    {
      int num_items = backup_root_node.Items.Count - 1;
      for(int i = 0; i < num_items; i++)
      {
        if (backup_root_node.Items[i] is TreeViewItem)
        {
          var item = (TreeViewItem)backup_root_node.Items[i];
          if (item.Tag is DiskBackup)
          {
            var backup = (DiskBackup)item.Tag;
            if (item.Header.ToString().Equals(backup.future_params.name) == false)
              item.Header = backup.future_params.name;
          }
          else if (item.Tag is EncryptedBackup)
          {
            var backup = (EncryptedBackup)item.Tag;
            if (item.Header.ToString().Equals(backup.future_params.name) == false)
              item.Header = backup.future_params.name;
          }
        }


      }
    }

    

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
      if (check_for_mods == false) return;

      // Find out if any page has been modified.
      bool modified = false;

      if (new_backup_added) modified = true;

      foreach(var page in pages)
      {
        if(page.Modified)
        {
          modified = true;
          break;
        }
      }
      
      // If modified, give the user a chance to save.
      if (modified)
      {
        var save_window = new Save_Window();
        save_window.ShowDialog();

        if(save_window.Save)
          app.save_and_reload();
      }
    }

    private void Save_menuItem_Click(object sender, RoutedEventArgs e)
    {
      app.save_and_reload();
      check_for_mods = false;
      Close();
    }
  }
}
