using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using System.Security.Cryptography; // for AesCryptoServiceProvider
using System.IO.Compression;  // for GZipStream


namespace WindowsBackup
{
  class EncryptStream : Stream
  {
    // Various streams, from the input file_stream to output mem_stream2.
    // See documentation for a diagram.

    // Operate on 64k chunks - due to observation of GZip stream.
    const int chunk_size = 64 * 1024;

    //   I did an experiment and saw GZip stream stored 56kB of stuff in the
    //   beginning. That is, 56kB went into GZip stream and only 10 bytes came 
    //   out. The next 8kB triggered output. 
    
    FileStream file_stream = null;
    GZipStream zip_stream = null;
    MyMemoryStream mem_stream1 = new MyMemoryStream(4 * chunk_size);
    CryptoStream crypto_stream = null;
    MyMemoryStream mem_stream2 = new MyMemoryStream(4 * chunk_size);

    // setting
    bool compress = false; // true means compression is in use

    // operational stage
    enum Stage { READ, FLUSHED, DONE };
    Stage stage = Stage.READ;
    // READ = Currently reading from the file_stream.
    //        This stage ends when there are no more bytes from file_stream.
    // FLUSHED = Flush first the zip_stream and then the crypto_stream.
    //           This stage ends when there are no more bytes in mem_stream2.
    // DONE = all data has been read

    // AWS requirement: Length and Position (read) properties
    long length = 0;
    public override long Length { get { return length; } }

    long position = 0; 
    public override long Position
    {
      get { return position; }
      set { throw new NotImplementedException(); }
    }
    
    // internal buffer
    byte[] internal_buffer = new byte[4 * chunk_size];



