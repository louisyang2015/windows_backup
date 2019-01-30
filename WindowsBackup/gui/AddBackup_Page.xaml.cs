using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;


namespace WindowsBackup
{
  /// <summary>
  /// Interaction logic for AddBackup_Page.xaml
  /// </summary>
  public partial class AddBackup_Page : Page
  {
    public class Rule_ListBoxItem
    {
      public string display;
      public int category;
      public int index;

      public Rule_ListBoxItem(string display, int category, int index)
      {
        this.display = display;
        this.category = category;
        this.index = index;
      }

      public override string ToString()
      {
        return display;
      }
    }



    bool ignore_gui_events = true; // set to false in Page_Loaded

    BackupManager backup_manager;
    List<CloudBackupService> cloud_backups;
    BasicKeyManager key_manager;

    BackupRuleLists rule_lists = new BackupRuleLists();

    AddBackup_Callback add_backup;

    internal AddBackup_Page(BackupManager backup_manager,
      List<CloudBackupService> cloud_backups,
      BasicKeyManager key_manager, AddBackup_Callback add_backup)
    {
      this.backup_manager = backup_manager;
      this.cloud_backups = cloud_backups;
      this.key_manager = key_manager;
      this.add_backup = add_backup;

      InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
      ignore_gui_events = false;
      
      // Maybe the user added more keys or cloud accounts, and then
      // returned to this page.

      update_DestinationName_cb();
      update_Keys_cb();

      BackupName_tb.Focus();
    }

    /// <summary>
    /// Update DestinationName_cb, to reflect update to cloud_backups, 
    /// while preserving user selection.
    /// </summary>
    void update_DestinationName_cb()
    {
      // Record current selections
      int old_index = DestinationName_cb.SelectedIndex;
      string old_name = "";
      if (old_index >= 0)
        old_name = DestinationName_cb.Items[old_index].ToString();

      // Attach cloud_backups names to DestinationName_cb.Items
      DestinationName_cb.Items.Clear();
      DestinationName_cb.Items.Add("Disk");
      foreach (var cloud_backup in cloud_backups)
        DestinationName_cb.Items.Add(cloud_backup.Name);

      // Set SelectedIndex to the previous selection.
      if (old_index <= 0)
        DestinationName_cb.SelectedIndex = 0;
      else
      {
        for (int i = 1; i < DestinationName_cb.Items.Count; i++)
        {
          if (DestinationName_cb.Items[i].ToString().Equals(old_name))
          {
            DestinationName_cb.SelectedIndex = i;
            break;
          }
        }
      }
    }

    /// <summary>
    /// Update Keys_cb, to reflect update to key_manager, 
    /// while preserving user selection.
    /// </summary>
    void update_Keys_cb()
    {
      // Record current selections
      int old_index = Keys_cb.SelectedIndex;
      string old_name = "";
      if (old_index >= 0)
        old_name = Keys_cb.Items[old_index].ToString();

      // Generate a list of names from 
      var names = key_manager.get_key_names();
      names.Sort();

      // Attach names to Keys_cb.Items
      Keys_cb.Items.Clear();
      Keys_cb.Items.Add("New Key");
      foreach (var name in names)
        Keys_cb.Items.Add(name);

      // Set SelectedIndex to the previous selection.
      if (old_index <= 0)
        Keys_cb.SelectedIndex = 0;
      else
      {
        int index = names.IndexOf(old_name);
        if (index >= 0)
          Keys_cb.SelectedIndex = index + 1;
        else
          Keys_cb.SelectedIndex = -1;
      }
    }

    /// <summary>
    /// Renders a BackupRuleLists object onto a list box.
    /// This function is also used by EditBackup_Page.
    /// </summary>
    internal static void redraw_backup_rules_listbox(ListBox lisbox, BackupRuleLists rule_lists)
    {
      lisbox.Items.Clear();

      int num_categories = rule_lists.backup_rule_lists.Count;

      for (int category = 0; category < num_categories; category++)
      {
        for (int index = 0;
          index < rule_lists.backup_rule_lists[category].Count; index++)
        {
          string display = rule_lists.backup_rule_lists[category][index].ToString();
          if (num_categories > 1)
            display = category + " " + display;

          var item = new Rule_ListBoxItem(display, category, index);
          lisbox.Items.Add(item);
        }
      }
    }

    void generate_default_embedded_prefix()
    {
      if (Encryption_cb.IsChecked == false) return;

      // Default the embedded prefix to the source directory name.
      string source_dir = Source_tb.Text.Trim();
      WindowsBackup_App.remove_ending_slash(ref source_dir);
      Source_tb.Text = source_dir;

      int index = source_dir.LastIndexOf('\\');
      if (index >= 0)
        EmbeddedPrefix_tb.Text = source_dir.Substring(index + 1);
      else
        EmbeddedPrefix_tb.Text = source_dir;
    }

    void enable_encryption_gui()
    {
      EmbeddedPrefix_tb.IsEnabled = true;
      Keys_cb.IsEnabled = true;
      generate_default_embedded_prefix();
    }

