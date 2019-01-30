using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using System.Diagnostics; // for Debug
using System.Xml.Linq; // for XML


using WindowsBackup;

namespace WindowsBackup_Console
{
  class Test
  {
    #region Test Data

    // AES encryption key
    string key = "jKCVxYp8n+kfpCCFEmZBvEJOS5gt+PzmaWytxby8c20=";

    XElement key_manager_xml = XElement.Parse(@"
        <BasicKeyManager>
            <key number=""100"" name=""louis"">jKCVxYp8n+kfpCCFEmZBvEJOS5gt+PzmaWytxby8c20=</key>
        </BasicKeyManager>
        ");

    // FileNameRegistration initialization
    XElement file_name_reg_xml = XElement.Parse(
        @"<FileNameRegistration>
              <default_prefix>a</default_prefix>
              <path>E:\temp\temp\ignore\fnr.tsv</path>
          </FileNameRegistration>"
        );
    
    // cloud_backup_services_xml
    // TODO zz - replace the secret strings with filler characters
    XElement cloud_backup_services_xml = XElement.Parse(
      @"
          <cloud_backup_services>
              <AWS_CloudBackupService name=""aws"">
                  <id>xxxxxxxxxxxxxxxxxxxxxxx</id>
                  <secret_key>xxxxxxxxxxxxxxxxxxxxxxx</secret_key>
                  <region>us-west-1</region>
              </AWS_CloudBackupService>
              <AzureBlob_CloudBackupService name=""azure"">
                  DefaultEndpointsProtocol=https;AccountName=xxxxxxx;AccountKey=xxxxxxxxxxxxxxxxxxxxxxx;EndpointSuffix=core.windows.net
              </AzureBlob_CloudBackupService>
              <GCP_CloudBackupService  name=""gcp""> {
                  ""type"": ""service_account"",
                  ""project_id"": ""my-project-1514720626755"",
                  ""private_key_id"": ""xxxxxxxxxxxxxxxxxxxxxxx"",
                  ""private_key"": ""-----BEGIN PRIVATE KEY-----\xxxxxxxxxxxxxxxxxxxxxxx\n-----END PRIVATE KEY-----\n"",
                  ""client_email"": ""starting-account-xxxxxxxxxxxx@my-project-xxxxxxxxxxxxx.iam.gserviceaccount.com"",
                  ""client_id"": ""xxxxxxxxxxxxxxxxxxxxxxx"",
                  ""auth_uri"": ""https://accounts.google.com/o/oauth2/auth"",
                  ""token_uri"": ""https://accounts.google.com/o/oauth2/token"",
                  ""auth_provider_x509_cert_url"": ""https://www.googleapis.com/oauth2/v1/certs"",
                  ""client_x509_cert_url"": ""https://www.googleapis.com/robot/v1/metadata/x509/starting-account-5ov3b465cs2c%40my-project-1514720626755.iam.gserviceaccount.com""
              } </GCP_CloudBackupService>
          </cloud_backup_services>"
      );


    // BackupManager
    XElement backup_xml = XElement.Parse(
          @"
        <BackupManager>
          <cloud_live_backup_max_mb>1</cloud_live_backup_max_mb>
          <backups>
            <EncryptedBackup enable=""true"" name=""temp"">
              <source>E:\temp\temp</source>
              <embedded_prefix>temp</embedded_prefix>
              <key_number>100</key_number>
              <!-- <destination_name>disk</destination_name>
              <destination_path>E:\temp\temp2</destination_path> -->
              <destination_name>aws</destination_name>
              <destination_path>xxxxxxxx</destination_path> 
              <BackupRuleList>
                <BackupRule>
                    <directory>E:\temp\temp</directory>
                    <rule_type>ACCEPT_ALL</rule_type>
                    <suffixes></suffixes>
                </BackupRule>
                <BackupRule>
                    <directory>E:\temp\temp\ignore</directory>
                    <rule_type>REJECT_ALL</rule_type>
                    <suffixes></suffixes>
                </BackupRule>
                <BackupRule>
                    <directory>E:\temp\temp\config</directory>
                    <rule_type>ACCEPT_SUFFIX</rule_type>
                    <suffixes>.xml .json</suffixes>
                </BackupRule>
                <BackupRule>
                    <directory>E:\temp\temp\src</directory>
                    <rule_type>REJECT_SUFFIX</rule_type>
                    <suffixes>.tmp</suffixes>
                </BackupRule>
              </BackupRuleList>
            </EncryptedBackup>

            <DiskBackup enable=""false"" name=""temp unencrypted"">
              <source>E:\temp\temp</source>
              <destination>E:\temp\temp2</destination>
              <BackupRuleList>
                <BackupRule>
                    <directory>E:\temp\temp</directory>
                    <rule_type>ACCEPT_ALL</rule_type>
                    <suffixes></suffixes>
                </BackupRule>
                <BackupRule>
                    <directory>E:\temp\temp\ignore</directory>
                    <rule_type>REJECT_ALL</rule_type>
                    <suffixes></suffixes>
                </BackupRule>
                <BackupRule>
                    <directory>E:\temp\temp\config</directory>
                    <rule_type>ACCEPT_SUFFIX</rule_type>
                    <suffixes>.xml .json</suffixes>
                </BackupRule>
                <BackupRule>
                    <directory>E:\temp\temp\src</directory>
                    <rule_type>REJECT_SUFFIX</rule_type>
                    <suffixes>.tmp</suffixes>
                </BackupRule>
              </BackupRuleList>
            </DiskBackup>

          </backups>
        </BackupManager>"
          );


    // RestoreManager
    XElement restore_xml = XElement.Parse(
          @"
          <RestoreManager>
              <default_destinations>
                  <default_destination embedded_prefix=""temp"" 
                          destination=""E:\temp\temp3"" />
              </default_destinations>
              <restores>
                  <restore>
                      <destination_name>disk</destination_name>
                      <destination_path>E:\temp\temp2</destination_path>
                      <file_prefixes>a b</file_prefixes>
                  </restore>
                  <restore>
                      <destination_name>aws</destination_name>
                      <destination_path>xxxxxxxx</destination_path>
                      <file_prefixes>a b</file_prefixes>
                  </restore>
              </restores>
          </RestoreManager>
          ");
    #endregion 

    /// <summary>
    /// Returns true if two files match.
    /// </summary>
    /// <param name="length">Match only the first "length" bytes of two files.
    /// Null means to compare all of two files.</param>
    bool compare_files(string file_path1, string file_path2, long? length = null)
    {
      if (length == null)
      {
        // Require the two files to be the same length
        long length1 = new FileInfo(file_path1).Length;
        long length2 = new FileInfo(file_path2).Length;
        if (length1 != length2) return false;
        length = length1;
      }
      
      // compare file content
      using (var fs1 = new FileStream(file_path1, FileMode.Open))
      using (var fs2 = new FileStream(file_path2, FileMode.Open))
      {
        long bytes_left = length.Value;

        byte[] buffer1 = new byte[1024 * 100];
        byte[] buffer2 = new byte[1024 * 100];

        do
        {
          int bytes_to_read = buffer1.Length;
          if (bytes_to_read > bytes_left) bytes_to_read = (int)bytes_left;

          fs1.Read(buffer1, 0, bytes_to_read);
          fs2.Read(buffer2, 0, bytes_to_read);

          for (int i = 0; i < bytes_to_read; i++)
          {
            if (buffer1[i] != buffer2[i]) return false;
          }

          bytes_left = bytes_left - bytes_to_read;

        } while (bytes_left > 0);
      }

      return true; // Files are the same if the code manages to get this far.
    }
    

    /// <summary>
    /// Compare two directories and print out the differences.
    /// </summary>
    void compare_directories(string dir1_path, string dir2_path)
    {
      char sep = Path.DirectorySeparatorChar;

      // Check that files in dir1_path matches corresponding files in dir2_path
      var file_names_full_path = Directory.GetFiles(dir1_path);
      foreach (var file_name_full_path in file_names_full_path)
      {
        var file_name = Path.GetFileName(file_name_full_path);
        var dir2_plus_file_name = dir2_path + sep + file_name;

        if (File.Exists(dir2_plus_file_name))
        {
          bool match = compare_files(file_name_full_path, dir2_plus_file_name);
          if (!match)
            Console.WriteLine("File mismatch: " + file_name_full_path + " " + dir2_plus_file_name);
        }
        else
          Console.WriteLine(file_name_full_path + " does not exist inside " + dir2_path);
      }

      // Check that files in dir2_path also exist in dir1_path
      file_names_full_path = Directory.GetFiles(dir2_path);
      foreach (var file_name_full_path in file_names_full_path)
      {
        var file_name = Path.GetFileName(file_name_full_path);
        var dir1_plus_file_name = dir1_path + sep + file_name;

        if (File.Exists(dir1_plus_file_name) == false)
          Console.WriteLine(file_name_full_path + " does not exist inside " + dir1_path);
      }

      // Check subdirectories
      var dir_names_full_path = Directory.GetDirectories(dir1_path);
      foreach(var dir_name_full_path in dir_names_full_path)
      {
        var dir_name = Path.GetFileName(dir_name_full_path);
        var dir2_plus_dir_name = dir2_path + sep + dir_name;
        if (Directory.Exists(dir2_plus_dir_name))
          compare_directories(dir_name_full_path, dir2_plus_dir_name);
        else
          Console.WriteLine(dir_name_full_path + " does not exist inside " + dir2_path);
      }
    }


    /// <summary>
    /// Reads a single key from the user and return true if that key is ESC.
    /// </summary>
    bool hit_esc_key()
    {
      var key_info = Console.ReadKey(true);
      if (key_info.Key == ConsoleKey.Escape)
      {
        Console.WriteLine("Test skipped.");
        return true;
      }
      else return false;
    }

    /// <summary>
    /// Waits for a key press from the user.
    /// </summary>
    void pause()
    {
      Console.WriteLine();
      Console.WriteLine("Press any key to continue...");
      Console.ReadKey(true);
      Console.WriteLine();
      Console.WriteLine();
    }

    /// <summary>
    /// Tests the CloudBackupService classes. The content of a storage account
    /// is listed. A file is uploaded, downloaded, and finally deleted.
    /// </summary>
    public void test_CloudBackupService()
    {
      Console.WriteLine("About to start a test using CloudBackupService."
        + " Press any key to continue, or ESC to skip.\n");
      if (hit_esc_key()) return;
      
      var backup_services = create_cloud_backup_services_from_xml(cloud_backup_services_xml);

      // Choose one of the "backup_services" for use in testing.
      CloudBackupService backup_service = backup_services[1];
      string bucket_name = "xxxxxxxx"; // Choose bucket name

      Console.WriteLine("Testing using " + backup_service.Name + "\\" + bucket_name);

      Console.WriteLine("Listing objects:");
      foreach (var obj in backup_service.list_objects(bucket_name, null))
        Console.WriteLine(new String(' ', 4) + obj);

      Console.WriteLine("Manually confirm that this is correct.");
      pause();

      // generate files for testing
      string longer_file_path = @"E:\temp\temp2\temp.bin";
      string shorter_file_path = @"E:\temp\temp2\temp_shorter.bin";
      string download_file_path = @"E:\temp\temp2\temp_downloaded.bin";
      string key_name = Path.GetFileName(longer_file_path);

      // generate shorter file
      var random = new Random();
      byte[] buffer = new byte[1 * 1024];
      random.NextBytes(buffer);

      using (FileStream fs_short = new FileStream(shorter_file_path, FileMode.Create, FileAccess.Write))
      {
        fs_short.Write(buffer, 0, buffer.Length);
      }

      // generate longer file
      using (FileStream fs_long = new FileStream(longer_file_path, FileMode.Create, FileAccess.Write))
      {
        fs_long.Write(buffer, 0, buffer.Length);

        for (int i = 0; i < 10; i++)
        {
          random.NextBytes(buffer);
          fs_long.Write(buffer, 0, buffer.Length);
        }
      }

      // upload file
      Console.WriteLine("Uploading file.");
      using (FileStream fs_long = new FileStream(longer_file_path, FileMode.Open, FileAccess.Read))
      {
        backup_service.upload(fs_long, bucket_name, key_name);
      }

      // download partial and compare file
      Console.WriteLine("Downloading the start of the file and checking.");
      using (FileStream fs_downloaded = new FileStream(download_file_path, FileMode.Create, FileAccess.Write))
      {
        backup_service.download(bucket_name, key_name, fs_downloaded, 0, 1 * 1024);
      }
      Debug.Assert(compare_files(shorter_file_path, download_file_path));

      // download full file and compare file
      Console.WriteLine("Downloading the full file and checking.");
      using (FileStream fs_downloaded = new FileStream(download_file_path, FileMode.Create, FileAccess.Write))
      {
        backup_service.download(bucket_name, key_name, fs_downloaded);
      }
      Debug.Assert(compare_files(longer_file_path, download_file_path));

      // Test to see if downloading too many bytes cause errors?
      using (FileStream fs_downloaded = new FileStream(download_file_path, FileMode.Create, FileAccess.Write))
      {
        backup_service.download(bucket_name, key_name, fs_downloaded, 0, 1024 * 1024);
      }
      Debug.Assert(compare_files(longer_file_path, download_file_path));

      // delete file
      Console.WriteLine("Deleting the file and checking.");
      backup_service.delete(bucket_name, key_name);

      bool found = false;
      foreach (var obj in backup_service.list_objects(bucket_name, null))
      {
        if(obj.Equals(key_name))
        {
          found = true;
          break;
        }
      }
      Debug.Assert(found == false);
      Console.WriteLine("CloudBackupService test completed.");
    }

    public void test_BasicKeyManager_to_xml()
    {
      Console.WriteLine("Existing BasicKeyManager:");
      var basic_key_manager = new BasicKeyManager(key_manager_xml);
      Console.WriteLine(basic_key_manager.to_xml());
      Console.WriteLine("\n");

      Console.WriteLine("New BasicKeyManager:");
      basic_key_manager = new BasicKeyManager();
      basic_key_manager.add_key("temp");
      Console.WriteLine(basic_key_manager.to_xml());
    }

    // For use with DecryptStream
    long file_pre_encrypt_size;

    string full_path_request_handler(string relative_path, DateTime? file_modified_time, 
      long file_pre_encrypt_size, long total_file_length)
    {
      this.file_pre_encrypt_size = file_pre_encrypt_size;
      return @"E:\temp\temp2" + Path.DirectorySeparatorChar + relative_path;
    }

    /// <summary>
    /// Tests EncryptStream and DecryptStream using disk files. Different files 
    /// are encrypted and then decrypted, both with and without compression.
    /// </summary>
    public void test_Encrypt_and_Decrypt_streams_on_disk()
    {
      Console.WriteLine("About to test EncryptStream and DecryptStream using files on disk."
        + " Press any key to continue, or ESC to skip.\n");
      if (hit_esc_key()) return;

      // paths:
      string original_file = @"E:\temp\temp\temp.bin";
      string base_path = @"E:\temp\temp";
      string relative_path = original_file.Substring(base_path.Length + 1);

      string encrypted_file = @"E:\temp\temp2\temp_en.bin";
      string decrypted_file = @"E:\temp\temp2\temp.bin";
      
      var encrypt_stream = new EncryptStream();
      var decrypt_stream = new DecryptStream();

      Random random = new Random();
      
      for(int i = 0; i < 5; i++ )
      {
        bool do_not_compress = false;

        // The different tests try different input files.
        if (i == 0)
        {
          // zero byte file
          using (var fs = new FileStream(original_file, FileMode.Create, FileAccess.Write)) { }
        }
        else if (i == 1)
        {
          // one byte file
          using (var fs = new FileStream(original_file, FileMode.Create, FileAccess.Write))
            fs.WriteByte((byte)(random.Next(0, 256)));
        }

        else if (i == 2 || i == 3)
        {
          // test #2: file with repetitive bytes
          // test #3: file with repetitive bytes - no compression
          if (i == 3) do_not_compress = true;

          byte[][] patterns = new byte[32][];
          for (int j = 0; j < patterns.Length; j++)
          {
            patterns[j] = new byte[32];
            random.NextBytes(patterns[j]);
          }         

          using (var fs = new FileStream(original_file, FileMode.Create, FileAccess.Write))
          {
            for (int j = 0; j < 1024 * 10; j++)
            {
              int pattern_to_use = random.Next(0, 32);
              fs.Write(patterns[pattern_to_use], 0, patterns[pattern_to_use].Length);
            }
          }
        }

        else if (i == 4)
        {
          // random file
          byte[] buffer = new byte[1024];

          using (var fs = new FileStream(original_file, FileMode.Create, FileAccess.Write))
          {
            for (int j = 0; j < 1024; j++)
            {
              random.NextBytes(buffer);
              fs.Write(buffer, 0, buffer.Length);
            }
          }
        }

        // The rest of the test is the same - encrypt, decrypt, the compare files.

        encrypt_stream.reset(original_file, relative_path, key, do_not_compress);
        using (var output_fs = new FileStream(encrypted_file, FileMode.Create))
          encrypt_stream.CopyTo(output_fs);

        decrypt_stream.reset(key, full_path_request_handler);
        using (var fs = new FileStream(encrypted_file, FileMode.Open))
          fs.CopyTo(decrypt_stream);
        decrypt_stream.Flush();

        Debug.Assert(compare_files(original_file, decrypted_file));
      }
      
      Console.WriteLine("EncryptStream and DecryptStream tested using disk.\n");
    }


    /// <summary>
    /// Tests EncryptStream and DecryptStream using cloud storage. A single
    /// file is uploaded in encrypted form and then downloaded.
    /// </summary>
    public void test_Encrypt_and_Decrypt_streams_on_cloud()
    {
      Console.WriteLine("About to upload a file to the cloud and download it back, "
        + "using EncryptStream and DecryptStream. Press any key to continue, or ESC to skip.\n");
      if (hit_esc_key()) return;

      var backup_services = create_cloud_backup_services_from_xml(cloud_backup_services_xml);

      // Choose one of the "backup_services" for use in testing.
      CloudBackupService backup_service = backup_services[2];
      string bucket_name = "xxxxxxxx";

      string original_file = @"E:\temp\temp\temp.bin";
      string base_path = @"E:\temp\temp";
      string relative_path = original_file.Substring(base_path.Length + 1);

      string decrypted_file = @"E:\temp\temp2\temp.bin";

      var encrypt_stream = new EncryptStream();
      var decrypt_stream = new DecryptStream();
      
      Random random = new Random();

      byte[] buffer = new byte[1024];

      // create random file
      using (var fs = new FileStream(original_file, FileMode.Create, FileAccess.Write))
      {
        for (int j = 0; j < 640; j++)
        {
          random.NextBytes(buffer);
          fs.Write(buffer, 0, buffer.Length);
        }
      }

      // encrypted upload
      Console.WriteLine("Encrypting and uploading file to the cloud.");
      encrypt_stream.reset(original_file, relative_path, key);
      backup_service.upload(encrypt_stream, bucket_name, "temp.bin");

      Console.WriteLine("The file has been encrypted and uploaded to the cloud.");
      Thread.Sleep(2000);
      
      // download and decrypt
      decrypt_stream.reset(key, full_path_request_handler);

      // be sure the download can be done in pieces
      backup_service.download(bucket_name, "temp.bin", decrypt_stream, 0, 1024);
      Console.WriteLine("The first 1kB header says the pre-encrypt file size is " 
        + file_pre_encrypt_size + " bytes.");

      backup_service.download(bucket_name, "temp.bin", decrypt_stream, 1024, file_pre_encrypt_size);

      decrypt_stream.Flush();
      backup_service.delete(bucket_name, "temp.bin");

      Debug.Assert(compare_files(original_file, decrypted_file));

      Console.WriteLine("EncryptStream and DecryptStream has been tested using cloud storage.\n");
    }


    /// <summary>
    /// Tests the FileNameRegistration class. Retrieve names for some
    /// files, delete nodes, add nodes, and perform a compact operation.
    /// </summary>
    public void test_FileNameRegistration()
    {
      Console.WriteLine("About to test the FileNameRegistration. Attempt to get a "
        + "few alternative file names. Then delete some files to trigger "
        + "compaction.  Press any key to continue, or ESC to skip.");
      if (hit_esc_key()) return;

      // Add files to the file name registration tree.
      var file_name_reg = new FileNameRegistration(file_name_reg_xml);

      Console.WriteLine();
      Console.WriteLine("The test now creates a basic directory structure and "
        + "print information about it.");

      // Create a basic directory structure.
      var path_status = file_name_reg.get_path_status(@"E:\a");
      if (path_status == null)
      {
        // .add_file(...) can only happen once per file, so this block
        // should run just once.
        Console.WriteLine(file_name_reg.add_file(@"E:\a"));
        Console.WriteLine(file_name_reg.add_file(@"E:\a1\b"));
        Console.WriteLine(file_name_reg.add_file(@"E:\a1\c"));

        Console.WriteLine(file_name_reg.add_file(@"E:\a1\c2\cc2"));
        Console.WriteLine(file_name_reg.add_file(@"E:\a1\c2\cc2b"));
        Console.WriteLine(file_name_reg.add_file(@"E:\a1\c2\cc2c"));
        Console.WriteLine(file_name_reg.add_file(@"E:\a1\c2\cc2dd\c2a"));
        Console.WriteLine(file_name_reg.add_file(@"E:\a1\c2\cc2dd\c2b"));

        Console.WriteLine(file_name_reg.add_file(@"E:\a1\c3"));
      }           

      // print how things look so far
      file_name_reg.print();
      file_name_reg.print(@"E:\temp\temp\fnr.txt");
      Console.WriteLine();

      // Test the .get_path_status(...)
      string[] paths = { @"E:\a1\c2", @"E:\a1\b", @"E:\a1\no_such_thing" };
      foreach (string path in paths)
      {
        var path_info = file_name_reg.get_path_status(path);
        if (path_info == null)
          Console.WriteLine(path + " does not exist.");
        else
        {
          Console.WriteLine(path);
          Console.WriteLine("is_file = " + path_info.is_file
            + "; alt_file_name = " + path_info.alt_file_name
            + "; modified_time = " + path_info.modified_time);
        }
        Console.WriteLine();
      }

      // Test the .get_names(...)
      List<string> sub_dir_names = null, file_names = null, alt_file_names = null;
      string path2 = @"E:\a1\c2";
      file_name_reg.get_names(path2, ref sub_dir_names, ref file_names, ref alt_file_names);

      if (sub_dir_names != null)
      {
        Console.Write(path2 + " sub directory names: ");
        foreach (var name in sub_dir_names)
          Console.Write(name + ", ");
        Console.WriteLine();
      }
      
      if (file_names != null)
      {
        Console.Write(path2 + " file names: ");
        foreach (var name in file_names)
          Console.Write(name + ", ");
        Console.WriteLine();
      }
      
      if (alt_file_names != null)
      {
        Console.Write(path2 + " alternative file names: ");
        foreach (var name in alt_file_names)
          Console.Write(name + ", ");
        Console.WriteLine();
      }
      
      pause();
            
      // Delete
      Console.WriteLine(@"Delete E:\a1\c2");
      file_name_reg.delete(@"E:\a1\c2");
      file_name_reg.print();
      pause();
            
      file_name_reg.Dispose();
      
      // Reload from file to trigger compaction
      file_name_reg = new FileNameRegistration(file_name_reg_xml);

      // Get modified time
      Console.WriteLine("Test changing the modified time.");
      Console.WriteLine(@"E:\a1\b modified time = " +
        file_name_reg.get_modified_time(@"E:\a1\b"));

      // Set modified time
      file_name_reg.set_modified_time(@"E:\a1\b", DateTime.Now);
      Console.WriteLine(@"E:\a1\b modified time = " + 
        file_name_reg.get_modified_time(@"E:\a1\b"));

      pause();

      // Try a bigger directory
      Console.WriteLine("Attaching files from " + @"E:\temp\temp");
      
      string[] file_paths = Directory.GetFiles(@"E:\temp\temp", "*", 
        SearchOption.AllDirectories);

      if (file_name_reg.get_path_status(file_paths[0]) == null)
      {
        foreach (string path in file_paths)
          Console.Write(file_name_reg.add_file(path) + " ");
      }
      

      Console.WriteLine();
      file_name_reg.print();
      pause();

      // Try reload from disk
      Console.WriteLine("Reload from disk.");
      file_name_reg.Dispose();

      file_name_reg = new FileNameRegistration(file_name_reg_xml);
      file_name_reg.print();

      // Try to create files using explicit file IDs. This is necessary
      // for the restoration process. The file IDs need to be >= 1000
      // per design spec.
      pause();
      file_name_reg.Dispose();
      
      Console.WriteLine("Adding new files with explicit file IDs\n");
      file_name_reg = new FileNameRegistration(file_name_reg_xml);

      if (file_name_reg.get_path_status(@"c:\test\whatever\123.txt") == null)
      {  
        file_name_reg.add_file(@"c:\test\whatever\123.txt", "a", 2123, 0);
        file_name_reg.add_file(@"c:\test\whatever\67.txt", "a", 2067, 0);
        file_name_reg.add_file(@"c:\test\whatever2\320.txt", "a", 2320, 0);
        file_name_reg.set_modified_time(@"c:\test\whatever2\320.txt", DateTime.Now);
        file_name_reg.add_file(@"c:\test\whatever\999.txt", "b", 2999, 0);
      }
      file_name_reg.Dispose();

      file_name_reg = new FileNameRegistration(file_name_reg_xml);

      path_status = file_name_reg.get_path_status(@"c:\test\whatever\999.txt");
      Console.WriteLine(@"c:\test\whatever\999.txt");
      Console.WriteLine("is_file = " + path_status.is_file
            + "; alt_file_name = " + path_status.alt_file_name
            + "; modified_time = " + path_status.modified_time);

      file_name_reg.Dispose();
      Console.WriteLine();
    }


    /// <summary>
    /// Tests the BackupRuleList class. Instantiate rules from XML and
    /// asks these rules to accept or reject some file paths.
    /// </summary>
    public void test_BackupRuleList()
    {
      // BackupRuleList
      XElement backup_rule_list_xml = XElement.Parse(
          @"<BackupRuleList>
              <category>
                  <BackupRule>
                      <directory>E:\temp\temp</directory>
                      <rule_type>ACCEPT_ALL</rule_type>
                  </BackupRule>
                  <BackupRule>
                      <directory>E:\temp\temp\packages</directory>
                      <rule_type>REJECT_ALL</rule_type>
                  </BackupRule>
                  <BackupRule>
                      <directory>E:\temp\temp\config</directory>
                      <rule_type>ACCEPT_SUFFIX</rule_type>
                      <suffixes>.xml .json</suffixes>
                  </BackupRule>
                  <BackupRule>
                      <directory>E:\temp\temp\src</directory>
                      <rule_type>REJECT_SUFFIX</rule_type>
                      <suffixes>.tmp</suffixes>
                  </BackupRule>
              </category>
              <category>
                  <BackupRule>
                      <directory>E:\temp\temp</directory>
                      <rule_type>REJECT_SUB_DIR</rule_type>
                      <subdirs>__pycache__ .git</subdirs>
                  </BackupRule>
                  <BackupRule>
                      <directory>E:\temp\temp\temp2</directory>
                      <rule_type>ACCEPT_ALL</rule_type>
                  </BackupRule>
              </category>
          </BackupRuleList>"
          );

      Console.WriteLine("Testing BackupRuleList");
      var backup_rule_lists = new BackupRuleLists(backup_rule_list_xml);
            
      Console.WriteLine("backup_rule_list in XML form:");
      Console.WriteLine(backup_rule_lists.to_xml());
      
      Console.WriteLine();
      Console.WriteLine("Trying some files on BackupRuleList:");
      Debug.Assert(backup_rule_lists.accepts(@"E:\temp\temp\whatever.txt") == true);
      Debug.Assert(backup_rule_lists.accepts(@"E:\temp\temp\xyz.anything") == true);

      Debug.Assert(backup_rule_lists.accepts(@"E:\temp\temp\packages\whatever.txt") == false);
      Debug.Assert(backup_rule_lists.accepts(@"E:\temp\temp\packages\xyz.anything") == false);

      Debug.Assert(backup_rule_lists.accepts(@"E:\temp\temp\config\whatever.txt") == false);
      Debug.Assert(backup_rule_lists.accepts(@"E:\temp\temp\config\whatever.xml") == true);
      Debug.Assert(backup_rule_lists.accepts(@"E:\temp\temp\config\whatever.json") == true);

      Debug.Assert(backup_rule_lists.accepts(@"E:\temp\temp\src\xyz.tmp") == false);
      Debug.Assert(backup_rule_lists.accepts(@"E:\temp\temp\src\xyz.anything_else") == true);
      
      Debug.Assert(backup_rule_lists.accepts(@"E:\temp\temp\src\__pycache__\whatever") == false);
      Debug.Assert(backup_rule_lists.accepts(@"E:\temp\temp\src\.git\whatever") == false);
      Debug.Assert(backup_rule_lists.accepts(@"E:\temp\temp\temp2\temp3\__pycache__\whatever") == true);

      Console.WriteLine("BackupRuleList has been tested.");
    }

    
    List<CloudBackupService> create_cloud_backup_services_from_xml(XElement xml)
    {
      var cloud_backup_services = new List<CloudBackupService>();
      foreach (var tag in xml.Elements())
      {
        if (tag.Name.LocalName.Equals("AWS_CloudBackupService"))
          cloud_backup_services.Add(new AWS_CloudBackupService(tag));
        else if (tag.Name.LocalName.Equals("AzureBlob_CloudBackupService"))
          cloud_backup_services.Add(new AzureBlob_CloudBackupService(tag));
        else if (tag.Name.LocalName.Equals("GCP_CloudBackupService"))
          cloud_backup_services.Add(new GCP_CloudBackupService(tag));
      }

      return cloud_backup_services;
    }

    void handle_app_events(AppEventType event_type, params object[] param_array)
    {
      if (event_type == AppEventType.LOG)
        Console.WriteLine(param_array[0]);
      else
      {
        // A default handler that relies on the .ToString() 
        Console.Write(event_type + ": ");
        if (param_array != null && param_array.Length > 0)
        {
          foreach (var obj in param_array)
            Console.WriteLine(obj);
        }
        else
          Console.WriteLine();        
      }
    }


    void wait_for_backup_manager_thread_to_quit(BackupManager backup_manager)
    {
      Console.WriteLine("BackupManager object started. Press 's' to stop.");
      Console.WriteLine();

      bool stop = false;

      while (stop == false)
      {
        Thread.Sleep(100);

        if (Console.KeyAvailable)
        {
          var key_info = Console.ReadKey(true);
          if (key_info.Key == ConsoleKey.S)
            stop = true;
        }
      }

      backup_manager.quit();
      Console.WriteLine("Quitting the BackupManager object.");
    }

    /// <summary>
    /// Sets up a EncryptedBackupManager object to mirror files between two 
    /// locations. Externally copy and move files and directories
    /// to test functionality.
    /// </summary>
    public void test_BackupManager()
    {
      Console.WriteLine("Testing BackupManager");

      var file_name_reg = new FileNameRegistration(file_name_reg_xml);
      var cloud_backup_services = create_cloud_backup_services_from_xml(cloud_backup_services_xml);
      var key_manager = new BasicKeyManager(key_manager_xml);
      
      var backup_manager = new BackupManager(backup_xml, file_name_reg, 
        cloud_backup_services, key_manager, handle_app_events);
      backup_manager.start_live_backup();

      wait_for_backup_manager_thread_to_quit(backup_manager);
      Console.WriteLine(); 
    }

    public void test_BackupManager_check_backups()
    {
      Console.WriteLine("Testing BackupManager .check_backups()");

      var file_name_reg = new FileNameRegistration(file_name_reg_xml);
      var cloud_backup_services = create_cloud_backup_services_from_xml(cloud_backup_services_xml);
      var key_manager = new BasicKeyManager(key_manager_xml);

      var backup_manager = new BackupManager(backup_xml, file_name_reg,
        cloud_backup_services, key_manager, handle_app_events);
      backup_manager.start_live_backup();

      Console.WriteLine("Live backup started. Will switch to check_all_backups() in 5 seconds.");
      Thread.Sleep(5000);

      Console.WriteLine("Running .check_all_backups();");
      backup_manager.check_all_backups();

      wait_for_backup_manager_thread_to_quit(backup_manager);
    }

    public void test_BackupManager_to_xml()
    {
      Console.WriteLine("Testing BackupManager .to_xml()");
      var file_name_reg = new FileNameRegistration(file_name_reg_xml);
      var cloud_backup_services = create_cloud_backup_services_from_xml(cloud_backup_services_xml);
      var key_manager = new BasicKeyManager(key_manager_xml);

      var backup_manager = new BackupManager(backup_xml, file_name_reg,
        cloud_backup_services, key_manager, handle_app_events);

      Backup backup = backup_manager.backups[0];
      if(backup is EncryptedBackup)
      {
        var encrypted_backup = (EncryptedBackup)backup;
        encrypted_backup.future_params.name = "new name";
        encrypted_backup.future_params.enabled = false;
        encrypted_backup.future_params.rule_lists.add_rule(
          new BackupRuleLists.BackupRule("z:\\temp", 
                  BackupRuleLists.BackupRuleType.ACCEPT_ALL, null, null),
          category: 0);
      }

      backup = backup_manager.backups[1];
      if (backup is DiskBackup)
      {
        var disk_backup = (DiskBackup)backup;
        disk_backup.future_params.name = "new name2";
        disk_backup.future_params.enabled = false;
        disk_backup.future_params.rule_lists.add_rule(
          new BackupRuleLists.BackupRule("z:\\temp2",
                  BackupRuleLists.BackupRuleType.ACCEPT_ALL, null, null),
          category: 0);
      }

      Console.WriteLine(backup_manager.to_xml());
      backup_manager.quit();
    }

    public void test_RestoreManager()
    {
      // Test settings:
      // The index of the restore object(s) to use:
      int[] restore_indices = { 1 };

      // The restored file gets its own file name registration, at:
      string file_name_reg_path = @"E:\temp\temp\ignore\fnr2.tsv";

      // At the very end, this test compares two directories
      string dir1_path = @"E:\temp\temp"; // the original directory
      string dir2_path = @"E:\temp\temp3"; // the restored directory
      
      Console.WriteLine("Testing RestoreManager.");

      var cloud_backup_services = create_cloud_backup_services_from_xml(cloud_backup_services_xml);
      var key_manager = new BasicKeyManager(key_manager_xml);

      var restore_manager = new RestoreManager(restore_xml, cloud_backup_services,
        key_manager, handle_app_events);

      // Print restore names
      Console.WriteLine("Restore names:");
      string[] restore_names = restore_manager.get_restore_names();
      foreach (var name in restore_names)
        Console.WriteLine("  " + name);
      Console.WriteLine();

      // Get Info about the restores
      Console.Write("Getting info on: ");
      foreach (var i in restore_indices)
        Console.Write(restore_names[i] + " ");
      Console.WriteLine();

      // To run the restore_manager.get_info(...) on a separate thread, use
      // the background manager.
      var file_name_reg = new FileNameRegistration(file_name_reg_xml);
      var backup_manager = new BackupManager(backup_xml, file_name_reg,
        cloud_backup_services, key_manager, handle_app_events);

      backup_manager.get_restore_info(restore_manager, restore_indices, skip_file_names: false);

      // Create a RestoreSettings settings object and then initiate a restore
      var restore_settings = new RestoreSettings();
      
      if (File.Exists(file_name_reg_path)) File.Delete(file_name_reg_path);
      restore_settings.file_name_reg = new FileNameRegistration(file_name_reg_path);

      restore_settings.indices = restore_indices;
      restore_settings.restore_destination_lookup = restore_manager.default_destinations;

      Console.WriteLine("Starting restore.");
      backup_manager.restore(restore_manager, restore_settings);
      
      wait_for_backup_manager_thread_to_quit(backup_manager);

      // Code gets here after user hit "s" key to stop the backup manager thread.
      Console.WriteLine("\nComparing directories:");
      compare_directories(dir1_path, dir2_path);

      Console.WriteLine("\n");       
    }


    public void test_RestoreManager_to_xml()
    {
      Console.WriteLine("Testing RestoreManager.to_xml()");

      var restore_manager = new RestoreManager();
      restore_manager.set_default_destination("temp", @"E:\temp\temp3");
      restore_manager.add_restore(new Restore("disk", @"E:\temp\temp2", "a b"));
      restore_manager.add_restore(new Restore("aws", @"xxxxxxxx", "a b"));

      Console.WriteLine(restore_manager.to_xml());
      Console.WriteLine();
    }


  }
}
