using System;
using System.IO;
using System.Text;

using System.Collections.Generic;
using System.Xml.Linq; // for XML



namespace WindowsBackup
{
  class FileNameRegistration
  {
    class TreeStatus
    {
      // The highest ID values in use
      public uint highest_file_id = 999; // first file will be 1000

      // Track how many of the lines are deleted lines
      public uint real_lines = 0;
      public uint deleted_lines = 0;
      
      /// <summary>
      /// Look at the newly provided ID and if they are higher, 
      /// then record these as the highest IDs in use.
      /// </summary>
      public void check_for_higher_ids(uint file_id)
      {
        if (file_id > highest_file_id) highest_file_id = file_id;
      }
    }

    public class PathStatus
    {
      public bool is_file = false;
      public string alt_file_name = null; 
      public DateTime? modified_time = null;
      // alt_file_name and modified_time are valid only if is_file == true
    }

    class DirectoryNode : IComparable<DirectoryNode>
    {
      public readonly string name;

      // Children of the directory.
      // Pre-allocated - waste some space, but makes coding easier.
      SortedList<string, DirectoryNode> dir_children = new SortedList<string, DirectoryNode>();
      SortedList<string, FileNode> file_children = new SortedList<string, FileNode>();

      public int CompareTo(DirectoryNode other)
      {
        return name.CompareTo(other.name);
      }
      
      public DirectoryNode(string name)
      {
        this.name = name;
      }

      /// <summary>
      /// Return information about the path. If no such path exist, return null. 
      /// The start_index marks the relative path currently being processed.
      /// </summary>
      public PathStatus get_path_status(string path, int start_index)
      {
        int end_index = path.IndexOf(Path.DirectorySeparatorChar, start_index);

        if (end_index > start_index)
        {
          // Current node is not the final node. It is a directory.
          string node_name = path.Substring(start_index, (end_index - start_index));

          DirectoryNode node = null;
          bool found = dir_children.TryGetValue(node_name, out node);

          if (found)
            return node.get_path_status(path, end_index + 1);          
          else
            return null;          
        }
        else
        {
          // Current node is the final node. It can be a file or a directory.
          string node_name = path.Substring(start_index);

          DirectoryNode node = null;
          bool found = dir_children.TryGetValue(node_name, out node);
          if (found)
          {
            // Current node is a directory.
            var status = new PathStatus();
            status.is_file = false;
            return status;
          }
          else
          {
            FileNode file_node = null;
            found = file_children.TryGetValue(node_name, out file_node);
            if (found)
            {
              // Current node is a file.
              var status = new PathStatus();
              status.is_file = true;
              status.modified_time = file_node.get_modified_time();
              status.alt_file_name = file_node.get_alt_file_name();

              return status;
            }
            else
              return null; // Current node not found.
          }
        }
      }

      /// <summary>
      /// Returns the list of sub directories, files in the directory, 
      /// and alternative file names. Empty lists are set as null.
      /// </summary>
      public void get_names(ref List<string> sub_dir_names,
        ref List<string> file_names, ref List<string> alt_file_names)
      {
        sub_dir_names = null;
        file_names = null;
        alt_file_names = null;

        if (dir_children.Count > 0)
        {
          sub_dir_names = new List<string>();
          foreach (var dir in dir_children.Values)
            sub_dir_names.Add(dir.name);
        }

        if (file_children.Count > 0)
        {
          file_names = new List<string>();
          alt_file_names = new List<string>();

          foreach (var file in file_children.Values)
          {
            file_names.Add(file.name);
            alt_file_names.Add(file.get_alt_file_name());
          }
        }
      }

      /// <summary>
      /// Retrieve the directory node pointed to by path. Returns null if no node 
      /// is found. The start_index marks the relative path currently being processed.
      /// </summary>
      public DirectoryNode get_dir_node(string path, int start_index)
      {
        int end_index = path.IndexOf(Path.DirectorySeparatorChar, start_index);
        string node_name = null;

        if (end_index < 0) node_name = path.Substring(start_index);
        else node_name = path.Substring(start_index, (end_index - start_index));

        DirectoryNode node = null;
        bool found = dir_children.TryGetValue(node_name, out node);

        if (found)
        {
          if (end_index < 0) return node; // node found
          else return node.get_dir_node(path, end_index + 1); // continue search
        }
        else
          return null; // path not found
      }

