using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;


namespace WindowsBackup
{
  /// <summary>
  /// Interaction logic for Cloud_Page.xaml
  /// </summary>
  public partial class Cloud_Page : Page, CanBeModified
  {
    List<CloudBackupService> cloud_backup_services;

    bool modified = false;
    public bool Modified { get { return modified;  } }

    public Cloud_Page(List<CloudBackupService> cloud_backup_services)
    {
      this.cloud_backup_services = cloud_backup_services;

      InitializeComponent();

      reset_gui();
    }

    void reset_gui()
    {
      CloudType_cb.SelectedIndex = -1;
      hide_add_cloud_controls();

      if (cloud_backup_services.Count > 0)
      {
        // Add "cloud_backup_services" names to the GUI.
        ExistingClouds_lb.Items.Clear();

        foreach (var cloud_backup in cloud_backup_services)
          ExistingClouds_lb.Items.Add(cloud_backup.Name);

        ExistingClouds_lb.SelectedIndex = -1;

        show_controls(new UIElement[] { ExistingClouds_text, ExistingClouds_lb });
      }
      else
      {
        hide_controls(new UIElement[] { ExistingClouds_text, ExistingClouds_lb });
      }

      hide_test_existing_controls();

      // Clear inputs, possibly from adding new accounts previously.
      clear_textboxes(new TextBox[] { ID_tb, SecretKey_tb, Region_tb,
                LongConfigStr_tb, BucketName_tb, AccountName_tb,
                BucketName2_tb });
      Test_text.Text = "";
      TestExisting_text.Text = "";
    }

    /// <summary>
    /// Hides the controls within the "New Cloud Account" box, leaving
    /// only the combo box at the very top visible.
    /// </summary>
    void hide_add_cloud_controls()
    {
      hide_controls(new UIElement[] { AWS_grid, LongConfigStr_text,
          LongConfigStr_tb, Test_sep, BucketName_text, BucketName_tb,
          Test_btn, Test_text, AccountName_text, AccountName_tb,
          Add_btn });
    }

    void hide_test_existing_controls()
    {
      hide_controls(new UIElement[] { BucketName2_text, BucketName2_tb,
        TestExisting_btn, TestExisting_text });
    }

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

    void clear_textboxes(TextBox[] textboxes)
    {
      foreach (var textbox in textboxes)
        textbox.Text = "";
    }

    /// <summary>
    /// Comparison function for sorting list of "CloudBackupService".
    /// </summary>
    int compare_two_cloud_backups(CloudBackupService c1, CloudBackupService c2)
    {
      return String.Compare(c1.Name, c2.Name);
    }

    private void CloudType_cb_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      hide_add_cloud_controls();
      hide_test_existing_controls();

      var index = CloudType_cb.SelectedIndex;
      if (index == 0)
      {
        // User want to add AWS S3 account.
        show_controls(new UIElement[] { AWS_grid, Test_btn, BucketName_text,
                BucketName_tb, Test_sep });
      }
      else if (index == 1)
      {
        // User want to add Azure Blob account.
        LongConfigStr_text.Text = "Connection String:";
        show_controls(new UIElement[] { LongConfigStr_text, LongConfigStr_tb, Test_btn,
                BucketName_text, BucketName_tb, Test_sep });
      }
      else if (index == 2)
      {
        // User want to add GCP Storage account.
        LongConfigStr_text.Text = "Credentials (JSON):";
        show_controls(new UIElement[] { LongConfigStr_text, LongConfigStr_tb, Test_btn,
                BucketName_text, BucketName_tb, Test_sep });
      }
    }

    private void Test_btn_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        if(BucketName_tb.Text.Trim().Length == 0)
        {
          MyMessageBox.show("Bucket name needed.", "Error");
          return;
        }

        var cloud_backup = create_cloud_bakcup_from_user_input("");

        // Read items from "cloud_backup"
        var names = cloud_backup.list_objects(BucketName_tb.Text.Trim(), 10);

