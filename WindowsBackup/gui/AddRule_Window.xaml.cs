using System.IO;
using System.Windows;
using System.Windows.Controls;


namespace WindowsBackup
{
  /// <summary>
  /// Interaction logic for AddRule_Window.xaml
  /// </summary>
  public partial class AddRule_Window : Window
  {
    // ignore GUI events until GUI has been initialized
    bool ignore_gui = true; 

    internal BackupRuleLists.BackupRule rule = null;

    int max_category = 0; // highest category number possible
    int category = 0;
    public int Category { get { return category; } }

    public AddRule_Window(int max_category)
    {
      this.max_category = max_category;

      InitializeComponent();
    }
                
    private void BrowseDirectory_Click(object sender, RoutedEventArgs e)
    {
      Directory_tb.Text = WindowsBackup_App.get_folder(Directory_tb.Text);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
      Close();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
      // Check the fields
      Directory_tb.Text = Directory_tb.Text.Trim();
      if (Directory.Exists(Directory_tb.Text) == false)
      {
        MyMessageBox.show("The directory does not exist on disk.", "Error");
        return;
      }

      if (Rules_cb.SelectedIndex > 1)
      {
        Suffixes_tb.Text = Suffixes_tb.Text.Trim();
        if (Suffixes_tb.Text.Length == 0)
        {
          if (Rules_cb.SelectedIndex == 2 || Rules_cb.SelectedIndex == 3)
          {
            MyMessageBox.show("The suffixes field cannot be empty if the "
                + "rule type is to accept or reject suffixes.", "Error");
            return;
          }
          else if (Rules_cb.SelectedIndex == 4)
          {
            MyMessageBox.show("The sub-directories field cannot be empty if the "
                + "rule type is to reject sub-directories.", "Error");
            return;
          }          
        }
      }

      // Update the category number
      category = Categories_cb.SelectedIndex;

      // Create a rule object and exit.
      string directory = Directory_tb.Text;
      WindowsBackup_App.remove_ending_slash(ref directory);

      string suffixes = null;
      string subdirs = null;

      var rule_type = BackupRuleLists.BackupRuleType.ACCEPT_ALL;

      if (Rules_cb.SelectedIndex == 1)
        rule_type = BackupRuleLists.BackupRuleType.REJECT_ALL;
      else if (Rules_cb.SelectedIndex == 2)
      {
        rule_type = BackupRuleLists.BackupRuleType.ACCEPT_SUFFIX;
        suffixes = Suffixes_tb.Text.Trim();
      }
      else if (Rules_cb.SelectedIndex == 3)
      {
        rule_type = BackupRuleLists.BackupRuleType.REJECT_SUFFIX;
        suffixes = Suffixes_tb.Text.Trim();
      }
      else if (Rules_cb.SelectedIndex == 4)
      {
        rule_type = BackupRuleLists.BackupRuleType.REJECT_SUB_DIR;
        subdirs = Suffixes_tb.Text.Trim();
      }
           
      rule = new BackupRuleLists.BackupRule(directory, rule_type, 
        suffixes, subdirs);

      Close();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
      ignore_gui = false;

      // initialize Categories_cb
      Categories_cb.Items.Clear();
      Categories_cb.Items.Add("Default Category");

      for (int i = 1; i < max_category; i++)
        Categories_cb.Items.Add("Category " + i);

      if (max_category > 0)
        Categories_cb.Items.Add("New Category");

      Categories_cb.SelectedIndex = 0;

      Directory_tb.Focus();
    }

    private void Rules_cb_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (ignore_gui) return;

      int index = Rules_cb.SelectedIndex;
      if (index == 4)
        Suffixes_text.Text = "Sub-directories";
      else
        Suffixes_text.Text = "Suffixes";
    }
  }
}
