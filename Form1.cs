using System;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Windows.Forms;

public static class StatusLabelExtensions
{
    private const string DefaultMessage = "Double click an image from the list to view";
    private static System.Windows.Forms.Timer? notificationTimer;

    public static void ShowTemporaryNotification(
        this ToolStripStatusLabel label,
        string notificationText,
        double displayDurationSeconds = 3.0)
    {
        if (notificationTimer != null)
        {
            notificationTimer.Stop();
            notificationTimer.Dispose();
            notificationTimer = null;
        }

        label.Text = notificationText;

        notificationTimer = new System.Windows.Forms.Timer
        {
            Interval = (int)(displayDurationSeconds * 1000)
        };
        notificationTimer.Tick += (sender, e) =>
        {
            if (notificationTimer != null)
            {
                notificationTimer.Stop();
                notificationTimer.Dispose();
                notificationTimer = null;
            }

            label.Text = DefaultMessage;
        };

        notificationTimer.Start();
    }
}

namespace LocalWallpaperViewer
{
    public partial class Form1 : Form
    {
        private FileInfo[] _allAssetsCache;
        private int _absoluteTotalCount;
        private Dictionary<PictureBox, FileInfo> pictureMap = [];

        private ToolStrip? _toolStrip;
        private ToolStripButton? _toggleViewButton;
        private FlowLayoutPanel? _thumbnailPanel;
        private TreeView? _fileTreeView;
        private ToolStripStatusLabel statusStripLabel = new ToolStripStatusLabel("Double click an image from the list to view");
        private ToolStripDropDownButton? _statusStripThumbnailFilterButton;
        private ToolStripDropDownButton? _statusStripResolutionFilterButton;
        private PictureBox? selectedThumbnail = null;
        private ToolStripStatusLabel? _statusStripFilterLabel;
        private ContextMenuStrip? _fileContextMenuStrip;
        private TreeNode? generalNode;
        private TreeNode? lockscreenNode;
        private TreeNode? spotLightNode;
        private Label? no_results_label;
        private ToolStripLabel? FilesToolStripLabel;

        private string assetsUserDirectory = string.Empty;
        private string assetsLockScreenDirectory = string.Empty;
        private string assetsSpotLightDirectory = string.Empty;
        private bool ViewMode;
        private int visibleItemCount = 0;
        private bool LandscapeFilterActive = false;
        private OrientationFilterStates OrientationFilter;
        private ResolutionFilterStates ResolutionFilter;
        private Dictionary<string, bool> folderVisibilityStates = [];

        enum OrientationFilterStates { All, Portrait, Landscape }
        enum ResolutionFilterStates { All, HD, FullHD, vFullHD, UltraHD }

        [Flags]
        enum FolderVisibility
        {
            NotInitialized = 0, // never configured
            None = 1 << 3, // 1000 user chose no folders
            SourceA = 1 << 0, // 0001
            SourceB = 1 << 1, // 0010
            SourceC = 1 << 2, // 0100
            All = SourceA | SourceB | SourceC // 0111
        }

        public class ThumbnailMetadata
        {
            public string? Folder { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public FileInfo? File { get; set; }
        }

        public Form1()
        {
            InitializeComponent();
            ViewMode = Settings.Default.ViewMode;
            OrientationFilter = (OrientationFilterStates)Settings.Default.OrientationFilter;
            ResolutionFilter = (ResolutionFilterStates)Settings.Default.ResolutionFilter;

            GetAssets();

            var visibility = (FolderVisibility)Settings.Default.FolderVisibilityMask;

            if (visibility == FolderVisibility.NotInitialized)
            {
                // first run
                folderVisibilityStates[assetsUserDirectory] = Directory.Exists(assetsUserDirectory);
                folderVisibilityStates[assetsLockScreenDirectory] = Directory.Exists(assetsLockScreenDirectory);
                folderVisibilityStates[assetsSpotLightDirectory] = Directory.Exists(assetsSpotLightDirectory);
            }
            else
            {
                // or use saved settings
                folderVisibilityStates[assetsUserDirectory] = visibility.HasFlag(FolderVisibility.SourceA);
                folderVisibilityStates[assetsLockScreenDirectory] = visibility.HasFlag(FolderVisibility.SourceB);
                folderVisibilityStates[assetsSpotLightDirectory] = visibility.HasFlag(FolderVisibility.SourceC);
            }
            SetupUI();
        }

