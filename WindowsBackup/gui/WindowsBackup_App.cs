using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

using System.Xml.Linq; // for XML
using System.Windows.Controls; // for TextBox
using Forms = System.Windows.Forms; // for NotifyIcon
using System.Drawing; // for Icon

namespace WindowsBackup
{
  class WindowsBackup_App
  {
    // File locations
    const string settings_path = "data\\settings.xml";
    const string fnr_path = "data\\fnr.tsv"; // fnr = file name registration

    // Data structures
    internal BasicKeyManager key_manager;
    internal FileNameRegistration file_name_reg;
    internal List<CloudBackupService> cloud_backup_services;
    internal BackupManager backup_manager;
    internal RestoreManager restore_manager;

    // GUI - main window
    TextBox Output_tb;
    Icon ready_icon, busy_icon, error_icon;
    internal Forms.NotifyIcon notify_icon;

    // GUI - restore window
    Restore_Window restore_window;
    TextBlock RestoreInfo_text, RestoreStatus_text;
    int restore_mode_files_processed;

    // Delegates for the backup manager thread calling the GUI thread
    delegate void Func_void();
    delegate void Func_string(string text);
    delegate void Func_Icon(Icon icon);
    delegate void Func_TextBlock_string(TextBlock text_block, string s);
    delegate void Func_RestoreInfo(RestoreInfo restore_info);

    // The mode this application is in
    enum AppMode { BACKUP, ERROR, RESTORE_GET_INFO, RESTORE_UNDER_WAY, RESTORE_DONE }
    AppMode app_mode = AppMode.BACKUP;

    // Data structures for adding text updates to the Output_tb
    DelayThread delay_thread; // To reduce the number of WPF dispatches, text
    // updates are delayed.

    const int buffer_size = 200;
    TextBuffer text_buffer_sv = new TextBuffer(buffer_size);
    // Shared variable: text_buffer_sv
    // This is shared between the background thread that is 
    // generating the text, and the GUI thread that is
    // displaying the text.



    #region Loading and saving settings 

    public WindowsBackup_App(TextBox Output_tb, Forms.NotifyIcon notify_icon,
      Icon ready, Icon busy, Icon error)
    {
      this.Output_tb = Output_tb;
      this.notify_icon = notify_icon;
      ready_icon = ready;
      busy_icon = busy;
      error_icon = error;

      if (File.Exists(settings_path) == false)
      {
        create_default_file();

        // Dispose the objects so to recreate from file later
        file_name_reg.Dispose();
      }

      try
      {
        // The "delay_thread" needs to be constructed first, before calling "read_settings()".
        // The reason is that if there is an error inside "read_settings()",
        // then the "delay_thread" is used to write messages to the "Output_tb".
        delay_thread = new DelayThread(Output_tb, new Func_void(add_text_to_Output_tb));
        
        read_settings();

        backup_manager.start_live_backup();
      }
      catch (Exception ex)
      {
        handle_event(AppEventType.ERROR, new object[] { ex.Message });
      }
    }

    void create_default_file()
    {
      // Create a directory if needed.
      var dir_name = Path.GetDirectoryName(settings_path);
      if (Directory.Exists(dir_name) == false)
        Directory.CreateDirectory(dir_name);
      
      key_manager = new BasicKeyManager();
      file_name_reg = new FileNameRegistration(fnr_path);
      cloud_backup_services = new List<CloudBackupService>();
      backup_manager = new BackupManager();
      restore_manager = new RestoreManager();
      
      save_settings();
    }

    /// <summary>
    /// Comparison function for sorting list of "CloudBackupService".
    /// </summary>
    int compare_two_cloud_backups(CloudBackupService c1, CloudBackupService c2)
    {
      return String.Compare(c1.Name, c2.Name);
    }

    void read_settings()
    {
      var x_doc = XDocument.Load(settings_path);
      var root = x_doc.Element("WindowsBackup_App");

      // key_manager, file_name_reg
      key_manager = new BasicKeyManager(root.Element("BasicKeyManager"));
      file_name_reg = new FileNameRegistration(root.Element("FileNameRegistration"));

      // cloud_backup_services
      cloud_backup_services = new List<CloudBackupService>();
      foreach (var tag in root.Element("cloud_backup_services").Elements())
      {
        if (tag.Name.LocalName.Equals("AWS_CloudBackupService"))
          cloud_backup_services.Add(new AWS_CloudBackupService(tag));
        else if (tag.Name.LocalName.Equals("AzureBlob_CloudBackupService"))
          cloud_backup_services.Add(new AzureBlob_CloudBackupService(tag));
        else if (tag.Name.LocalName.Equals("GCP_CloudBackupService"))
          cloud_backup_services.Add(new GCP_CloudBackupService(tag));
      }
      cloud_backup_services.Sort(compare_two_cloud_backups);

      // backup_manager, restore_manager
      backup_manager = new BackupManager(root.Element("BackupManager"),
              file_name_reg, cloud_backup_services, key_manager, handle_event);
      restore_manager = new RestoreManager(root.Element("RestoreManager"),
              cloud_backup_services, key_manager, handle_event);
    }