    void disable_encryption_gui()
    {
      EmbeddedPrefix_tb.IsEnabled = false;
      Keys_cb.IsEnabled = false;
    }

    private void BrowseSource_btn_Click(object sender, RoutedEventArgs e)
    {
      string directory = WindowsBackup_App.get_folder(Source_tb.Text);
      WindowsBackup_App.remove_ending_slash(ref directory);
      Source_tb.Text = directory;
      generate_default_embedded_prefix();
    }

    /// <summary>
    /// Event handler for adding a rule.
    /// </summary>
    private void Add_btn_Click(object sender, RoutedEventArgs e)
    {
      // Check that there is a valid source directory.
      string directory = Source_tb.Text.Trim();
      WindowsBackup_App.remove_ending_slash(ref directory);
      Source_tb.Text = directory;

      if (Directory.Exists(Source_tb.Text) == false)
      {
        MyMessageBox.show("Before adding rules, the source field needs "
          + "to be an existing directory.", "Error");
        return;
      }

      var add_rule_window = new AddRule_Window(rule_lists.backup_rule_lists.Count);
      add_rule_window.ShowDialog();

      if (add_rule_window.rule != null)
      {
        // Check directory for validity.
        if (add_rule_window.rule.directory.StartsWith(Source_tb.Text) == false)
        {
          MyMessageBox.show("The rule just added is not a subdirectory "
            + "of the source field. Therefore it is rejected. "
            + "(\"" + add_rule_window.rule.directory
            + "\" not a subdirectory of \"" + Source_tb.Text + "\")", 
            "Error");
          return;
        }

        // Add new rule to rule_list.
        rule_lists.add_rule(add_rule_window.rule, add_rule_window.Category);
        redraw_backup_rules_listbox(BackupRules_lb, rule_lists);
      }
    }

    private void Remove_btn_Click(object sender, RoutedEventArgs e)
    {
      int index = BackupRules_lb.SelectedIndex;
      if (index < 0) return;

      var item = (Rule_ListBoxItem)BackupRules_lb.SelectedItem;

      rule_lists.backup_rule_lists [item.category].RemoveAt(item.index);
      redraw_backup_rules_listbox(BackupRules_lb, rule_lists);
    }

    private void BrowseDestination_Click(object sender, RoutedEventArgs e)
    {
      string directory = WindowsBackup_App.get_folder(DestinationPath_tb.Text);
      WindowsBackup_App.remove_ending_slash(ref directory);
      DestinationPath_tb.Text = directory;
    }

    private void DestinationName_cb_Changed(object sender, SelectionChangedEventArgs e)
    {
      if (ignore_gui_events) return;

      int index = DestinationName_cb.SelectedIndex;
      if (index == 0)
      {
        // Disk backup
        DestinationType_text.Text = "Directory";
        BrowseDestination_btn.IsEnabled = true;
        Encryption_cb.IsEnabled = true;
      }
      else
      {
        // Cloud backup - encryption is required
        DestinationType_text.Text = "Bucket";
        BrowseDestination_btn.IsEnabled = false;
        Encryption_cb.IsEnabled = false; 
        Encryption_cb.IsChecked = true;
        enable_encryption_gui();
      }
    }

    private void TestFilePath_btn_Click(object sender, RoutedEventArgs e)
    {
      string file_path = TestFilePath_tb.Text.Trim();
      if (file_path.Length == 0) return;

      bool accepted = true;
      if (file_path.StartsWith(Source_tb.Text.Trim()) == false)
        accepted = false;
      else
        accepted = rule_lists.accepts(file_path);

      if (accepted)
        MyMessageBox.show("The file \"" + file_path + "\" will be backed up.", 
          "File Accepted");
      else
        MyMessageBox.show("The file \"" + file_path + "\" will be ignored.",
          "File Rejected");
    }

    private void Source_tb_KeyUp(object sender, KeyEventArgs e)
    {
      if(Encryption_cb.IsChecked == true)
        generate_default_embedded_prefix();
    }

    private void Encryption_cb_Click(object sender, RoutedEventArgs e)
    {
      if (Encryption_cb.IsChecked == true)
        enable_encryption_gui();
      else
        disable_encryption_gui();
    }
    