      /// <summary>
      /// Retrieve the file node pointed to by path. Returns null if no node is 
      /// found. The start_index marks the relative path currently being processed.
      /// </summary>
      public FileNode get_file_node(string path, int start_index)
      {
        int end_index = path.IndexOf(Path.DirectorySeparatorChar, start_index);

        if (end_index < 0)
        {
          // Current node is the final node.
          string node_name = path.Substring(start_index);

          FileNode node = null;
          bool found = file_children.TryGetValue(node_name, out node);

          if (found) return node; // File found.
          else return null; // File not found.
        }
        else
        {
          // Current node is a directory.
          string node_name = path.Substring(start_index, (end_index - start_index));

          DirectoryNode node = null;
          bool found = dir_children.TryGetValue(node_name, out node);

          if (found) return node.get_file_node(path, end_index + 1); // Continue the search.
          else return null; // Directory not found.
        }
      }

      /// <summary>
      /// Attach a new file node to the given path. New directory node will be created
      /// as needed. Will throw an exception if the file node already exists.
      /// </summary>
      /// <param name="file_path">This path includes the file name.</param>
      /// <param name="start_index">The start_index marks the relative path 
      /// currently being processed.</param>
      /// <param name="file_node"></param>
      public void attach_new_file_node(string file_path, int start_index, FileNode file_node)
      {
        int end_index = file_path.IndexOf(Path.DirectorySeparatorChar, start_index);

        if (end_index < 0)
        {
          // Attach file_node to this directory node.
          // Check for existing file.
          if (file_children.ContainsKey(file_node.name))
            throw new Exception("Software error. The software is trying to create a new "
              + "file at " + file_node + ", but the same file already exists.");

          file_children.Add(file_node.name, file_node);
        }
        else
        {
          // Ask the next directory node to attempt the file attach.
          string node_name = file_path.Substring(start_index, (end_index - start_index));

          DirectoryNode node = null;
          bool found = dir_children.TryGetValue(node_name, out node);

          if (found == false)
          {
            // The next directory node does not exist.
            // Create new directory node and have it attempt the file attach.
            node = new DirectoryNode(node_name);
            dir_children.Add(node_name, node);
          }

          node.attach_new_file_node(file_path, end_index + 1, file_node);
        }
      }

      /// <summary>
      /// Print the current node, and all children. Precede the name using
      /// "spaces". Prints to the screen if stream_writer is null.
      /// </summary>
      public void print(int spaces, StreamWriter stream_writer = null)
      {
        // print name of this directory
        string dir_name = new String(' ', spaces) + name;

        if (stream_writer != null) stream_writer.WriteLine(dir_name);
        else Console.WriteLine(dir_name);
        
        var sb = new StringBuilder();

        // print files in this directory
        foreach (var file in file_children.Values)
        {
          sb.Clear();
          sb.Append(new String(' ', spaces + 2) + file.name);
          sb.Append(" (" + file.get_alt_file_name());
          
          DateTime? modified_time = file.get_modified_time();
          if (modified_time != null)
            sb.Append(", modified: " + modified_time.Value + ")");
          else
            sb.Append(")");

          sb.Append("\n");

          if (stream_writer != null) stream_writer.Write(sb.ToString());
          else Console.Write(sb.ToString());
        }

        // print sub-directories, and their children
        foreach (var dir in dir_children.Values)
          dir.print(spaces + 2, stream_writer);
      }
      
      /// <summary>
      /// Delete all children, both file and directory, for this node.
      /// </summary>
      void delete_all_children()
      {
        foreach (var file_node in file_children.Values)
          file_node.delete_from_disk();

        file_children.Clear();

        foreach (var dir_node in dir_children.Values)
          dir_node.delete_all_children();

        dir_children.Clear();
      }