    void save_settings()
    {
      var x_doc = new XDocument();
      var root = new XElement("WindowsBackup_App");
      x_doc.Add(root);

      // <BasicKeyManager>, <FileNameRegistration>
      root.Add(key_manager.to_xml());
      root.Add(file_name_reg.to_xml());

      // <cloud_backup_services>
      root.Add(new XElement("cloud_backup_services"));
      foreach (var cloud_backup in cloud_backup_services)
        root.Element("cloud_backup_services").Add(cloud_backup.to_xml());

      // <BackupManager>, <RestoreManager>
      root.Add(backup_manager.to_xml());
      root.Add(restore_manager.to_xml());

      x_doc.Save(settings_path);
    }

    public void Dispose()
    {
      // "backup_manager" and "file_name_reg" would be null in the
      // event of a "WindowsBackup_App" construction error.
      if (backup_manager != null) backup_manager.quit();
      if (file_name_reg != null) file_name_reg.Dispose();

      delay_thread.quit();
    }

    public void save_and_reload()
    {
      save_settings();

      // Terminate all objects in use so to recreate them from file later
      backup_manager.quit();
      file_name_reg.Dispose();

      read_settings();
      backup_manager.start_live_backup();
    }

    #endregion



    #region Handling events from BackupManager thread

    /// <summary>
    /// Event handler for events issued by the backup manager thread.
    /// This function runs on the backup manager thread, so it cannot
    /// access the GUI elements directly.
    /// </summary>
    void handle_event(AppEventType event_type, params object[] param_array)
    {
      if (event_type == AppEventType.LOG)
      {
        string text = (string)param_array[0];
        add_line_of_text(text);
      }
      else if (event_type == AppEventType.ERROR)
      {
        app_mode = AppMode.ERROR;
        Output_tb.Dispatcher.BeginInvoke(new Func_Icon(change_notify_icon), error_icon);

        var error_message = (string)param_array[0];
        Output_tb.Dispatcher.BeginInvoke(
          new Func_string(show_error_message_box), error_message);

        add_line_of_text(error_message);
      }
      else if (event_type == AppEventType.CHECK_BACKUPS_DONE)
      {
        // This is the backup manager thread, so better to not call:
        // backup_manager.start_live_backup();
        // directly. Better call it via GUI thread, for consistency.
        Output_tb.Dispatcher.BeginInvoke(new Func_void(start_live_backup), null);
        add_line_of_text("All backups are up to date.");
      }
      else if (event_type == AppEventType.GET_RESTORE_INFO_DONE)
      {
        if (app_mode == AppMode.RESTORE_GET_INFO)
        {
          RestoreInfo info = (RestoreInfo)param_array[0];
          RestoreInfo_text.Dispatcher.BeginInvoke(
            new Func_RestoreInfo(restore_window.get_restore_info_done), info);

          app_mode = AppMode.RESTORE_UNDER_WAY;
          restore_mode_files_processed = 0;
        }
      }
      else if (event_type == AppEventType.FILES_PROCESSED)
      {
        int files_processed = (int)param_array[0];

        restore_mode_files_processed += files_processed;
        string progress = "Files processed: " + restore_mode_files_processed.ToString();

        if (app_mode == AppMode.RESTORE_GET_INFO)
        {          
          RestoreInfo_text.Dispatcher.BeginInvoke(
            new Func_TextBlock_string(set_text_block_text), RestoreInfo_text, progress);
        }
        else if (app_mode == AppMode.RESTORE_UNDER_WAY)
        {
          RestoreInfo_text.Dispatcher.BeginInvoke(
            new Func_TextBlock_string(set_text_block_text), RestoreStatus_text, progress);
        }
      }
      else if (event_type == AppEventType.RESTORE_DONE)
      {
        if (app_mode == AppMode.RESTORE_UNDER_WAY)
        {
          string message = "Files processed: " + restore_mode_files_processed.ToString()
            + "\nRestore completed.";
          RestoreInfo_text.Dispatcher.BeginInvoke(
            new Func_TextBlock_string(set_text_block_text), RestoreStatus_text, message);
          app_mode = AppMode.RESTORE_DONE;
        }
      }
      else if (event_type == AppEventType.BM_THREAD_IDLE)
      {
        if (app_mode != AppMode.ERROR)
        {
          Output_tb.Dispatcher.BeginInvoke(
            new Func_Icon(change_notify_icon), ready_icon);
        }
      }
      else if (event_type == AppEventType.BM_THREAD_RUNNING)
      {
        if (app_mode != AppMode.ERROR)
        {
          Output_tb.Dispatcher.BeginInvoke(
            new Func_Icon(change_notify_icon), busy_icon);
        }
      }
    }