    private void AddBackup_btn_Click(object sender, RoutedEventArgs e)
    {
      // Backup Name has to be filled
      BackupName_tb.Text = BackupName_tb.Text.Trim();
      if (BackupName_tb.Text.Length == 0)
      {
        MyMessageBox.show("The backup name field must be filled.", "Error");
        return;
      }

      // Source directory has to exist
      string directory = Source_tb.Text.Trim();
      WindowsBackup_App.remove_ending_slash(ref directory);
      Source_tb.Text = directory;

      if (directory.Length == 0)
      {
        MyMessageBox.show("The source directory field is not filled out.", "Error");
        return;
      }

      if (Directory.Exists(directory) == false)
      {
        MyMessageBox.show("The backup source directory \"" + directory
          + "\" does not exist.", "Error");
        return;
      }

      // Destination, if it's a disk directory, has to exist. This directory
      // has to be unused by other backups.
      if (DestinationName_cb.SelectedIndex == 0)
      {
        // Clean up destination directory.
        directory = DestinationPath_tb.Text.Trim();
        WindowsBackup_App.remove_ending_slash(ref directory);
        DestinationPath_tb.Text = directory;

        // Make sure destination directory exists, and is not the same as source.
        if (directory.Length == 0)
        {
          MyMessageBox.show("The backup destination directory field "
            + "is not filled out.", "Error");
          return;
        }

        if (Directory.Exists(directory) == false)
        {
          MyMessageBox.show("The backup destination directory \"" + directory
            + "\" does not exist.", "Error");
          return;
        }

        if (DestinationPath_tb.Text.Equals(Source_tb.Text))
        {
          MyMessageBox.show("The backup destination directory is the same "
            + "as the backup source directory.", "Error");
          return;
        }

        // Make sure destination directory has not been used before.
        foreach(var backup in backup_manager.backups)
        {
          if(backup is DiskBackup)
          {
            var disk_backup = (DiskBackup)backup;
            if (disk_backup.destination_base.Equals(directory))
            {
              MyMessageBox.show("The backup destination directory \"" + directory
                      + "\" is already being used by the backup \""
                      + disk_backup.Name + "\" .", "Error");
              return;
            }
          }
          else if (backup is EncryptedBackup)
          {
            var encrypted_backup = (EncryptedBackup)backup;
            if (encrypted_backup.destination_name.ToLower().Equals("disk")
              && encrypted_backup.destination_base.Equals(directory))
            {
              MyMessageBox.show("The backup destination directory \"" + directory
                      + "\" is already being used by the encrypted backup \""
                      + encrypted_backup.Name + "\" .", "Error");
              return;
            }
          }
        }
      }

      // Destination, if it's a bucket, has to be readable. 
      if (DestinationName_cb.SelectedIndex > 0)
      {
        string bucket = DestinationPath_tb.Text.Trim();
        DestinationPath_tb.Text = bucket;

        int index = DestinationName_cb.SelectedIndex - 1;

        // Check that the destination bucket field is filled out.
        if (bucket.Length == 0)
        {
          MyMessageBox.show("The backup destination bucket field needs to "
            + "be filled out.", "Error");
          return;
        }

        // Check that the destination bucket is readable.
        try
        {
          cloud_backups[index].list_objects(bucket, 10);
        }
        catch
        {
          MyMessageBox.show("Failed to read from cloud backup \""
            + cloud_backups[index].Name + "\\" + bucket + "\".", "Error");
          return;
        }
      }

      // If encryption is used, check the embedded prefix.
      if (Encryption_cb.IsChecked == true)
      {
        // Make sure there is an embedded prefix.
        string prefix = EmbeddedPrefix_tb.Text.Trim();
        if (prefix.Length == 0)
        {
          MyMessageBox.show("All encrypted backups must have an unique embedded prefix.",
            "Error");
          return;
        }

        WindowsBackup_App.remove_ending_slash(ref prefix);
        EmbeddedPrefix_tb.Text = prefix;

        // Make sure the embedded prefix has no '\' character
        if (prefix.IndexOf('\\') >= 0)
        {
          MyMessageBox.show("The embedded prefix \"" + prefix + "\" has a '\\', "
            + "which is not allowed.", "Error");
          return;
        }

        // Make sure the embedded prefix is unique.
        foreach (var backup in backup_manager.backups)
        {
          if (backup is EncryptedBackup)
          {
            var encrypted_backup = (EncryptedBackup)backup;
            if (encrypted_backup.embedded_prefix.Equals(prefix))
            {
              MyMessageBox.show("The embedded prefix \"" + prefix
                + "\" is already being used by the encrypted backup \""
                + encrypted_backup.Name + "\".", "Error");
              return;
            }
          }
        }
      }

      // Determine the key number. Generate new key if needed.
      UInt16 key_number = 0;
      if (Encryption_cb.IsChecked == true)
      {
        if (Keys_cb.SelectedIndex == 0)
          key_number = key_manager.add_key();
        else
        {
          string key_name = Keys_cb.Items[Keys_cb.SelectedIndex].ToString();
          key_number = key_manager.get_key_number(key_name).Value;
        }
      }

      // Determine destination_name
      string destination_name = "disk"; // use lower case for storage
      if (DestinationName_cb.SelectedIndex > 0)
        destination_name = cloud_backups[DestinationName_cb.SelectedIndex - 1].Name;

      // Create backup
      Backup new_backup = null;
      if (Encryption_cb.IsChecked == true)
      {
        new_backup = new EncryptedBackup(Source_tb.Text, EmbeddedPrefix_tb.Text,
          key_number, destination_name, DestinationPath_tb.Text, rule_lists, 
          BackupName_tb.Text);
      }
      else
      {
        new_backup = new DiskBackup(Source_tb.Text, DestinationPath_tb.Text,
          rule_lists, BackupName_tb.Text);
      }
      
      add_backup(new_backup);
    }
  }
}
