using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("EverQuest Beta UI Copier")]
[assembly: AssemblyDescription("Copy Live EverQuest character UI settings to Beta.")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace EverQuestBetaUiCopier
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show(e.Exception.Message, "EverQuest Beta UI Copier", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new CopierForm());
        }
    }

    internal sealed class CopierForm : Form
    {
        private readonly Color ink = Color.FromArgb(31, 43, 59);
        private readonly Color muted = Color.FromArgb(85, 101, 121);
        private readonly Color accent = Color.FromArgb(29, 91, 166);
        private readonly Button liveButton = new Button();
        private readonly Button betaButton = new Button();
        private readonly TextBox livePath = new TextBox();
        private readonly TextBox betaPath = new TextBox();
        private readonly DataGridView profilesGrid = new DataGridView();
        private readonly Label summary = new Label();
        private readonly Label result = new Label();
        private readonly Button copyButton = new Button();
        private readonly Button backupButton = new Button();
        private readonly Button selectAll = new Button();
        private readonly Button selectNone = new Button();
        private readonly CheckBox includeMaps = new CheckBox();
        private List<Profile> profiles = new List<Profile>();
        private string liveDirectory;
        private string betaDirectory;
        private string backupDirectory;
        private bool filling;
        private bool copying;

        public CopierForm()
        {
            SuspendLayout();
            Text = "EverQuest Beta UI Copier";
            Font = new Font("Segoe UI", 10F);
            BackColor = Color.FromArgb(246, 248, 251);
            ForeColor = ink;
            ClientSize = new Size(880, 732);
            MinimumSize = new Size(800, 722);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            using (Icon executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                if (executableIcon != null) Icon = (Icon)executableIcon.Clone();

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(24, 18, 24, 18);
            layout.ColumnCount = 1;
            layout.RowCount = 10;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 83));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Controls.Add(layout);

            Panel heading = new Panel { Dock = DockStyle.Fill };
            heading.Controls.Add(new Label { Text = "Your Live UI, ready for Beta", Font = new Font(Font.FontFamily, 22F, FontStyle.Bold), AutoSize = true, Location = new Point(0, 0) });
            heading.Controls.Add(new Label { Text = "Choose your two launchers, review the characters, then copy.", AutoSize = true, ForeColor = muted, Location = new Point(2, 45) });
            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(CreatePathRow("1  LIVE", "Choose Live launcher...", liveButton, livePath), 0, 1);
            layout.Controls.Add(CreatePathRow("2  BETA", "Choose Beta launcher...", betaButton, betaPath), 0, 2);
            liveButton.Click += delegate { ChooseLive(); };
            betaButton.Click += delegate { ChooseBeta(); };

            TableLayoutPanel listHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
            listHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            listHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            listHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            listHeader.Controls.Add(new Label { Text = "3  REVIEW & COPY", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Anchor = AnchorStyles.Left }, 0, 0);
            StyleButton(selectAll, "Select all");
            StyleButton(selectNone, "Clear");
            selectAll.Dock = selectNone.Dock = DockStyle.Fill;
            listHeader.Controls.Add(selectAll, 1, 0);
            listHeader.Controls.Add(selectNone, 2, 0);
            selectAll.Click += delegate { SetSelection(true); };
            selectNone.Click += delegate { SetSelection(false); };
            layout.Controls.Add(listHeader, 0, 3);

            ConfigureGrid();
            layout.Controls.Add(profilesGrid, 0, 4);
            summary.Dock = DockStyle.Fill;
            summary.TextAlign = ContentAlignment.MiddleLeft;
            summary.ForeColor = muted;
            summary.Margin = new Padding(0);
            layout.Controls.Add(summary, 0, 5);

            FlowLayoutPanel mapOptions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
            includeMaps.Text = "Include New Maps";
            includeMaps.AutoSize = true;
            includeMaps.Checked = true;
            includeMaps.Margin = new Padding(0, 3, 14, 0);
            includeMaps.CheckedChanged += delegate { UpdateSummary(); };
            mapOptions.Controls.Add(includeMaps);
            mapOptions.Controls.Add(new Label { Text = "Downloads to Beta's maps folder; replaces matching files.", AutoSize = true, ForeColor = muted, Margin = new Padding(0, 5, 0, 0) });
            layout.Controls.Add(mapOptions, 0, 6);

            Label help = new Label { Dock = DockStyle.Fill, ForeColor = muted, Margin = new Padding(0), Text = "Copies matching UI + character INI files and changes the server in each filename to beta.\nExisting Beta files are backed up automatically. Custom UI skin folders are not included." };
            layout.Controls.Add(help, 0, 7);

            TableLayoutPanel actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            StyleButton(backupButton, "Open backup folder");
            backupButton.Dock = DockStyle.Fill;
            backupButton.Enabled = false;
            backupButton.Click += delegate { if (Directory.Exists(backupDirectory)) Process.Start(new ProcessStartInfo(backupDirectory) { UseShellExecute = true }); };
            actions.Controls.Add(backupButton, 0, 0);
            StyleButton(copyButton, "Copy to Beta");
            copyButton.Dock = DockStyle.Fill;
            copyButton.BackColor = accent;
            copyButton.ForeColor = Color.White;
            copyButton.Font = new Font(Font, FontStyle.Bold);
            copyButton.FlatAppearance.BorderSize = 0;
            copyButton.Click += CopySelected;
            actions.Controls.Add(copyButton, 2, 0);
            layout.Controls.Add(actions, 0, 8);

            result.Dock = DockStyle.Fill;
            result.TextAlign = ContentAlignment.MiddleLeft;
            result.Margin = new Padding(0);
            layout.Controls.Add(result, 0, 9);
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (copying) e.Cancel = true; };
            UpdateSummary();
            ResumeLayout(true);
        }

        private Control CreatePathRow(string title, string buttonText, Button button, TextBox path)
        {
            TableLayoutPanel row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0, 0, 0, 12) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
            row.Controls.Add(new Label { Text = title, Font = new Font(Font, FontStyle.Bold), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            path.ReadOnly = true;
            path.BackColor = Color.White;
            path.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            path.AccessibleName = title + " game folder";
            path.Text = "Select a desktop shortcut or LaunchPad.exe";
            row.Controls.Add(path, 1, 0);
            StyleButton(button, buttonText);
            button.Dock = DockStyle.Fill;
            row.Controls.Add(button, 2, 0);
            return row;
        }

        private void StyleButton(Button button, string text)
        {
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(202, 211, 222);
            button.BackColor = Color.White;
            button.ForeColor = ink;
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(4);
        }

        private void ConfigureGrid()
        {
            profilesGrid.Dock = DockStyle.Fill;
            profilesGrid.BackgroundColor = Color.White;
            profilesGrid.BorderStyle = BorderStyle.FixedSingle;
            profilesGrid.AllowUserToAddRows = profilesGrid.AllowUserToDeleteRows = false;
            profilesGrid.AllowUserToResizeRows = false;
            profilesGrid.AutoGenerateColumns = false;
            profilesGrid.RowHeadersVisible = false;
            profilesGrid.MultiSelect = false;
            profilesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            profilesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            profilesGrid.ColumnHeadersHeight = 34;
            profilesGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            profilesGrid.RowTemplate.Height = 29;
            profilesGrid.EnableHeadersVisualStyles = false;
            profilesGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(234, 239, 245);
            profilesGrid.ColumnHeadersDefaultCellStyle.ForeColor = ink;
            profilesGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(234, 239, 245);
            profilesGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(223, 236, 252);
            profilesGrid.DefaultCellStyle.SelectionForeColor = ink;
            profilesGrid.GridColor = Color.FromArgb(231, 235, 241);
            profilesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "Copy", FillWeight = 12, MinimumWidth = 45 });
            AddColumn("Character", 32, 100);
            AddColumn("Class", 15, 48);
            AddColumn("Live server", 26, 90);
            AddColumn("Files", 30, 100);
            AddColumn("Beta UI filename", 66, 180);
            profilesGrid.CurrentCellDirtyStateChanged += delegate
            {
                if (profilesGrid.IsCurrentCellDirty) profilesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            profilesGrid.CellValueChanged += delegate { if (!filling) UpdateSummary(); };
        }

        private void AddColumn(string title, float weight, int minimum)
        {
            profilesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, ReadOnly = true, FillWeight = weight, MinimumWidth = minimum, SortMode = DataGridViewColumnSortMode.NotSortable });
        }

        private string PickDirectory(string edition)
        {
            using (OpenFileDialog picker = new OpenFileDialog())
            {
                picker.Title = "Find the " + edition + " EverQuest launcher or desktop shortcut";
                picker.Filter = "EverQuest launcher or shortcut (*.lnk;*.exe)|*.lnk;*.exe";
                picker.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                picker.DereferenceLinks = false;
                picker.CheckFileExists = true;
                picker.Multiselect = false;
                picker.RestoreDirectory = true;
                if (picker.ShowDialog(this) != DialogResult.OK) return null;
                return LauncherResolver.GetGameDirectory(picker.FileName);
            }
        }

        private void ChooseLive()
        {
            try
            {
                string chosen = PickDirectory("LIVE");
                if (chosen == null) return;
                if (betaDirectory != null) TransferCore.ValidateFolders(chosen, betaDirectory);
                List<Profile> found = TransferCore.Scan(chosen);
                liveDirectory = chosen;
                livePath.Text = chosen;
                profiles = found;
                result.Text = "";
                PopulateProfiles();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void ChooseBeta()
        {
            try
            {
                string chosen = PickDirectory("BETA");
                if (chosen == null) return;
                if (liveDirectory != null) TransferCore.ValidateFolders(liveDirectory, chosen);
                betaDirectory = chosen;
                betaPath.Text = chosen;
                result.Text = "";
                UpdateSummary();
                ShowCollisionGuidance();
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void PopulateProfiles()
        {
            filling = true;
            profilesGrid.Rows.Clear();
            HashSet<string> collisions = new HashSet<string>(profiles.GroupBy(p => p.DestinationUiName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.OrdinalIgnoreCase);
            foreach (Profile profile in profiles)
            {
                int index = profilesGrid.Rows.Add(!collisions.Contains(profile.DestinationUiName), profile.Character, profile.ClassCode, profile.Server,
                    profile.CompanionSourcePath == null ? "UI only" : "UI + character", profile.DestinationUiName);
                profilesGrid.Rows[index].Tag = profile;
                profilesGrid.Rows[index].Cells[4].ToolTipText = profile.CompanionSourcePath == null
                    ? "No matching character INI was found. The UI file can still be copied."
                    : Path.GetFileName(profile.UiSourcePath) + "\n" + Path.GetFileName(profile.CompanionSourcePath);
                if (collisions.Contains(profile.DestinationUiName))
                {
                    profilesGrid.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(255, 245, 220);
                    profilesGrid.Rows[index].Cells[3].ToolTipText = "This character and class exist on multiple servers. Choose just one source server.";
                }
            }
            filling = false;
            UpdateSummary();
            ShowCollisionGuidance();
        }

        private void ShowCollisionGuidance()
        {
            if (profiles.GroupBy(p => p.DestinationUiName, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                result.Text = "Highlighted profiles share a Beta filename. Select one Live server for each character + class.";
        }

        private List<Profile> SelectedProfiles()
        {
            return profilesGrid.Rows.Cast<DataGridViewRow>().Where(r => Convert.ToBoolean(r.Cells[0].Value)).Select(r => (Profile)r.Tag).ToList();
        }

        private void SetSelection(bool selected)
        {
            filling = true;
            foreach (DataGridViewRow row in profilesGrid.Rows) row.Cells[0].Value = selected;
            filling = false;
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            if (copying) return;
            List<Profile> selected = SelectedProfiles();
            int files = selected.Sum(p => p.CompanionSourcePath == null ? 1 : 2);
            bool conflict = selected.GroupBy(p => p.DestinationUiName, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1);
            copyButton.Enabled = liveDirectory != null && betaDirectory != null && files > 0 && !conflict;
            selectAll.Enabled = selectNone.Enabled = profiles.Count > 0;
            copyButton.Text = files == 0 ? "Copy to Beta" : "Copy " + files + " files" + (includeMaps.Checked ? " + maps" : "") + " to Beta";
            summary.ForeColor = conflict ? Color.FromArgb(160, 62, 23) : muted;
            if (liveDirectory == null) summary.Text = "Start by choosing your Live EverQuest desktop shortcut or launcher.";
            else if (profiles.Count == 0) summary.Text = "No UI_<character>_<server>_<class>.ini files found in this Live folder.";
            else if (conflict) summary.Text = "Some selected characters have the same Beta filename. Select only one source server per character + class.";
            else
            {
                int existing = betaDirectory == null ? 0 : selected.Sum(p => (File.Exists(Path.Combine(betaDirectory, p.DestinationUiName)) ? 1 : 0) +
                    (p.CompanionSourcePath != null && File.Exists(Path.Combine(betaDirectory, p.DestinationCompanionName)) ? 1 : 0));
                int missing = selected.Count(p => p.CompanionSourcePath == null);
                summary.Text = selected.Count + " of " + profiles.Count + " profiles selected  |  " + files + " UI files" +
                    (betaDirectory == null ? "  |  Choose your Beta launcher next." : "  |  " + existing + " existing UI files will be backed up.") +
                    (missing == 0 ? "" : "\n" + missing + " profiles have no matching character INI; their UI files will still be copied.");
            }
        }

        private void CopySelected(object sender, EventArgs e)
        {
            if (copying || !copyButton.Enabled) return;
            try
            {
                TransferCore.ValidateFolders(liveDirectory, betaDirectory);
            }
            catch (Exception ex) { ShowError(ex); return; }

            List<Profile> selected = SelectedProfiles();
            bool downloadMaps = includeMaps.Checked;
            copying = true;
            liveButton.Enabled = betaButton.Enabled = profilesGrid.Enabled = selectAll.Enabled = selectNone.Enabled = copyButton.Enabled = backupButton.Enabled = includeMaps.Enabled = false;
            copyButton.Text = "Copying...";
            result.ForeColor = muted;
            result.Text = downloadMaps ? "Downloading the latest maps from GitHub..." : "Backing up existing Beta files and copying your selected settings...";
            BackgroundWorker worker = new BackgroundWorker { WorkerReportsProgress = true };
            worker.ProgressChanged += delegate(object progressSender, ProgressChangedEventArgs progress) { result.Text = (string)progress.UserState; };
            worker.DoWork += delegate(object workSender, DoWorkEventArgs work)
            {
                List<MapFile> maps = downloadMaps
                    ? MapDownloader.Download(delegate(string message) { worker.ReportProgress(0, message); })
                    : new List<MapFile>();
                worker.ReportProgress(0, "Backing up existing Beta files and copying your selected settings" + (downloadMaps ? " and maps..." : "..."));
                work.Result = TransferCore.Copy(liveDirectory, betaDirectory, selected, maps);
            };
            worker.RunWorkerCompleted += delegate(object completedSender, RunWorkerCompletedEventArgs completed)
            {
                copying = false;
                liveButton.Enabled = betaButton.Enabled = profilesGrid.Enabled = includeMaps.Enabled = true;
                UpdateSummary();
                if (completed.Error != null)
                {
                    result.ForeColor = Color.FromArgb(160, 62, 23);
                    result.Text = "Copy did not complete. See the error details.";
                    ShowError(completed.Error);
                }
                else
                {
                    TransferResult transfer = (TransferResult)completed.Result;
                    backupDirectory = transfer.BackupDirectory;
                    result.ForeColor = Color.FromArgb(23, 111, 67);
                    result.Text = "Done! Copied " + transfer.CopiedUiFiles + " UI files" +
                        (transfer.CopiedMapFiles == 0 ? "" : " and " + transfer.CopiedMapFiles + " maps") +
                        " to Beta. " + transfer.ReplacedFiles + " previous files backed up.\nYou can close this window and start Beta.";
                }
                backupButton.Enabled = backupDirectory != null && Directory.Exists(backupDirectory);
                worker.Dispose();
            };
            worker.RunWorkerAsync();
        }

        private void ShowError(Exception ex)
        {
            MessageBox.Show(this, ex.Message, "EverQuest Beta UI Copier", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
