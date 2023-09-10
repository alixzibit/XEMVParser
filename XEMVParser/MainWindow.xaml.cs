using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using System.Xml.Linq;
using Microsoft.Win32;
using System.Diagnostics;

namespace XEMVParser
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {

        public event PropertyChangedEventHandler PropertyChanged;

        private bool _isDataGridEmpty = true;

        public ObservableCollection<TagData> TagDataList { get; set; } = new ObservableCollection<TagData>();

        public MainWindow()
        {
            InitializeComponent();
            Trace.Listeners.Add(new TextWriterTraceListener(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log")));
            Trace.AutoFlush = true;
        }


        private List<EMVData> ParseEMVData(string emvDataDump, Dictionary<string, List<TagData>> dgiDataDictionary)
        {
            List<EMVData> parsedData = new List<EMVData>();
            int currentIndex = 0;

            while (currentIndex < emvDataDump.Length)
            {
                EMVData emvData = new EMVData();

                // Identify DGI
                string dgi = IdentifyDGI(emvDataDump, ref currentIndex);
                if (dgi == null)
                {
                    // Handle error: Unable to identify DGI
                    break;
                }

                emvData.DGI = dgi;
                currentIndex += 4;

                // Extract length of the DGI data
                string lengthHex = emvDataDump.Substring(currentIndex, 2);
                int length = Convert.ToInt32(lengthHex, 16);
                currentIndex += 2;

                // Extract the DGI data
                string dgiData = emvDataDump.Substring(currentIndex, length * 2);
                currentIndex += length * 2;

                // Get the list of tags for the current DGI from the dictionary
                List<TagData> tagList = dgiDataDictionary.ContainsKey(dgi) ? dgiDataDictionary[dgi] : new List<TagData>();

                // Parse tags directly using the list of tags from the dictionary
                emvData.Tags = ParseTags(dgiData, tagList);

                parsedData.Add(emvData);
            }

            return parsedData;
        }

        private Dictionary<string, string> ParseTags(string dgiData, List<TagData> tagDataList)
        {
            Dictionary<string, string> parsedTags = new Dictionary<string, string>();
            int currentIndex = 0;

            while (currentIndex < dgiData.Length)
            {
                string currentTag = null;
                TagData currentTagData = null;

                // Step 1: Try to find a 2-byte tag in the dictionary
                foreach (var tagData in tagDataList)
                {
                    if (dgiData.Substring(currentIndex, 2).Equals(tagData.Tag))
                    {
                        currentTag = tagData.Tag;
                        currentTagData = tagData;
                        break;
                    }
                }

                // Step 2: If a 2-byte tag is not found, try to find a 4-byte tag in the dictionary
                if (currentTag == null && currentIndex <= dgiData.Length - 4)
                {
                    foreach (var tagData in tagDataList)
                    {
                        if (dgiData.Substring(currentIndex, 4).Equals(tagData.Tag))
                        {
                            currentTag = tagData.Tag;
                            currentTagData = tagData;
                            break;
                        }
                    }
                }

                // Step 3: If neither a 2-byte nor a 4-byte tag is found, log an error and break the loop
                if (currentTag == null)
                {
                    Trace.WriteLine($"Error: Tag not found in dictionary at index {currentIndex}. Current data: {dgiData.Substring(currentIndex, Math.Min(10, dgiData.Length - currentIndex))}");
                    break;
                }

                currentIndex += currentTag.Length * 2;

                // Extract length of the tag data
                if (currentIndex + 2 > dgiData.Length) break;
                string lengthHex = dgiData.Substring(currentIndex, 2);
                int length = Convert.ToInt32(lengthHex, 16);
                currentIndex += 2;

                // If the length is above 0x80, it means it's using more bytes to represent the length
                if (length >= 0x80)
                {
                    int additionalLengthBytes = length - 0x80;
                    if (currentIndex + additionalLengthBytes * 2 > dgiData.Length) break;
                    lengthHex = dgiData.Substring(currentIndex, additionalLengthBytes * 2);
                    length = Convert.ToInt32(lengthHex, 16);
                    currentIndex += additionalLengthBytes * 2;
                }

                // Extract the tag data
                if (currentIndex + length * 2 > dgiData.Length) break;
                string tagDataHex = dgiData.Substring(currentIndex, length * 2);
                currentIndex += length * 2;

                // Add the tag and its data to the dictionary
                parsedTags.Add(currentTag, tagDataHex);
            }

            Trace.WriteLine("ParseTags method completed successfully.");
            return parsedTags;
        }



        private string IdentifyDGI(string emvDataDump, ref int currentIndex)
        {
            // Define the possible prefixes that can precede a DGI
            List<string> prefixes = new List<string> { "XEMV", "~", "PPSE", "XPSE" };

            foreach (var prefix in prefixes)
            {
                int prefixIndex = emvDataDump.IndexOf(prefix, currentIndex);
                if (prefixIndex != -1)
                {
                    // Move the current index past the prefix and return the 4-digit DGI
                    currentIndex = prefixIndex + prefix.Length;
                    return emvDataDump.Substring(currentIndex, 4);
                }
            }

            // If no DGI is found, return null
            return null;
        }

        private async void ParseButton_Click(object sender, RoutedEventArgs e)
        {
            // Step 1: Load the JSON dictionary
            string dictionaryFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EMVTagsDictionary.json");

            if (!File.Exists(dictionaryFilePath))
            {
                MessageBox.Show("JSON dictionary file not found.");
                return;
            }

            string dictionaryJson = await File.ReadAllTextAsync(dictionaryFilePath);
            Dictionary<string, List<TagData>> dgiDataDictionary = JsonConvert.DeserializeObject<Dictionary<string, List<TagData>>>(dictionaryJson);

            // Step 2: Parse the EMV data dump
            string emvDataDump = InputTextBox.Text;
            List<EMVData> parsedData = ParseEMVData(emvDataDump, dgiDataDictionary);

            // Step 3: Search for relevant tags in the JSON dictionary and Step 4: Retrieve DGI, TAG, DESCRIPTION, and VALUE
            List<DisplayData> displayDataList = new List<DisplayData>();
            foreach (var data in parsedData)
            {
                if (dgiDataDictionary.ContainsKey(data.DGI))
                {
                    foreach (var tag in data.Tags)
                    {
                        var tagData = dgiDataDictionary[data.DGI].FirstOrDefault(td => td.Tag == tag.Key);
                        if (tagData != null)
                        {
                            DisplayData displayData = new DisplayData
                            {
                                DGI = data.DGI,
                                Tag = tag.Key,
                                Description = tagData.Description,
                                Value = tag.Value
                            };
                            displayDataList.Add(displayData);
                        }
                    }
                }
            }

            // Step 5: Populate the data grid with the retrieved data
            DataGrid.ItemsSource = displayDataList;
        }

        private void UpdateDictionaryButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "XML Files (*.xml)|*.xml";
            if (openFileDialog.ShowDialog() == true)
            {
                XElement rootElement = XElement.Load(openFileDialog.FileName);
                Dictionary<string, List<TagData>> dgiDataDictionary = new Dictionary<string, List<TagData>>();
                foreach (var node in rootElement.Descendants("NODE"))
                {
                    string nodeName = node.Attribute("NAME")?.Value;
                    string tag = node.Attribute("TAG")?.Value;
                    XElement parentNode = node;
                    while (parentNode.Parent != null && parentNode.Parent.Attribute("PATH") != null)
                    {
                        parentNode = parentNode.Parent;
                    }
                    string parentPath = parentNode.Attribute("PATH")?.Value;
                    string dgi = parentPath.StartsWith("DT") ? parentPath.Substring(3) : parentPath;

                    if (!string.IsNullOrEmpty(nodeName) && !string.IsNullOrEmpty(tag))
                    {
                        var tagData = new TagData
                        {
                            Tag = tag,
                            Description = nodeName
                        };

                        if (!dgiDataDictionary.ContainsKey(dgi))
                        {
                            dgiDataDictionary[dgi] = new List<TagData>();
                        }
                        dgiDataDictionary[dgi].Add(tagData);
                    }
                }

                // Save the data to a file
                SaveDataToFile(dgiDataDictionary);
            }
        }

        private void SaveDataToFile(Dictionary<string, List<TagData>> dgiDataDictionary)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "JSON Files (*.json)|*.json";
            if (saveFileDialog.ShowDialog() == true)
            {
                string json = JsonConvert.SerializeObject(dgiDataDictionary, Formatting.Indented);
                File.WriteAllText(saveFileDialog.FileName, json);
            }
        }
        public class EMVData
        {
            public string DGI { get; set; }
            public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
            public EFDirectory EF70 { get; set; }
            public EFDirectory EFA5 { get; set; }
        }

        public class EFDirectory
        {
            public string Length { get; set; }
            public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
            public EFDirectory EF61 { get; set; } // Nested EF directory under A5
        }


        public class DisplayData
        {
            public string DGI { get; set; }
            public string Tag { get; set; }
            public string Description { get; set; }
            public string Value { get; set; }
        }


        public class TagData
        {
            public string Tag { get; set; }
            public string Description { get; set; }
            public string Data { get; set; }  // New property to hold the data of the tag
        }


    }
}
