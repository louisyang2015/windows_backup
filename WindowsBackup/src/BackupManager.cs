using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using System.Xml.Linq; // for XML


namespace WindowsBackup
{
  class BackupRuleLists
  {
    public enum BackupRuleType { ACCEPT_ALL, REJECT_ALL, ACCEPT_SUFFIX, REJECT_SUFFIX,
                                 REJECT_SUB_DIR }

    public class BackupRule : IComparable<BackupRule>
    {
      public readonly string directory;
      readonly BackupRuleType rule_type = BackupRuleType.REJECT_ALL;
      readonly string[] suffixes = new string[] { };
      readonly string[] subdirs = new string[] { };

      /// <summary>
      /// The sort is based on the length of the directory path. Longer
      /// directory path is more specific.
      /// </summary>
      public int CompareTo(BackupRule other)
      {
        return other.directory.Length - directory.Length;
      }

      /// <summary>
      /// Returns true if the file_path belongs in the directory of this rule.
      /// </summary>
      public bool applies_to(string file_path)
      {
        if (file_path.StartsWith(directory)) return true;
        else return false;
      }

      /// <summary>
      /// Returns true if the file_path is accepted by this rule.
      /// </summary>
      public bool accepts(string file_path)
      {
        if (rule_type == BackupRuleType.REJECT_ALL) return false;
        else if (rule_type == BackupRuleType.ACCEPT_ALL) return true;

        else if (rule_type == BackupRuleType.REJECT_SUFFIX)
        {
          foreach (string suffix in suffixes)
            if (file_path.EndsWith(suffix)) return false;
                    
          return true;
        }
        else if (rule_type == BackupRuleType.ACCEPT_SUFFIX)
        {
          foreach (string suffix in suffixes)
            if (file_path.EndsWith(suffix)) return true;

          return false;
        }
        else if (rule_type == BackupRuleType.REJECT_SUB_DIR)
        {
          // extract sub directories
          string subdir_str = file_path.Substring(directory.Length);
          string[] file_name_sub_dirs = subdir_str.Split(
                        new char[] { Path.DirectorySeparatorChar }, 
                        StringSplitOptions.RemoveEmptyEntries);

          foreach (string subdir in subdirs)
          {
            // The last element of "file_name_sub_dirs" is actually a file name,
            // so don't compare against that.
            for (int i = 0; i < file_name_sub_dirs.Length - 1; i++)
              if (subdir.Equals(file_name_sub_dirs[i]))
                return false; // accept = false if sub directory matches
          }

          return true;
        }

        // Code should not get here as all cases of rule_type is 
        // being handled.
        return true; 
      }

      public BackupRule(XElement xml)
      {
        // directory
        directory = xml.Element("directory").Value;

        // rule_type
        if (Enum.TryParse(xml.Element("rule_type").Value, out rule_type) == false)
          throw new Exception("XML file error. Failed to parse a <rule_type> "
            + "tag with the value " + xml.Element("rule_type").Value
            + " while processing the backup rules.");

        // suffixes
        if (xml.Element("suffixes") != null
              && xml.Element("suffixes").Value.Trim().Length > 0)
        {
          suffixes = xml.Element("suffixes").Value.Trim().Split(' ');
        }

        // subdirs
        if (xml.Element("subdirs") != null
              && xml.Element("subdirs").Value.Trim().Length > 0)
        {
          subdirs = xml.Element("subdirs").Value.Trim().Split(' ');
        }
      }

      /// <param name="suffixes_str">The suffixes as a single string, separated by spaces.</param>
      /// <param name="subdirs_str">The sub-directories as a single string, separated by spaces.</param>
      public BackupRule(string directory, BackupRuleType rule_type, 
        string suffixes_str, string subdirs_str)
      {
        this.directory = directory;
        this.rule_type = rule_type;

        if (rule_type == BackupRuleType.ACCEPT_SUFFIX 
            || rule_type == BackupRuleType.REJECT_SUFFIX)
        {
          if (suffixes_str != null && suffixes_str.Trim().Length > 0)
            suffixes = suffixes_str.Split(' ');
        }
        else if (rule_type == BackupRuleType.REJECT_SUB_DIR)
        {
          if (subdirs_str != null && subdirs_str.Trim().Length > 0)
            subdirs = subdirs_str.Split(' ');
        }
      }

      public XElement to_xml()
      {
        var tag = new XElement("BackupRule",
                      new XElement("directory", directory),
                      new XElement("rule_type", rule_type.ToString()));

        // <suffixes> child
        if (suffixes.Length > 0)
        {
          string suffixes_str = String.Join(" ", suffixes);
          tag.Add(new XElement("suffixes", suffixes_str));
        }

        // <subdirs> child
        if (subdirs.Length > 0)
        {
          string subdirs_str = String.Join(" ", subdirs);
          tag.Add(new XElement("subdirs", subdirs_str));
        }

        return tag;
      }

      public override string ToString()
      {
        string s = directory + "\t" + rule_type;
        if (suffixes.Length > 0) s += "\t" + String.Join(" ", suffixes);
        if (subdirs.Length > 0) s += "\t" + String.Join(" ", subdirs);

        return s;
      }

      public BackupRule Clone()
      {
        string suffixes_str = null;
        if (suffixes.Length > 0)
          suffixes_str = String.Join(" ", suffixes);

        string subdirs_str = null;
        if (subdirs.Length > 0)
          subdirs_str = String.Join(" ", subdirs);

        return new BackupRule(directory, rule_type, suffixes_str, subdirs_str);        
      }
    }

    public List<List<BackupRule>> backup_rule_lists = new List<List<BackupRule>>();

    /// <summary>
    /// Creates an object with no rules.
    /// </summary>
    public BackupRuleLists() { }

    public BackupRuleLists(XElement xml)
    {
      int index = -1;
      foreach (var category_tag in xml.Elements("category"))
      {
        backup_rule_lists.Add(new List<BackupRule>());
        index++;

        foreach (var rule_tag in category_tag.Elements("BackupRule"))
          backup_rule_lists[index].Add(new BackupRule(rule_tag));

        backup_rule_lists[index].Sort();
      }
    }

    /// <summary>
    /// Private constructor intended for cloning use.
    /// </summary>
    /// <param name="backup_rules_clone">Make sure this is a clone, not the original.</param>
    BackupRuleLists(List<List<BackupRule>> backup_rule_lists_clone)
    {
      this.backup_rule_lists = backup_rule_lists_clone;
    }
    