      /// <summary>
      /// Delete a particular path. This might be a file or a directory.
      /// </summary>
      /// /// <param name="start_index">The start_index marks the relative path 
      /// currently being processed.</param>
      public void delete(string path, int start_index)
      {
        int end_index = path.IndexOf(Path.DirectorySeparatorChar, start_index);

        if (end_index < 0)
        {
          // This is the final node. Delete this node.
          string node_name = path.Substring(start_index);

          FileNode node = null;
          bool found = file_children.TryGetValue(node_name, out node);

          if (found)
          {
            node.delete_from_disk();
            file_children.Remove(node.name);
            return;
          }

          DirectoryNode dir_node = null;
          found = dir_children.TryGetValue(node_name, out dir_node);
          if (found)
          {
            dir_node.delete_all_children();
            dir_children.Remove(dir_node.name);
            return;
          }
        }
        else
        {
          // This is not the final node. Pass the delete request on.
          string node_name = path.Substring(start_index, (end_index - start_index));

          DirectoryNode node = null;
          bool found = dir_children.TryGetValue(node_name, out node);

          if (found)
            node.delete(path, end_index + 1);
        }

        // for now do nothing if path is not found??
        // alternatively throw an exception?
      }

      /// <summary>
      /// Rewrite all children, both file and directory nodes, to disk.
      /// A new_file_stream is provided - the old one is not usable.
      /// </summary>
      /// <param name="path_so_far">The path_so_far includes the name of the current
      /// directory.</param>
      public void rewrite_children_to_disk(FileStream new_file_stream, string path_so_far)
      {
        // Write all files in this directory to disk.
        foreach (var file_node in file_children.Values)
        {
          file_node.update_file_stream(new_file_stream);
          file_node.append_to_disk(path_so_far + Path.DirectorySeparatorChar + file_node.name);
        }

        // Then do the same to sub directories.
        foreach (var dir_node in dir_children.Values)
        {
          if (path_so_far.Length == 0)
          {
            // Current node is the root node
            // Special case, next path_so_far = name
            dir_node.rewrite_children_to_disk(new_file_stream, dir_node.name);
          }
          else
          {
            // Standard case, next path_so_far = path_so_far + \ + name
            dir_node.rewrite_children_to_disk(new_file_stream,
              path_so_far + Path.DirectorySeparatorChar + dir_node.name);
          }
        }
      }
      
    }

    class FileNode : IComparable<FileNode>
    {
      public readonly string name; // real name of the file

      // alternative name of the file = prefix + file_id
      public readonly string prefix;
      public readonly uint file_id;

      public string get_alt_file_name() { return prefix + file_id + ".bin"; }

      // Disk access
      FileStream file_stream;
      long file_offset;

      public void update_file_stream(FileStream file_stream)
      {
        this.file_stream = file_stream;
      }

      // modified_time
      DateTime? modified_time = null;
      public DateTime? get_modified_time() { return modified_time; }
          
      /// <summary>
      /// Not only sets the modified_time in memory, but also writes
      /// to disk as well.
      /// </summary>
      public void set_modified_time(DateTime? modified_time)
      {
        this.modified_time = modified_time;
        
        file_stream.Seek(file_offset, SeekOrigin.Begin);

        write_payload_to_disk();
        file_stream.Flush();
      }

      public int CompareTo(FileNode other)
      {
        return name.CompareTo(other.name);
      }

      /// <summary>
      /// Private constructor used by read_FileNode_from_file(...)
      /// </summary>
      public FileNode(string name, string prefix, uint file_id, 
        DateTime? modified_time, FileStream file_stream, long file_offset)
      {
        this.name = name;
        this.prefix = prefix;
        this.file_id = file_id;
        this.modified_time = modified_time;
        this.file_stream = file_stream;
        this.file_offset = file_offset;
      }
      

      // <summary>
      /// Writes the content of this node to disk at the end of the file_stream.
      /// This will also update the "file_offset" member variable.
      /// </summary>
      public void append_to_disk(string full_path)
      {
        // Position the file_stream file pointer.
        file_stream.Seek(0, SeekOrigin.End);

        // Path, tab, prefix, tab
        byte[] buf = Encoding.UTF8.GetBytes(full_path + "\t" + prefix + "\t");
        file_stream.Write(buf, 0, buf.Length);

        // Binary portion of the file starts here.
        file_offset = file_stream.Position;

        write_payload_to_disk(); // [file_id, modified_time] --> disk
        file_stream.Flush();
      }

