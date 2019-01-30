using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using System.Xml.Linq; // for XML


namespace WindowsBackup
{
  class RestoreInfo
  {
    public long total_file_size = 0;

    /// <summary>
    /// This is the first directory of the
    /// relative path embedded inside the encrypted file.
    /// </summary>
    public HashSet<string> embedded_prefixes = new HashSet<string>();

    /// <summary>
    /// A list of all the file names, grouped by the embedded_prefix.
    /// This might not be filled out, so to save memory.
    /// </summary>
    public Dictionary<string, List<string>> file_names = new Dictionary<string, List<string>>();

    /// <summary>
    /// Prints a summary.
    /// </summary>
    /// <returns></returns>
    public override string ToString()
    {
      var sb = new StringBuilder();
      sb.AppendLine("Total file size: " + total_file_size.ToString("G3"));
      sb.Append("Embedded prefixes: ");
      foreach (var prefix in embedded_prefixes)
        sb.Append(prefix + " ");
      sb.AppendLine("\n");

      if (file_names != null && file_names.Count > 0)
      {
        sb.AppendLine("File names:");
        foreach (var embedded_prefix in file_names.Keys)
        {
          sb.AppendLine(embedded_prefix + Path.DirectorySeparatorChar);
          var names = file_names[embedded_prefix];
          foreach (var name in names)
            sb.AppendLine("    " + name);
        }
        sb.AppendLine("\n");
      }

      return sb.ToString();
    }
  }

  class RestoreSettings
  {
    /// <summary>
    /// Required parameter. This represents which restore objects to restore.
    /// </summary>
    public int[] indices = null;

    /// <summary>
    /// If non-null, then restore files will go to this directory. Either 
    /// "restore_destination_base" or "restore_destination_lookup" is required.
    /// </summary>
    public string restore_destination_base = null;

    /// <summary>
    /// If non-null, then restore destination is determined by inserting
    /// embedded_prefix into this dictionary. Either "restore_destination_base" 
    /// or "restore_destination_lookup" is required.
    /// </summary>
    public Dictionary<string, string> restore_destination_lookup = null;

    /// <summary>
    /// Optional parameter. If non-null, then only destinations 
    /// that satisfy this set of prefixes will get restored.
    /// </summary>
    public List<string> prefix_filter = null;

    /// <summary>
    /// Required parameter. All restored files will be 
    /// registered using this file name registration object.
    /// </summary>
    public FileNameRegistration file_name_reg = null;
  }

  class Restore
  {
    public readonly string backup_destination_base, backup_destination_name;
    CloudBackupService cloud_backup = null;
    string[] file_prefixes;

    // Decryption data structures
    DecryptStream decrypt_stream;
    KeyManager key_manager;

    // **********************************************************
    // information obtained by the handlers that gets passed 
    // into decrypt_stream.reset(...)
    string relative_path = null;
    DateTime? file_modified_time = null;
    long file_pre_encrypt_size = 0;
    long total_file_length = 0;

    // Flag that sets when the handler gets called
    bool handler_called = false;

    // The following represent the decision made inside restore_handler(...)
    bool continue_download = false;
    string restore_destination = null;

    // Restore configuration:
    // Only destinations with specific prefixes get restored.
    List<string> prefix_filter; // if null, then it's not in use

    // Only one of the following two items should be in use (non-null).
    // (1) All restores go to the same destination
    string restore_destination_base = null; 
    
    // (2) Look up the destination using the embedded prefix
    Dictionary<string, string> restore_destination_lookup = null;

    // Callback to provide output for the end user.
    ReportEvent_Callback report_event = null;

    readonly char sep = Path.DirectorySeparatorChar;


    public Restore(XElement xml,
      List<CloudBackupService> cloud_backup_services, 
      DecryptStream decrypt_stream,
      KeyManager key_manager,    
      ReportEvent_Callback report_event = null)
    {
      this.decrypt_stream = decrypt_stream;
      this.key_manager = key_manager;
      this.report_event = report_event;

      // <destination_name>
      backup_destination_name = xml.Element("destination_name").Value;
      
      if (backup_destination_name.Equals("disk") == false)
      {
        // search for the destination in cloud_backup_services
        foreach (var cloud_backup in cloud_backup_services)
        {
          if (backup_destination_name.Equals(cloud_backup.Name))
          {
            this.cloud_backup = cloud_backup;
            break;
          }
        }
        if (cloud_backup == null)
          throw new Exception("XML file error. While processing <RestoreManager> "
            + "there is a <restore> with destination_name \"" 
            + backup_destination_name + "\" that refers to an unknown cloud backup service.");
      }

      // <destination_path>, <file_prefixes>
      backup_destination_base = xml.Element("destination_path").Value;
      file_prefixes = xml.Element("file_prefixes").Value.Trim().Split(' ');
    }

