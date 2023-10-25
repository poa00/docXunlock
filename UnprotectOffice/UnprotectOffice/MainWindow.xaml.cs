﻿using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Navigation;
using Microsoft.Win32;
using System.Xml

namespace UnprotectOffice
{
    /// <summary>
    ///     MainWindow interaction logic
    /// </summary>
    public partial class MainWindow : Window
    {
        private string[] _files;

        public MainWindow()
        {
            InitializeComponent();
            Title += FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion;
        }

        private string PickFolderPath = "";
        
        /// <summary>
        ///     Open file picker and refresh label and button based on input
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PickFiles(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                InitialDirectory = Directory.Exists(PickFolderPath) ? PickFolderPath : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Filter = "OOXML files|*.docx;*.docm;*.pptx;*.pptm;*.xlsx;*.xlsm"
            };

            if (dialog.ShowDialog() == true)
            {
                UnprotectButton.IsEnabled = true;
                FilesText.Text = string.Join(
                    ", ",
                    dialog.FileNames.Select(fn => FileArray(fn)[1])
                );

                _files = dialog.FileNames;
                PickFolderPath = Directory.GetParent(dialog.FileName).FullName;
            }
            else
            {
                ResetFields();
            }
        }

        private void ResetFields()
        {
            UnprotectButton.IsEnabled = false;
            FilesText.Text = "None";

            _files = Array.Empty<string>();
        }

        private void OpenUri(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(e.Uri.ToString());
        }