      /// <summary>
      /// Writes [file_id, modified_time] to disk. The file_stream is assumed
      /// to be properly positioned. This function does not call flush.
      /// </summary>
      void write_payload_to_disk()
      {
        byte[] file_id_bytes = BitConverter.GetBytes(file_id);

        long ticks = 0;
        if (modified_time != null) ticks = modified_time.Value.Ticks;

        byte[] mod_time_bytes = BitConverter.GetBytes(ticks);

        byte[] buf = new byte[12];
        Buffer.BlockCopy(file_id_bytes, 0, buf, 0, 4);
        Buffer.BlockCopy(mod_time_bytes, 0, buf, 4, 8);

        string base64_str = Convert.ToBase64String(buf);
        buf = Encoding.UTF8.GetBytes(base64_str + "\n");

        file_stream.Write(buf, 0, buf.Length);
      }

      /// <summary>
      /// "Delete" this node from the disk by setting its binary payload to zero.
      /// </summary>
      public void delete_from_disk()
      {
        file_stream.Seek(file_offset, SeekOrigin.Begin);

        // Right now the binary payload is 12 bytes long.
        // That is (12 * 8 / 6) = 16 letter "A".
        for (int i = 0; i < 16; i++)
          file_stream.WriteByte(0x41);
      }
    }

    
    TreeStatus tree_status;
    DirectoryNode root;
    
    FileStream file_stream; // on disk representation

    public readonly string default_prefix = "a";
    readonly string path = null;
    

    /// <summary>
    /// Creates a FileNameRegistration object, which maps a file path to 
    /// its metadata.
    /// </summary>
    /// <param name="path">The on disk representation of this object.</param>
    public FileNameRegistration(string path = "file_name_reg.bin")
    {
      this.path = path;
      init(path);
    }

    public FileNameRegistration(XElement xml)
    {
      path = xml.Element("path").Value;
      default_prefix = xml.Element("default_prefix").Value;
      init(path);
    }

    public XElement to_xml()
    {
      return new XElement("FileNameRegistration",
        new XElement("default_prefix", default_prefix),
        new XElement("path", path)
        );
    }

    void init(string path)
    {
      tree_status = new TreeStatus();
      root = new DirectoryNode("root");

      if (File.Exists(path))
      {
        populate_tree_from_file(path);
      }
      else
      {
        // File does not exist, create brand new file.
        file_stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
      }
    }

    /// <summary>
    /// Read from file path to create the tree.
    /// </summary>
    void populate_tree_from_file(string path)
    {
      file_stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);

      byte[] buffer = new byte[2048];

      while(true)
      {
        // Process one line per loop

        // read [path, tab, prefix, tab]
        int length = read_until_2nd_tab(buffer);
        if (length < 0) break; // quit due to lack of data

        string path_and_prefix = Encoding.UTF8.GetString(buffer, 0, length);
        string[] tokens = path_and_prefix.Split('\t');
        string file_full_path = tokens[0].Trim();
        string prefix = tokens[1];

        string file_name = Path.GetFileName(file_full_path);

        // Next 12 bytes is the payload. This is base64 encoded as 16 bytes.
        long offset = file_stream.Position;
        const int payload_length = 16; 

        length = file_stream.Read(buffer, 0, payload_length);
        if (length < 12)
          throw new Exception("File name registration error. While reading "
            + "in the file from disk, found the first 2 tabs of a line, "
            + "but cannot read the next 12 byte pay load.");

        string payload_str = Encoding.UTF8.GetString(buffer, 0, payload_length);
        byte[] payload_bytes = Convert.FromBase64String(payload_str);

        UInt32 file_id = BitConverter.ToUInt32(payload_bytes, 0);
        long ticks = BitConverter.ToInt64(payload_bytes, 4);

        // Next byte should be "\n", decimal 10        
        int new_line_byte = file_stream.ReadByte();
        if (new_line_byte != 10)
        {
          bool new_line_error = true;

          // There is an alternative situation - if a spreadsheet 
          // software has been used to edit the file name registration file,
          // on MS Windows, then the OS newline termination will be 
          // CR LF (decimal 13, 10)
          if (new_line_byte == 13)
          {
            int new_line_byte2 = file_stream.ReadByte();
            if (new_line_byte2 == 10)
              new_line_error = false;
          }

          if (new_line_error)
            throw new Exception("File name registration error. While reading "
              + "in the file name registration file from disk, unable to "
              + "find a newline at the expected end of a line.");
        }

        // Code gets here means successfully processed one line of the file name registration file
        if (file_id != 0)
        {
          // This file node is valid. Attach it to the tree.
          tree_status.check_for_higher_ids(file_id);
          tree_status.real_lines++;

          DateTime? mod_time = null;
          if (ticks != 0) mod_time = new DateTime(ticks);

          // create the file_node object
          var file_node = new FileNode(file_name, prefix, file_id, mod_time, file_stream, offset);
          root.attach_new_file_node(file_full_path, 0, file_node);
        }
        else
        {
          // file_id == 0 means this node is a deleted node.
          tree_status.deleted_lines++;
        }
      }

