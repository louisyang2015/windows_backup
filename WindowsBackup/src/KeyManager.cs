using System;
using System.Collections.Generic;

using System.Security.Cryptography; // for RNGCryptoServiceProvider
using System.Xml.Linq; // for XML


namespace WindowsBackup
{
  interface KeyManager
  {
    // Keys are stored as base64 encoded strings.
    string get_key_value(UInt16 key_number);
  }

  class BasicKeyManager : KeyManager
  {
    // Hash tables to store key information.
    // Keys are stored as base64 encoded strings.
    // They can be retrieved via 16 bit key numbers.
    Dictionary<UInt16, string> key_values = new Dictionary<ushort, string>();

    // A mechanism to locate key numbers by name. This is optional - not all keys have names.
    Dictionary<string, UInt16> key_numbers = new Dictionary<string, UInt16>(); 
    
    // Highest key number in use:
    UInt16 highest_key_number = 99; // first key number defaults to 100.

    /// <summary>
    /// Returns null if no such key exist.
    /// </summary>
    public string get_key_value(UInt16 key_number)
    {
      string value = null;
      key_values.TryGetValue(key_number, out value);
      return value;
    }
    
    /// <summary>
    /// The key names, if used, need to be unique. Returns true if 
    /// the given key_name can be used for a new key.
    /// </summary>
    public bool is_key_name_available(string key_name)
    {
      if (key_numbers.ContainsKey(key_name)) return false;
      else return true;
    }

    /// <summary>
    /// Adds a new key. A key name is optional. Returns the key number that 
    /// can be used to retrieve a key.
    /// </summary>
    public UInt16 add_key(string key_name = null)
    {
      // Check the name is unique
      if (key_name != null)
      {
        if (key_numbers.ContainsKey(key_name))
          throw new Exception("A key with the name \"" + key_name
            + "\" already exists. Please use another name.");
      }

      // Generate a 32 byte long key.
      byte[] b_array = new byte[32];

      using (var random = new RNGCryptoServiceProvider())
        random.GetBytes(b_array);

      highest_key_number++;
      key_values.Add(highest_key_number, Convert.ToBase64String(b_array));
      
      // Add the optional name.
      if (key_name != null)
        key_numbers.Add(key_name.Trim(), highest_key_number);

      return highest_key_number;
    }

    /// <summary>
    /// Return all key names. Note that not all keys have names.
    /// </summary>
    public List<string> get_key_names()
    {
      var all_names = new List<string>();
      foreach (var name in key_numbers.Keys)
        all_names.Add(name);

      return all_names;
    }

    /// <summary>
    /// Returns null if no such key exist.
    /// </summary>
    public UInt16? get_key_number(string key_name)
    {
      if (key_numbers.ContainsKey(key_name) == false) return null;
      return key_numbers[key_name];
    }


    
    public BasicKeyManager() { }

    public BasicKeyManager(XElement xml)
    {
      foreach(var tag in xml.Elements("key"))
      {
        string key_value = tag.Value;

        // key number is required        
        UInt16 key_number = UInt16.Parse(tag.Attribute("number").Value);

        key_values.Add(key_number, key_value);

        if (key_number > highest_key_number) highest_key_number = key_number;

        // key name is optional
        string key_name = null;
        if (tag.Attribute("name") != null)
        {
          key_name = tag.Attribute("name").Value;
          key_numbers.Add(key_name, key_number);
        }
      }
    }

    public XElement to_xml()
    {
      var basic_key_manager_tag = new XElement("BasicKeyManager");

      // Add keys with names first.
      var key_numbers_already_added = new HashSet<ushort>();

      foreach(var name in key_numbers.Keys)
      {
        // For each name, get the key_number
        UInt16 key_number = key_numbers[name];

        // and make sure this key_number has not been added already.
        if (key_values.ContainsKey(key_number)
          && key_numbers_already_added.Contains(key_number) == false)
        {
          string key_value = key_values[key_number];

          // Add this particular key to the XML.
          var key_tag = new XElement("key", key_value);
          key_tag.SetAttributeValue("number", key_number);
          key_tag.SetAttributeValue("name", name);

          basic_key_manager_tag.Add(key_tag);
          key_numbers_already_added.Add(key_number);
        }
      }

      // Now add keys without names.
      foreach(var number in key_values.Keys)
      {
        // For each key number, make sure it hasn't been added yet.
        if (key_numbers_already_added.Contains(number) == false)
        {
          // Add this key. There's no name.
          string key_value = key_values[number];

          var key_tag = new XElement("key", key_value);
          key_tag.SetAttributeValue("number", number);

          basic_key_manager_tag.Add(key_tag);
          key_numbers_already_added.Add(number);
        }
      }

      return basic_key_manager_tag;
    }
  }
}