        // Test fails if no item is requested.
        if (names.Count == 0)
        {
          MyMessageBox.show("No object found at cloud account.", "Error");
          return;
        }
        else
        {
          // Test passes if at least 1 item is read.
          // List objects.
          var sb = new StringBuilder();
          if (names.Count >= 10)
            sb.AppendLine("Ten or more objects found:");
          else
            sb.AppendLine("Objects found:");

          // Limit printing to 10 items.
          int length = names.Count;
          if (length > 10) length = 10;

          for (int i = 0; i < length; i++)
            sb.AppendLine(names[i]);

          Test_text.Text = sb.ToString();

          // Show additional controls
          show_controls(new UIElement[] { Test_text, AccountName_text,
                    AccountName_tb, Add_btn });
        }
      }
      catch (Exception ex)
      {
        MyMessageBox.show(ex.Message, "Error");
      }
    }

    /// <summary>
    /// Creates a new "CloudBackupService" object based on the user 
    /// input in the "New Cloud Account" section.
    /// </summary>
    CloudBackupService create_cloud_bakcup_from_user_input(string account_name)
    {
      var index = CloudType_cb.SelectedIndex;
      if (index == 0)
      {
        return new AWS_CloudBackupService(ID_tb.Text.Trim(),
                SecretKey_tb.Text.Trim(), Region_tb.Text.Trim(),
                account_name);
      }
      else if (index == 1)
      {
        return new AzureBlob_CloudBackupService(LongConfigStr_tb.Text.Trim(), account_name);
      }
      else if (index == 2)
      {
        return new GCP_CloudBackupService(LongConfigStr_tb.Text.Trim(), account_name);
      }
      else
        return null;
    }

    private void Add_btn_Click(object sender, RoutedEventArgs e)
    {
      // Check that account name has been entered.
      if (AccountName_tb.Text.Trim().Length == 0)
      {
        MyMessageBox.show("The account name is missing.", "Error");
        return;
      }
      string account_name = AccountName_tb.Text.Trim();

      // Check that account name is not in use.
      bool name_found = false;
      foreach(var backup in cloud_backup_services)
      {
        if (backup.Name.Equals(account_name))
        {
          name_found = true;
          break;
        }
      }

      if (name_found)
      {
        MyMessageBox.show("The account name \"" + account_name
          + "\" is already used by another cloud account. "
          + "Choose something else as the account name.", "Error");
        return;
      }

      var cloud_backup = create_cloud_bakcup_from_user_input(account_name);
      cloud_backup_services.Add(cloud_backup);
      cloud_backup_services.Sort(compare_two_cloud_backups);
      modified = true;

      reset_gui();
    }

    private void ExistingClouds_lb_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      int index = ExistingClouds_lb.SelectedIndex;
      if (index >= 0)
      {
        TestExisting_text.Text = "";
        show_controls(new UIElement[] { BucketName2_text, BucketName2_tb,
                  TestExisting_btn, TestExisting_text });
      }
      else
        hide_test_existing_controls();
    }

    private void TestExisting_btn_Click(object sender, RoutedEventArgs e)
    {
      int index = ExistingClouds_lb.SelectedIndex;
      if (index < 0) return;

      try
      {
        if (BucketName2_tb.Text.Trim().Length == 0)
        {
          MyMessageBox.show("Bucket name needed.", "Error");
          return;
        }

        // Read items from "cloud_backup"
        var names = cloud_backup_services[index].list_objects(BucketName2_tb.Text.Trim(), 10);
        if (names.Count > 0)
        {
          var sb = new StringBuilder();
          if (names.Count >= 10)
            sb.AppendLine("Ten or more objects found:");
          else
            sb.AppendLine("Objects found:");

          // Limit printing to 10 items.
          int length = names.Count;
          if (length > 10) length = 10;

          for(int i = 0; i < length; i++)
            sb.AppendLine(names[i]);

          TestExisting_text.Text = sb.ToString();
        }
        else
        {
          TestExisting_text.Text = "No object found.";
        }
      }
      catch (Exception ex)
      {
        MyMessageBox.show(ex.Message, "Error");
      }
    }
  }
}
