using System;
using System.Windows;
using System.Windows.Controls;


namespace WindowsBackup
{
  /// <summary>
  /// Interaction logic for Key_Page.xaml
  /// </summary>
  public partial class Key_Page : Page, CanBeModified
  {
    BasicKeyManager key_manager;

    bool modified = false;
    public bool Modified { get { return modified; } }

    internal Key_Page(BasicKeyManager key_manager)
    {
      InitializeComponent();

      this.key_manager = key_manager;
      update_gui();
    }

    void update_gui()
    {
      var key_names = key_manager.get_key_names();
      if (key_names.Count == 0)
      {
        ExistingKeys_text.Visibility = Visibility.Collapsed;
        ExistingKeys_lb.Visibility = Visibility.Collapsed;
      }
      else
      {
        // key_names.Count > 0. Populate list box with key_names
        key_names.Sort();

        ExistingKeys_lb.Items.Clear();
        foreach(var key_name in key_names)
          ExistingKeys_lb.Items.Add(key_name);

        ExistingKeys_text.Visibility = Visibility.Visible;
        ExistingKeys_lb.Visibility = Visibility.Visible;
      }

      NewKeyName_tb.Text = "";
    }
    
    private void AddKey_btn_Click(object sender, RoutedEventArgs e)
    {
      if (NewKeyName_tb.Text.Trim().Length == 0) return;

      try
      {
        modified = true;
        key_manager.add_key(NewKeyName_tb.Text);
        update_gui();
      }
      catch(Exception ex)
      {
        MyMessageBox.show(ex.Message, "Error");
      }
    }

  }
}