    /// <summary>
    /// Call this on the GUI thread only, using Dispatcher.BeginInvoke(...)
    /// </summary>
    void change_notify_icon(Icon icon)
    {
      if (notify_icon != null)
        notify_icon.Icon = icon;
    }

    /// <summary>
    /// Call this on the GUI thread only, using Dispatcher.BeginInvoke(...)
    /// </summary>
    void show_error_message_box(string message)
    {
      // Gradually increase the width of the error message window.
      int width = 300;
      if (message.Length > 300) width = 400;
      if (message.Length > 400) width = 500;
      if (message.Length > 500) width = 600;

      MyMessageBox.show(message, "Error", width);
    }

    /// <summary>
    /// Call this on the GUI thread only, using Dispatcher.BeginInvoke(...)
    /// </summary>
    void start_live_backup()
    {
      backup_manager.start_live_backup();
    }

    #endregion



    #region TextBuffer data structure

    /// <summary>
    /// A circular buffer to hold the most recent lines of text added.
    /// </summary>
    private class TextBuffer
    {
      public string[] buffer;
      int start = 0;
      int end = 0;
      // The valid strings are at [start, end).
      // The string at buffer[end] is not valid, so the length
      // of the buffer is end - start, with wrap around adjustment.    

      /// <summary>
      /// Number of valid strings in the circular buffer.
      /// </summary>
      public int Length
      {
        get
        {
          // Length is (end - start) if no wrap around adjustment is needed.
          if (end >= start)
            return end - start;
          else
          {
            // When "end" < "start", do a wrap around adjustment
            return end + buffer.Length - start;
          }
        }
      }

      public TextBuffer(int max_size)
      {
        buffer = new string[max_size + 1];
        // It's +1 because there is a "wasted spot" at index "end". 
        // The "start" and "end" are off by 1 even if the size is zero.
      }

      /// <summary>
      /// Store "text" into a local buffer. If the buffer is filled up,
      /// oldest strings are removed.
      /// </summary>
      public void add_string(string text)
      {
        // Place text at buffer[end]. Then need to adjust "end"
        buffer[end] = text;
        end++;
        if (end >= buffer.Length) end = 0;

        // When the buffer is filled up, "end" will catch up to "start".
        // So need to adjust "start".
        if (end == start)
        {
          start++;
          if (start >= buffer.Length) start = 0;
        }
      }

      /// <summary>
      /// Remove "n" strings from the buffer.
      /// </summary>
      public void remove_strings(int n)
      {
        if (n >= Length)
        {
          // Remove all strings
          start = end;
        }
        else
        {
          // Remove only some of the strings. In this case, "start" will
          // not be "end" after removal.
          // Increment "start" and apply a wrap around.
          start += n;
          if (start >= buffer.Length) start -= buffer.Length;
        }
      }

      /// <summary>
      /// Remove all strings from the buffer.
      /// </summary>
      public void remove_all()
      {
        start = 0; end = 0;
      }

      public string this[int index]
      {
        get
        {
          // Check "index" for valid range.
          if (index < 0)
            throw new Exception("Software error. Software attempted to access "
              + "a TextBuffer object at index " + index + ", which is less than zero.");
          if (index >= Length)
            throw new Exception("Software error. Software attempted to access "
              + "a TextBuffer object at index " + index
              + ", which is beyond than the buffer length " + Length);

          // The "true_index" is "start + index", with wrap around.
          int true_index = index + start;
          if (true_index >= buffer.Length) true_index -= buffer.Length;

          return buffer[true_index];
        }
      }
    }

    #endregion



    #region Thread to delay WPF dispatcher launch
    