      // Check for percent of deleted nodes
      if ((float)tree_status.deleted_lines / tree_status.real_lines > 0.10)
        // trigger a compaction process if deleted nodes > 10%
        rewrite_tree_to_disk(path);
    }

    /// <summary>
    /// Attempt to read from file_stream, into buffer, assuming UTF8 encoding.
    /// Keep reading until a second '\t' is seen. Return length of the
    /// string in bytes. Return -1 if failed to find the second tab.
    /// </summary>
    /// <param name="buffer">The buffer has to be allocated by the caller.</param>
    int read_until_2nd_tab(byte[] buffer)
    {
      int num_tabs = 0;
      int index = 0;

      while (num_tabs < 2 && index <= buffer.Length - 4 - 1)
      {
        // -4 so enough bytes to hold one UTF8 character
        // -1 for terminal zero needed at the end for successful extraction

        // Each loop reads one UTF8 character
        // read one byte
        int a_byte = file_stream.ReadByte();
        if (a_byte < 0) return -1;

        buffer[index] = (byte)a_byte;
        index++;

        if (a_byte == 9) num_tabs++;

        // read more bytes if needed
        if (a_byte >= 0x80)
        {
          // this character is multi-byte
          int extra_bytes_to_read = 0;

          if (a_byte >= 0xc0 && a_byte <= 0xdf) extra_bytes_to_read = 1;
          else if (a_byte >= 0xe0 && a_byte <= 0xef) extra_bytes_to_read = 2;
          else if (a_byte >= 0xf0 && a_byte <= 0xf7) extra_bytes_to_read = 3;
          else
            // invalid UTF8 character found
            throw new Exception("File name registration error. The file on disk used "
              + "for file name registration does not appear to be UTF-8 encoded.");

          for(int i = 0; i < extra_bytes_to_read; i++)
          {
            a_byte = file_stream.ReadByte();
            if (a_byte < 0) return -1;

            buffer[index] = (byte)a_byte;
            index++;
          }
        }
      }

      if (num_tabs == 2)
      {
        // successfully located second tab character.
        return index; // At this point index is also the length of  the string.
      }

      // If read a lot of stuff, but cannot find second tab, then it's an error.
      if (index > buffer.Length - 4 - 1)
        throw new Exception("File name registration error. Read "
          + (buffer.Length - 4).ToString() + " bytes from file but cannot "
          + "find the second tab character.");

      return -1; // out of data is okay, not an error.
    }

    /// <summary>
    /// Rewrite the tree structure to a file at location "path".
    /// </summary>
    void rewrite_tree_to_disk(string path)
    {
      // Close current file stream and backup the file.
      file_stream.Dispose();
      File.Copy(path, path + ".old", overwrite: true);

      file_stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
      root.rewrite_children_to_disk(file_stream, "");
      file_stream.Flush();
    }

    public void Dispose()
    {
      if (file_stream != null)
      {
        file_stream.Dispose();
        file_stream = null;
      }
    }

    public void Flush()
    {
      if (file_stream != null) file_stream.Flush();
    }