        private FileInfo[] GetAssets()
        {
            var userDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            this.assetsUserDirectory = Path.Combine(userDirectory, @"AppData\Local\Packages\Microsoft.Windows.ContentDeliveryManager_cw5n1h2txyewy\LocalState\Assets");
            this.assetsLockScreenDirectory = Path.Combine(userDirectory, @"AppData\Roaming\Microsoft\Windows\Themes");
            this.assetsSpotLightDirectory = Path.Combine(userDirectory, @"AppData\Local\Packages\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\LocalCache\Microsoft\IrisService");

            var directories = new[]
            {
                this.assetsUserDirectory,
                this.assetsLockScreenDirectory,
                this.assetsSpotLightDirectory
            };
            _allAssetsCache = GetAssetsFromDirectories(directories);
            _absoluteTotalCount = _allAssetsCache.Length;
            return _allAssetsCache;
        }

        private static FileInfo[] GetAssetsFromDirectories(IEnumerable<string> directories)
        {
            var results = new List<FileInfo>();
            foreach (var directory in directories)
            {
                if (!Directory.Exists(directory)) continue;
                var files = new DirectoryInfo(directory)
                    .GetFiles("*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    if (file.Length < 50 * 1024)
                        continue;
                    try
                    {
                        using var img = Image.FromFile(file.FullName);
                        results.Add(file);
                    }
                    catch
                    {
                        // skip if not an image
                    }
                }
            }
            return results.ToArray();
        }

        private static Bitmap CreateThumbnail(Image original, Size maxSize)
        {
            double ratioX = (double)maxSize.Width / original.Width;
            double ratioY = (double)maxSize.Height / original.Height;
            double ratio = Math.Min(ratioX, ratioY);

            int newWidth = (int)(original.Width * ratio);
            int newHeight = (int)(original.Height * ratio);

            var thumb = new Bitmap(newWidth, newHeight);
            using (var graphics = Graphics.FromImage(thumb))
            {
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.DrawImage(original, 0, 0, newWidth, newHeight);
            }
            return thumb;
        }

        private void UpdateTreeViewNodes()
        {
            if (_fileTreeView == null ||
                generalNode == null ||
                lockscreenNode == null ||
                spotLightNode == null
                )
            {
                return;
            }

            var nodes = _fileTreeView.Nodes;
            UpdateNodeVisibility(nodes, generalNode, folderVisibilityStates[assetsUserDirectory]);
            UpdateNodeVisibility(nodes, lockscreenNode, folderVisibilityStates[assetsLockScreenDirectory]);
            UpdateNodeVisibility(nodes, spotLightNode, folderVisibilityStates[assetsSpotLightDirectory]);
        }

        private void UpdateNodeVisibility(TreeNodeCollection nodes, TreeNode node, bool shouldBeVisible)
        {
            var isCurrentlyVisible = nodes.Contains(node);

            if (shouldBeVisible && !isCurrentlyVisible)
            {
                nodes.Add(node);
            }
            else if (!shouldBeVisible && isCurrentlyVisible)
            {
                nodes.Remove(node);
            }
        }

        private void ApplyFilter()
        {
            if (_thumbnailPanel == null)
            {
                UpdateFilterButton();
                return;
            }

            UpdateTreeViewNodes();

            visibleItemCount = 0;
            
            int effectiveTotalCount = 0;

            foreach (var container in _thumbnailPanel.Controls.OfType<Panel>())
            {
                var picBox = container.Controls.OfType<PictureBox>().FirstOrDefault();
                bool visible = false;

                if (picBox == null)
                {
                    visible = OrientationFilter == 0; // hide if missing
                }
                else if (container.Tag is ThumbnailMetadata meta)
                {
                    visible = true;

                    // folder filter:
                    if (visible && meta.Folder != null)
                    {
                        foreach (var record in folderVisibilityStates)
                        {
                            if (meta.Folder.StartsWith(record.Key, StringComparison.OrdinalIgnoreCase) && !record.Value)
                            {
                                visible = false;
                                break;
                            }
                        }
                    }

                    if (visible) 
                    {
                        effectiveTotalCount++;
                    }

                    // orientation filter
                    if (OrientationFilter == OrientationFilterStates.Portrait)
                    {
                        visible &= meta.Height > meta.Width;
                    }
                    else if (OrientationFilter == OrientationFilterStates.Landscape)
                    {
                        visible &= meta.Width > meta.Height;
                    }

                    // resolution filter
                    if (visible)
                    {
                        if (ResolutionFilter == ResolutionFilterStates.FullHD)
                            visible &= meta.Width == 1920 && meta.Height == 1080;
                        else if (ResolutionFilter == ResolutionFilterStates.vFullHD)
                            visible &= meta.Width == 1080 && meta.Height == 1920;
                        else if (ResolutionFilter == ResolutionFilterStates.UltraHD)
                            visible &= meta.Width == 3840 && meta.Height == 2160;
                    }

                }

                container.Visible = visible;

                if (visible)
                {
                    visibleItemCount++;
                }
            }
            
            LandscapeFilterActive = OrientationFilter == OrientationFilterStates.Landscape;

            if (no_results_label != null)
            {
                no_results_label.Visible = visibleItemCount == 0; 
            }

            if (FilesToolStripLabel != null)
            {
                FilesToolStripLabel.Text = $"Showing {visibleItemCount} of {effectiveTotalCount} files";
            }
            UpdateFilterButton();
            UpdateViewState();
        }

