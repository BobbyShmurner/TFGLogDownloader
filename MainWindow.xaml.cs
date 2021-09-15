using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using SharpAdbClient;

namespace LogDownloader
{
    public partial class MainWindow : Window
    {
        private readonly BackgroundWorker adbWorker = new BackgroundWorker();
        private readonly BackgroundWorker saveLogsWorker = new BackgroundWorker();

        static AdbClient client;
        static DeviceData logDevice;

        public MainWindow()
        {
            InitializeComponent();

            adbWorker.DoWork += adbWorker_DoWork;
            adbWorker.RunWorkerCompleted += adbWorker_RunWorkerCompleted;

            saveLogsWorker.DoWork += saveLogsWorker_DoWork;
            saveLogsWorker.RunWorkerCompleted += saveLogsWorker_RunWorkerComplete;

            Print("Locating ADB...", end: " ");

            string adbLoc = FindADB();

            if (adbLoc == null)
            {
                Print("Failed! :(");
                MessageBox.Show("Could Not Find ADB! Make sure you have ADB installed and in the Path...", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);

                return;
            }

            Print("Success!");

            AdbServer server = new AdbServer();
            client = new AdbClient();

            Print($"Started ADB Server: {server.StartServer(adbLoc, true)}");

            PopulateLogList();
        }

        void PopulateLogList()
        {
            foreach(string logName in GetLogList())
            {
                CheckBox logCheckBox = new CheckBox();

                logCheckBox.Content = logName;
                logCheckBox.Checked += LogCheckBox_Checked;

                LogList.Children.Add(logCheckBox);
            }
        }

        List<String> GetLogList()
        {
            ScanDevices();

            Print("Attempting to find the \"_logs\" folder...", end: " ");

            foreach (DeviceData device in client.GetDevices())
            {
                using (SyncService service = new SyncService(new AdbSocket(new IPEndPoint(IPAddress.Loopback, AdbClient.AdbServerPort)), device))
                {
                    IEnumerable<FileStatistics> logs = service.GetDirectoryListing("/sdcard/Android/data/com.voidroom.TeaForGod/files/_logs/");
                    if (logs.Count() == 0) continue; // Cannot check if dir exists, so im just checking to see if there are any files in the expected location

                    Print($"Success!");
                    Print($"Found Logs Folder on {device.Model}!");

                    logDevice = device;
                    List<String> logLocations = new List<string>();

                    foreach (FileStatistics fileStat in logs)
                    {
                        if (!fileStat.Path.ToLower().EndsWith(".log")) continue;

                        logLocations.Add(fileStat.Path);
                    }

                    return logLocations;
                }
            }

            Print("Failed! :(");
            MessageBox.Show("Could not find any Logs! Make sure your quest is plugged in and that you have granted ADB permision", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

            return null;
        }

        void ScanDevices()
        {
            Print($"Connected Devices: ");

            List<DeviceData> devices = client.GetDevices();
            foreach (DeviceData device in devices)
            {
                Print($"\t- {device.Model} ({device.Serial})");
            }
        }

        string FindADB()
        {
            foreach (var path in Environment.GetEnvironmentVariable("PATH").Split(';'))
            {
                if (File.Exists($@"{path}/adb.exe")) return $@"{path}/adb.exe";
            }

            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (File.Exists($@"{drive.Name}:\Program Files (x86)\android-sdk\platform-tools\adb.exe")) return $@"{drive.Name}:\Program Files(x86)\android - sdk\platform - tools\adb.exe";
            }

            return null;
        }

        private void adbWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            AdbArgs args = (AdbArgs)e.Argument;

