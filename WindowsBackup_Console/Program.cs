using System;


namespace WindowsBackup_Console
{
  class Program
  {
    static void Main(string[] args)
    {
      run_tests();
      
      Console.ReadLine();
    }

    /// <summary>
    /// These are manual tests.
    /// </summary>
    static void run_tests()
    {
      var test = new Test();

      // test.test_CloudBackupService();
      // test.test_BasicKeyManager_to_xml();
      // test.test_Encrypt_and_Decrypt_streams_on_disk();
      // test.test_Encrypt_and_Decrypt_streams_on_cloud();
      // test.test_FileNameRegistration();
      test.test_BackupRuleList();

      // test.test_BackupManager();
      // test.test_BackupManager_check_backups();
      // test.test_BackupManager_to_xml();

      // test.test_RestoreManager();
      // test.test_RestoreManager_to_xml();
    }



  }
}