        private void UpdateFilterButton()
        {
            if (_statusStripThumbnailFilterButton != null)
            {
                switch (OrientationFilter)
                {
                    case OrientationFilterStates.All:
                        _statusStripThumbnailFilterButton.Text = "All";
                        _statusStripThumbnailFilterButton.Image = GetIconFromFont('\ue8cc'); // All images icon
                        break;
                    case OrientationFilterStates.Portrait:
                        _statusStripThumbnailFilterButton.Text = "Portrait";
                        _statusStripThumbnailFilterButton.Image = GetIconFromFont('\uee64'); // Portrait icon
                        break;
                    case OrientationFilterStates.Landscape:
                        _statusStripThumbnailFilterButton.Text = "Landscape";
                        _statusStripThumbnailFilterButton.Image = GetIconFromFont('\ue70a'); // Landscape icon
                        break;
                }
            }
            if (_statusStripResolutionFilterButton != null)
            {
                switch (ResolutionFilter)
                {
                    case ResolutionFilterStates.All:
                        _statusStripResolutionFilterButton.Text = "All Resolutions";
                        _statusStripResolutionFilterButton.Image = GetIconFromFont('\ue8cc'); // Resolution Arrows
                        break;
                    case ResolutionFilterStates.FullHD:
                        _statusStripResolutionFilterButton.Text = "1920x1080";
                        _statusStripResolutionFilterButton.Image = GetIconFromFont('\ue70a'); // Landscape icon
                        break;
                    case ResolutionFilterStates.vFullHD:
                        _statusStripResolutionFilterButton.Text = "1080x1920";
                        _statusStripResolutionFilterButton.Image = GetIconFromFont('\uee64'); // Portrait Icon //F573
                        break;
                    case ResolutionFilterStates.UltraHD:
                        _statusStripResolutionFilterButton.Text = "3840x2160";
                        _statusStripResolutionFilterButton.Image = GetIconFromFont('\ue70a'); // Landscape icon
                        break;
                }
            }
        }

        private void ShowImageInPopup(FileInfo fileInfo)
        {
            var img = Image.FromFile(fileInfo.FullName);

            // screen dimensions
            int screenW = Screen.PrimaryScreen?.Bounds.Width ?? 1024;
            int screenH = Screen.PrimaryScreen?.Bounds.Height ?? 768;
            double scale = 0.7; // use 70% of screen size

            // calculate scaled form size based on image aspect
            double ratioW = (screenW * scale) / img.Width;
            double ratioH = (screenH * scale) / img.Height;
            double ratio = Math.Min(ratioW, ratioH);

            int finalW = (int)(img.Width * ratio);
            int finalH = (int)(img.Height * ratio);

            var form = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterScreen,
                TopMost = true,
                BackColor = Color.Black,
                ClientSize = new Size(finalW, finalH)
            };

            var pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = img
            };
            form.Controls.Add(pictureBox);

            string fileName = fileInfo.Name;
            string clippedName = (fileName.Length > 33 && finalW < finalH) ? fileName.Substring(0, 33) + "..." : fileName;

            // full-width semi-transparent description bar at the bottom
            var overlay = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                BackColor = Color.FromArgb(160, 0, 0, 0),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = $"{clippedName} - {img.Width}x{img.Height} - {fileInfo.Length / 1024} KB"
            };

            pictureBox.Controls.Add(overlay);
            overlay.BringToFront();