        /// <summary>
        ///     Remove write protection from files
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UnprotectFiles(object sender, RoutedEventArgs e)
        {
            UnprotectButton.IsEnabled = false;
            ProgressText.Text = "Starting...";

            try
            {
                foreach (var file in _files)
                {
                    var f = FileArray(file);
                    string transSuffix = "";

                    ProgressText.Text = $"Extracting {f[1]}...";
                    var extractPath = ExtractFile(f);

                    ProgressText.Text = $"Unprotecting {f[1]}...";
                    Unprotect(extractPath, f[3]);

                    if (MathtransCheck.IsChecked == true)
                    {
                        ProgressText.Text = $"Transforming {f[1]}...";
                        TransMathFormulaToTxt(extractPath);
                        transSuffix = "-transformed";
                    }
                    
                    ProgressText.Text = $"Compressing {f[1]}...";
                    var newFile = $"{extractPath}.{f[3]}";
                    ZipFile.CreateFromDirectory(extractPath, newFile);

                    ProgressText.Text = $"Saving {f[1]}...";
                    var newFilePath = BackupCheck.IsChecked == true
                        ? $"{f[4]}{Path.DirectorySeparatorChar}{f[2]}-unprotected{transSuffix}.{f[3]}"
                        : f[0];

                    File.Copy(newFile, newFilePath, true);

                    Directory.Delete(new DirectoryInfo(extractPath).Parent.FullName, true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"An error occured while trying to unprotect the files.\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }

            ProgressText.Text = "Done.";
            ResetFields();
        }

        /// <summary>
        ///     Extract file to temporary directory
        /// </summary>
        /// <param name="file">File information array</param>
        /// <returns>Extraction directory path</returns>
        private static string ExtractFile(string[] file)
        {
            var extractPath = Path.Combine(Path.GetTempPath(),
                "UnprotectOffice",
                DateTime.Now.ToString("yyyyMMddhhmmss"),
                file[2]
            );

            ZipFile.ExtractToDirectory(file[0], extractPath);

            return extractPath;
        }

        /// <summary>
        ///     Remove lines that enable write protection from files
        /// </summary>
        /// <param name="path">File path</param>
        /// <param name="ext">File extension</param>
        private static void Unprotect(string path, string ext)
        {
            RemoveFileTextRegex(Path.Combine(path, "docProps", "app.xml"), "<DocSecurity>.*?</DocSecurity>");

            var type = ext[0];
            switch (type)
            {
                case 'd':
                    {
                        for (int i = 0; i <= 5; i++)
                        {
                            try
                            {
                                if (i == 0)
                                {
                                    RemoveFileTextRegex(Path.Combine(path, "word", "settings.xml"), "<w:documentProtection.*?/>");
                                }
                                else
                                {
                                    RemoveFileTextRegex(Path.Combine(path, "word", "settings" + i.ToString() + ".xml"), "<w:documentProtection.*?/>");
                                }
                            }
                            catch (FileNotFoundException)
                            {
                            }
                            catch (Exception)
                            {
                                throw;
                            }
                        }
                        break;
                    }
            }
        }

        /// <summary>
        ///     Remove a regex from a file
        /// </summary>
        /// <param name="file">File path</param>
        /// <param name="pattern">Regex pattern</param>
        private static void RemoveFileTextRegex(string file, string pattern)
        {
            var fileText = File.ReadAllText(file);
            fileText = Regex.Replace(fileText, pattern, string.Empty);
            File.WriteAllText(file, fileText);
        }

        
        private static void TransMathFormulaToTxt(string path)
        {
            string file = Path.Combine(path, "word", "document.xml");

            string wordURI = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            string mathURI = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            // Read the document
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load(file);
            // Add a namespace
            var xmlNsm = new XmlNamespaceManager(xmlDoc.NameTable);
            xmlNsm.AddNamespace("w", wordURI);
            xmlNsm.AddNamespace("m", mathURI);
            // Read the root node
            XmlNode xmlNode = xmlDoc.SelectSingleNode("/w:document", xmlNsm);
            // Convert formulas to text
            xmlDoc = transOmathToTxt(xmlDoc, xmlNode, wordURI);
            // Write the result to an XML file without line breaking
            // using (XmlTextWriter xmlTw = new XmlTextWriter(file, System.Text.Encoding.UTF8))
            // {
            //    xmlTw.Formatting = Formatting.None;
            //    xmlDoc.Save(xmlTw);
            // }
            // Write the result to an XML file in a newline fashion
            xmlDoc.Save(file);
        }

        /// <summary>
        /// Recursively replace formulas in Word with text
        /// </summary>
        /// <param name="xmlDoc"></param>
        /// <param name="xmlNode"></param>
        /// <param name="wordURI"></param>
        /// <returns></returns>
        private static XmlDocument transOmathToTxt(XmlDocument xmlDoc, XmlNode xmlNode, string wordURI)
        {
            if (xmlNode.HasChildNodes)
            {
                for (int nodeNum=0; nodeNum< xmlNode.ChildNodes.Count; nodeNum++)
                {
                    if ((xmlNode.ChildNodes[nodeNum].Name == "m:oMathPara") || (xmlNode.ChildNodes[nodeNum].Name == "m:oMath"))
                    {
                        XmlNode newNode = xmlDoc.CreateNode(XmlNodeType.Element, "w:r", wordURI);
                        newNode.AppendChild(xmlDoc.CreateNode(XmlNodeType.Element, "w:t", wordURI));
                        newNode.FirstChild.InnerText = xmlNode.ChildNodes[nodeNum].InnerText;
                        xmlNode.ReplaceChild(newNode, xmlNode.ChildNodes[nodeNum]);
                    }
                    else
                    {
                        xmlDoc = transOmathToTxt(xmlDoc, xmlNode.ChildNodes[nodeNum], wordURI);
                    }
                }
            }
            return xmlDoc;
        }


        /// <summary>
        ///     Create a file information array
        /// </summary>
        /// <param name="file">File path</param>
        /// <returns>
        ///     [0] Full path
        ///     [1] File name
        ///     [2] File name without extension
        ///     [3] File extension
        ///     [4] Parent directory
        /// </returns>
        private static string[] FileArray(string file)
        {
            var dirArray = file.Split(Path.DirectorySeparatorChar);
            var extArray = dirArray.Last().Split('.');

            return new[]
            {
                file,
                dirArray.Last(),
                string.Join(".", extArray.Take(extArray.Length - 1)),
                extArray.Last(),
                string.Join(Path.DirectorySeparatorChar.ToString(), dirArray.Take(dirArray.Length - 1))
            };
        }
    }
}