            var adbProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = FindADB(),
                    WorkingDirectory = args.WorkingDirectory,
                    Arguments = args.Args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true
                }
            };

            adbProcess.Start();

            string outputText = null;

            do
            {
                string standardOutput = adbProcess.StandardOutput.ReadToEnd();
                if (standardOutput != string.Empty)
                {
                    outputText += standardOutput;
                    Dispatcher.Invoke(() => { Print($"ADB: {standardOutput}", end:""); });
                }
                string standardError = adbProcess.StandardError.ReadToEnd();
                if (standardError != string.Empty)
                {
                    outputText += standardError;
                    Dispatcher.Invoke(() => { Print($"ADB: {standardError}", end: ""); });
                }
            }
            while (!adbProcess.HasExited);

            args.OutputText = outputText;
            e.Result = args;
        }

        private void adbWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                AdbArgs args = (AdbArgs)e.Result;
                args.CompleteFunction(args, args.ExtraArg);
            });
        }

        public void Print(string text, string end = "\n")
        {
            ConsoleTextblock.Text += $"{text}{end}";
        }

        void ShowLogPreview(AdbArgs adbArgs, object fileName)
        {
            string fileLoc = $@"{Environment.CurrentDirectory}/_temp/{fileName}";

            Paragraph paragraph = new Paragraph();

            if (!File.Exists(fileLoc))
            {
                paragraph.Inlines.Add("Failed To Load Log :(");
                Print($"Failed to Load log \"{fileName}\" from {Environment.CurrentDirectory}/_temp");
            }
            else
            {
                paragraph.Inlines.Add(File.ReadAllText(fileLoc));
            }

            LogPreviewTitle.Text = $"Log Preview - {fileName}";

            LogPrewviewFlowDocument.Blocks.Clear();
            LogPrewviewFlowDocument.Blocks.Add(paragraph);
        }

        void LogPreviewEnabled_Checked(object sender, RoutedEventArgs e)
        {
            CheckBox checkBox = sender as CheckBox;
            bool isChecked = (bool)checkBox.IsChecked;

            if (isChecked)
            {
                LogPreviewRowDefinition.Height = new GridLength(1, GridUnitType.Star);
            }
            else
            {
                LogPreviewRowDefinition.Height = new GridLength(0);
                LogPreviewTitle.Text = "Log Preview";

                LogPrewviewFlowDocument.Blocks.Clear();
            }
        }

        private void LogCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (LogPreviewEnabled.IsChecked == false) return;
            if (adbWorker.IsBusy) return;

            using (SyncService service = new SyncService(new AdbSocket(new IPEndPoint(IPAddress.Loopback, AdbClient.AdbServerPort)), logDevice))
            {
                IEnumerable<FileStatistics> logs = service.GetDirectoryListing("/sdcard/Android/data/com.voidroom.TeaForGod/files/_logs/");
                foreach (FileStatistics fileStat in logs)
                {
                    if (!fileStat.Path.Equals(((CheckBox)sender).Content)) { continue; }

                    string loc = $@"/sdcard/Android/data/com.voidroom.TeaForGod/files/_logs/{fileStat.Path}";
                    if (!Directory.Exists($@"{Environment.CurrentDirectory}/_temp")) Directory.CreateDirectory($@"{Environment.CurrentDirectory}/_temp");

                    Print($"Loading Log File \"{fileStat.Path}\"");

                    LogPreviewTitle.Text = $"Log Preview - {fileStat.Path}";

                    LogPrewviewFlowDocument.Blocks.Clear();
                    LogPrewviewFlowDocument.Blocks.Add(new Paragraph(new Run("Loading Log...")));

                    const string quote = "\"";

                    AdbArgs args = new AdbArgs
                    {
                        WorkingDirectory = $@"{Environment.CurrentDirectory}/_temp",
                        Args = $@" -s {logDevice.Serial} pull {quote}{loc}{quote}",
                        ExtraArg = fileStat.Path,
                        CompleteFunction = ShowLogPreview,
                    };

                    adbWorker.RunWorkerAsync(args);
                }
            }
        }

        private void saveLogsWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            string saveLoc = e.Argument as string;

            const string logsFolder = @"/sdcard/Android/data/com.voidroom.TeaForGod/files/_logs/";
            const string quote = "\"";

            List<string> names = new List<string>();

            Dispatcher.Invoke(() => { foreach (CheckBox logCheckBox in LogList.Children) {
                    if (logCheckBox.IsChecked == true) names.Add((string)logCheckBox.Content);
                }
            });

            for (int i = 0; i < names.Count; i++)
            {
                while (adbWorker.IsBusy);

                string fileName = names[i];
                string fileLoc = $@"{logsFolder}{fileName}";

                AdbArgs args = new AdbArgs
                {
                    WorkingDirectory = saveLoc,
                    Args = $@" -s {logDevice.Serial} pull {quote}{fileLoc}{quote}",
                    ExtraArg = fileName,
                    CompleteFunction = ShowLogPreview,
                };

                adbWorker.RunWorkerAsync(args);
            }

            while (adbWorker.IsBusy) ;
        }

        private void saveLogsWorker_RunWorkerComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            Dispatcher.Invoke(() => { Print("Saving Complete!"); });
        }

        private void SaveLogsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();

            dialog.Description = "Select Where To Save Your Logs";
            dialog.ShowNewFolderButton = true;
            dialog.UseDescriptionForTitle = true;

            dialog.ShowDialog();

            if (!saveLogsWorker.IsBusy) saveLogsWorker.RunWorkerAsync(argument:dialog.SelectedPath);
        }
    }
}