    /// <summary>
    /// Return information about a given path.
    /// </summary>
    public PathStatus get_path_status(string path)
    {
      return root.get_path_status(path, 0);
    }

    /// <summary>
    /// For a directory path, returns the list of sub directories,
    /// files in the directory, and alternative file names.
    /// Empty lists are set as null.
    /// </summary>
    public void get_names(string dir_path, ref List<string> sub_dir_names, 
      ref List<string> file_names, ref List<string> alt_file_names)
    {
      var dir_node = root.get_dir_node(dir_path, 0);
      if (dir_node != null)
        dir_node.get_names(ref sub_dir_names, ref file_names, ref alt_file_names);
    }

    /// <summary>
    /// Add a new file to the file name registration system.
    /// Returns the alternative file name for the newly added file. 
    /// </summary>
    public string add_file(string file_path)
    {
      var file_name = Path.GetFileName(file_path);

      tree_status.highest_file_id++;
      uint file_id = tree_status.highest_file_id;

      var file_node = new FileNode(file_name, default_prefix, file_id, null, file_stream, 0);
      root.attach_new_file_node(file_path, 0, file_node);
      file_node.append_to_disk(file_path);

      return file_node.get_alt_file_name();
    }

    /// <summary>
    /// Create a new node with a specific prefix, file_id, modified_time. 
    /// This is for restoration situation, when there is a bunch of files 
    /// in cloud storage, but no file name registration system on disk. 
    /// This file_id is assumed to be unique.
    /// </summary>
    public void add_file(string file_path, string prefix, uint file_id, 
      long modified_time_ticks)
    {
      // Since this is meant for restoration use only, so it's important
      // that this file so far does not exist.
      if (root.get_file_node(file_path, 0) != null)
        throw new Exception("Error. Attempting to specify the file_id of a "
          + "file that is already present in the file name registration system.");

      // Although the above code checks to see that this particular file does not 
      // already exist, there is currently no check on whether the (prefix + file_id)
      // is already in use. So it's possible for two files to use the same file_id.

      // The standard use of the FileNameRegistration relies on auto increment
      // of the tree_status highest_file_id. So during restoration, the external
      // code needs to enforce the (prefix + file_id) uniqueness - if that is needed.

      var file_name = Path.GetFileName(file_path);
      DateTime? modified_time = null;
      if (modified_time_ticks != 0) modified_time = new DateTime(modified_time_ticks);

      var file_node = new FileNode(file_name, prefix, file_id, modified_time, file_stream, 0);
      if (prefix.Equals(default_prefix))
        tree_status.check_for_higher_ids(file_id);

      root.attach_new_file_node(file_path, 0, file_node);
      file_node.append_to_disk(file_path);
    }

    /// <summary>
    /// Returns the file modified time for a particular file.
    /// Throws an exception if the file does not exist.
    /// </summary>
    public DateTime? get_modified_time(string file_path)
    {
      var node = root.get_file_node(file_path, 0);

      if (node == null)
        throw new Exception("Software error. Attempted to read the modified time of a "
          + "non-existent file.");
      else
        return node.get_modified_time();
    }

    /// <summary>
    /// Sets the file modified time for a particular file.
    /// Throws an exception if the file does not exist.
    /// </summary>
    public void set_modified_time(string file_path, DateTime? modified_time)
    {
      var node = root.get_file_node(file_path, 0);

      if (node == null)
        throw new Exception("Software error. Attempted to set the modified time of a "
          + "non-existent file.");

      node.set_modified_time(modified_time); 
    }

    /// <summary>
    /// Print to screen or file as a tree structure.
    /// </summary>
    /// <param name="file_path">If path is null, this function prints to the screen.</param>
    public void print(string file_path = null)
    {
      if (file_path != null)
      {
        using (var stream_writer = new StreamWriter(file_path))
          root.print(0, stream_writer);
      }
      else
        root.print(0);
    }

    /// <summary>
    /// Delete a particular node, which might be a file or a directory.
    /// </summary>
    public void delete(string file_or_dir_path)
    {
      root.delete(file_or_dir_path, 0);
    }

  }
}