    public XElement to_xml()
    {
      if (backup_rule_lists.Count == 0) return null;

      var rule_list_tag = new XElement("BackupRuleLists");

      foreach (var rule_list in backup_rule_lists)
      {
        if (rule_list.Count > 0)
        {
          var category_tag = new XElement("category");
          foreach (var rule in rule_list)
            category_tag.Add(rule.to_xml());

          rule_list_tag.Add(category_tag);
        }        
      }

      return rule_list_tag;
    }

    /// <summary>
    /// Returns true if the current set of rules accept the given file_path.
    /// If none of the rules apply, the default is to accept the file for backup.
    /// </summary>
    public bool accepts(string file_path)
    {
      foreach (var rule_list in backup_rule_lists)
      {
        foreach (var rule in rule_list)
        {
          if (rule.applies_to(file_path))
          {
            bool acceptance = rule.accepts(file_path);

            // one rejection is sufficient to reject file_path
            if (acceptance == false) return false;

            // acceptance means code is done with the current 
            // category (list) of rules.
            break;
          }
        }
      }

      // Code gets here either because no rules apply, or because
      // no applied rule so far rejects the file_path.
      // The default is to accept the file for backup.
      return true;
    }

    public BackupRuleLists Clone()
    {
      var backup_rule_lists_clone = new List<List<BackupRule>>();

      int index = -1;
      foreach (var rule_list in backup_rule_lists)
      {
        backup_rule_lists_clone.Add(new List<BackupRule>());
        index++;

        foreach (var rule in rule_list)
          backup_rule_lists_clone[index].Add(rule.Clone());
      }

      return new BackupRuleLists(backup_rule_lists_clone);
    }

    /// <summary>
    /// Adds a new rule. If there is an old rule that uses the same 
    /// directory as the new rule, the old rule will be replaced 
    /// by the new rule.
    /// </summary>
    public void add_rule(BackupRule rule, int category)
    {
      if (category > backup_rule_lists.Count)
        throw new Exception("Internal software error. The BcakupRuleLists :: "
          + "void add_rule(...) is being told to add a new rule "
          + "to a category value that is too high. ");

      if (category == backup_rule_lists.Count)
        backup_rule_lists.Add(new List<BackupRule>());

      var rule_list = backup_rule_lists[category];

      // look for existing rule with the same directory as the new rule
      for (int i =0; i < rule_list.Count; i++)
      {
        if(rule_list[i].directory.Equals(rule.directory))
        {
          rule_list[i] = rule;
          return;
        }
      }

      // Standard add
      rule_list.Add(rule);
      rule_list.Sort();
    }
  }
  

  // For reporting events to the GUI thread
  enum AppEventType { LOG, ERROR, CHECK_BACKUPS_DONE, FILES_PROCESSED,
                      GET_RESTORE_INFO_DONE, RESTORE_DONE, BM_THREAD_IDLE,
                      BM_THREAD_RUNNING }
  delegate void ReportEvent_Callback(AppEventType event_type, params object[] param_array);

  abstract class Backup
  {
    // ********************************************************
    // Specialization to be done by the derived class

    // The live_backup() copies a file to its destination as soon as the file 
    // has changed. This function will call backup_one_path(...), which 
    // will be specialized by the derived class.
    abstract protected void backup_one_path(string source_full_path, FS_Watcher_EventType event_type);

    // The check_all_backups() goes through all existing files and backup
    // anything that is out of date. 
    abstract public void check_all_backups();

    abstract public XElement to_xml();

    // ********************************************************
    // Data Structures
    
    protected enum FS_Watcher_EventType { CREATED, CHANGED, DELETED }

    protected class FileEvent
    {
      public readonly string source;
      public readonly FS_Watcher_EventType event_type;

      public FileEvent(string source, FS_Watcher_EventType event_type)
      {
        this.source = source;
        this.event_type = event_type;
      }
    }

    protected class PathStatus
    {
      public readonly bool exists, is_directory, is_hidden;

      public PathStatus(bool exists, bool is_directory, bool is_hidden)
      {
        this.exists = exists;
        this.is_directory = is_directory;
        this.is_hidden = is_hidden;
      }

