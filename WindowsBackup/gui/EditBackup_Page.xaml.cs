using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;


namespace WindowsBackup
{
  /// <summary>
  /// Interaction logic for EditBackup_Page.xaml
  /// </summary>
  public partial class EditBackup_Page : Page, CanBeModified
  {
    bool modified = false;
    public bool Modified { get { return modified; } }

    // Only one of the following "backup" objects should be non-null
    DiskBackup disk_backup = null;
    EncryptedBackup encrypted_backup = null;

    // parameters of the backup object being edited
    BackupRuleLists rule_lists = null;
    string source_base = null;

    // When leaving the page, update the tree view node's header
    // to have the latest name appear there.
    Basic_Callback update_backup_node_headers = null;

    internal EditBackup_Page(Basic_Callback update_backup_node_headers)
    {
      InitializeComponent();
      this.update_backup_node_headers = update_backup_node_headers;
    }

    internal void load_backup(Backup backup)
    {
      // Loading a new backup object could mean leaving a previous 
      // backup object. Update the name in the tree view.
      if (update_backup_node_headers != null)
        update_backup_node_headers();

      // It's either "disk_backup" or "encrypted_backup".
      disk_backup = backup as DiskBackup;
      encrypted_backup = backup as EncryptedBackup;

      if (disk_backup != null) init_gui(disk_backup);
      else if (encrypted_backup != null) init_gui(encrypted_backup);
      else
      {
        MyMessageBox.show("Software error. EditBackup_Page :: "
          + " load_backup(Backup backup) is being fed an unknown object",
          "Error");
      }
    }

    void init_gui(DiskBackup disk_backup)
    {
      // Name_tb, Enabled_cb
      Name_tb.Text = disk_backup.future_params.name;
      Enabled_cb.IsChecked = disk_backup.future_params.enabled;

      // Info_text
      source_base = disk_backup.source_base;
      Info_text.Text = "Source: " + source_base + "\n"
                      + "Destination: " + disk_backup.destination_base;

      // Rules_lb
      rule_lists = disk_backup.future_params.rule_lists;
      AddBackup_Page.redraw_backup_rules_listbox(Rules_lb, rule_lists);
    }

    void init_gui(EncryptedBackup encrypted_backup)
    {
      // Name_tb, Enabled_cb
      Name_tb.Text = encrypted_backup.future_params.name;
      Enabled_cb.IsChecked = encrypted_backup.future_params.enabled;

      // Info_text
      source_base = encrypted_backup.source_base;

      var sb = new StringBuilder();
      sb.AppendLine("Source: " + encrypted_backup.source_base);

      if (encrypted_backup.destination_name.ToLower().Equals("disk"))
        sb.AppendLine("Destination: " + encrypted_backup.destination_base);
      else
        sb.AppendLine("Destination: " + encrypted_backup.destination_name
                       + "\\" + encrypted_backup.destination_base);

      sb.AppendLine("Embedded Prefix: " + encrypted_backup.embedded_prefix);
      Info_text.Text = sb.ToString();

      // Rules_lb
      rule_lists = encrypted_backup.future_params.rule_lists;
      AddBackup_Page.redraw_backup_rules_listbox(Rules_lb, rule_lists);
    }

    /// <summary>
    /// Update disk_backup or encrypted_backup to match user input 
    /// on the GUI.
    /// </summary>
    void update_future_params()
    {
      // modified = true; - this function is called during Unload event 
      // handler as a safety measure. The "modified" flag is set 
      // via Enabled_cb_Click and Name_tb_KeyUp

      if (disk_backup != null)
      {
        disk_backup.future_params.enabled = Enabled_cb.IsChecked.Value;
        disk_backup.future_params.name = Name_tb.Text.Trim();
      }
      else if (encrypted_backup != null)
      {
        encrypted_backup.future_params.enabled = Enabled_cb.IsChecked.Value;
        encrypted_backup.future_params.name = Name_tb.Text.Trim();
      }
    }

    private void Remove_btn_Click(object sender, RoutedEventArgs e)
    {
      int index = Rules_lb.SelectedIndex;
      if (index < 0) return;

      var item = (AddBackup_Page.Rule_ListBoxItem)Rules_lb.SelectedItem;
      rule_lists.backup_rule_lists[item.category].RemoveAt(item.index);

      modified = true;
      AddBackup_Page.redraw_backup_rules_listbox(Rules_lb, rule_lists);
    }

    private void Add_btn_Click(object sender, RoutedEventArgs e)
    {
      var add_rule_window = new AddRule_Window(rule_lists.backup_rule_lists.Count);
      add_rule_window.ShowDialog();

      if (add_rule_window.rule != null)
      {
        // Check directory for validity.
        if (add_rule_window.rule.directory.StartsWith(source_base) == false)
        {
          MyMessageBox.show("The rule just added is not a subdirectory "
            + "of the source field. Therefore it is rejected. "
            + "(\"" + add_rule_window.rule.directory
            + "\" not a subdirectory of \"" + source_base + "\")",
            "Error");
          return;
        }

        // Add new rule to rule_list.
        rule_lists.add_rule(add_rule_window.rule, add_rule_window.Category);
        modified = true;
        AddBackup_Page.redraw_backup_rules_listbox(Rules_lb, rule_lists);
      }
    }

    private void Enabled_cb_Click(object sender, RoutedEventArgs e)
    {
      modified = true;
      update_future_params();
    }

    private void Name_tb_KeyUp(object sender, KeyEventArgs e)
    {
      modified = true;
      update_future_params();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
      update_future_params();

      if (update_backup_node_headers != null)
        update_backup_node_headers();
    }
    
  }
}