    /// <summary>
    /// This stream is meant to be read by others. Reading from
    /// this stream produces an encrypted representation of a file.
    /// The stream is reusable - call reset to configure it for next use.
    /// </summary>
    public EncryptStream()
    {
    }

    
    /// <summary>
    /// This stream will read in a file via file_path and encrypt
    /// its content using the base64_key. 
    /// </summary>
    /// <param name="file_path">The file that will be encrypted.</param>
    /// <param name="relative_path">Restoration information that will be stored in the header.</param>
    /// <param name="base64_key">Encryption key to use to encrypt file_path.</param>
    /// <param name="do_not_compress">If true, no compression will be attempted.</param>
    /// <param name="key_hint">Restoration information that will be stored in the header.</param>
    public void reset(string file_path, string relative_path, 
      string base64_key, bool do_not_compress = false, UInt16 key_hint = 0)
    {
      // operational stage
      stage = Stage.READ;
      
      // Setup file_stream
      file_stream = new FileStream(file_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
      var file_info = new FileInfo(file_path);

      // Setup compression
      compress = false; // default
      long file_length = file_info.Length; // the pre-encryption length
      
      if (do_not_compress == false)
      {
        var counting_stream = new CountingStream();       
        using (var zip_stream = new GZipStream(counting_stream, CompressionMode.Compress))
        {
          file_stream.CopyTo(zip_stream);
        }

        file_stream.Seek(0, SeekOrigin.Begin);

        // apply compression if the file can be shrunk by 5%
        if (counting_stream.Count < file_info.Length * 0.95)
        {
          compress = true;
          file_length = counting_stream.Count;
        }          
      }
         
      if (compress) zip_stream = new GZipStream(mem_stream1, CompressionMode.Compress);
      else zip_stream = null;

      // Setup mem_stream1, crypto_stream, mem_stream2
      mem_stream1.reset();
      
      var aes = new AesCryptoServiceProvider();
      aes.Key = Convert.FromBase64String(base64_key);

      var encryptor = aes.CreateEncryptor();
      crypto_stream = new CryptoStream(mem_stream2, encryptor, CryptoStreamMode.Write);

      mem_stream2.reset();
            
      // Initialize mem_stream2 with header
      
      // Version, flags, initialization vector
      mem_stream2.WriteByte(1);

      if (compress) mem_stream2.WriteByte(1);
      else mem_stream2.WriteByte(0);

      mem_stream2.Write(aes.IV, 0, 16);

      // Relative path length
      byte[] relative_path_bytes = Encoding.UTF8.GetBytes(relative_path);
      byte[] relative_path_length_bytes = BitConverter.GetBytes(
        (ushort)(relative_path_bytes.Length));
      mem_stream2.Write(relative_path_length_bytes, 0, 2);

      // pre-encryption file size, file modified time
      byte[] file_size_bytes = BitConverter.GetBytes(file_length);
      mem_stream2.Write(file_size_bytes, 0, 8);
      
      DateTime file_mod_time = file_info.LastWriteTimeUtc;
      byte[] file_mod_time_bytes = BitConverter.GetBytes(file_mod_time.Ticks);
      mem_stream2.Write(file_mod_time_bytes, 0, 8);
            
      // key hint
      byte[] key_hint_bytes = BitConverter.GetBytes(key_hint);
      mem_stream2.Write(key_hint_bytes, 0, 2);

      // relative path
      crypto_stream.Write(relative_path_bytes, 0, relative_path_bytes.Length);

      // Reset length and position.
      // All this is because of the AWS upload function :( 
      position = 0;

      // At this point, the file_length is the pre-encryption file length.
      // The encryption is on both the relative_path and file_length, so
      // the total pre-encryption stuff length is:
      long pre_encrypt_stuff_length = file_length + relative_path_bytes.Length;

      // The encryption will add a "padded_length".
      long padded_length = 16 - (pre_encrypt_stuff_length % 16);

      length = 38 + relative_path_bytes.Length + file_length + padded_length;

      // write file data into mem_stream2
      fill_mem_stream2_from_file(chunk_size);
    }

    /// <summary>
    /// Fill mem_stream2 with data by reading from file_stream. 
    /// The "max_count" is an upper limit on how many bytes to fill.
    /// Returns the number of bytes read from file_stream.
    /// </summary>
    int fill_mem_stream2_from_file(int max_count)
    {
      // Non-compression case.
      //   Do this first, since this is the straight forward case.
      int bytes_read_from_file = 0;

      if (compress == false)
      {
        // file_stream --> crypto_stream
        bytes_read_from_file = file_stream.Read(internal_buffer, 0, max_count);
        if (bytes_read_from_file == 0) return bytes_read_from_file;

        crypto_stream.Write(internal_buffer, 0, bytes_read_from_file);
        return bytes_read_from_file;
      }

      // The remainder of this function handles the compression case.

      int bytes_to_read = max_count;

      // Throttling case.
      //   This case is triggered if "chunk_size" is too small.
      //   The way zip_stream works, it stores data to learn a 
      //   pattern to do the compression. Then it sometimes 
      //   dump a lot of data into mem_stream1.
      if (mem_stream1.Count > max_count / 2)
        bytes_to_read = max_count / 2;

      // file_stream --> zip_stream
      bytes_read_from_file = file_stream.Read(internal_buffer, 0, bytes_to_read);
      zip_stream.Write(internal_buffer, 0, bytes_read_from_file);

      // mem_stream1 --> crypto_stream
      bytes_to_read = mem_stream1.Count;
      if (bytes_to_read > max_count) bytes_to_read = max_count;

      mem_stream1.Read(internal_buffer, 0, bytes_to_read);
      crypto_stream.Write(internal_buffer, 0, bytes_to_read);
                  
      return bytes_read_from_file;      
    }

    void transition_to_flushed_stage()
    {
      if (compress)
      {
        zip_stream.Dispose();

        // mem_stream1 --> crypto_stream
        int bytes_read = mem_stream1.Read(internal_buffer, 0, mem_stream1.Count);
        crypto_stream.Write(internal_buffer, 0, bytes_read);
      }

      crypto_stream.Dispose();
      file_stream.Dispose();
      file_stream = null;
      stage = Stage.FLUSHED;
    }
        
    public override int Read(byte[] buffer, int offset, int count)
    {
      if (stage == Stage.DONE) return 0;

      int bytes_copied = 0;

      do
      {
        if (stage == Stage.READ)
        {
          // Read from mem_stream2 to fulfill the read.
          int bytes_to_read = count - bytes_copied;
          int bytes_read = mem_stream2.Read(buffer, offset, bytes_to_read);

          bytes_copied += bytes_read;
          offset += bytes_read;

          if (bytes_copied < count)
          {
            // If reading from mem_stream2 is not sufficient, read from 
            // file_stream to replenish mem_stream2.
            bytes_read = fill_mem_stream2_from_file(chunk_size);
            if (bytes_read == 0) transition_to_flushed_stage();
          }
        }
        else if (stage == Stage.FLUSHED)
        {
          // Read from mem_stream2 to fulfill the read.
          int bytes_to_read = count - bytes_copied;
          int bytes_read = mem_stream2.Read(buffer, offset, bytes_to_read);

          bytes_copied += bytes_read;
          offset += bytes_read;

          if (bytes_copied < count)
          {
            // if even this step is unable to fulfill the "count" requirement
            stage = Stage.DONE;
          }
        }

        // keep doing this to satisfy the buffer, as long as:
        // bytes_copied < count, meaning more bytes are needed
        // AND stage != DONE, meaning we are not at end of file
      } while ((bytes_copied < count) && (stage != Stage.DONE));

      position += bytes_copied;

      return bytes_copied;
    }
       

    protected override void Dispose(bool disposing)
    {
      base.Dispose(disposing);

      if (disposing)
      {
        if (file_stream != null)
        {
          file_stream.Dispose();
          file_stream = null;
        }
      }
    }


    #region not implemented or True / False

    public override void Write(byte[] buffer, int offset, int count)
    {
      throw new NotImplementedException();
    }

    public override bool CanRead
    {
      get { return true; }
    }

    public override bool CanSeek
    {
      get { return false; }
    }

    public override bool CanWrite
    {
      get { return false; }
    }

    public override void Flush()
    {
      throw new NotImplementedException();
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



  class MyMemoryStream : Stream
  {
    // internal buffer and book keeping
    byte[] internal_buffer = null;
    int valid_bytes = 0;
    int position = 0;
    // valid_bytes = 10 means internal_buffer[9] is the last valid byte
    // position = 9 means next read(...) will take data out of internal_buffer[9]

    public int Count
    {
      get { return valid_bytes - position; }
    }


    /// <summary>
    /// Advance the "position". When all data is consumed, the 
    /// internal_buffer is reset so it can be reused.
    /// </summary>
    void advance_position(int increment)
    {
      position += increment;

      // if (position >= valid_bytes) ?? - nah, let it break
      if (position == valid_bytes)
      {
        valid_bytes = 0;
        position = 0;
      }
    }

    /// <summary>
    /// Move the unread data to the start of the internal_buffer, 
    /// so to reuse it.
    /// </summary>
    void repack()
    {
      if (position == 0) return; // unread data is already at the start of the buffer

      Buffer.BlockCopy(internal_buffer, position, internal_buffer, 0, (valid_bytes - position));
      valid_bytes -= position;
      position = 0;
    }

    /// <summary>
    /// Returns size of the internal_buffer.
    /// </summary>
    public int Capacity
    {
      get { return internal_buffer.Length; }
    }


    /// <summary>
    /// Resets internal state variables. This stream object is reusable. 
    /// </summary>
    public void reset()
    {
      valid_bytes = 0;
      position = 0;
    }


    public MyMemoryStream(int buffer_capacity)
    {
      internal_buffer = new byte[buffer_capacity];
    }

    /// <summary>
    /// Unlike the default MemoryStream, when all data has been read, 
    /// the internal_buffer status will be reset so that it can be reused.
    /// </summary>
    public override int ReadByte()
    {
      if (position == valid_bytes) return -1; // out of data case

      byte return_val = internal_buffer[position];

      advance_position(1);
      return return_val;
    }

    /// <summary>
    /// Unlike the default MemoryStream, when all data has been read, 
    /// the internal_buffer status will be reset so that it can be reused.
    /// </summary>
    public override int Read(byte[] buffer, int offset, int count)
    {
      if (position == valid_bytes) return 0; // out of data case

      // determine how many bytes can be copied
      int bytes_to_copy = count;
      if (bytes_to_copy > valid_bytes - position)
        bytes_to_copy = valid_bytes - position;

      Buffer.BlockCopy(internal_buffer, position, buffer, offset, bytes_to_copy);

      advance_position(bytes_to_copy);
      return bytes_to_copy;
    }

    /// <summary>
    /// Unlike the default MemoryStream, when the internal_buffer is all
    /// used up, this class will not expand it. Instead, repack the data.
    /// </summary>
    public override void WriteByte(byte value)
    {
      // check for buffer overflow
      if (valid_bytes + 1 > internal_buffer.Length)
      {
        repack();

        if (valid_bytes + 1 > internal_buffer.Length)
          throw new Exception("Software error. Buffer overflow inside class MyMemoryStream.");
      }

      internal_buffer[valid_bytes] = value;
      valid_bytes++;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
      // check for buffer overflow
      if (valid_bytes + count > internal_buffer.Length)
      {
        repack();
        if (valid_bytes + count > internal_buffer.Length)
          throw new Exception("Software error. Buffer overflow inside class MyMemoryStream.");
      }

      Buffer.BlockCopy(buffer, offset, internal_buffer, valid_bytes, count);
      valid_bytes += count;
    }

    #region Basic Implementations

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

    public override void Flush()
    {
      // throw new NotImplementedException();
    }

    public override long Length
    {
      get { throw new NotImplementedException(); }
    }

    public override long Position
    {
      get { throw new NotImplementedException(); }
      set { throw new NotImplementedException(); }
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



  class CountingStream : Stream
  {
    long count = 0;
    public long Count { get { return count; } }
    public void clear() { count = 0; }

    public override void Write(byte[] buffer, int offset, int count)
    {
      this.count += count;
    }

    public override void WriteByte(byte value)
    {
      count++;
    }

    #region Not implemented, True / False

    public override int Read(byte[] buffer, int offset, int count)
    {
      throw new NotImplementedException();
    }

    public override bool CanRead
    {
      get { return false; }
    }

    public override bool CanWrite
    {
      get { return true; }
    }

    public override bool CanSeek
    {
      get { return false; }
    }

    public override void Flush()
    {
    }

    public override long Length
    {
      get { throw new NotImplementedException(); }
    }

    public override long Position
    {
      get { throw new NotImplementedException(); }
      set { throw new NotImplementedException(); }
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


  
  class DecryptStream : Stream
  {
    // Various streams, from the input mem_stream1 to output file_stream
    // See documentation for a diagram.

    // Operate on 32k chunks - due to observation of GZip stream.
    const int chunk_size = 32 * 1024;

    // This program uses compression if the file size can be reduced by
    // 5% or more. That means on average, reading 100 bytes from the
    // zip_stream requires 95 bytes of input. However, compression 
    // ratio is not uniform.

    // Decompression happens by reading from the GZip stream.

    // During testing, when reading in 8kB chunks from GZip stream, 
    // the most GZip stream will consume is 16kB. This means occasionally,
    // reading a 8kB chunk from GZip stream causes it to read 16kB from
    // the underlying source.

    // Likewise, when reading in 64kB chunks from GZip stream, it will 
    // at most read 72kB chunks from the underlying source. Vast 
    // majority of the time though it will use less than 64kB.

    // Observation is that larger chunks will make GZip stream more 
    // consistent in its demand of the underlying source. Also, the
    // overflow seems to be limited to 8kB.

    MyMemoryStream mem_stream1 = new MyMemoryStream(chunk_size * 4);
    AesCryptoServiceProvider aes = null;
    CryptoStream crypto_stream = null;
    MyMemoryStream mem_stream2 = new MyMemoryStream(chunk_size * 4);
    GZipStream zip_stream = null;
    FileStream file_stream = null;

    // Encryption key management
    string base64_key = null;
    KeyManager key_manager = null; // converts 2 byte key_hint into key

    // internal buffer
    byte[] internal_buffer = new byte[chunk_size * 4];

    // The unencrypted part of the header
    List<byte> header_bytes = new List<byte>();

    // Header information
    const int header_length = 38;
    bool compress = false; 
    UInt16 relative_path_length = 0;
    long file_pre_encrypt_size = 0;
    DateTime? file_modified_time = null;
    UInt16 key_hint = 0;
    string relative_path = null;

    // Keep track of how many bytes, post decryption
    long file_bytes_decrypted = 0;

    // After obtaining the relative_path, this class will have to request the
    // full_path from the application.
    public delegate string FullPathRequest_handler(string relative_path, 
      DateTime? file_modified_time, long file_pre_encrypt_size,
      long total_file_length);

    FullPathRequest_handler full_path_request;
    string full_path = null;

    // for progress indicator
    public long BytesToBeDecrypted { get { return file_pre_encrypt_size; } }
    public long BytesDecrypted { get { return file_bytes_decrypted; } }


    /// <summary>
    /// This stream is meant to be written to by others. The stream 
    /// is reusable - call reset to configure it for next use.
    /// </summary>
    /// <param name="key_manager">An object that converts a 2 byte key_hint
    /// into a 32 byte key. This object can be null, in which case the key
    /// is provided via the reset(...) function.</param>
    public DecryptStream(KeyManager key_manager = null)
    {
      this.key_manager = key_manager;
    }

    /// <summary>
    /// Sets up this stream to decrypt a file using the base64_key. 
    /// The output file will be at "folder_path" + the "relative_path"
    /// that is stored in the encrypted file's header.
    /// </summary>
    /// <param name="base64_key">The key used for decryption. If relying on
    /// the header's key_hint mechanism, put a null for this field.</param>
    /// <param name="full_path_request">A delegate function that takes the 
    /// information found in the header and generate a full file path.</param>
    public void reset(string base64_key, FullPathRequest_handler full_path_request)
    {
      this.base64_key = base64_key;
      this.full_path_request = full_path_request;

      // Reset various streams.
      mem_stream1.reset();

      aes = new AesCryptoServiceProvider();
      // Don't apply key right away:
      // aes.Key = Convert.FromBase64String(base64_key);
      // Key application occurs in process_unenrypted_header_bytes(), 
      // after extracting the key_hint from the header.

      mem_stream2.reset();

      // The following are allocated in process_unenrypted_header_bytes():
      // aes.IV
      crypto_stream = null;
      zip_stream = null;

      // The following is allocated in extract_relative_path():
      file_stream = null;

      // The unencrypted part of the header
      header_bytes.Clear();

      // Header information reset
      compress = false;
      relative_path_length = 0;
      file_pre_encrypt_size = 0;
      file_modified_time = null;
      key_hint = 0;
      relative_path = null;

      file_bytes_decrypted = 0;
    }

    /// <summary>
    /// Read the header bytes stored at mem_stream1.
    /// </summary>
    void process_unenrypted_header_bytes()
    {
      // Check that there is sufficient data.
      if (mem_stream1.Count < header_length)
        throw new Exception("There is too little data. The file is assumed to have "
          + "at least " + header_length + " bytes. Instead there is only "
          + mem_stream1.Count + " bytes.");

      // mem_stream1 --> header_bytes
      for (int i = 0; i < header_length; i++)
        header_bytes.Add((byte)mem_stream1.ReadByte());

      // Version should be 1.
      if (header_bytes[0] != 1)
        throw new Exception("This software supports version 1 only. Version "
          + (int)header_bytes[0] + " found instead.");

      // Compress
      if (header_bytes[1] == 1)
      {
        compress = true;
        zip_stream = new GZipStream(mem_stream2, CompressionMode.Decompress);
      }

      // IV, relative path length, file size, file modified time, key hint
      byte[] header_b_array = header_bytes.ToArray();

      byte[] iv = new byte[16];
      Buffer.BlockCopy(header_b_array, 2, iv, 0, 16);

      // The following does not work at this point - due to the lack of a key
      // aes.IV = iv;
      // var decryptor = aes.CreateDecryptor(); // <-- this won't work

      // So the IV obtained above is applied after processing key_hint
          
      relative_path_length = BitConverter.ToUInt16(header_b_array, 18);

      file_pre_encrypt_size = BitConverter.ToInt64(header_b_array, 20);

      long ticks = BitConverter.ToInt64(header_b_array, 28);
      // ticks should never be zero, and file_modified_time should never be null
      // Nevertheless:
      if (ticks != 0) file_modified_time = new DateTime(ticks);

      key_hint = BitConverter.ToUInt16(header_b_array, 36);

      // Apply the key_hint, IF it is not zero.
      string new_key = null;

      if (key_manager != null && key_hint != 0)
        new_key = key_manager.get_key_value(key_hint);

      // So now there are two keys: base64_key via reset(...) and new_key via key_manager.
      // Prefer new_key to base64_key
      if (new_key != null)
        aes.Key = Convert.FromBase64String(new_key);
      else if (base64_key != null)
        aes.Key = Convert.FromBase64String(base64_key);
      else
        throw new Exception("Error while processing an encrypted file. While "
          + "processing the unencrypted header bytes, encountered key_hint of "
          + key_hint + "and both cannot find the necessary key, and the default "
          + "key is invalid.");

      // At this point the "key" issue is resolved. Now can call "aes.CreateDecryptor()".
      aes.IV = iv;
      var decryptor = aes.CreateDecryptor();
      crypto_stream = new CryptoStream(mem_stream1, decryptor, CryptoStreamMode.Read);
    }


    void extract_relative_path()
    {
      // Decrypt data from mem_stream1 and move it to mem_stream2.
      // There are two different cases, depending on file_pre_encrypt_size.

      // Case 1 - the file is very small.
      if (file_pre_encrypt_size <= 48)
      {
        // Extract both the relative_path and file content together.

        // Check for necessary size.
        int total_bytes = (int)(relative_path_length + file_pre_encrypt_size);
        if (mem_stream1.Count < total_bytes)
          throw new Exception("Insufficient data received. Expecting a total of "
            + total_bytes + " but only received " + mem_stream1.Count + " bytes.");

        // Decrypt all bytes, both the relative_path and file content.
        int bytes_decrypted = decrypt_to_mem_stream2(total_bytes, decrypt_multiples_of_16: false);

        // Update file_bytes_decrypted, relative path bytes don't count.
        file_bytes_decrypted += (bytes_decrypted - relative_path_length);
      }
      // Case 2 - standard case
      else
      {
        // Decryption must happen in multiple of 16 bytes.
        int bytes_to_decrypt = relative_path_length;
        if (bytes_to_decrypt % 16 != 0)
          bytes_to_decrypt += (16 - (bytes_to_decrypt % 16));

        // Check for sufficient data in mem_stream1.
        if (mem_stream1.Count < bytes_to_decrypt)
          throw new Exception("Insufficient data received. Attempting to decode "
            + "the relative path and expecting " + bytes_to_decrypt 
            + " of data, but only have " + mem_stream1.Count + " bytes.");

        int bytes_decrypted = decrypt_to_mem_stream2(bytes_to_decrypt);

        // Update file_bytes_decrypted, relative path bytes don't count.
        file_bytes_decrypted += (bytes_decrypted - relative_path_length);
      }

      // Remove the relative_path from mem_stream2 and decode.
      mem_stream2.Read(internal_buffer, 0, relative_path_length);
      relative_path = Encoding.UTF8.GetString(internal_buffer, 0, relative_path_length);

      // It's GCP's download requirement to know the total file length
      long pre_encrypt_size = file_pre_encrypt_size + relative_path_length;
      long post_encrypt_size = pre_encrypt_size + (16 - (pre_encrypt_size % 16));
      long total_file_length = 38 + post_encrypt_size;

      // Provide information to the caller, and obtain destination path, via "full_path_request".
      full_path = full_path_request(relative_path, file_modified_time, 
        file_pre_encrypt_size, total_file_length);

      // Open file_stream
      if (full_path != null)
      {
        // Create the directory if it does not already exist.
        var file_info = new FileInfo(full_path);
        file_info.Directory.Create();

        file_stream = new FileStream(full_path, FileMode.Create, FileAccess.Write);
      }
    }

    /// <summary>
    /// Process the data in mem_stream1, allowing this stream to
    /// accept more writes.
    /// </summary>
    void process_data()
    {
      // It's assumed that even on the very first pass, the header
      // and relative_path can be extracted in its entirety. This
      // is due to the chunk size being so much larger than the 
      // header size and relative_path_length.

      // So only the first call to process_data() will trigger
      // "process_unenrypted_header_bytes()" and "extract_relative_path()".
      // Later calls will skip to "if (full_path == null)"

      // Process the unencrypted header bytes, if needed.
      if (header_bytes.Count < header_length)
        process_unenrypted_header_bytes();
      
      // Process the relative_path, if needed.
      if (relative_path == null)
        extract_relative_path();

      // Inside extract_relative_path() there would have been a call to
      // full_path_request(...).
      // If only header information is needed, empty mem_stream1 and exit.
      if (full_path == null)
      {
        mem_stream1.Read(internal_buffer, 0, mem_stream1.Count);
        return;
      }

      // Process data in mem_stream1 so that more data can be written to it.
      // The last 32 bytes is special, due to the nature of AES encryption.
      int bytes_to_decrypt = mem_stream1.Count - 32;
      if (bytes_to_decrypt < 16) return;

      int bytes_decrypted = decrypt_to_mem_stream2(bytes_to_decrypt);
      file_bytes_decrypted += bytes_decrypted;

      if (compress)
      {
        // Compression, then zip_stream --> file_stream
        copy_zip_stream_to_file_stream();
      }
      else
      {
        // No compression, then mem_stream2 --> file_stream
        copy_mem_stream2_to_file_stream();
      }
    }

    /// <summary>
    /// Decrypt from mem_stream1 and put the result in mem_stream2. Except at 
    /// the very end of a file, decryption is done in multiple of 16 bytes.
    /// </summary>
    /// <param name="bytes_to_decrypt">This need not be multiple of 16.</param>
    /// <param name="decrypt_multiples_of_16">If necessary, this function can round down 
    /// bytes_to_decrypt to a multiple of 16.</param>
    /// <returns>Returns the number of bytes decrypted.</returns>
    int decrypt_to_mem_stream2(int bytes_to_decrypt, bool decrypt_multiples_of_16 = true)
    {
      // Decrypt in multiples of 16 bytes if necessary.
      if (decrypt_multiples_of_16)
      {
        if (bytes_to_decrypt % 16 != 0)
          bytes_to_decrypt -= (bytes_to_decrypt % 16);
      }
      
      int bytes_read = crypto_stream.Read(internal_buffer, 0, bytes_to_decrypt);
      mem_stream2.Write(internal_buffer, 0, bytes_read);

      return bytes_read;
    }

    /// <summary>
    /// Read from zip_stream and write to file_stream.
    /// </summary>
    /// <param name="read_all_data">By default, leave some data unread to prevent 
    /// over-requesting mem_stream2.</param>
    void copy_zip_stream_to_file_stream(bool read_all_data = false)
    {
      // First decide how much data to leave in mem_stream2.
      int remainder_data = 0; // for "read_all_data" == true

      if (read_all_data == false)
      {
        // Experiments indicate that GZip stream at most request
        //   8kB extra. That is, if I request X kB from GZip stream, 
        //   it will request at most (X + 8) kB from mem_stream2.
        //   This is taken into account when chunk_size is selected.
        //
        // Leave sufficient data in mem_stream2 to avoid over-requesting
        //   data from zip_stream.
        remainder_data = chunk_size + chunk_size / 2;
      }

      while(mem_stream2.Count > remainder_data)
      {
        int bytes_read = zip_stream.Read(internal_buffer, 0, chunk_size);
        file_stream.Write(internal_buffer, 0, bytes_read);
      }

      if (read_all_data)
      {
        // When read_all_data is true, remainder_data = 0.
        // The above "while(mem_stream2.Count > remainder_data)" will
        // empty out mem_stream2, but not necessarily zip_stream.
        int bytes_read = 1;
        while(bytes_read > 0)
        {
          bytes_read = zip_stream.Read(internal_buffer, 0, chunk_size);
          file_stream.Write(internal_buffer, 0, bytes_read);
        }
      }
    }

    /// <summary>
    /// Read all data in mem_stream2 and write them to file_stream.
    /// </summary>
    void copy_mem_stream2_to_file_stream()
    {
      int bytes_to_read = mem_stream2.Count;
      mem_stream2.Read(internal_buffer, 0, bytes_to_read);
      file_stream.Write(internal_buffer, 0, bytes_to_read);
    }

    /// <summary>
    /// Process the final 32 bytes in mem_stream1, and any left over
    /// bytes anywhere else.
    /// </summary>
    void process_data_final()
    {
      // Encrypted data should be in a multiple of 16 bytes.
      // Standard case: the process_data() function should leave exactly 32 
      //   bytes in mem_stream1.
      // Alternatively, if the file is very short, mem_stream1 is cleaned out
      //   by extract_relative_path(), and its size is zero.
      if (mem_stream1.Count != 32 && mem_stream1.Count != 0)
        throw new Exception("Data error while decrypting the final bytes of a file. "
          + "Expecting 32 byte of data but instead received " 
          + mem_stream1.Count + " bytes.");

      // Decrypt last 32 bytes in mem_stream1 --> mem_stream2.
      if (mem_stream1.Count == 32)
      {
        int bytes_to_decrypt = (int)(file_pre_encrypt_size - file_bytes_decrypted);

        try
        {
          int bytes_decrypted = decrypt_to_mem_stream2(bytes_to_decrypt, decrypt_multiples_of_16: false);
          file_bytes_decrypted += bytes_decrypted;
        }
        catch (Exception ex)
        {
          file_stream.Dispose();
          file_stream = null;

          throw new Exception("Error while decrypting the end of file. System message: " + ex.Message);
        }
      }


      if (compress)
        // Compression, then zip_stream --> file_stream
        copy_zip_stream_to_file_stream(read_all_data: true);
      else
        // No compression, then mem_stream2 --> file_stream
        copy_mem_stream2_to_file_stream();

      file_stream.Dispose();
      file_stream = null;
    }


    public override void Write(byte[] buffer, int offset, int count)
    {
      int bytes_processed = 0;

      while (bytes_processed < count)
      {
        // clear out mem_stream1 before writing more data to it
        while (mem_stream1.Count >= chunk_size)
          process_data();

        // buffer --> mem_stream1
        int bytes_to_write = count - bytes_processed;
        if (bytes_to_write > chunk_size) bytes_to_write = chunk_size;
        mem_stream1.Write(buffer, offset, bytes_to_write);

        bytes_processed += bytes_to_write;
        offset += bytes_to_write;

        // note: bytes_processed refers to data moved from "buffer" to 
        // "mem_stream1", not to data handled by "process_data()".

        // In the very beginning, the application code downloads 1KB
        // from the cloud and needs to process the header ASAP. The
        // above code will not trigger "process_data()", since the
        // chunk_size > 1KB.
        process_data();
      }
    }

    public override void WriteByte(byte value)
    {
      mem_stream1.WriteByte(value);
      if (mem_stream1.Count >= chunk_size)
        process_data();
    }


    protected override void Dispose(bool disposing)
    {
      base.Dispose(disposing);

      if (disposing)
      {
        if (file_stream != null)
        {
          file_stream.Dispose();
          file_stream = null;
        }
      }
    }

    /// <summary>
    /// This needs to be called to completely process the data in various buffers.
    /// </summary>
    public override void Flush()
    {
      if (mem_stream1.Count > 32)
        process_data();

      process_data_final();
    }


    #region not implemented or True / False

    public override int Read(byte[] buffer, int offset, int count)
    {
      throw new NotImplementedException();
    }

    public override bool CanRead
    {
      get { return false; }
    }

    public override bool CanSeek
    {
      get { return false; }
    }

    public override bool CanWrite
    {
      get { return true; }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
      throw new NotImplementedException();
    }

    public override void SetLength(long value)
    {
      throw new NotImplementedException();
    }

    public override long Length
    {
      get { throw new NotImplementedException(); }
    }

    public override long Position
    {
      get { throw new NotImplementedException(); }

      set { throw new NotImplementedException(); }
    }

    #endregion
  }
  
}
