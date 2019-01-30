# Windows Backup
## Features
* Backup to the Cloud (AWS, Azure, GCP)
* Encryption (AES 256 bit)
* Compression
* Hierarchical Rules

## [Video Presentation (13 minutes)](https://youtu.be/zlimeqw3WhQ)

## Understanding the Code
* When building this project, it will download NuGet packages from the cloud providers (AWS, Azure, GCP).
* The documentation is at /WindowsBackup.docx
* The "WindowsBackup_Console" project covers all the core functionality. 
    * The Program.cs :: run_tests() shows how the components work. These tests have been commented out though.
    * The Test.cs requires various passwords and bucket names to work. Search for "xxxx" to identify areas that need to be filled out.
    