    /// <summary>
    /// A thread that will call Dispatcher.BeginInvoke(...) after
    /// a delay, so to not overwhelm WPF dispatcher system.
    /// </summary>
    private class DelayThread
    {
      TextBox Output_tb;
      Func_void update_function;
      // This thread will call:
      // Output_tb.Dispatcher.BeginInvoke(update_function)
      // after a delay.

      bool quit_flag = false;

      AutoResetEvent wait_flag = new AutoResetEvent(false);

      public DelayThread(TextBox Output_tb, Func_void update_function)
      {
        this.Output_tb = Output_tb;
        this.update_function = update_function;

        var thread = new Thread(new ThreadStart(run));
        thread.Name = "Delay thread";
        thread.Start();
      }

      void run()
      {
        while (true)
        {
          if (quit_flag == true) return;
          wait_flag.WaitOne();
          if (quit_flag == true) return;

          Thread.Sleep(200);
          Output_tb.Dispatcher.BeginInvoke(update_function);
        }
      }

      public void quit()
      {
        quit_flag = true;
        wake();
      }

      public void wake()
      {
        wait_flag.Set();
      }
    }

    #endregion



    #region Code that renders text from text_buffer_sv
    
    /// <summary>
    /// Adds "text" to "text_buffer_sv". Uses "delay_thread" to trigger
    /// a delayed update to Output_tb in the future.
    /// </summary>
    void add_line_of_text(string text)
    {
      // This function is on the background thread. The "buffer"
      // is being used as a flag variable.
      lock (text_buffer_sv)
      {
        text_buffer_sv.add_string(text);
      }

      delay_thread.wake();
    }

    void add_text_to_Output_tb()
    {
      // update Output_tb with what's in the text_buffer_sv
      lock (text_buffer_sv)
      {
        var sb = new StringBuilder();
        for (int i = 0; i < text_buffer_sv.Length; i++)
          sb.Append(text_buffer_sv[i] + "\n");

        Output_tb.Text = sb.ToString();
      }

      Output_tb.ScrollToEnd();
    }

    #endregion



    #region Restore mode

    /// <summary>
    /// Returns a "success" flag. 
    /// </summary>
    /// <returns>Returns false if unable to enter into
    /// restore mode - due to existing error mode.</returns>
    public bool enter_restore_mode()
    {
      if (app_mode == AppMode.ERROR) return false;

      app_mode = AppMode.RESTORE_GET_INFO;
      restore_mode_files_processed = 0;
      return true;
    }

    public void leave_restore_mode()
    {
      if (app_mode == AppMode.ERROR) return;

      app_mode = AppMode.BACKUP;
      restore_window = null;
      RestoreInfo_text = null;
      RestoreStatus_text = null;
    }

    /// <summary>
    /// Call this on the GUI thread only, using Dispatcher.BeginInvoke(...) 
    /// </summary>
    void set_text_block_text(TextBlock text_block, string text)
    {
      text_block.Text = text;
    }

    /// <summary>
    /// The restore window is recreated every time the user
    /// clicks on the "restore" menu item, so the GUI references
    /// need to be updated.
    /// </summary>
    public void update_restore_GUI(Restore_Window restore_window,
      TextBlock RestoreInfo_text, TextBlock RestoreStatus_text)
    {
      this.restore_window = restore_window;
      this.RestoreInfo_text = RestoreInfo_text;
      this.RestoreStatus_text = RestoreStatus_text;
    }

    #endregion



    #region Utility functions used by multiple classes

    /// <summary>
    /// Invokes Windows Form's FolderBrowserDialog to get a directory
    /// from the user. 
    /// </summary>
    /// <returns>If the user cancels the directory selection, return
    /// the starting_directory.</returns>
    public static string get_folder(string starting_directory)
    {
      using (var dialog = new Forms.FolderBrowserDialog())
      {
        if (starting_directory.Trim().Length > 0
            && Directory.Exists(starting_directory.Trim()))
        {
          dialog.SelectedPath = starting_directory;
        }

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
          return dialog.SelectedPath;
        else
          return starting_directory;
      }
    }

    /// <summary>
    /// For this software to work, the directory name should not
    /// end in '\'.
    /// </summary>
    public static void remove_ending_slash(ref string directory)
    {
      if (directory.EndsWith("\\"))
      {
        // Find the last character that is not a '\'.
        int index = directory.LastIndexOf('\\');
        index--;
        while (index >= 0 && directory[index] == '\\')
          index--;
        if (index >= 0)
          directory = directory.Substring(0, index + 1);
        else
          directory = "";
      }
    }

    #endregion
  }

}