    /// <summary>
    /// Creates a basic Restore object that is NOT usable, but will 
    /// generate the proper XML when .to_xml() is called.
    /// This is meant to be a starting point for GUI based configuration.
    /// </summary>
    /// <param name="file_prefixes_str">This is file_prefixes as a single string, 
    /// separated by spaces.</param>
    public Restore(string backup_destination_name, string destination_path, 
      string file_prefixes_str)
    {
      this.backup_destination_name = backup_destination_name;
      backup_destination_base = destination_path;
      file_prefixes = file_prefixes_str.Trim().Split(' ');
    }

    public XElement to_xml()
    {
      var file_prefixes_str = String.Join(" ", file_prefixes);

      return new XElement("restore",
        new XElement("destination_name", backup_destination_name),
        new XElement("destination_path", backup_destination_base),
        new XElement("file_prefixes", file_prefixes_str)
        );
    }


    /// <summary>
    /// Goes over every file in this directory / bucket and download its first 1kB 
    /// to get information. Accumulate data in the restore_info object.
    /// </summary>
    /// <param name="restore_info">The "file_names" field can be null, which causes
    /// file names to not get recorded. This will save memory.</param>
    public void get_info(RestoreInfo restore_info)
    {
      // Get a list of alt_file_names. These look like "a1000.bin"
      string[] alt_file_names = get_alt_file_names();

      // To get information, it's only necessary to read the early part of the file.
      const int bytes_to_read = 1024;
      byte[] buffer = new byte[bytes_to_read];

      int files_processed = 0; // report progress to the GUI

      // For each alt_file_name, extract info
      foreach (var alt_file_name in alt_file_names)
      {
        // Check alt_file_name format. Work only on files that has the right format.
        string prefix = null;
        UInt32 id;
        break_down_alt_file_name(alt_file_name, ref prefix, out id);
        
        if (prefix != null)
        {
          // Set up before getting the first bytes.
          decrypt_stream.reset(null, get_info_handler);
          handler_called = false;

          // Read the early bytes.
          if (cloud_backup == null)
          {
            using (var fs = new FileStream(alt_file_name, FileMode.Open, FileAccess.Read))
            {
              int bytes_read = fs.Read(buffer, 0, bytes_to_read);
              decrypt_stream.Write(buffer, 0, bytes_read);
            }
          }
          else
            cloud_backup.download(backup_destination_base, alt_file_name, 
              decrypt_stream, 0, bytes_to_read);

          // At this point "handler_called" should have happened.
          if (handler_called == false)
            throw new Exception("Restore file error. While examining the file at \""
              + backup_destination_name + "\\" + backup_destination_base + "\\" + alt_file_name
              + "\", failed to determine the true file name after reading the first "
              + bytes_to_read + " bytes.");

          // At this point, the get_info_handler(...) handler has updated 
          // relative_path and file_pre_encrypt_size.
          restore_info.total_file_size += total_file_length;

          // Extract embedded_prefix from relative_path.
          int index = relative_path.IndexOf('\\');
          
          // If index == -1, this file has no prefix. This should be an error.
          // But throwing exception aborts the restore. So continue for now.
          // Just ignore this file.

          if (index > 0)
          {
            string embedded_prefix = relative_path.Substring(0, index);
            restore_info.embedded_prefixes.Add(embedded_prefix);

            // The file_names is optional, so test for null.
            if (restore_info.file_names != null)
            {
              if (restore_info.file_names.ContainsKey(embedded_prefix) == false)
                restore_info.file_names.Add(embedded_prefix, new List<string>());

              restore_info.file_names[embedded_prefix].Add(relative_path.Substring(index + 1));
            }

            // report progress to the user
            files_processed++;
            if (report_event != null)
            {
              if (files_processed == 10)
              {
                report_event(AppEventType.FILES_PROCESSED, files_processed);
                files_processed = 0;
              }                
            }
          }
        }
      } // end of: foreach (var alt_file_name in alt_file_names)

      if (report_event != null)
      {
        report_event(AppEventType.FILES_PROCESSED, files_processed);
        files_processed = 0;
      }
    }

