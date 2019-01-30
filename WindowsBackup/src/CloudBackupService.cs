using System;
using System.IO;
using System.Collections.Generic;

using System.Xml.Linq; // for XML

using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace WindowsBackup
{
  public interface CloudBackupService
  {
    /// <summary>
    /// List objects in a bucket. Set max_objects = null to have no upper limit.
    /// </summary>
    List<string> list_objects(string bucket_name, int? max_objects);

    /// <summary>
    /// Upload a stream to (bucket_name \ key_name).
    /// </summary>
    void upload(Stream stream, string bucket_name, string key_name);

    /// <summary>
    /// Download from (bucket_name \ key_name) to the given stream.
    /// </summary>
    void download(string bucket_name, string key_name, Stream stream);

    void download(string bucket_name, string key_name, Stream stream, long start, long length);

    /// <summary>
    /// Delete the given (bucket_name \ key_names[]) objects.
    /// </summary>
    void delete(string bucket_name, params string[] key_names);

    /// <summary>
    /// Returns XML representation.
    /// </summary>
    XElement to_xml();

    string Name { get; }
  }


  #region AWS S3

  public class AWS_CloudBackupService : CloudBackupService
  {
    readonly string id, secret_key, region;

    AmazonS3Client client;
    TransferUtility transfer_utility;

    readonly string name = "";
    public string Name { get { return name; } }

    public AWS_CloudBackupService(string id, string secret_key, string region, string name="")
    {
      this.id = id;
      this.secret_key = secret_key;
      this.region = region;
      this.name = name;
      init();
    }

    public AWS_CloudBackupService(XElement xml)
    {
      id = xml.Element("id").Value;
      secret_key = xml.Element("secret_key").Value;
      region = xml.Element("region").Value;
      name = xml.Attribute("name").Value;

      init();
    }

    void init()
    {
      try
      {
        client = new AmazonS3Client(id, secret_key, Amazon.RegionEndpoint.GetBySystemName(region));
        transfer_utility = new TransferUtility(client);
      }
      catch (Exception ex)
      {
        // (Jan 28, 2018): Creating AWS S3 Client always seems to work, even if
        // the secret_key is wrong. This might never get triggered.
        throw new Exception("Failed to connect to AWS S3. Error message from AWS: "
          + ex.Message);
      }
    }

    public XElement to_xml()
    {
      var aws_tag = new XElement("AWS_CloudBackupService");
      aws_tag.Add(new XElement("id", id));
      aws_tag.Add(new XElement("secret_key", secret_key));
      aws_tag.Add(new XElement("region", region));
      aws_tag.SetAttributeValue("name", name);

      return aws_tag;
    }

    public List<string> list_objects(string bucket_name, int? max_objects)
    {
      try
      {
        var object_list = new List<string>();
        if (max_objects == 0) return object_list;

        var request = new ListObjectsV2Request
        {
          BucketName = bucket_name,
          // MaxKeys = ... - defaults to 1000
        };

        ListObjectsV2Response response;
        do
        {
          response = client.ListObjectsV2(request);

          foreach (var entry in response.S3Objects)
          {
            object_list.Add(entry.Key);
            if (object_list.Count == max_objects) return object_list;
          }
          // there's also entry.Size

          request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);

        return object_list;
      }
      catch(Exception ex)
      {
        throw new Exception("Error while trying to list objects in the AWS bucket \""
          + bucket_name + "\". AWS error message: " + ex.Message);
      }
    }
        
    public void upload(Stream stream, string bucket_name, string key_name)
    {
      try
      {
        transfer_utility.Upload(stream, bucket_name, key_name);
      }
      catch (Exception ex)
      {
        throw new Exception("Error while trying to upload to AWS \""
          + bucket_name + "\\" + key_name + "\". AWS error message: "
          + ex.Message);
      }
    }
        
    public void download(string bucket_name, string key_name, Stream stream)
    {
      try
      {
        GetObjectRequest request = new GetObjectRequest
        {
          BucketName = bucket_name,
          Key = key_name
        };

        using (GetObjectResponse response = client.GetObject(request))
        using (Stream response_stream = response.ResponseStream)
        {
          // the title is @ response.Metadata["x-amz-meta-title"];
          response_stream.CopyTo(stream);
          stream.Flush();
        }
      }
      catch (Exception ex)
      {
        throw new Exception("Error while trying to download from AWS \""
          + bucket_name + "\\" + key_name + "\". AWS error message: "
          + ex.Message);
      }
    }

    /// <summary>
    /// This version of download() gets a range of bytes. Since this is a partial download,
    /// this function DOES NOT flush the stream object.
    /// </summary>
    public void download(string bucket_name, string key_name, Stream stream, long start, long length)
    {
      try
      {
        GetObjectRequest request = new GetObjectRequest
        {
          BucketName = bucket_name,
          Key = key_name,
          ByteRange = new ByteRange(start, start + length - 1)
        };

        using (GetObjectResponse response = client.GetObject(request))
        using (Stream response_stream = response.ResponseStream)
        {
          // the title is @ response.Metadata["x-amz-meta-title"];
          response_stream.CopyTo(stream);
        }
      }
      catch (Exception ex)
      {
        throw new Exception("Error while trying to download from AWS \""
          + bucket_name + "\\" + key_name + "\". AWS error message: "
          + ex.Message);
      }
    }


    public void delete(string bucket_name, params string[] key_names)
    {
      try
      {
        // the "DeleteObjectsRequest" has a 1000 element limit, per:
        // https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/S3/TDeleteObjectsRequest.html

        for (int i = 0; i < key_names.Length; i += 900) // using 900 instead of 1000
        {
          int keys_to_add = key_names.Length - i;
          if (keys_to_add > 900) keys_to_add = 900;

          var request = new DeleteObjectsRequest();
          request.BucketName = bucket_name;

          for (int j = 0; j < keys_to_add; j++)
            request.AddKey(key_names[i + j], null); // version ID is null.

          var response = client.DeleteObjects(request);

          // As of Jan 28, 2018
          // Deleting objects "works" even if the objects don't exist
          // No error will be reported.
          if (response.DeleteErrors.Count > 0)
            throw new Exception(response.DeleteErrors.Count +
              " error(s) while deleting objects from the AWS bucket \""
              + bucket_name + "\".");
        }
      }
      catch (Exception ex)
      {
        throw new Exception("Error while trying to delete from AWS \""
          + bucket_name + "\". AWS error message: " + ex.Message);
      }
    }
  }

  #endregion


  #region GCP Storage

  public class GCP_CloudBackupService : CloudBackupService
  {
    readonly string gcp_credentials;
    StorageClient client;

    readonly string name = "";
    public string Name { get { return name; } }

    public GCP_CloudBackupService(string gcp_credentials, string name = "")
    {
      this.gcp_credentials = gcp_credentials;
      this.name = name;
      init();
    }

    public GCP_CloudBackupService(XElement xml)
    {
      gcp_credentials = xml.Value;
      name = xml.Attribute("name").Value;
      init();
    }

    void init()
    {
      try
      {
        var credential = GoogleCredential.FromJson(gcp_credentials);
        client = StorageClient.Create(credential);
      }
      catch (Exception ex)
      {
        throw new Exception("Failed to connect to GCP Storage. Error message from GCP: "
          + ex.Message);
      }
    }

    public XElement to_xml()
    {
      var tag = new XElement("GCP_CloudBackupService", gcp_credentials);
      tag.SetAttributeValue("name", name);
      return tag;
    }

    public List<string> list_objects(string bucket_name, int? max_objects)
    {
      try
      {
        var object_list = new List<string>();
        if (max_objects == 0) return object_list;

        foreach (var obj in client.ListObjects(bucket_name))
        {
          object_list.Add(obj.Name);
          if (object_list.Count == max_objects) return object_list;
        }

        return object_list;
      }
      catch (Exception ex)
      {
        throw new Exception("Error while trying to list objects in the GCP bucket \""
          + bucket_name + "\". GCP error message: " + ex.Message);
      }
    }

    public void upload(Stream stream, string bucket_name, string key_name)
    {
      try
      {
        client.UploadObject(bucket_name, key_name, null, stream);
      }
      catch (Exception ex)
      {
        throw new Exception("Error while trying to upload to GCP \""
          + bucket_name + "\\" + key_name + "\". GCP error message: "
          + ex.Message);
      }
    }

    public void download(string bucket_name, string key_name, Stream stream)
    {
      try
      {
        client.DownloadObject(bucket_name, key_name, stream);
        stream.Flush();
      }
      catch (Exception ex)
      {
        throw new Exception("Error while trying to download from GCP \""
          + bucket_name + "\\" + key_name + "\". GCP error message: "
          + ex.Message);
      }
    }

    /// <summary>
    /// This version of download() gets a range of bytes. Since this is a partial download,
    /// this function DOES NOT flush the stream object.
    /// </summary>
    public void download(string bucket_name, string key_name, Stream stream, long start, long length)
    {
      try
      {
        var download_options = new DownloadObjectOptions();
        download_options.Range = new System.Net.Http.Headers.RangeHeaderValue(start, start + length - 1);
        client.DownloadObject(bucket_name, key_name, stream, download_options);
      }
      catch (Exception ex)
      {
        throw new Exception("Error while trying to download from GCP \""
          + bucket_name + "\\" + key_name + "\". GCP error message: "
          + ex.Message);
      }
    }

    public void delete(string bucket_name, params string[] key_names)
    {
      foreach (string key_name in key_names)
      {
        try
        {
          client.DeleteObject(bucket_name, key_name);
        }
        catch (Exception ex)
        {
          throw new Exception("Error while trying to delete \"" + bucket_name
            + "\\" + key_name + "\" from GCP. GCP error message: " + ex.Message);
        }
      }
    }
  }

  #endregion


  #region Azure Storage

  class AzureBlob_CloudBackupService : CloudBackupService
  {
    readonly string connection_string;
    CloudBlobClient client;

    readonly string name = "";
    public string Name { get { return name; } }

    public AzureBlob_CloudBackupService(string connection_string, string name = "")
    {
      this.connection_string = connection_string;
      this.name = name;
      init();
    }

    public AzureBlob_CloudBackupService(XElement xml)
    {
      connection_string = xml.Value;
      name = xml.Attribute("name").Value;
      init();
    }

    void init()
    {
      try
      {
        // note the .Trim() operation - Azure seems to be picky about white spaces
        var storage_account = CloudStorageAccount.Parse(connection_string.Trim());
        client = storage_account.CreateCloudBlobClient();
      }
      catch (Exception ex)
      {
        throw new Exception("Failed to connect to Azure Blob Storage. Error message from Azure: "
          + ex.Message);
      }
    }

    public XElement to_xml()
    {
      var tag = new XElement("AzureBlob_CloudBackupService", connection_string);
      tag.SetAttributeValue("name", name);
      return tag;
    }

    public List<string> list_objects(string bucket_name, int? max_objects)
    {
      try
      {
        var object_list = new List<string>();
        if (max_objects == 0) return object_list;

        var container = client.GetContainerReference(bucket_name);

        BlobContinuationToken continuation_token = null;
        do
        {
          var blob_segment = container.ListBlobsSegmented(null, continuation_token);
          continuation_token = blob_segment.ContinuationToken;

          foreach (var item in blob_segment.Results)
          {
            // item.Uri looks like:
            // https://xxx.blob.core.windows.net/xxx/xxx
            string key_name = Path.GetFileName(item.Uri.ToString());
            object_list.Add(key_name);
            if (object_list.Count == max_objects) return object_list;
          }

        } while (continuation_token != null);

        return object_list;
      }
      catch (Exception ex)
      {
        throw new Exception("Error while trying to list objects in the Azure container \""
          + bucket_name + "\". Azure error message: " + ex.Message);
      }
    }

    public void upload(Stream stream, string bucket_name, string key_name)
    {
      try
      {
        var container = client.GetContainerReference(bucket_name);
        var blob = container.GetBlockBlobReference(key_name);
        blob.UploadFromStream(stream);
      }
      catch (Exception ex)
      {
        throw new Exception("Error while trying to upload to Azure \""
          + bucket_name + "\\" + key_name + "\". Azure error message: "
          + ex.Message);
      }
    }

    public void download(string bucket_name, string key_name, Stream stream)
    {
      try
      {
        var container = client.GetContainerReference(bucket_name);
        var blob = container.GetBlockBlobReference(key_name);

        blob.DownloadToStream(stream);
        stream.Flush();
      }
      catch(Exception ex)
      {
        throw new Exception("Error while trying to download from Azure \""
          + bucket_name + "\\" + key_name + "\". Azure error message: "
          + ex.Message);
      }
    }

    /// <summary>
    /// This version of download() gets a range of bytes. Since this is a partial download,
    /// this function DOES NOT flush the stream object.
    /// </summary>
    public void download(string bucket_name, string key_name, Stream stream, long start, long length)
    {
      try
      {
        var container = client.GetContainerReference(bucket_name);
        var blob = container.GetBlockBlobReference(key_name);

        blob.DownloadRangeToStream(stream, start, length);
      }
      catch (Exception ex)
      {
        throw new Exception("Error while trying to download from Azure \""
          + bucket_name + "\\" + key_name + "\". Azure error message: "
          + ex.Message);
      }
    }

    public void delete(string bucket_name, params string[] key_names)
    {
      var container = client.GetContainerReference(bucket_name);

      foreach (string key_name in key_names)
      {
        try
        {
          var blob = container.GetBlockBlobReference(key_name);
          blob.DeleteIfExists();

          // no need to check for delete success??
          // If there's the need, then:
          // bool deleted = blob.DeleteIfExists();
          // Console.WriteLine("blob.DeleteIfExists() returns " + deleted);
          // This returns True for the first time only
        }
        catch (Exception ex)
        {
          throw new Exception("Error while trying to delete \"" + bucket_name
            + "\\" + key_name + "\" from Azure. Azure error message: " + ex.Message);
        }

        
      }
    }
  }

  #endregion



  /// <summary>
  /// A dummy stream to see how the cloud storage providers utilize the stream.
  /// </summary>
  class TestStream : Stream
  {
    int counter = 0;
    // const int upper_limit = 100;
    // const int upper_limit = 100 * 1024;
    const int upper_limit = 1024 * 1024;
    // const int upper_limit = 10 * 1024 * 1024;
    // const int upper_limit = 30 * 1024 * 1024; // only for Google, to see the write limit

    /// <summary>
    /// Returns a sequence, up to the point of "upper_limit".
    /// </summary>
    public override int Read(byte[] buffer, int offset, int count)
    {
      int bytes_written = 0;
      for (int i = 0; i < count; i++)
      {
        if (counter + i <= upper_limit)
        {
          buffer[offset + i] = (byte)(counter + i);
          bytes_written++;
        }
        else
          break;
      }

      counter += bytes_written;
      Console.WriteLine(bytes_written + " bytes returned in response to Read(...)");

      return bytes_written;
    }

    /// <summary>
    /// Print out what is being written to this stream.
    /// </summary>
    public override void Write(byte[] buffer, int offset, int count)
    {
      // Print out the values received.
      Console.Write("Received " + count + " bytes: ");

      if (count <= 6)
      {
        // Print all bytes received.
        for (int i = 0; i < count; i++)
          Console.Write((int)buffer[offset + i] + " ");
      }
      else
      {
        // Print out the first and last few bytes received.
        for (int i = 0; i < 3; i++)
          Console.Write((int)buffer[offset + i] + " ");

        Console.Write(" ... ");

        for (int i = count - 3; i < count; i++)
          Console.Write((int)buffer[offset + i] + " ");
      }

      Console.WriteLine();
    }

    public override long Length
    {
      get
      {
        Console.WriteLine("Length = " + (upper_limit + 1) + " has been returned.");
        return upper_limit + 1;
      }
    }

    public override long Position
    {
      get
      {
        Console.WriteLine("Position = " + counter + " has been returned.");
        return counter;
      }
      set { throw new NotImplementedException(); }
    }

    public override void Flush()
    {
      Console.WriteLine("Flush() called.");
    }

    protected override void Dispose(bool disposing)
    {
      Console.WriteLine("Dispose(" + disposing + ") called.");
    }


    #region Not implemented / true / false

    public override bool CanRead
    {
      get { return true; }
    }

    public override bool CanWrite
    {
      get { return true; }
    }

    public override bool CanSeek
    {
      get { return false; }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
      throw new NotImplementedException();
    }

    public override void SetLength(long value)
    {
      throw new NotImplementedException();
    }

    #endregion
  }

}
