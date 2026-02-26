using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CodeWalker.GameFiles;

namespace CodeWalker.ClothingExporter
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ClothingExporterForm());
        }
    }

    public class ClothingExporterForm : Form
    {
        // Controls
        private TextBox GtaFolderTextBox;
        private TextBox OutputFolderTextBox;
        private Button BrowseGtaButton;
        private Button BrowseOutputButton;
        private Button ScanButton;
        private CheckedListBox DlcListBox;
        private Button SelectAllButton;
        private Button DeselectAllButton;
        private CheckBox SkipExistingCheckBox;
        private Button ExtractButton;
        private Button AbortButton;
        private ProgressBar ExtractProgressBar;
        private Label ProgressLabel;
        private Label StatusLabel;
        private TextBox LogTextBox;

        // State
        private volatile bool InProgress = false;
        private volatile bool AbortOperation = false;
        private volatile bool KeysLoaded = false;
        private RpfManager rpfManager;
        private Dictionary<string, List<RpfEntry>> dlcEntries; // dlcName -> entries

        public ClothingExporterForm()
        {
            InitializeControls();
        }

        private void InitializeControls()
        {
            Text = "CodeWalker Clothing Exporter";
            Size = new Size(650, 700);
            MinimumSize = new Size(550, 600);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;

            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 1,
                RowCount = 12,
                AutoSize = false
            };

            // Row heights: fixed for controls, fill for lists/log
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: GTA folder
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Output folder
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2: Scan button
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 40)); // 3: DLC list
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 4: Select/Deselect
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 5: Skip existing
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 6: Extract/Abort
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 7: Progress bar
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 8: Progress label
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 9: Status label
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 60)); // 10: Log
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // Row 0: GTA Folder
            var gtaPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            gtaPanel.Controls.Add(new Label { Text = "GTA V Folder:", AutoSize = true, Margin = new Padding(0, 6, 5, 0) });
            GtaFolderTextBox = new TextBox { Width = 380 };
            gtaPanel.Controls.Add(GtaFolderTextBox);
            BrowseGtaButton = new Button { Text = "Browse...", AutoSize = true };
            BrowseGtaButton.Click += BrowseGtaButton_Click;
            gtaPanel.Controls.Add(BrowseGtaButton);
            mainPanel.Controls.Add(gtaPanel, 0, 0);

            // Row 1: Output Folder
            var outPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            outPanel.Controls.Add(new Label { Text = "Output Folder:", AutoSize = true, Margin = new Padding(0, 6, 5, 0) });
            OutputFolderTextBox = new TextBox { Width = 380 };
            outPanel.Controls.Add(OutputFolderTextBox);
            BrowseOutputButton = new Button { Text = "Browse...", AutoSize = true };
            BrowseOutputButton.Click += BrowseOutputButton_Click;
            outPanel.Controls.Add(BrowseOutputButton);
            mainPanel.Controls.Add(outPanel, 0, 1);

            // Row 2: Scan button
            ScanButton = new Button { Text = "Scan DLCs", AutoSize = true, Margin = new Padding(3, 5, 3, 5) };
            ScanButton.Click += ScanButton_Click;
            mainPanel.Controls.Add(ScanButton, 0, 2);

            // Row 3: DLC CheckedListBox
            var dlcGroup = new GroupBox { Text = "DLC Packs", Dock = DockStyle.Fill };
            DlcListBox = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
            dlcGroup.Controls.Add(DlcListBox);
            mainPanel.Controls.Add(dlcGroup, 0, 3);

            // Row 4: Select All / Deselect All
            var selPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            SelectAllButton = new Button { Text = "Select All", AutoSize = true };
            SelectAllButton.Click += (s, e) => SetAllChecked(true);
            selPanel.Controls.Add(SelectAllButton);
            DeselectAllButton = new Button { Text = "Deselect All", AutoSize = true };
            DeselectAllButton.Click += (s, e) => SetAllChecked(false);
            selPanel.Controls.Add(DeselectAllButton);
            mainPanel.Controls.Add(selPanel, 0, 4);

            // Row 5: Skip existing
            SkipExistingCheckBox = new CheckBox { Text = "Skip already exported files", AutoSize = true, Checked = true, Margin = new Padding(3, 5, 3, 5) };
            mainPanel.Controls.Add(SkipExistingCheckBox, 0, 5);

            // Row 6: Extract / Abort
            var extractPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            ExtractButton = new Button { Text = "Extract", AutoSize = true, Enabled = false };
            ExtractButton.Click += ExtractButton_Click;
            extractPanel.Controls.Add(ExtractButton);
            AbortButton = new Button { Text = "Abort", AutoSize = true, Enabled = false };
            AbortButton.Click += AbortButton_Click;
            extractPanel.Controls.Add(AbortButton);
            mainPanel.Controls.Add(extractPanel, 0, 6);

            // Row 7: Progress bar
            ExtractProgressBar = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, Value = 0, Height = 22 };
            mainPanel.Controls.Add(ExtractProgressBar, 0, 7);

            // Row 8: Progress label
            ProgressLabel = new Label { Text = "", AutoSize = true, Dock = DockStyle.Fill };
            mainPanel.Controls.Add(ProgressLabel, 0, 8);

            // Row 9: Status label
            StatusLabel = new Label { Text = "Ready. Set GTA V folder and click Scan DLCs.", AutoSize = true, Dock = DockStyle.Fill };
            mainPanel.Controls.Add(StatusLabel, 0, 9);

            // Row 10: Log
            var logGroup = new GroupBox { Text = "Log", Dock = DockStyle.Fill };
            LogTextBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 8.25f) };
            logGroup.Controls.Add(LogTextBox);
            mainPanel.Controls.Add(logGroup, 0, 10);

            Controls.Add(mainPanel);

            // Try to auto-detect GTA folder
            string autoDetected = AutoDetectGtaFolder();
            if (autoDetected != null)
            {
                GtaFolderTextBox.Text = autoDetected;
            }
        }

        private string AutoDetectGtaFolder()
        {
            // Check registry for Steam install
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V"))
                {
                    var val = key?.GetValue("InstallFolder") as string;
                    if (!string.IsNullOrEmpty(val) && Directory.Exists(val))
                        return val;
                }
            }
            catch { }

            // Check common paths
            string[] commonPaths = {
                @"C:\Program Files\Rockstar Games\Grand Theft Auto V",
                @"C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V",
                @"D:\SteamLibrary\steamapps\common\Grand Theft Auto V"
            };
            foreach (var p in commonPaths)
            {
                if (Directory.Exists(p)) return p;
            }

            return null;
        }

        private void SetAllChecked(bool checkedState)
        {
            for (int i = 0; i < DlcListBox.Items.Count; i++)
                DlcListBox.SetItemChecked(i, checkedState);
        }

        private void BrowseGtaButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select GTA V installation folder";
                if (!string.IsNullOrEmpty(GtaFolderTextBox.Text) && Directory.Exists(GtaFolderTextBox.Text))
                    dialog.SelectedPath = GtaFolderTextBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    GtaFolderTextBox.Text = dialog.SelectedPath;
            }
        }

        private void BrowseOutputButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select output folder for extracted files";
                if (!string.IsNullOrEmpty(OutputFolderTextBox.Text) && Directory.Exists(OutputFolderTextBox.Text))
                    dialog.SelectedPath = OutputFolderTextBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    OutputFolderTextBox.Text = dialog.SelectedPath;
            }
        }

        private void SetControlsEnabled(bool enabled)
        {
            GtaFolderTextBox.Enabled = enabled;
            OutputFolderTextBox.Enabled = enabled;
            BrowseGtaButton.Enabled = enabled;
            BrowseOutputButton.Enabled = enabled;
            ScanButton.Enabled = enabled;
            SelectAllButton.Enabled = enabled;
            DeselectAllButton.Enabled = enabled;
            SkipExistingCheckBox.Enabled = enabled;
            ExtractButton.Enabled = enabled && DlcListBox.Items.Count > 0;
            AbortButton.Enabled = !enabled;
        }

        // --- Scan Phase ---

        private void ScanButton_Click(object sender, EventArgs e)
        {
            if (InProgress) return;

            string gtaFolder = GtaFolderTextBox.Text;
            if (string.IsNullOrWhiteSpace(gtaFolder) || !Directory.Exists(gtaFolder))
            {
                MessageBox.Show(this, "Please select a valid GTA V folder.", "Invalid Folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            InProgress = true;
            AbortOperation = false;
            DlcListBox.Items.Clear();
            dlcEntries = null;
            SetControlsEnabled(false);
            LogTextBox.Clear();
            ExtractProgressBar.Value = 0;
            ProgressLabel.Text = "";

            bool gen9 = File.Exists(Path.Combine(gtaFolder, "gta5_enhanced.exe"));

            Task.Run(() =>
            {
                try
                {
                    UpdateStatus("Loading keys...");

                    if (!KeysLoaded)
                    {
                        GTA5Keys.LoadFromPath(gtaFolder, gen9);
                        KeysLoaded = true;
                    }

                    UpdateStatus("Initializing RPF manager (this may take a while)...");
                    rpfManager = new RpfManager();
                    rpfManager.Init(gtaFolder, gen9,
                        status => UpdateStatus(status),
                        error => AppendLog("RPF Error: " + error));

                    UpdateStatus("Discovering freemode clothing entries...");
                    var discovered = DiscoverEntries(rpfManager);

                    // Group by DLC name
                    var grouped = new Dictionary<string, List<RpfEntry>>();
                    foreach (var kvp in discovered)
                    {
                        string dlcName = kvp.Key.dlc;
                        if (!grouped.TryGetValue(dlcName, out var list))
                        {
                            list = new List<RpfEntry>();
                            grouped[dlcName] = list;
                        }
                        list.AddRange(kvp.Value);
                    }

                    dlcEntries = grouped;

                    int totalFiles = grouped.Values.Sum(l => l.Count);
                    AppendLog($"Found {totalFiles} entries across {grouped.Count} DLC packs.");

                    // Populate DLC list on UI thread
                    Invoke(new Action(() =>
                    {
                        foreach (var dlc in grouped.OrderBy(k => k.Key))
                        {
                            string displayText = $"{dlc.Key} ({dlc.Value.Count} files)";
                            DlcListBox.Items.Add(displayText, true); // checked by default
                        }
                        UpdateStatus($"Scan complete. {grouped.Count} DLC packs found ({totalFiles} files total).");
                    }));
                }
                catch (Exception ex)
                {
                    AppendLog("ERROR: " + ex.Message);
                    UpdateStatus("Scan failed: " + ex.Message);
                }
                finally
                {
                    InProgress = false;
                    Invoke(new Action(() => SetControlsEnabled(true)));
                }
            });
        }

        // --- Extract Phase ---

        private void ExtractButton_Click(object sender, EventArgs e)
        {
            if (InProgress) return;
            if (dlcEntries == null || dlcEntries.Count == 0)
            {
                MessageBox.Show(this, "No DLC packs scanned. Click Scan DLCs first.", "Nothing to Extract", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string outputFolder = OutputFolderTextBox.Text;
            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                MessageBox.Show(this, "Please select an output folder.", "No Output Folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Get selected DLCs
            var selectedDlcNames = new List<string>();
            var dlcKeys = dlcEntries.Keys.OrderBy(k => k).ToList();
            for (int i = 0; i < DlcListBox.Items.Count; i++)
            {
                if (DlcListBox.GetItemChecked(i))
                    selectedDlcNames.Add(dlcKeys[i]);
            }

            if (selectedDlcNames.Count == 0)
            {
                MessageBox.Show(this, "No DLC packs selected.", "Nothing Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Build flat list of entries to extract with their DLC/category info
            var toExtract = new List<(RpfEntry entry, string dlc, string category)>();
            foreach (var dlcName in selectedDlcNames)
            {
                if (dlcEntries.TryGetValue(dlcName, out var entries))
                {
                    foreach (var entry in entries)
                    {
                        string pathLower = entry.Path?.ToLowerInvariant() ?? "";
                        string category = ClassifyCategory(pathLower);
                        if (category == null) category = "other";
                        toExtract.Add((entry, dlcName, category));
                    }
                }
            }

            bool skipExisting = SkipExistingCheckBox.Checked;

            InProgress = true;
            AbortOperation = false;
            SetControlsEnabled(false);
            LogTextBox.Clear();
            ExtractProgressBar.Maximum = toExtract.Count;
            ExtractProgressBar.Value = 0;
            ProgressLabel.Text = $"0% (0/{toExtract.Count})";

            Directory.CreateDirectory(outputFolder);

            int total = toExtract.Count;
            int completed = 0;
            int skipped = 0;
            int errors = 0;
            var errorLog = new List<string>();
            long lastUIUpdate = Environment.TickCount;

            Task.Run(() =>
            {
                try
                {
                    Parallel.ForEach(toExtract,
                        new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                        (item, loopState) =>
                        {
                            if (AbortOperation)
                            {
                                loopState.Stop();
                                return;
                            }

                            var fileEntry = item.entry as RpfFileEntry;
                            if (fileEntry == null)
                            {
                                Interlocked.Increment(ref completed);
                                return;
                            }

                            string outDir = Path.Combine(outputFolder, item.dlc, item.category);

                            // Skip-existing check
                            if (skipExisting)
                            {
                                string xmlPath = Path.Combine(outDir, fileEntry.Name + ".xml");
                                if (File.Exists(xmlPath))
                                {
                                    Interlocked.Increment(ref skipped);
                                    int cur = Interlocked.Increment(ref completed);
                                    ThrottledProgressUpdate(cur, total, ref lastUIUpdate, $"Skipped {item.dlc}/{item.category}/{fileEntry.Name}");
                                    return;
                                }
                            }

                            Directory.CreateDirectory(outDir);

                            try
                            {
                                byte[] data = item.entry.File.ExtractFile(fileEntry);
                                if (data == null)
                                {
                                    string msg = $"Failed to extract: {item.entry.Path} (null data)";
                                    lock (errorLog) { errorLog.Add(msg); }
                                    Interlocked.Increment(ref errors);
                                    AppendLog("ERROR: " + msg);
                                    int cur = Interlocked.Increment(ref completed);
                                    ThrottledProgressUpdate(cur, total, ref lastUIUpdate, null);
                                    return;
                                }

                                string xml = MetaXml.GetXml(fileEntry, data, out string filename, outDir);
                                if (xml != null && xml.Length > 0)
                                {
                                    string xmlPath = Path.Combine(outDir, filename);
                                    File.WriteAllText(xmlPath, xml);
                                    int cur = Interlocked.Increment(ref completed);
                                    string logMsg = $"[{cur}/{total}] {item.dlc}/{item.category}/{filename}";
                                    AppendLog(logMsg);
                                    ThrottledProgressUpdate(cur, total, ref lastUIUpdate, $"Extracting {item.dlc}/{item.category}/{filename}");
                                }
                                else
                                {
                                    string msg = $"Failed to convert: {item.entry.Path} (unsupported format)";
                                    lock (errorLog) { errorLog.Add(msg); }
                                    Interlocked.Increment(ref errors);
                                    AppendLog("ERROR: " + msg);
                                    int cur = Interlocked.Increment(ref completed);
                                    ThrottledProgressUpdate(cur, total, ref lastUIUpdate, null);
                                }
                            }
                            catch (Exception ex)
                            {
                                string msg = $"Error processing {item.entry.Path}: {ex.Message}";
                                lock (errorLog) { errorLog.Add(msg); }
                                Interlocked.Increment(ref errors);
                                AppendLog("ERROR: " + msg);
                                int cur = Interlocked.Increment(ref completed);
                                ThrottledProgressUpdate(cur, total, ref lastUIUpdate, null);
                            }
                        });

                    if (AbortOperation)
                    {
                        UpdateStatus("Extraction aborted.");
                        AppendLog($"Aborted. Completed {completed}/{total} files before abort. {errors} errors, {skipped} skipped.");
                    }
                    else
                    {
                        if (errorLog.Count > 0)
                        {
                            string errorFile = Path.Combine(outputFolder, "_errors.txt");
                            File.WriteAllLines(errorFile, errorLog);
                            AppendLog($"Errors written to: {errorFile}");
                        }

                        string finalMsg = $"Done. Exported {completed - errors - skipped}/{total} files. {errors} errors, {skipped} skipped.";
                        UpdateStatus(finalMsg);
                        AppendLog(finalMsg);
                    }

                    // Final progress update
                    Invoke(new Action(() =>
                    {
                        ExtractProgressBar.Value = Math.Min(completed, total);
                        int pct = total > 0 ? (completed * 100) / total : 100;
                        ProgressLabel.Text = $"{pct}% ({completed}/{total})";
                    }));
                }
                catch (Exception ex)
                {
                    UpdateStatus("Extraction failed: " + ex.Message);
                    AppendLog("FATAL ERROR: " + ex.ToString());
                }
                finally
                {
                    InProgress = false;
                    AbortOperation = false;
                    Invoke(new Action(() => SetControlsEnabled(true)));
                }
            });
        }

        private void ThrottledProgressUpdate(int current, int total, ref long lastUpdate, string statusText)
        {
            long now = Environment.TickCount;
            if (now - Interlocked.Read(ref lastUpdate) < 100 && current < total)
                return;

            Interlocked.Exchange(ref lastUpdate, now);

            try
            {
                BeginInvoke(new Action(() =>
                {
                    ExtractProgressBar.Value = Math.Min(current, total);
                    int pct = total > 0 ? (current * 100) / total : 100;
                    ProgressLabel.Text = $"{pct}% ({current}/{total})";
                    if (statusText != null)
                        StatusLabel.Text = statusText;
                }));
            }
            catch { }
        }

        private void AbortButton_Click(object sender, EventArgs e)
        {
            if (!InProgress) return;
            AbortOperation = true;
            UpdateStatus("Aborting...");
        }

        // --- UI Update Helpers ---

        private void UpdateStatus(string text)
        {
            try
            {
                if (InvokeRequired)
                    Invoke(new Action(() => { StatusLabel.Text = text; }));
                else
                    StatusLabel.Text = text;
            }
            catch { }
        }

        private void AppendLog(string text)
        {
            try
            {
                BeginInvoke(new Action(() =>
                {
                    LogTextBox.AppendText(text + Environment.NewLine);
                }));
            }
            catch { }
        }

        // --- Discovery Logic ---

        static Dictionary<(string dlc, string category), List<RpfEntry>> DiscoverEntries(RpfManager rpfManager)
        {
            var result = new Dictionary<(string dlc, string category), List<RpfEntry>>();

            foreach (var rpf in rpfManager.AllRpfs)
            {
                if (rpf.AllEntries == null) continue;

                foreach (var entry in rpf.AllEntries)
                {
                    if (entry is RpfDirectoryEntry) continue;

                    string path = entry.Path;
                    if (string.IsNullOrEmpty(path)) continue;

                    string pathLower = path.ToLowerInvariant();

                    if (!pathLower.Contains("dlcpacks\\") && !pathLower.Contains("dlcpacks/"))
                        continue;

                    string name = entry.NameLower;
                    if (!name.EndsWith(".ytd") && !name.EndsWith(".ydd") && !name.EndsWith(".ydr"))
                        continue;

                    string category = ClassifyCategory(pathLower);
                    if (category == null) continue;

                    string dlc = ExtractDlcName(pathLower);
                    if (dlc == null) continue;

                    var key = (dlc, category);
                    if (!result.TryGetValue(key, out var list))
                    {
                        list = new List<RpfEntry>();
                        result[key] = list;
                    }
                    list.Add(entry);
                }
            }

            return result;
        }

        static string ClassifyCategory(string pathLower)
        {
            if (pathLower.Contains("mp_m_freemode_01_p") || pathLower.Contains("_male_p.rpf"))
                return "male_props";
            if (pathLower.Contains("mp_f_freemode_01_p") || pathLower.Contains("_female_p.rpf"))
                return "female_props";

            if (pathLower.Contains("mp_m_freemode_01"))
                return "male";
            if (pathLower.Contains("mp_f_freemode_01"))
                return "female";

            if (pathLower.Contains("\\peds\\") || pathLower.Contains("/peds/"))
                return "peds";

            return null;
        }

        static string ExtractDlcName(string pathLower)
        {
            int idx = pathLower.IndexOf("dlcpacks\\");
            if (idx < 0) idx = pathLower.IndexOf("dlcpacks/");
            if (idx < 0) return null;

            int start = idx + "dlcpacks\\".Length;
            int end = pathLower.IndexOf('\\', start);
            if (end < 0) end = pathLower.IndexOf('/', start);
            if (end < 0) return null;

            return pathLower.Substring(start, end - start);
        }
    }
}