    /// <summary>
    /// This is used as the decrypt_stream.reset(...) callback handler. It will 
    /// gather information on the restore archive.
    /// </summary>
    /// <returns>Returns null since this is information gathering only,
    /// not the real download.</returns>
    string get_info_handler(string relative_path, DateTime? file_modified_time, 
      long file_pre_encrypt_size, long total_file_length)
    {
      handler_called = true;
      this.relative_path = relative_path;
      this.file_modified_time = file_modified_time;
      this.file_pre_encrypt_size = file_pre_encrypt_size;
      this.total_file_length = total_file_length;
      return null;
    }

    /// <summary>
    /// Returns the alternative file names, which looks like "a1000.bin".
    /// If the restore is on a disk, then the full path is returned. If 
    /// the restore is in the cloud, only the file name is returned.
    /// </summary>
    string[] get_alt_file_names()
    {
      if (cloud_backup == null)
        return Directory.GetFiles(backup_destination_base);
      else
        return cloud_backup.list_objects(backup_destination_base, null).ToArray();
    }

    /// <summary>
    /// Break alt_file_name, such as "a1000.bin", into prefix "a" and id "1000".
    /// </summary>
    /// <param name="prefix">If conversion fails, the prefix = null.</param>
    /// <param name="id">If conversion fails, the id = 0.</param>
    void break_down_alt_file_name(string alt_file_name, ref string prefix, out UInt32 id)
    {
      prefix = null;
      id = 0;

      // For cloud backup, the alt_file_name looks like "a1000.bin".
      // But for disk backup, the alt_file_name is a full path, 
      // like "blah\blah\a1000.bin". So need to extract just 
      // the file name part.
      string alt_file_name2 = alt_file_name;
      if (cloud_backup == null)
        alt_file_name2 = Path.GetFileName(alt_file_name);

      foreach (var prefix2 in file_prefixes)
      {
        if (alt_file_name2.StartsWith(prefix2))
        {
          // currently, the ".bin" ending is not enforced here
          int index = alt_file_name2.LastIndexOf('.');
          int length = index - prefix2.Length;
          string number_str = alt_file_name2.Substring(prefix2.Length, length);

          bool success = UInt32.TryParse(number_str, out id);
          if (success)
          {
            prefix = prefix2;
            return;
          }          
        }
      }
    }