            // close on left click
            pictureBox.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) form.Close(); };
            overlay.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) form.Close(); };

            // take out the trash
            form.FormClosed += (s, e) =>
            {
                pictureBox.Image?.Dispose();
                pictureBox.Image = null;
            };

            form.Show();
            form.Deactivate += (s, e) => form.Close();
        }

        private void OnShowImageMenuItemClick(object? sender, EventArgs e)
        {
            if (sender is ToolStripItem menuItem)
            {
                if (menuItem.Owner is not ContextMenuStrip contextMenu)
                {
                    return;
                }

                FileInfo? fileInfo = null;

                // the context menu was opened on a PictureBox in the FlowLayoutPanel
                if (contextMenu.SourceControl is PictureBox pictureBox &&
                    pictureMap.TryGetValue(pictureBox, out var pictureFileInfo))
                {
                    fileInfo = pictureFileInfo;
                }
                // the context menu was opened on the TreeView
                else if (contextMenu.SourceControl is TreeView treeView &&
                        treeView.SelectedNode?.Tag is FileInfo nodeFileInfo)
                {
                    fileInfo = nodeFileInfo;
                }

                // show the image if a FileInfo was found
                if (fileInfo != null)
                {
                    ShowImageInPopup(fileInfo);
                }
            }
        }

        private void OnOpenFolderMenuItemClick(object? sender, EventArgs e)
        {
            if (sender is ToolStripItem menuItem)
            {
                if (menuItem.Owner is not ContextMenuStrip contextMenu)
                {
                    return;
                }

                FileInfo? fileInfo = null;

                // if PictureBox in the FlowLayoutPanel
                if (contextMenu.SourceControl is PictureBox pictureBox &&
                    pictureMap.TryGetValue(pictureBox, out var pictureFileInfo))
                {
                    fileInfo = pictureFileInfo;
                }
                // if Node in TreeView
                else if (contextMenu.SourceControl is TreeView treeView &&
                        treeView.SelectedNode?.Tag is FileInfo nodeFileInfo)
                {
                    fileInfo = nodeFileInfo;
                }

                // open folder if a FileInfo was found
                if (fileInfo != null)
                {
                    string? directory = fileInfo.DirectoryName;

                    if (directory != null)
                    {
                        if (directory.StartsWith(assetsLockScreenDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            Process.Start(new ProcessStartInfo { Arguments = directory, FileName = "explorer.exe" });
                        }
                        else if (directory.StartsWith(assetsSpotLightDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            Process.Start(new ProcessStartInfo { Arguments = directory, FileName = "explorer.exe" });
                        }
                        else
                        {
                            Process.Start(new ProcessStartInfo { Arguments = directory, FileName = "explorer.exe" });
                        }
                    }
                }
            }
        }

        private void OnPictureBoxDoubleClick(object? sender, EventArgs e)
        {
            if (sender is PictureBox pictureBox &&
                pictureMap.TryGetValue(pictureBox, out var fileInfo))
            {
                ShowImageInPopup(fileInfo);
            }
        }

        private void OnFileTreeViewDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node?.Tag is FileInfo fileInfo)
            {
                ShowImageInPopup(fileInfo);
            }
        }

        private void OnSettingsMenuItemSetQuickSaveClick(object? sender, EventArgs e)
        {
            using var folderDialog = new FolderBrowserDialog
            {
                Description = "Select Quick Save Folder",
                SelectedPath = string.IsNullOrEmpty(Settings.Default.QuickSavePath) 
                    ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                    : Settings.Default.QuickSavePath,
                ShowNewFolderButton = true
            };

            if (folderDialog.ShowDialog() == DialogResult.OK)
            {
                Settings.Default.QuickSavePath = folderDialog.SelectedPath;
                Settings.Default.Save();

                MessageBox.Show($"Quick save folder set to:\n{folderDialog.SelectedPath}",
                                "Settings Updated", MessageBoxButtons.OK, MessageBoxIcon.None);
            }
        }

        private void OnSettingsMenuItemAboutClick(object? sender, EventArgs e)
        {
            MessageBox.Show("Version 0.01 alpha", "About", MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        private void OnFolderMenuItemCheckedChanged(object? sender, EventArgs e)
        {
            var menuItem = (ToolStripMenuItem)sender!;

            if (menuItem.Tag is string folderPath)
            {
                // validate that the folder exists if user is trying to check it
                if (menuItem.Checked && !Directory.Exists(folderPath))
                {
                    MessageBox.Show($"Directory '{folderPath}' does not exist!", "Warning",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    menuItem.Checked = false; // folder doesn't exist
                    folderVisibilityStates[folderPath] = false; // ignore user setting
                    return;
                }
                
                folderVisibilityStates[folderPath] = menuItem.Checked;
            }

            ApplyFilter();
        }

        private void OnSaveButtonMenuItemClick(object? sender, EventArgs e)
        {
            FileInfo? selectedFile = null;

            if (_fileTreeView?.Visible == true && _fileTreeView.SelectedNode?.Tag is FileInfo selectedFileFromTree)
            {
                selectedFile = selectedFileFromTree;
            }
            else if (_thumbnailPanel?.Visible == true &&
                    selectedThumbnail != null &&
                    this.pictureMap.TryGetValue(selectedThumbnail, out var fileFromThumbnail))
            {
                selectedFile = fileFromThumbnail;
            }

            if (selectedFile != null && File.Exists(selectedFile.FullName))
            {
                var saveFileDialog = new SaveFileDialog
                {
                    FileName = $"{Path.GetFileNameWithoutExtension(selectedFile.Name)}.jpg",
                    Filter = "JPEG (*.jpg)|*.jpeg;*.jpg",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                };

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    using var img = Image.FromFile(selectedFile.FullName);
                    img.Save(saveFileDialog.FileName, ImageFormat.Jpeg);
                }
            }
            else
            {
                MessageBox.Show("Click on an image to select it and save it",
                                "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void OnQuickSaveMenuItemClick(object? sender, EventArgs e)
        {
            FileInfo? selectedFile = null;

            if (_fileTreeView?.Visible == true && _fileTreeView.SelectedNode?.Tag is FileInfo selectedFileFromTree)
            {
                selectedFile = selectedFileFromTree;
            }
            else if (_thumbnailPanel?.Visible == true &&
                    selectedThumbnail != null &&
                    this.pictureMap.TryGetValue(selectedThumbnail, out var fileFromThumbnail))
            {
                selectedFile = fileFromThumbnail;
            }

            if (selectedFile != null && File.Exists(selectedFile.FullName))
            {
                // check if FolderPath is configured
                if (string.IsNullOrEmpty(Settings.Default.QuickSavePath))
                {
                    MessageBox.Show("Please set a quick save folder first using Settings > Set Quick Save Folder",
                                    "No Folder Configured", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string fileName = $"{Path.GetFileNameWithoutExtension(selectedFile.Name)}.jpg";
                string savePath = Path.Combine(Settings.Default.QuickSavePath, fileName);

                // check for file name conflicts
                int counter = 1;
                while (File.Exists(savePath))
                {
                    fileName = $"{Path.GetFileNameWithoutExtension(selectedFile.Name)}_{counter}.jpg";
                    savePath = Path.Combine(Settings.Default.QuickSavePath, fileName);
                    counter++;
                }

                using var img = Image.FromFile(selectedFile.FullName);
                img.Save(savePath, ImageFormat.Jpeg);

                this.statusStripLabel.ShowTemporaryNotification($"Image saved to: {savePath}");
            }
            else
            {
                MessageBox.Show("Click on an image to select it and save it.",
                                "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void UpdateViewState()
        {
            // clear previous selection
            selectedThumbnail = null;

            if (_thumbnailPanel == null ||
                _fileTreeView == null ||
                _toggleViewButton == null ||
                _statusStripFilterLabel == null ||
                _statusStripThumbnailFilterButton == null ||
                _statusStripResolutionFilterButton == null)
            {
                return; // nothing to update
            }

            foreach (var container in _thumbnailPanel.Controls.OfType<Panel>())
            {
                container.BorderStyle = BorderStyle.None;
            }

            _thumbnailPanel.Visible = ViewMode;
            _fileTreeView.Visible = !ViewMode;
            _toggleViewButton.Text = ViewMode ? "Show file list" : "Show thumbnails";
            _toggleViewButton.Image = ViewMode ? GetIconFromFont('\uE9a4') : GetIconFromFont('\uE91b');
            _statusStripFilterLabel.Visible = ViewMode;
            _statusStripThumbnailFilterButton.Visible = ViewMode;
            _statusStripResolutionFilterButton.Visible = LandscapeFilterActive;
            _statusStripResolutionFilterButton.Visible = ViewMode;
        }

        private void SaveSettings()
        {
            // convert dict to bitmask
            FolderVisibility visibility = 0;
            if (folderVisibilityStates.TryGetValue(assetsUserDirectory, out var a) && a) visibility |= FolderVisibility.SourceA;
            if (folderVisibilityStates.TryGetValue(assetsLockScreenDirectory, out var b) && b) visibility |= FolderVisibility.SourceB;
            if (folderVisibilityStates.TryGetValue(assetsSpotLightDirectory, out var c) && c) visibility |= FolderVisibility.SourceC;

            // if nothing is selected, explicitly set to None
            if (visibility == 0)
            {
                visibility = FolderVisibility.None;
            }

            Settings.Default.FolderVisibilityMask = (int)visibility;
            Settings.Default.ViewMode = ViewMode;
            Settings.Default.OrientationFilter = (int)OrientationFilter;
            Settings.Default.ResolutionFilter = (int)ResolutionFilter;
            Settings.Default.Save();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SaveSettings();
            base.OnFormClosed(e);
        }

        private void OnThumbnailFilterButtonClick(object? sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem clickedItem)
            {
                OrientationFilter = clickedItem.Text switch
                {
                    "All" => OrientationFilterStates.All,
                    "Portrait" => OrientationFilterStates.Portrait,
                    "Landscape" => OrientationFilterStates.Landscape,
                    _ => OrientationFilterStates.All,
                };
                ApplyFilter();
            }
        }

        private void OnResolutionFilterButtonClick(object? sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem clickedItem)
            {
                ResolutionFilter = clickedItem.Text switch
                {
                    "All Resolutions" => ResolutionFilterStates.All,
                    "1920x1080" => ResolutionFilterStates.FullHD,
                    "1080x1920" => ResolutionFilterStates.vFullHD,
                    "3840x2160" => ResolutionFilterStates.UltraHD,
                    _ => ResolutionFilterStates.All,
                };
                ApplyFilter();
            }
        }

        private void InitializeFileContextMenuStrip()
        {
            _fileContextMenuStrip = new ContextMenuStrip();
            _fileContextMenuStrip.Items.Add("Show image", GetIconFromFont('\uE91b'), OnShowImageMenuItemClick);
            _fileContextMenuStrip.Items.Add("Quick save", GetIconFromFont('\uE91b'), OnQuickSaveMenuItemClick);
            _fileContextMenuStrip.Items.Add("Save to...", GetIconFromFont('\uE74e'), OnSaveButtonMenuItemClick);
            _fileContextMenuStrip.Items.Add(new ToolStripSeparator());
            _fileContextMenuStrip.Items.Add("Open image folder", GetIconFromFont('\uE838'), OnOpenFolderMenuItemClick);
        }

        public static Bitmap GetIconFromFont(char iconChar)
        {
            int fontSize = 16;
            int padding = (int)(fontSize * 0.6); // padding to prevent clipping 
            int bitmapSize = fontSize + padding;

            Bitmap bitmap = new(bitmapSize, bitmapSize);
            using Graphics graphics = Graphics.FromImage(bitmap);
            // set up drawing properties for quality
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            // use a StringFormat to center the glyph and allow for full rendering
            StringFormat format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.NoWrap
            };

            // create the font and brush
            Font font = new("Segoe Fluent Icons", fontSize);
            SolidBrush brush = new(Color.Black);

            // define a rectangle for the drawing, including padding
            RectangleF rect = new(0, 0, bitmapSize, bitmapSize);

            // draw the character onto the bitmap within the defined rectangle
            graphics.DrawString(iconChar.ToString(), font, brush, rect, format);

            return bitmap;
        }

        private void SetupUI()
        {
            // top Toolbar
            _toolStrip = new ToolStrip
            {
                Dock = DockStyle.Top
            };

            // save button
            var saveButton = new ToolStripButton
            {
                Text = "Save image",
                Alignment = ToolStripItemAlignment.Left,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uE74e') // save icon
            };

            // toggle View button
            _toggleViewButton = new ToolStripButton
            {
                Text = "Show file list",
                Alignment = ToolStripItemAlignment.Left,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uE74e')
            };

            // toolstrip label
            FilesToolStripLabel = new ToolStripLabel
            {
                Text = "41 Pictures found (12 filtered)",
                Alignment = ToolStripItemAlignment.Right,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ue91b')
            };

            // toolstrip separator
            var toolStripSeparator = new ToolStripSeparator
            {
                Alignment = ToolStripItemAlignment.Right
            };

            // settings button
            var settingsButton = new ToolStripDropDownButton
            {
                Text = "Settings",
                ShowDropDownArrow = false,
                ToolTipText = "",
                Alignment = ToolStripItemAlignment.Right,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uE713') // cog icon
            };

            // show Assets Folder menu item
            var SettingsMenuItemFolder1 = new ToolStripMenuItem("Show Assets Folder")
            {
                CheckOnClick = true,
                Checked = folderVisibilityStates[assetsUserDirectory],
                Tag = assetsUserDirectory
            };
            SettingsMenuItemFolder1.CheckedChanged += OnFolderMenuItemCheckedChanged;

            // show Lockscreen Folder menu item
            var SettingsMenuItemFolder2 = new ToolStripMenuItem("Show Lockscreen Folder")
            {
                CheckOnClick = true,
                Checked = folderVisibilityStates[assetsLockScreenDirectory],
                Tag = assetsLockScreenDirectory
            };
            SettingsMenuItemFolder2.CheckedChanged += OnFolderMenuItemCheckedChanged;

            // show Spotlight Folder menu item
            var SettingsMenuItemFolder3 = new ToolStripMenuItem("Show Spotlight Folder")
            {
                CheckOnClick = true,
                Checked = folderVisibilityStates[assetsSpotLightDirectory],
                Tag = assetsSpotLightDirectory
            };
            SettingsMenuItemFolder3.CheckedChanged += OnFolderMenuItemCheckedChanged;

            // show Set Quick Save Folder menu item
            var SettingsMenuItemSetQuickSave = new ToolStripMenuItem("Set Quick Save folder")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ued43') // Folder icon
            };
            SettingsMenuItemSetQuickSave.Click += OnSettingsMenuItemSetQuickSaveClick;

            // about menu item
            var SettingsMenuItemAbout = new ToolStripMenuItem("About")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uE946') // Info icon
            };
            SettingsMenuItemAbout.Click += OnSettingsMenuItemAboutClick;

            settingsButton.DropDownItems.Add(SettingsMenuItemFolder1);
            settingsButton.DropDownItems.Add(SettingsMenuItemFolder2);
            settingsButton.DropDownItems.Add(SettingsMenuItemFolder3);
            settingsButton.DropDownItems.Add(new ToolStripSeparator());
            settingsButton.DropDownItems.Add(SettingsMenuItemSetQuickSave);
            settingsButton.DropDownItems.Add(new ToolStripSeparator());
            settingsButton.DropDownItems.Add(SettingsMenuItemAbout);

            var toolStripMenuItem1 = new ToolStripMenuItem("Show All")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uE8a9') // All images icon
            };

            var toolStripMenuItem2 = new ToolStripMenuItem("Portrait")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uee64') // Portrait icon
            };

            var toolStripMenuItem3 = new ToolStripMenuItem("Landscape")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ue70a') // Landscape icon
            };

            var toolStripMenuItem4 = new ToolStripMenuItem("All Resolutions")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ue8cc') // All Resolutions Icon
            };

            var toolStripMenuItem5 = new ToolStripMenuItem("1920x1080")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ue70a') // Landscape icon
            };

            var toolStripMenuItem6 = new ToolStripMenuItem("1080x1920")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uee64') // Portrait icon
            };

            var toolStripMenuItem7 = new ToolStripMenuItem("3840x2160")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ue70a') // Landscape icon
            };

            _toolStrip.Items.Add(saveButton);
            _toolStrip.Items.Add(_toggleViewButton);
            _toolStrip.Items.Add(settingsButton);
            _toolStrip.Items.Add(toolStripSeparator);
            _toolStrip.Items.Add(FilesToolStripLabel);

            saveButton.Click += OnSaveButtonMenuItemClick;

            _toggleViewButton.Click += (s, e) =>
            {
                ViewMode = !ViewMode;
                UpdateViewState();
            };

            toolStripMenuItem1.Click += OnThumbnailFilterButtonClick;
            toolStripMenuItem2.Click += OnThumbnailFilterButtonClick;
            toolStripMenuItem3.Click += OnThumbnailFilterButtonClick;
            toolStripMenuItem4.Click += OnResolutionFilterButtonClick;
            toolStripMenuItem5.Click += OnResolutionFilterButtonClick;
            toolStripMenuItem6.Click += OnResolutionFilterButtonClick;
            toolStripMenuItem7.Click += OnResolutionFilterButtonClick;

            // file tree view
            _fileTreeView = new()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.DarkGray,
                AutoSize = true
            };

            generalNode = new("Assets folder");
            lockscreenNode = new("Lockscreen folder");
            spotLightNode = new("SpotLight folder");

            _fileTreeView.Nodes.Add(generalNode);
            _fileTreeView.Nodes.Add(lockscreenNode);
            _fileTreeView.Nodes.Add(spotLightNode);

            _fileTreeView.ExpandAll();
            _fileTreeView.NodeMouseDoubleClick += OnFileTreeViewDoubleClick;
            _fileTreeView.NodeMouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    _fileTreeView.SelectedNode = e.Node;
                    _fileContextMenuStrip?.Show(_fileTreeView, e.Location);
                }
            };

            foreach (var fileInfo in _allAssetsCache)
            {
                TreeNode fileNode = new(fileInfo.Name) { Tag = fileInfo };

                if (fileInfo.FullName.StartsWith(assetsLockScreenDirectory, StringComparison.OrdinalIgnoreCase))
                    lockscreenNode.Nodes.Add(fileNode);
                else if (fileInfo.FullName.StartsWith(assetsSpotLightDirectory, StringComparison.OrdinalIgnoreCase))
                    spotLightNode.Nodes.Add(fileNode);
                else
                    generalNode.Nodes.Add(fileNode);
            }

            if (_allAssetsCache.Length == 0)
            {
                MessageBox.Show("No files found");
            }

            // thumbnail panel
            this._thumbnailPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(20, 20, 5, 20),
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.DarkGray
            };

            no_results_label = new Label
            {
                Text = "No files match selected filters",
                Dock = DockStyle.Top,
                Height = 30,
                Visible = false,
            };
            _thumbnailPanel.Controls.Add(no_results_label);

            foreach (var fileInfo in this._allAssetsCache)
            {
                try
                {
                    using var tempImage = Image.FromFile(fileInfo.FullName);
                    var thumbnail = CreateThumbnail(tempImage, new Size(100, 60));

                    var picBox = new PictureBox
                    {
                        Image = thumbnail,
                        Dock = DockStyle.Top,
                        Height = 60,
                        Width = 100,
                        SizeMode = PictureBoxSizeMode.Zoom,
                        Cursor = Cursors.Hand,
                    };

                    picBox.DoubleClick += OnPictureBoxDoubleClick;

                    picBox.MouseClick += (s, e) =>
                    {
                        if (selectedThumbnail != null && selectedThumbnail.Parent is Panel oldContainer)
                        {
                            oldContainer.BorderStyle = BorderStyle.None;
                        }
                        selectedThumbnail = (PictureBox?)s; // cast sender to PictureBox
                        if (selectedThumbnail != null && selectedThumbnail.Parent is Panel newContainer)
                        {
                            newContainer.BorderStyle = BorderStyle.FixedSingle;
                        }
                        if (e.Button == MouseButtons.Right)
                        {
                            // display the context menu at the cursor's location
                            _fileContextMenuStrip?.Show(picBox, e.Location);
                        }
                    };

                    var label = new Label
                    {
                        Text = $"{tempImage.Width}x{tempImage.Height}, {fileInfo.Length / 1024} KB",
                        Dock = DockStyle.Bottom,
                        Height = 30,
                        TextAlign = ContentAlignment.TopCenter,
                        AutoEllipsis = true
                    };

                    var container = new Panel
                    {
                        Width = 110,
                        Height = 95,
                        Margin = new Padding(5),
                        Tag = new ThumbnailMetadata
                        {
                            Folder = fileInfo.DirectoryName,
                            Width = tempImage.Width,
                            Height = tempImage.Height,
                            File = fileInfo,
                        }
                    };
                    container.Controls.Add(picBox);
                    container.Controls.Add(label);

                    this._thumbnailPanel.Controls.Add(container);
                    this.pictureMap[picBox] = fileInfo;
                }
                catch
                {
                    // ignore invalid files
                }
            }

            // status strip
            var statusStrip = new StatusStrip { ShowItemToolTips = true };
            statusStrip.Items.Add(statusStripLabel);
            statusStrip.Items.Add(new ToolStripStatusLabel("") { Spring = true, TextAlign = ContentAlignment.MiddleRight });

            _statusStripFilterLabel = new ToolStripStatusLabel("Filter:");
            statusStrip.Items.Add(_statusStripFilterLabel);

            _statusStripThumbnailFilterButton = new ToolStripDropDownButton
            {
                Text = "Show All",
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uE8a9') // all images icon
            };

            _statusStripResolutionFilterButton = new ToolStripDropDownButton
            {
                Text = "All Resolutions",
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ue740') // resolution Arrows
            };

            // add controls to form
            Controls.Add(_fileTreeView);
            Controls.Add(_thumbnailPanel);
            Controls.Add(_toolStrip);
            Controls.Add(statusStrip);

            _statusStripThumbnailFilterButton.DropDownItems.Add(toolStripMenuItem1);
            _statusStripThumbnailFilterButton.DropDownItems.Add(toolStripMenuItem2);
            _statusStripThumbnailFilterButton.DropDownItems.Add(toolStripMenuItem3);
            statusStrip.Items.Add(_statusStripThumbnailFilterButton);

            _statusStripResolutionFilterButton.DropDownItems.Add(toolStripMenuItem4);
            _statusStripResolutionFilterButton.DropDownItems.Add(toolStripMenuItem5);
            
            _statusStripResolutionFilterButton.DropDownItems.Add(toolStripMenuItem7);
            _statusStripResolutionFilterButton.DropDownItems.Add(toolStripMenuItem6);
            statusStrip.Items.Add(_statusStripResolutionFilterButton);

            InitializeFileContextMenuStrip();
            ApplyFilter();
        }
    }
}
