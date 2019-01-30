using System;
using System.Windows;

using System.Windows.Forms; // NotifyIcon
using System.Drawing; // Icon


namespace WindowsBackup
{
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window
  {
    WindowsBackup_App app;

    // Icon related
    NotifyIcon notify_icon;
    WindowState old_state; // state before minimization
    bool window_closing = false; // set to true in Window_Closing(...)


    public MainWindow()
    {
      InitializeComponent();
    }
        
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
      // allocate icons
      var ready_icon = new Icon("icons\\ready.ico");
      var busy_icon = new Icon("icons\\busy.ico");
      var error_icon = new Icon("icons\\error.ico");

      // notify icon setup 
      notify_icon = new NotifyIcon();
      notify_icon.Icon = ready_icon;
      notify_icon.Click += notify_icon_Click;
      notify_icon.DoubleClick += notify_icon_Click;
      notify_icon.Visible = true;

      app = new WindowsBackup_App(Output_tb, notify_icon, ready_icon, busy_icon, error_icon);
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
      window_closing = true;
      app.Dispose();

      // prevent notify_icon use inside WindowsBackup_App :: change_notify_icon(...)
      app.notify_icon = null;

      notify_icon.Dispose();
      notify_icon = null;      
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
      // If normalizing or maximizing the window, update "old_state",
      // which records the last non-minimized state.
      if (WindowState == WindowState.Normal) old_state = WindowState.Normal;
      else if (WindowState == WindowState.Maximized) old_state = WindowState.Maximized;

      // If minimizing, hide the window to make it totally disappear.
      else if (WindowState == WindowState.Minimized)
        Hide(); 
    }

    void notify_icon_Click(object sender, EventArgs e)
    {
      // If the backup manager thread is running, and the user attempts
      // to close the program, then the clean up might take a long time.
      // The code in Window_Closing(...) by is waiting for the backup
      // manager thread to terminate.
      //
      // During this wait, if the user click on notificy_icon, cancel
      // the click - don't let execution get to Show(), since the 
      // window is closing.
      if (window_closing) return;

      Show();
      WindowState = old_state;
      Activate();
    }


    #region menu handlers

    private void CheckAllBackups_menuItem_Click(object sender, RoutedEventArgs e)
    {
      app.backup_manager.stop_live_backup();
      app.backup_manager.check_all_backups();
    }

    private void ClearMessages_menuItem_Click(object sender, RoutedEventArgs e)
    {
      Output_tb.Text = "";
    }

    private void WordWrap_menuItem_Click(object sender, RoutedEventArgs e)
    {
      if (WordWrap_menuItem.IsChecked)
        Output_tb.TextWrapping = TextWrapping.Wrap;
      else
        Output_tb.TextWrapping = TextWrapping.NoWrap;
    }

    private void Setup_menuItem_Click(object sender, RoutedEventArgs e)
    {
      var setup = new SetupWindow(app);
      setup.ShowDialog();
    }

    private void Restore_menuItem_Click(object sender, RoutedEventArgs e)
    {
      app.backup_manager.stop_live_backup();

      bool success = app.enter_restore_mode();

      if (success)
      {
        var restore = new Restore_Window(app);
        restore.Height = 300;
        restore.ShowDialog();

        app.leave_restore_mode();
      }
      else
      {
        MyMessageBox.show("Software stuck in error mode. Cannot start "
          + "\"restore\" window. Restart this software to clear error mode.",
          "Error");
      }
    }

    private void Compare_menuItem_Click(object sender, RoutedEventArgs e)
    {
      var window = new Compare_Window();
      window.ShowDialog();
    }

    private void FNR_menuItem_Click(object sender, RoutedEventArgs e)
    {
      var window = new FNR_Window(app.file_name_reg);
      window.ShowDialog();
    }

    #endregion 
  }
}