    public void restore(string restore_destination_base,
      Dictionary<string, string> restore_destination_lookup,
      List<string> prefix_filter, FileNameRegistration file_name_reg)
    {
      // The following variables are not used directly in this function,
      // but they are used in restore_handler(...)
      this.restore_destination_base = restore_destination_base;
      this.restore_destination_lookup = restore_destination_lookup;
      this.prefix_filter = prefix_filter;

      // Get a list of alt_file_names. These look like "a1000.bin"
      string[] alt_file_names = get_alt_file_names();

      // Each file restore happens in two steps. In the first step, only the
      // start of a file is read / downloaded. 
      const int starting_bytes = 1024;
      byte[] buffer = new byte[starting_bytes];

      int files_processed = 0; // report progress to the GUI

      // loop over all files
      foreach (var alt_file_name in alt_file_names)
      {
        // identify the file's prefix and file ID
        string prefix = null;
        UInt32 id;
        break_down_alt_file_name(alt_file_name, ref prefix, out id);

        if (prefix == null) continue;

        // setup for the decryption
        decrypt_stream.reset(null, restore_handler);
        handler_called = false;
        file_modified_time = null;
        continue_download = false;
        restore_destination = null;

        if (cloud_backup == null)
        {
          using (var fs = new FileStream(alt_file_name, FileMode.Open, FileAccess.Read))
          {
            // decrypt from alt_file_name
            int bytes_read = fs.Read(buffer, 0, starting_bytes);
            decrypt_stream.Write(buffer, 0, bytes_read);

            if (continue_download)
            {
              fs.CopyTo(decrypt_stream);
              decrypt_stream.Flush();
            }
          }
        }
        else
        {
          // download from the cloud
          cloud_backup.download(backup_destination_base, alt_file_name,
                  decrypt_stream, 0, starting_bytes);

          if (continue_download)
          {
            // The following does not work for GCP:
            // cloud_backup.download(backup_destination_base, alt_file_name,
            //      decrypt_stream, starting_bytes, file_pre_encrypt_size);

            // GCP error: Request range not satisfiable
            // GCP seems to require the starting byte location of the
            // download to be valid.

            if (total_file_length > starting_bytes)
            {
              cloud_backup.download(backup_destination_base, alt_file_name,
                    decrypt_stream, starting_bytes,
                    total_file_length - starting_bytes);
            }
            
            decrypt_stream.Flush();
          }
        }

        // check for "handler_called" errors?
        // if (handler_called == false) ??
        // This kind of error should have been filtered out when the GUI used
        // get_info(...). An exception now will interrupt the restore
        // operation. So for now just quietly skip the file??

        // Change file modified time to what it use to be.
        if (continue_download && file_modified_time != null)
          File.SetLastWriteTimeUtc(restore_destination, file_modified_time.Value);

        // Update file_name_reg.
        if (continue_download)
        {
          long file_mod_time_ticks = 0;
          if (file_modified_time != null) file_mod_time_ticks = file_modified_time.Value.Ticks;

          file_name_reg.add_file(restore_destination, prefix, id, file_mod_time_ticks);

          // report progress to the user
          files_processed++;
          if (report_event != null)
          {
            if (files_processed == 5)
            {
              report_event(AppEventType.FILES_PROCESSED, files_processed);
              files_processed = 0;
            }
          }
        }
      } // end of: foreach (var alt_file_name in alt_file_names)

      if (report_event != null)
      {
        report_event(AppEventType.FILES_PROCESSED, files_processed);
        files_processed = 0;
      }
    }

    public string restore_handler(string relative_path, DateTime? file_modified_time, 
      long file_pre_encrypt_size, long total_file_length)
    {
      handler_called = true;
      this.relative_path = relative_path;
      this.file_modified_time = file_modified_time;
      this.file_pre_encrypt_size = file_pre_encrypt_size;
      this.total_file_length = total_file_length;

      restore_destination = null;
      continue_download = false; 

      // Determine the restore location. There are two cases.
      // (1) All restores go to the same destination --> restore_destination_base
      if (restore_destination_base != null)
        restore_destination = restore_destination_base + sep + relative_path;

      // (2) Look up the destination using the embedded prefix
      else
      {
        int index = relative_path.IndexOf(sep);
        string embdded_prefix = relative_path.Substring(0, index);

        // In the GUI, the user can leave the mapping for a prefix blank to
        // avoid restoring a particular set of files.
        if (restore_destination_lookup.ContainsKey(embdded_prefix) == false)
          return null;

        string base_path = restore_destination_lookup[embdded_prefix];
        restore_destination = base_path + relative_path.Substring(index);
      }

      // If the destination exists, check the file modified time
      if (File.Exists(restore_destination) && file_modified_time != null)
      {
        DateTime disk_mod_time = File.GetLastWriteTimeUtc(restore_destination);
        if (disk_mod_time >= file_modified_time.Value)
          return null;
      }

      // If prefix_filter is being used, check it
      if (prefix_filter != null)
      {
        foreach (var prefix in prefix_filter)
        {
          if (restore_destination.StartsWith(prefix))
          {
            continue_download = true;
            return restore_destination; // prefix test passed
          }
        }
        // If the code gets here, that means none of the prefixes in "prefix_filter" matched
        return null;
      }
      else
      {
        // prefix_filter == null means no prefix check.
        continue_download = true;
        return restore_destination;
      }
    }
  }

  class RestoreManager
  {
    /// <summary>
    /// Stores the original source of the embedded_prefixes. These
    /// are used as defaults, causing the restore to put files
    /// back to their original directories.
    /// </summary>
    public Dictionary<string, string> default_destinations = new Dictionary<string, string>();

    List<Restore> restores = new List<Restore>();
    