      /// <summary>
      /// Gets file attribute information for a given (file system) path.
      /// </summary>
      public static PathStatus get_path_status(string path)
      {
        bool exists = false;
        bool is_directory = false;
        bool is_hidden = false;

        if (File.Exists(path))
          exists = true;
        else if (Directory.Exists(path))
        {
          exists = true;
          is_directory = true;
        }

        if (exists)
        {
          var file_attributes = File.GetAttributes(path);
          if ((file_attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
            is_hidden = true;
        }

        return new PathStatus(exists, is_directory, is_hidden);
      }

      /// <summary>
      /// Return true if the file is hidden. This assumes the file exists.
      /// </summary>
      public static bool is_file_hidden(string path)
      {
        var file_attributes = File.GetAttributes(path);
        if ((file_attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
          return true;
        else return false;
      }
    }

    // ********************************************************
    // Member Variables - modified by the derived class

    // Ideally nothing here...

    // ********************************************************
    // Member Variables - not modified by the derived class

    // The source of the backup:
    public readonly string source_base; // base path
        
    FileSystemWatcher file_system_watcher;

    // To wake up the backup_thread when it's sleeping
    AutoResetEvent backup_thread_wait_obj;

    // The list of FileEvent objects is a variable shared between 
    // multiple threads, hence the _sv suffix.
    List<FileEvent> file_events_sv = new List<FileEvent>();

    // Enabled, Name
    readonly bool enabled = true;
    readonly string name;

    public bool Enabled { get { return enabled; } }
    public string Name { get { return name; } }

    // Allows the outside world to poll for recent activity. 
    // The backup_thread will wait for multiple seconds of no activity.
    volatile bool recent_activity = false;
    public bool RecentActivity
    {
      get
      {
        // Reading "recent_activity" will clear this flag.
        if (recent_activity == true)
        {
          recent_activity = false;
          return true;
        }
        else
          return false;
      }
    }

    // ********************************************************
    // Constructors

    public Backup(XElement xml, AutoResetEvent backup_thread_wait_obj)
    {
      this.backup_thread_wait_obj = backup_thread_wait_obj;

      // attributes: "enable", "name"
      var enable_attr = xml.Attribute("enable");
      if (enable_attr.Value.ToLower().Equals("true") == false)
        enabled = false;

      name = xml.Attribute("name").Value;

      // child tag: <source>
      source_base = xml.Element("source").Value;

      // Create file_system_watcher - but it's not started here
      if (enabled)
      {
        file_system_watcher = new FileSystemWatcher();

        file_system_watcher.Path = source_base;
        file_system_watcher.NotifyFilter = NotifyFilters.LastWrite
          | NotifyFilters.FileName | NotifyFilters.DirectoryName;
        file_system_watcher.Filter = "*";
        file_system_watcher.IncludeSubdirectories = true;

        file_system_watcher.Created += new FileSystemEventHandler(file_created);
        file_system_watcher.Changed += new FileSystemEventHandler(file_changed);
        file_system_watcher.Deleted += new FileSystemEventHandler(file_deleted);
        file_system_watcher.Renamed += new RenamedEventHandler(file_renamed);
      }
    }

    /// <summary>
    /// Creates a Backup object that is not usable. 
    /// It is meant to be a starting point for GUI based configuration.
    /// </summary>
    public Backup(string source, string name)
    {
      source_base = source;
      this.name = name;
    }

    // ********************************************************
    // Member functions - called by the derived class

    // Ideally nothing here...


    // ********************************************************
    // Member functions - not called by the derived class

    public void start_live_backup()
    {
      if (enabled)
        file_system_watcher.EnableRaisingEvents = true;
    }

    public void stop_live_backup()
    {
      // file_system_watcher can be null if the object is created
      // using the non-xml constructor, resulting in non-usable object.
      if (enabled && file_system_watcher != null)
        file_system_watcher.EnableRaisingEvents = false;
    }
        
    void file_created(object source, FileSystemEventArgs e)
    {
      // Console.WriteLine("File: " + e.FullPath + " " + e.ChangeType);
      lock (file_events_sv)
      {
        // this is on the OS thread, from file_system_watcher
        file_events_sv.Add(new FileEvent(e.FullPath, FS_Watcher_EventType.CREATED));
      }
      wake_backup_thread();
    }
       
    void file_changed(object source, FileSystemEventArgs e)
    {
      // Console.WriteLine("File: " + e.FullPath + " " + e.ChangeType);
      lock (file_events_sv)
      {
        // this is on the OS thread, from file_system_watcher
        file_events_sv.Add(new FileEvent(e.FullPath, FS_Watcher_EventType.CHANGED));
      }
      wake_backup_thread();
    }
    
    void file_deleted(object source, FileSystemEventArgs e)
    {
      // Console.WriteLine("File: " + e.FullPath + " " + e.ChangeType);
      lock (file_events_sv)
      {
        // this is on the OS thread, from file_system_watcher
        file_events_sv.Add(new FileEvent(e.FullPath, FS_Watcher_EventType.DELETED));
      }
      wake_backup_thread();
    }

    void file_renamed(object source, RenamedEventArgs e)
    {
      // Console.WriteLine("File: {0} renamed to {1}", e.OldFullPath, e.FullPath);
      lock (file_events_sv)
      {
        // this is on the OS thread, from file_system_watcher
        file_events_sv.Add(new FileEvent(e.OldFullPath, FS_Watcher_EventType.DELETED));
        file_events_sv.Add(new FileEvent(e.FullPath, FS_Watcher_EventType.CREATED));
      }
      wake_backup_thread();
    }

    void wake_backup_thread()
    {
      recent_activity = true;
      backup_thread_wait_obj.Set();
    }

    /// <summary>
    /// Go through the "file_events_sv" list and make updates as necessary.
    /// </summary>
    public void live_backup()
    {
      if (enabled == false) return;

      // First make a copy of "file_events_sv".
      FileEvent[] file_events_array = null;
      lock (file_events_sv)
      {
        // this is on the backup_thread, spawned by the application
        file_events_array = file_events_sv.ToArray();
        file_events_sv.Clear();
      }
      // From now on use the "file_events_array" copy only.

      // Go through file_events and narrow it down to only the latest events.
      var file_events = new Dictionary<string, FS_Watcher_EventType>();

      foreach (var new_file_event in file_events_array)
        file_events[new_file_event.source] = new_file_event.event_type;

      foreach (string path in file_events.Keys)
        backup_one_path(path, file_events[path]);
    }
  }


  class DiskBackup : Backup
  {
    // To do the backup:
    public readonly string destination_base; // base paths
    public readonly BackupRuleLists backup_rule_lists = new BackupRuleLists();

    // Callback to provide output for the end user.
    ReportEvent_Callback report_event = null;
    
    /// <summary>
    /// These parameters will not take effect immediately. They 
    /// will be used by the to_xml() function.
    /// </summary>
    public Parameters future_params;

    public class Parameters
    {
      public bool enabled;

      public string name;
      public BackupRuleLists rule_lists;
    }


    public DiskBackup(XElement xml, AutoResetEvent backup_thread_wait_obj,
      ReportEvent_Callback report_event_callback = null)
      : base(xml, backup_thread_wait_obj)
    {
      report_event = report_event_callback;

      // child tags: <destination>, <BackupRuleList>
      destination_base = xml.Element("destination").Value;
      if (xml.Element("BackupRuleLists") != null)
        backup_rule_lists = new BackupRuleLists(xml.Element("BackupRuleLists"));

      // future_params
      future_params = new Parameters();
      future_params.enabled = Enabled;
      future_params.name = Name;
      future_params.rule_lists = backup_rule_lists.Clone();
    }


    /// <summary>
    /// Creates a basic DiskBackup object that is not usable. 
    /// It is meant to be a starting point for GUI based configuration.
    /// </summary>
    public DiskBackup(string source, string destination, BackupRuleLists rule_lists,
      string name) : base(source, name)
    {
      destination_base = destination;

      future_params = new Parameters();
      future_params.enabled = true;
      future_params.name = name;
      future_params.rule_lists = rule_lists;
    }
        
    /// <summary>
    /// Generate XML using future_params.
    /// </summary>
    override public XElement to_xml()
    {
      var backup_tag = new XElement("DiskBackup");

      // <DiskBackup> attributes
      backup_tag.SetAttributeValue("enable", future_params.enabled);
      backup_tag.SetAttributeValue("name", future_params.name);

      // <DiskBackup> child tags
      backup_tag.Add(new XElement("source", source_base));
      backup_tag.Add(new XElement("destination", destination_base));
      backup_tag.Add(future_params.rule_lists.to_xml());

      return backup_tag;
    }

    
    /// <summary>
    /// Given the backup source_full_path, compute the 
    /// destination_full_path.
    /// </summary>
    string get_destination(string source_full_path)
    {
      string relative_path = source_full_path.Substring(source_base.Length);
      return destination_base + relative_path;
    }

    /// <summary>
    /// Given the backup destination_full_path, compute the
    /// source_full_path.
    /// </summary>
    string get_source(string destination_full_path)
    {
      string relative_path = destination_full_path.Substring(destination_base.Length);
      return source_base + relative_path;
    }
        

    /// <summary>
    /// Handles the file system watcher event for a particular path.
    /// </summary>
    override protected void backup_one_path(string source_full_path, FS_Watcher_EventType event_type)
    {
      var source_status = PathStatus.get_path_status(source_full_path);

      // ignore hidden files and directory change events
      if (source_status.is_hidden) return;
      if (source_status.is_directory && event_type == FS_Watcher_EventType.CHANGED)
        return;

      if (source_status.exists == false)
        // case 1: source_full_path does not exist (has been deleted)
        delete(get_destination(source_full_path));
      else
      {
        if (source_status.is_directory)
          // case 2: source_full_path is currently a directory
          backup_directory(source_full_path);
        else
          // case 3: source_full_path is currently a file
          backup_file(source_full_path);
      }
    }

    /// <summary>
    /// Checks the source path against the backup_rule_list. If the 
    /// source path is accepted, mirror the source status.
    /// </summary>
    void backup_file(string source)
    {
      // The delete case is determined inside backup_one_path(...).
      // This code assumes that "source" exists and is a file.
      if (backup_rule_lists.accepts(source))
      {
        // However, no assumption can be made of the destination.
        // In particular, the destination should not be a directory.
        string destination = get_destination(source);

        if (Directory.Exists(destination))
          Directory.Delete(destination, recursive: true);

        // Copy file from source --> destination
        // The File.Copy(...) method will fail if destination directory
        // does not exist.
        string destination_dir = Path.GetDirectoryName(destination);
        if (Directory.Exists(destination_dir) == false)
          Directory.CreateDirectory(destination_dir);
        File.Copy(source, destination, overwrite: true);

        if (report_event != null)
          report_event(AppEventType.LOG, source + " --> " + destination);
      }
    }

    /// <summary>
    /// Deletes a particular file or directory.
    /// </summary>
    void delete(string path)
    {
      if (File.Exists(path))
      {
        File.Delete(path);
        if (report_event != null)
          report_event(AppEventType.LOG, "Deleted file " + path);
      }
      else if (Directory.Exists(path))
      {
        Directory.Delete(path, recursive: true);
        if (report_event != null)
          report_event(AppEventType.LOG, "Deleted directory " + path);
      }
    }
    
    /// <summary>
    /// Mirrors the source directory at the destination.
    /// </summary>
    void backup_directory(string source)
    {
      // backup all files in the current directory
      var file_names = Directory.GetFiles(source);
      foreach (var file_name in file_names)
        backup_file(file_name);

      // apply the same backup_directory(...) for all sub directories
      var dir_names = Directory.GetDirectories(source);
      foreach (var dir_name in dir_names)
        backup_directory(dir_name);
    }

    /// <summary>
    /// Go through every file in source_base and make sure they have been 
    /// backed up to destination_base. Go through every file in 
    /// destination_base and make sure they truly exist in source_base.
    /// </summary>
    override public void check_all_backups()
    {
      if (Enabled == false) return;
      
      check_source(source_base);
      check_destination(destination_base);
    }

    /// <summary>
    /// Check that everything in source has been backed up.
    /// </summary>
    void check_source(string source)
    {
      // check all files in the current directory
      var file_names = Directory.GetFiles(source);

      foreach (var file_name in file_names)
      {
        bool is_hidden = PathStatus.is_file_hidden(file_name);
        
        if (is_hidden == false && backup_rule_lists.accepts(file_name))
        {
          string destination = get_destination(file_name);

          if (File.Exists(destination) == false)
            // If the file doesn't exist at the destination, then
            // definitely need to back it up.
            backup_file(file_name);
          else
          {
            // If file exist, check modified time to determine 
            // whether it needs to be backed up.
            DateTime source_time = File.GetLastWriteTimeUtc(file_name);
            DateTime destination_time = File.GetLastWriteTimeUtc(destination);
            if (destination_time != source_time)
              backup_file(file_name);

            // The following check would be too lax:
            // if (destination_time < source_time)
            // See documentation --> BackupManager --> Minor Notes --> Modify time checking
          }
        }
      }

      // do the same to all sub-directories
      var dir_names = Directory.GetDirectories(source);
      foreach (var dir_name in dir_names)
        check_source(dir_name);
    }

    /// <summary>
    /// Check that everything in destination also exists in source
    /// and is accepted by the current set of backup rules.
    /// </summary>
    void check_destination(string destination)
    {
      // check all files in the current directory
      var file_names = Directory.GetFiles(destination);

      foreach (var file_name in file_names)
      {
        string source = get_source(file_name);
        if (File.Exists(source) == false
              || backup_rule_lists.accepts(source) == false)
          delete(file_name);
      }

      // do the same to all sub-directories
      var dir_names = Directory.GetDirectories(destination);
      foreach (var dir_name in dir_names)
        check_destination(dir_name);
    }

  }


  class EncryptedBackup : Backup
  {
    // Filters rules for the backup.
    public readonly BackupRuleLists backup_rule_lists = new BackupRuleLists();

    // This is a prefix to the relative path that is embedded in the encrypted file:
    public readonly string embedded_prefix;

    // Encryption key for the backup:
    readonly UInt16 key_number;
    readonly string key;    
    FileNameRegistration file_name_reg;
    EncryptStream encrypt_stream; // This is shared resource. Use it only inside the backup thread.

    // Live backup to cloud limitation:
    readonly long cloud_live_backup_max_size = 1024 * 1024;

    // The destination of backup:
    public readonly string destination_name, destination_base;
    CloudBackupService cloud_backup = null;
                
    // Callback to provide output for the end user.
    ReportEvent_Callback report_event = null;

    readonly char sep = Path.DirectorySeparatorChar;
        
    /// <summary>
    /// These parameters will not take effect immediately. They 
    /// will be used by the to_xml() function.
    /// </summary>
    public Parameters future_params; 

    public class Parameters
    {
      public bool enabled;

      public string name;
      public BackupRuleLists rule_lists;
    }

    public EncryptedBackup(XElement xml, FileNameRegistration file_name_reg,
      EncryptStream encrypt_stream, List<CloudBackupService> cloud_backup_services,
      KeyManager key_manager, AutoResetEvent backup_thread_wait_obj,
      long cloud_live_backup_max_size = 1024 * 1024,
      ReportEvent_Callback report_event_callback = null) 
      : base(xml, backup_thread_wait_obj)
    {
      this.file_name_reg = file_name_reg;
      this.encrypt_stream = encrypt_stream;      
      this.cloud_live_backup_max_size = cloud_live_backup_max_size;
      report_event = report_event_callback;
      
      // <EncryptedBackup> \ <embedded_prefix>
      embedded_prefix = xml.Element("embedded_prefix").Value;

      // <EncryptedBackup> \ <key_number> --> key
      key_number = UInt16.Parse(xml.Element("key_number").Value);
      key = key_manager.get_key_value(key_number);
      if (key == null)
        throw new Exception("XML file error. When processing <EncryptedBackup> "
          + "with source \"" + source_base + "\" and key number " 
          + key_number + " the encryption key cannot be found.");

      // <EncryptedBackup> \ <destination_name>, <destination_path>
      destination_name = xml.Element("destination_name").Value;
      destination_base = xml.Element("destination_path").Value;

      // <EncryptedBackup> \ <BackupRuleList>
      if (xml.Element("BackupRuleLists") != null)
        backup_rule_lists = new BackupRuleLists(xml.Element("BackupRuleLists"));

      if (destination_name.Equals("disk") == false)
      {
        // search for the destination in cloud_backup_services
        foreach (var cloud_backup in cloud_backup_services)
        {
          if (destination_name.Equals(cloud_backup.Name))
          {
            this.cloud_backup = cloud_backup;
            break;
          }
        }
        if (cloud_backup == null)
          throw new Exception("XML file error. While processing <EncryptedBackup> "
            + "with source \"" + source_base + "\", unable to find destination "
            + "with name " + destination_name + ".");
      }

      // future_params
      future_params = new Parameters();

      future_params.enabled = Enabled;
      future_params.name = Name;
      future_params.rule_lists = backup_rule_lists.Clone();
    }


    /// <summary>
    /// Creates a basic EncryptedBackup object that is not usable. 
    /// It is meant to be a starting point for GUI based configuration.
    /// </summary>
    public EncryptedBackup(string source, string embedded_prefix,
      UInt16 key_number, string destination_name, string destination_path,
      BackupRuleLists rule_lists, string name)
      : base(source, name)
    {
      this.embedded_prefix = embedded_prefix;
      this.key_number = key_number; // note at this point key = null
      this.destination_name = destination_name;
      destination_base = destination_path;

      future_params = new Parameters();
      future_params.enabled = true;
      future_params.name = name;
      future_params.rule_lists = rule_lists;
    }
    
    /// <summary>
    /// Generate XML using future_params.
    /// </summary>
    override public XElement to_xml()
    {
      var backup_tag = new XElement("EncryptedBackup");

      // <EncryptedBackup> attributes
      backup_tag.SetAttributeValue("enable", future_params.enabled);
      backup_tag.SetAttributeValue("name", future_params.name);

      // <EncryptedBackup> \ <source>, <key_number>, <embedded_prefix>,       
      backup_tag.Add(new XElement("source", source_base));
      backup_tag.Add(new XElement("key_number", key_number));
      backup_tag.Add(new XElement("embedded_prefix", embedded_prefix));

      // EncryptedBackup> \ <destination_name>, <destination_path>
      backup_tag.Add(new XElement("destination_name", destination_name));
      backup_tag.Add(new XElement("destination_path", destination_base));
            
      // <EncryptedBackup> \ <BackupRuleList>
      backup_tag.Add(future_params.rule_lists.to_xml());

      return backup_tag;
    }
        

    /// <summary>
    /// Given the backup source_full_path, compute the 
    /// destination_full_path. In the case of cloud backup, this
    /// is for display use in log files.
    /// </summary>
    string get_destination(string alt_file_name)
    {
      if (cloud_backup == null)
        return destination_base + sep + alt_file_name;
      else
        // Cloud backup case. The destination is for display purposes only.
        return destination_name + sep + destination_base + sep + alt_file_name;
    }

    /// <summary>
    /// Given the backup destination_full_path, compute the
    /// source_full_path.
    /// </summary>
    string get_source(string destination_full_path)
    {
      /* string relative_path = destination_full_path.Substring(destination_base.Length);
      return source_base + relative_path; */
      return "";
    }
        

    /// <summary>
    /// Handles the file system watcher event for a particular path.
    /// </summary>
    override protected void backup_one_path(string source_full_path, FS_Watcher_EventType event_type)
    {
      var source_status = PathStatus.get_path_status(source_full_path);

      // ignore hidden files and directory change events
      if (source_status.is_hidden) return;
      if (source_status.is_directory && event_type == FS_Watcher_EventType.CHANGED)
        return;

      if (source_status.exists == false)
        // case 1: source_full_path does not exist (has been deleted)
        handle_source_deleted(source_full_path);
      else
      {
        if (source_status.is_directory)
          // case 2: source_full_path is currently a directory
          backup_directory(source_full_path, live_backup: true);
        else
          // case 3: source_full_path is currently a file
          backup_file(source_full_path, live_backup: true);
      }
    }

    /// <summary>
    /// Checks the source path against the backup_rule_list. If the 
    /// source path is accepted, mirror the source status.
    /// </summary>
    void backup_file(string source, bool live_backup = false)
    {
      // The delete case is determined inside backup_one_path(...).
      // This code assumes that "source" exists and is a file.
      if (backup_rule_lists.accepts(source))
      {
        // In the case of live_backup to cloud, need to skip the file if the size is too large.
        if(live_backup && cloud_backup != null)
        {
          var file_info = new FileInfo(source);
          if (file_info.Length > cloud_live_backup_max_size)
          {
            report_event(AppEventType.LOG, "The file " + source 
              + " is larger than the maximum size allowed for live backup. "
              + "Therefore, it is not uploaded.");
            return;
          }
        }

        // No assumption can be made of the backup destination.
        // In particular, the backup destination should not be a directory.

        // Detect the situation that the source exist on the backup 
        // destination as a directory.
        var path_status = file_name_reg.get_path_status(source);
        string alt_file_name = null;
        if (path_status != null)
        {
          if (path_status.is_file == false)
          {
            // The source is currently a directory in the backup destination.
            // Remove this directory before proceeding.
            handle_source_dir_deleted(source);
          }
          else
          {
            // The source exists on the destination, but perhaps outdated.
            alt_file_name = path_status.alt_file_name;
          }
        }

        // If there is no alt_file_name, obtain one.
        if (alt_file_name == null)
          alt_file_name = file_name_reg.add_file(source);

        // Copy file from source --> destination
        string destination = get_destination(alt_file_name);

        // Figure out the relative path that will be embedded within the encrypted file.
        string relative_path = embedded_prefix + source.Substring(source_base.Length);
        encrypt_stream.reset(source, relative_path, key, 
          do_not_compress: false, key_hint: key_number);

        // Get file modified time, but set it to null for the time being
        DateTime file_mod_time = File.GetLastWriteTimeUtc(source);
        file_name_reg.set_modified_time(source, null);

        if (cloud_backup == null)
        {
          var output_file = new FileStream(destination, FileMode.Create);
          encrypt_stream.CopyTo(output_file);
          output_file.Dispose();
        }
        else
        {
          cloud_backup.upload(encrypt_stream, destination_base, alt_file_name);
        }

        // After the file has been updated, set its modified time.
        file_name_reg.set_modified_time(source, file_mod_time);

        if (report_event != null)
          report_event(AppEventType.LOG ,source + " --> " + destination);
      }
    }

    /// <summary>
    /// Handler for when a file or directory has been deleted.
    /// </summary>
    void handle_source_deleted(string source_file_or_dir_path)
    {
      var path_status = file_name_reg.get_path_status(source_file_or_dir_path);
      if (path_status == null) return; // nothing to do

      if (path_status.is_file)
        handle_source_file_deleted(source_file_or_dir_path, path_status.alt_file_name);
      else
        handle_source_dir_deleted(source_file_or_dir_path);
    }

    /// <summary>
    /// Handler for when a source file has been deleted.
    /// </summary>
    void handle_source_file_deleted(string source_file_path, string alt_file_name)
    {
      // Delete the file from destination.
      string dest_path = get_destination(alt_file_name);
      if (cloud_backup == null)
        File.Delete(dest_path);
      else
      {
        try
        {
          cloud_backup.delete(destination_base, alt_file_name);
          // The approach here is to alert the user and have him manually
          // fix the file name registration file - removing "source_file_path"
          // manually. 
          // An alternative approach is to suppress the error - usually the
          // object no longer exist at the cloud, and only exists locally in
          // the file name registration table.
        }
        catch(Exception ex)
        {
          throw new Exception("Error while trying to delete \""
            + destination_base + "\\" + alt_file_name
            + "\". The original file name is \""
            + source_file_path + "\". If this error message repeats, "
            + "it's likely that the file exists in the file name registration, but "
            + "not in the cloud. Remove the entry from the file name registration. \n\n"
            + ex.Message);
        }
      }

      // Delete the file from file name registration.
      file_name_reg.delete(source_file_path);

      if(report_event != null)
        report_event(AppEventType.LOG, "Deleted file " + dest_path + " (" + source_file_path + ")");
    }

    /// <summary>
    /// Handler for when a source directory has been deleted.
    /// </summary>
    void handle_source_dir_deleted(string source_dir_path)
    {
      List<string> sub_dir_names = null;
      List<string> file_names = null;
      List<string> alt_file_names = null;

      // Get a list of all files and sub-directories under source_dir_path.
      file_name_reg.get_names(source_dir_path, ref sub_dir_names, ref file_names, ref alt_file_names);

      // Delete all files in the current directory.
      if (alt_file_names != null)
      {
        for (int i = 0; i < alt_file_names.Count; i++)
          handle_source_file_deleted(source_dir_path + sep + file_names[i], alt_file_names[i]);
      }
      
      // Repeat the same thing for all sub-directories
      if (sub_dir_names != null)
      {
        foreach (var sub_dir in sub_dir_names)
          handle_source_dir_deleted(source_dir_path + sep + sub_dir);
      }

      // Delete the directory from file name registration.
      file_name_reg.delete(source_dir_path);
    }

    /// <summary>
    /// Mirrors the source directory at the destination.
    /// </summary>
    void backup_directory(string source, bool live_backup = false)
    {
      // backup all files in the current directory
      var file_names = Directory.GetFiles(source);
      foreach (var file_name in file_names)
        backup_file(file_name, live_backup);

      // apply the same backup_directory(...) for all sub directories
      var dir_names = Directory.GetDirectories(source);
      foreach (var dir_name in dir_names)
        backup_directory(dir_name, live_backup);
    }

    /// <summary>
    /// Go through every file in source_base and make sure they have been 
    /// backed up to destination_base. Go through every file in 
    /// destination_base and make sure they truly exist in source_base.
    /// </summary>
    override public void check_all_backups()
    {
      if (Enabled == false) return;

      check_source(source_base);

      // The "destination", in this case, is the source path, but queried
      // via file_name_registration.
      check_destination(source_base);
    }

    /// <summary>
    /// Check that everything in source has been backed up.
    /// </summary>
    void check_source(string source_dir)
    {
      // check all files in the current directory
      var file_names = Directory.GetFiles(source_dir);

      foreach (var file_name in file_names)
      {
        bool is_hidden = PathStatus.is_file_hidden(file_name);
        
        if (is_hidden == false && backup_rule_lists.accepts(file_name))
        {
          var path_status = file_name_reg.get_path_status(file_name);

          if (path_status == null)
            // If the file doesn't exist at the destination, then
            // definitely need to back it up.
            backup_file(file_name);
          else
          {
            // If file exist, check modified time to determine 
            // whether it needs to be backed up.
            if (path_status.modified_time == null)
              // If modified time is unknown, definitely back it up.
              backup_file(file_name);
            else
            {
              DateTime source_time = File.GetLastWriteTimeUtc(file_name);
              DateTime destination_time = path_status.modified_time.Value;
              if (destination_time != source_time)
                backup_file(file_name);

              // The following check would be too lax:
              // if (destination_time < source_time)
              // See documentation --> BackupManager --> Minor Notes --> Modify time checking
            }
          }
        }
      }

      // do the same to all sub-directories
      var dir_names = Directory.GetDirectories(source_dir);
      foreach (var dir_name in dir_names)
        check_source(dir_name);
    }

    /// <summary>
    /// Check that everything in destination also exists in source.
    /// and is accepted by current set of backup rules.
    /// </summary>
    void check_destination(string destination_dir)
    {
      // The destination_dir is basically the same as the source directory,
      // except that the destination is directed against file_name_registration.

      List<string> sub_dir_names = null;
      List<string> file_names = null;
      List<string> alt_file_names = null;

      file_name_reg.get_names(destination_dir, ref sub_dir_names, ref file_names, ref alt_file_names);

      // check all files in the current destination directory
      if (file_names != null)
      {
        for (int i = 0; i < file_names.Count; i++)
        {
          string full_source_path = destination_dir + sep + file_names[i];
          if (File.Exists(full_source_path) == false
                || backup_rule_lists.accepts(full_source_path) == false)
            handle_source_file_deleted(full_source_path, alt_file_names[i]);
        }
      }
      
      // do the same to all sub-directories
      if (sub_dir_names != null)
      {
        foreach (var dir_name in sub_dir_names)
        {
          string full_source_path = destination_dir + sep + dir_name;
          if (Directory.Exists(full_source_path) == false)
            handle_source_dir_deleted(full_source_path);
          else
            check_destination(full_source_path);
        }
      }
    }
  }

    


  class BackupManager
  {
    public List<Backup> backups = new List<Backup>();

    // Encryption related data structures are shared by multiple Backup objects.
    FileNameRegistration file_name_reg;
    EncryptStream encrypt_stream = new EncryptStream();

    // When live backup to cloud happens, files larger than 
    // "cloud_live_backup_max_mb" are skipped.
    public string cloud_live_backup_max_mb = null; // value from XML
    readonly long cloud_live_backup_max_size = 1024 * 1024; // value actually used

    List<CloudBackupService> cloud_backup_services = new List<CloudBackupService>();

    // The backups and restores are done on a separate thread.
    Thread backup_manager_thread;
    AutoResetEvent backup_thread_wait_obj;
    volatile bool quit_flag = false;
    
    ReportEvent_Callback report_event = null;

    // The event queue for the backup_manager_thread. This object is shared between
    // multiple threads, so has the "_sv" suffix.
    Queue<BM_Event> bm_events_sv = new Queue<BM_Event>();

    class BM_Event
    {
      public enum EventType { CHECK_BACKUPS, GET_RESTORE_INFO, RESTORE }
      public readonly EventType event_type;
      public readonly object[] param_array;

      public BM_Event(EventType event_type, params object[] param_array)
      {
        this.event_type = event_type;
        this.param_array = param_array;
      }
    }
    
        
    public BackupManager(XElement xml, FileNameRegistration file_name_reg, 
      List<CloudBackupService> cloud_backup_services,
      KeyManager key_manager,
      ReportEvent_Callback report_event_callback = null)
    {
      this.file_name_reg = file_name_reg;
      this.cloud_backup_services = cloud_backup_services;
      report_event = report_event_callback;

      // backup_thread, backup_thread_wait_obj
      backup_thread_wait_obj = new AutoResetEvent(false);
      backup_manager_thread = new Thread(new ThreadStart(backup_thread_run));
      backup_manager_thread.Name = "BackupManager Thread";
      backup_manager_thread.Start();

      // <BackupManager> \ <cloud_live_backup_max_mb>
      if (xml.Element("cloud_live_backup_max_mb") != null)
      {
        cloud_live_backup_max_mb = xml.Element("cloud_live_backup_max_mb").Value;
        cloud_live_backup_max_size = (long)(float.Parse(cloud_live_backup_max_mb) * 1024 * 1024);
      }
      
      // <BackupManager> \ <backups>
      var backups_tag = xml.Element("backups");

      if (backups_tag != null)
      {
        foreach (var tag in backups_tag.Elements())
        {
          if (tag.Name.LocalName.Equals("EncryptedBackup"))
          {
            backups.Add(new EncryptedBackup(tag, file_name_reg, encrypt_stream,
              cloud_backup_services, key_manager, backup_thread_wait_obj,
              cloud_live_backup_max_size, report_event));
          }
          else if(tag.Name.LocalName.Equals("DiskBackup"))
          {
            backups.Add(new DiskBackup(tag, backup_thread_wait_obj, report_event));
          }
        }
      }

      // check <EncryptedBackup> \ <embedded_prefix> uniqueness
      var existing_prefixes = new HashSet<string>();

      foreach(var backup in backups)
      {
        var encrypted_backup = backup as EncryptedBackup;
        if (encrypted_backup != null)
        {
          if(existing_prefixes.Contains(encrypted_backup.embedded_prefix))
          {
            throw new Exception("XML file error. The backup "
              + encrypted_backup.Name + " is using an embedded_prefix \""
              + encrypted_backup.embedded_prefix + "\" that has been "
              + "previously used.");
          }

          existing_prefixes.Add(encrypted_backup.embedded_prefix);
        }
      }
    }

    /// <summary>
    /// Creates a bare bone object that acts as a default starting point.
    /// </summary>
    public BackupManager()
    {
      backups = new List<Backup>();
      cloud_live_backup_max_mb = "1";
    }

    public XElement to_xml()
    {
      var backup_tag = new XElement("backups");

      foreach (var backup in backups)
        backup_tag.Add(backup.to_xml());

      var manager_tag = new XElement("BackupManager", 
        new XElement("cloud_live_backup_max_mb", cloud_live_backup_max_mb),
        backup_tag);

      return manager_tag;
    }

    /// <summary>
    /// Start all file_system_watcher objects.
    /// </summary>
    public void start_live_backup()
    {
      foreach (var backup in backups)
        backup.start_live_backup();
    }

    /// <summary>
    /// Stop all file_system_watcher objects.
    /// </summary>
    public void stop_live_backup()
    {
      foreach (var backup in backups)
        backup.stop_live_backup();
    }

    /// <summary>
    /// Sets the "quit_flag" so the backup manager thread will quit.
    /// </summary>
    /// <param name="wait">If true, this function will block and wait 
    /// for the backup manager thread to quit.</param>
    public void quit(bool wait = true)
    {
      foreach (var backup in backups)
        backup.stop_live_backup();

      quit_flag = true;

      // The backup_thread_wait_obj == null if the BackupManager object is just 
      // a bare bone default.
      if (backup_thread_wait_obj != null)
        backup_thread_wait_obj.Set();
            
      if (wait)
        backup_manager_thread.Join();

      if (file_name_reg != null) file_name_reg.Dispose();
    }

    /// <summary>
    /// This thread is awaken via backup_thread_wait_obj. It will handle
    /// events in bm_events_sv.
    /// </summary>
    void backup_thread_run()
    {
      try
      {
        while (quit_flag == false)
        {
          // Thread is asleep. Wait for activity.
          report_event(AppEventType.BM_THREAD_IDLE, null);
          backup_thread_wait_obj.WaitOne();

          report_event(AppEventType.BM_THREAD_RUNNING, null);

          if (quit_flag) return;

          // Handle events
          int num_events = 0;

          lock (bm_events_sv)
          {
            num_events = bm_events_sv.Count;
          }
          
          if(num_events == 0)
          {
            // No event to handle. So the thread is awaken to do live_backup().
            backup_thread_wait_for_disk_idle(5); // 5 second wait

            // call live_backup()
            foreach (var backup in backups)
              backup.live_backup();
          }
          else
          {
            // Handle all events.
            backup_thread_handle_events();
          }

          if (quit_flag) return;
        }
      }
      catch (Exception ex)
      {
        if (report_event != null)
          report_event(AppEventType.ERROR, ex.Message);

        file_name_reg.Flush();
        stop_live_backup();
      }
    }

    /// <summary>
    /// Wait for disk_backup.RecentActivity to be false.
    /// </summary>
    void backup_thread_wait_for_disk_idle(int idle_time_in_sec)
    {      
      int idle_time_so_far = 0; // idle time in seconds
      while (idle_time_so_far < idle_time_in_sec)
      {
        Thread.Sleep(1000);
        if (quit_flag) return;

        // check for disk activity
        bool idle = true;
        foreach (var backup in backups)
        {
          if (backup.RecentActivity)
            idle = false;
          // don't break right away - need to call RecentActivity
          // on all disk_backup to clear the recent_activity flags
        }

        // modify idle_time_so_far base on disk activity
        if (idle) idle_time_so_far++;
        else idle_time_so_far = 0;
      }
    }

    /// <summary>
    /// Process events in bm_events_sv.
    /// </summary>
    void backup_thread_handle_events()
    {
      BM_Event event0 = null;

      do
      {
        if (quit_flag) return;

        lock (bm_events_sv)
        {
          if (bm_events_sv.Count > 0) event0 = bm_events_sv.Dequeue();
          else event0 = null;
        }

        if(event0 != null)
        {
          if (event0.event_type == BM_Event.EventType.CHECK_BACKUPS)
          {
            if (report_event != null)
              report_event(AppEventType.LOG, "Starting to check all backups.");

            foreach (var backup in backups)
            {
              backup.check_all_backups();
              if (quit_flag) return;
            }

            if (report_event != null)
              report_event(AppEventType.CHECK_BACKUPS_DONE, null);

            // restart all live back ups
            foreach (var backup in backups)
              backup.start_live_backup();
          }
          else if(event0.event_type == BM_Event.EventType.GET_RESTORE_INFO)
          {
            RestoreManager restore_manager = (RestoreManager)event0.param_array[0];
            int[] indices = (int[])event0.param_array[1];
            bool skip_file_names = (bool)event0.param_array[2];

            var restore_info = restore_manager.get_info(indices, skip_file_names);

            if (report_event != null)
              report_event(AppEventType.GET_RESTORE_INFO_DONE, restore_info);
          }
          else if (event0.event_type == BM_Event.EventType.RESTORE)
          {
            RestoreManager restore_manager = (RestoreManager)event0.param_array[0];
            RestoreSettings settings = (RestoreSettings)event0.param_array[1];

            restore_manager.restore(settings);

            if (report_event != null)
              report_event(AppEventType.RESTORE_DONE, null);
          }
        }

      } while (event0 != null);
    }

    /// <summary>
    /// Go through all disk backup files and make sure all files are truly
    /// backup.
    /// </summary>
    public void check_all_backups()
    {
      // stop all live back ups
      foreach (var backup in backups)
        backup.stop_live_backup();

      lock(bm_events_sv)
      {
        bm_events_sv.Enqueue(new BM_Event(BM_Event.EventType.CHECK_BACKUPS, null));
      }
      backup_thread_wait_obj.Set();
    }

    /// <summary>
    /// Runs restore_manager.get_info(...) on backup thread. It's assumed that 
    /// the GUI has already stopped live_backup().
    /// </summary>
    /// <param name="indices">The objects in restore_manager to obtain info from.</param>
    /// <param name="skipo_file_names">Defaults to true to save memory.</param>
    public void get_restore_info(RestoreManager restore_manager, int[] indices,
      bool skip_file_names = true)
    {
      lock (bm_events_sv)
      {
        bm_events_sv.Enqueue(
          new BM_Event(BM_Event.EventType.GET_RESTORE_INFO,
            restore_manager, indices, skip_file_names));
      }
      backup_thread_wait_obj.Set();
    }

    /// <summary>
    /// Runs restore_manager.restore(...) on backup thread. It's assumed that 
    /// the GUI has already stopped live_backup().
    /// </summary>
    public void restore(RestoreManager restore_manager, RestoreSettings settings)
    {
      lock (bm_events_sv)
      {
        bm_events_sv.Enqueue(
          new BM_Event(BM_Event.EventType.RESTORE, restore_manager, settings));
      }
      backup_thread_wait_obj.Set();
    }

  }


}