    public RestoreManager(XElement xml, 
      List<CloudBackupService> cloud_backup_services,
      KeyManager key_manager, ReportEvent_Callback report_event = null)
    {
      // <default_destinations>
      var default_destinations_tag = xml.Element("default_destinations");
      if (default_destinations_tag != null)
      {
        foreach(var tag in default_destinations_tag.Elements("default_destination"))
        {
          string prefix = tag.Attribute("embedded_prefix").Value;
          string destination = tag.Attribute("destination").Value;

          if (default_destinations.ContainsKey(prefix))
            throw new Exception("XML file error. The <RestoreManager> contain "
              + "an <default_destination> with the embedded_prefix \"" 
              + prefix + "\", which has already been previously defined.");

          default_destinations.Add(prefix, destination);
        }
      }

      // All restore objects use the same decrypt_stream.
      DecryptStream decrypt_stream = new DecryptStream(key_manager);

      // <restores>
      var restores_tag = xml.Element("restores");
      if (restores_tag != null)
      {
        foreach (var tag in restores_tag.Elements("restore"))
          restores.Add(new Restore(tag, cloud_backup_services, 
            decrypt_stream, key_manager, report_event));
      }
    }

    /// <summary>
    /// Non usable object that can generate the correct XML when .xml() is
    /// called. This is meant as a starting point for the GUI.
    /// </summary>
    public RestoreManager() { }

    /// <summary>
    /// Adds a restore object to the internal "restores" list".
    /// </summary>
    public void add_restore(Restore restore)
    {
      restores.Add(restore);
    }

    /// <summary>
    /// Sets the destination for an embedded prefix.
    /// </summary>
    public void set_default_destination(string embedded_prefix, string destination)
    {
      default_destinations[embedded_prefix] = destination;
    }

    public XElement to_xml()
    {
      // <default_destinations>
      var default_destinations_tag = new XElement("default_destinations");
      foreach (var prefix in default_destinations.Keys)
      {
        var default_destination_tag = new XElement("default_destination");
        default_destination_tag.SetAttributeValue("embedded_prefix", prefix);
        default_destination_tag.SetAttributeValue("destination", default_destinations[prefix]);

        default_destinations_tag.Add(default_destination_tag);
      }

      // <restores>
      var restores_tag = new XElement("restores");
      foreach(var restore in restores)
        restores_tag.Add(restore.to_xml());

      return new XElement("RestoreManager", default_destinations_tag, restores_tag);
    }

    /// <summary>
    /// Returns string representation of internal List of Restore objects.
    /// </summary>
    public string[] get_restore_names()
    {
      string[] names = new string[restores.Count];
      for(int i = 0; i < restores.Count; i++)
      {
        if (restores[i].backup_destination_name.Equals("disk"))
          names[i] = restores[i].backup_destination_base;
        else
          names[i] = restores[i].backup_destination_name + Path.DirectorySeparatorChar 
            + restores[i].backup_destination_base;
      }
      return names;
    }


    /// <summary>
    /// Goes over every file in a set of restore objects and obtain information. 
    /// </summary>
    /// <param name="indices">The "indices" field describes which restore
    /// object to collect information on.</param>
    /// <param name="skip_file_names">Defaults to true, so to not retrieve file 
    /// names in order to save memory.</param>
    public RestoreInfo get_info(int[] indices, bool skip_file_names = true)
    {
      RestoreInfo restore_info = new RestoreInfo();

      if (skip_file_names) restore_info.file_names = null;
      
      foreach (int i in indices)
        restores[i].get_info(restore_info);

      return restore_info;
    }

    /// <summary>
    /// Go over restore objects and restore their files.
    /// </summary>
    public void restore(RestoreSettings settings)
    {
      int[] indices = settings.indices;

      foreach (var i in indices)
        restores[i].restore(settings.restore_destination_base,
            settings.restore_destination_lookup,
            settings.prefix_filter, settings.file_name_reg);
    }

    /// <summary>
    /// Returns true if a Restore object with the same destination information
    /// already exists.
    /// </summary>
    public bool does_restore_exist(string backup_destination_base, 
      string backup_destination_name)
    {
      foreach(var restore in restores)
      {
        if (restore.backup_destination_base.Equals(backup_destination_base)
          && restore.backup_destination_name.Equals(backup_destination_name))
          return true;
      }

      return false;
    }
  }
}
