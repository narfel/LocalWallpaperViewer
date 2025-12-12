using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;

namespace LocalWallpaperViewer
{
    internal sealed class Form1 : Form
    {
        private System.ComponentModel.Container? components = null;
        private FileInfo[] _allAssetsCache = [];
        private Dictionary<PictureBox, FileInfo> _pictureMap = [];

        private ToolStripButton? _toggleViewButton;
        private FlowLayoutPanel? _thumbnailPanel;
        private TreeView? _fileTreeView;
        private ToolStripStatusLabel _statusStripLabel = new("Double click an image from the list to view");
        private ToolStripDropDownButton? _statusStripThumbnailFilterButton;
        private ToolStripDropDownButton? _statusStripResolutionFilterButton;
        private PictureBox? _selectedThumbnail;
        private ToolStripStatusLabel? _statusStripFilterLabel;
        private ContextMenuStrip? _fileContextMenuStrip;
        private TreeNode? _generalNode;
        private TreeNode? _lockscreenNode;
        private TreeNode? _spotLightNode;
        private Label? _no_results_label;
        private ToolStripLabel? _filesToolStripLabel;
        private ImageList? _treeViewIcons;

        private string _assetsUserDirectory = string.Empty;
        private string _assetsLockScreenDirectory = string.Empty;
        private string _assetsSpotLightDirectory = string.Empty;
        private int _visibleItemCount;
        private bool _viewMode;
        private bool _isLandscapeFilterActive;

        private OrientationFilterStates OrientationFilter;
        private ResolutionFilterStates ResolutionFilter;
        private readonly Dictionary<string, bool> FolderVisibilityStates = [];

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

        private sealed class ThumbnailMetadata
        {
            public string? Folder { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public FileInfo? File { get; set; }
        }

        internal static class DefaultFolders
        {
            private static readonly string UserDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            internal static readonly string FolderA = Path.Combine(UserDirectory, @"AppData\Local\Packages\Microsoft.Windows.ContentDeliveryManager_cw5n1h2txyewy\LocalState\Assets");
            internal static readonly string FolderB = Path.Combine(UserDirectory, @"AppData\Roaming\Microsoft\Windows\Themes");
            internal static readonly string FolderC = Path.Combine(UserDirectory, @"AppData\Local\Packages\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\LocalCache\Microsoft\IrisService");
        }

        public Form1(SplashForm? splash = null)
        {
            InitializeComponent();
            LoadMainSettings();
            SetupAppIcon();
            LoadAssets(splash);
            InitializeFolderVisibility();
            SetupUI();
            PopulateTreeView();
            PopulateThumbnails(splash);
            ApplyFilter();
        }

        public void SetupAppIcon()
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("LocalWallpaperViewer.Assets.lwv.ico");
            if (stream != null)
            {
                this.Icon = new System.Drawing.Icon(stream);
            }
        }

        private void LoadMainSettings()
        {
            _viewMode = Settings.Default.ViewMode;
            OrientationFilter = (OrientationFilterStates)Settings.Default.OrientationFilter;
            ResolutionFilter = (ResolutionFilterStates)Settings.Default.ResolutionFilter;
        }

        private void LoadAssets(SplashForm? splash)
        {
            splash?.UpdateStatus($"Scanning Folders...");
            GetAssets();
        }

        private void InitializeFolderVisibility()
        {
            var visibility = (FolderVisibility)Settings.Default.FolderVisibilityMask;

            if (visibility == FolderVisibility.NotInitialized)
            {
                // First run - check if directories exist
                FolderVisibilityStates[_assetsUserDirectory] = Directory.Exists(_assetsUserDirectory);
                FolderVisibilityStates[_assetsLockScreenDirectory] = Directory.Exists(_assetsLockScreenDirectory);
                FolderVisibilityStates[_assetsSpotLightDirectory] = Directory.Exists(_assetsSpotLightDirectory);
            }
            else
            {
                // Use saved settings
                FolderVisibilityStates[_assetsUserDirectory] = visibility.HasFlag(FolderVisibility.SourceA);
                FolderVisibilityStates[_assetsLockScreenDirectory] = visibility.HasFlag(FolderVisibility.SourceB);
                FolderVisibilityStates[_assetsSpotLightDirectory] = visibility.HasFlag(FolderVisibility.SourceC);
            }
        }

        private FileInfo[] GetAssets()
        {
            var userDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            this._assetsUserDirectory = Path.Combine(userDirectory, @"AppData\Local\Packages\Microsoft.Windows.ContentDeliveryManager_cw5n1h2txyewy\LocalState\Assets");
            this._assetsLockScreenDirectory = Path.Combine(userDirectory, @"AppData\Roaming\Microsoft\Windows\Themes");
            this._assetsSpotLightDirectory = Path.Combine(userDirectory, @"AppData\Local\Packages\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\LocalCache\Microsoft\IrisService");

            var directories = new[]
            {
                this._assetsUserDirectory,
                this._assetsLockScreenDirectory,
                this._assetsSpotLightDirectory
            };
            _allAssetsCache = GetAssetsFromDirectories(directories);
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
                    catch (OutOfMemoryException) { }
                    catch (FileNotFoundException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }
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
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.DrawImage(original, 0, 0, newWidth, newHeight);
            }
            return thumb;
        }

        private void UpdateTreeViewNodes()
        {
            if (_fileTreeView == null ||
                _generalNode == null ||
                _lockscreenNode == null ||
                _spotLightNode == null
                )
            {
                return;
            }

            var nodes = _fileTreeView.Nodes;
            UpdateNodeVisibility(nodes, _generalNode, FolderVisibilityStates[_assetsUserDirectory]);
            UpdateNodeVisibility(nodes, _lockscreenNode, FolderVisibilityStates[_assetsLockScreenDirectory]);
            UpdateNodeVisibility(nodes, _spotLightNode, FolderVisibilityStates[_assetsSpotLightDirectory]);
        }

        private static void UpdateNodeVisibility(TreeNodeCollection nodes, TreeNode node, bool shouldBeVisible)
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

            _thumbnailPanel.SuspendLayout();

            UpdateTreeViewNodes();

            _visibleItemCount = 0;

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

                    // folder filter
                    if (visible && meta.Folder != null)
                    {
                        foreach (var record in FolderVisibilityStates)
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
                    _visibleItemCount++;
                }
            }

            _thumbnailPanel.ResumeLayout();

            _isLandscapeFilterActive = OrientationFilter == OrientationFilterStates.Landscape;

            if (_no_results_label != null)
            {
                _no_results_label.Visible = _visibleItemCount == 0;
            }

            if (_filesToolStripLabel != null)
            {
                _filesToolStripLabel.Text = $"Showing {_visibleItemCount} of {effectiveTotalCount} files";
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

        private static void ShowImageInPopup(FileInfo fileInfo)
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
            string clippedName = (fileName.Length > 33 && finalW < finalH) ? string.Concat(fileName.AsSpan(0, 33), "...") : fileName;

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
            form.FormClosed += (s, e) => form.Dispose();
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

                // context menu was opened on a PictureBox in the FlowLayoutPanel
                if (contextMenu.SourceControl is PictureBox pictureBox &&
                    _pictureMap.TryGetValue(pictureBox, out var pictureFileInfo))
                {
                    fileInfo = pictureFileInfo;
                }
                // context menu was opened on the TreeView
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
                    _pictureMap.TryGetValue(pictureBox, out var pictureFileInfo))
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
                        if (directory.StartsWith(_assetsLockScreenDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            Process.Start(new ProcessStartInfo { Arguments = directory, FileName = "explorer.exe" });
                        }
                        else if (directory.StartsWith(_assetsSpotLightDirectory, StringComparison.OrdinalIgnoreCase))
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
                _pictureMap.TryGetValue(pictureBox, out var fileInfo))
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

        private void OnSettingsMenuItemEditFoldersClick(object? sender, EventArgs e)
        {
            using var settingsForm = new SettingsDialog();
            settingsForm.ShowDialog(this);
        }

        private void OnSettingsMenuItemAboutClick(object? sender, EventArgs e)
        {
            using var aboutForm = new AboutDialog(this.Icon);
            aboutForm.ShowDialog(this);
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
                    FolderVisibilityStates[folderPath] = false; // ignore user setting
                    return;
                }

                FolderVisibilityStates[folderPath] = menuItem.Checked;
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
                    _selectedThumbnail != null &&
                    this._pictureMap.TryGetValue(_selectedThumbnail, out var fileFromThumbnail))
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
                    _selectedThumbnail != null &&
                    this._pictureMap.TryGetValue(_selectedThumbnail, out var fileFromThumbnail))
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

                this._statusStripLabel.ShowTemporaryNotification($"Image saved to: {savePath}");
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
            _selectedThumbnail = null;

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

            _thumbnailPanel.Visible = _viewMode;
            _fileTreeView.Visible = !_viewMode;
            _toggleViewButton.Text = _viewMode ? "Show file list" : "Show thumbnails";
            _toggleViewButton.Image = _viewMode ? GetIconFromFont('\uE9a4') : GetIconFromFont('\uE91b');
            _statusStripFilterLabel.Visible = _viewMode;
            _statusStripThumbnailFilterButton.Visible = _viewMode;
            _statusStripResolutionFilterButton.Visible = _isLandscapeFilterActive;
            _statusStripResolutionFilterButton.Visible = _viewMode;
        }

        private void SaveSettings()
        {
            // convert dict to bitmask
            FolderVisibility visibility = 0;
            if (FolderVisibilityStates.TryGetValue(_assetsUserDirectory, out var a) && a) visibility |= FolderVisibility.SourceA;
            if (FolderVisibilityStates.TryGetValue(_assetsLockScreenDirectory, out var b) && b) visibility |= FolderVisibility.SourceB;
            if (FolderVisibilityStates.TryGetValue(_assetsSpotLightDirectory, out var c) && c) visibility |= FolderVisibility.SourceC;

            // if nothing is selected, explicitly set to None
            if (visibility == 0)
            {
                visibility = FolderVisibility.None;
            }

            Settings.Default.FolderVisibilityMask = (int)visibility;
            Settings.Default.ViewMode = _viewMode;
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
            _fileContextMenuStrip.Items.Add("Open in preview", GetIconFromFont('\uE91b'), OnShowImageMenuItemClick);
            _fileContextMenuStrip.Items.Add("Quick save", GetIconFromFont('\ue896'), OnQuickSaveMenuItemClick);
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

        private void OnThumbnailMouseClick(object? sender, MouseEventArgs e)
        {
            if (_selectedThumbnail != null && _selectedThumbnail.Parent is Panel oldContainer)
            {
                oldContainer.BorderStyle = BorderStyle.None;
            }
            _selectedThumbnail = (PictureBox?)sender; // cast sender to PictureBox
            if (_selectedThumbnail != null && _selectedThumbnail.Parent is Panel newContainer)
            {
                newContainer.BorderStyle = BorderStyle.FixedSingle;
            }
            if (e.Button == MouseButtons.Right && sender is PictureBox clickedPictureBox)
            {
                // display the context menu at the cursor's location
                _fileContextMenuStrip?.Show(clickedPictureBox, e.Location);
            }
        }

        private void PopulateTreeView()
        {
            _generalNode = new("Assets folder")
            {
                ImageIndex = 0,
                SelectedImageIndex = 1
            };
            _lockscreenNode = new("Lockscreen folder")
            {
                ImageIndex = 0,
                SelectedImageIndex = 1
            };
            _spotLightNode = new("SpotLight folder")
            {
                ImageIndex = 0,
                SelectedImageIndex = 1
            };

            if (_fileTreeView == null) return;

            _fileTreeView.Nodes.Add(_generalNode);
            _fileTreeView.Nodes.Add(_lockscreenNode);
            _fileTreeView.Nodes.Add(_spotLightNode);

            _fileTreeView.ExpandAll();

            this._fileTreeView.BeginUpdate();

            foreach (var fileInfo in _allAssetsCache)
            {
                TreeNode fileNode = new(fileInfo.Name)
                {
                    Tag = fileInfo,
                    ImageIndex = 2,
                    SelectedImageIndex = 2
                };

                if (fileInfo.FullName.StartsWith(_assetsLockScreenDirectory, StringComparison.OrdinalIgnoreCase))
                    _lockscreenNode.Nodes.Add(fileNode);
                else if (fileInfo.FullName.StartsWith(_assetsSpotLightDirectory, StringComparison.OrdinalIgnoreCase))
                    _spotLightNode.Nodes.Add(fileNode);
                else
                    _generalNode.Nodes.Add(fileNode);
            }

            if (_allAssetsCache.Length == 0)
            {
                MessageBox.Show("No files found");
            }

            this._fileTreeView.EndUpdate();
        }

        private void PopulateThumbnails(SplashForm? splash)
        {
            if (_thumbnailPanel == null) return;

            _thumbnailPanel.SuspendLayout();
            int total = this._allAssetsCache.Length;
            int count = 0;

            foreach (var fileInfo in this._allAssetsCache)
            {
                count++;
                try
                {
                    splash?.UpdateStatus($"Generating thumbnail {count} of {total}...");
                    Application.DoEvents();
                    if (_thumbnailPanel.IsDisposed || !Application.OpenForms.Cast<Form>().Any()) return; // doevents tax
                    var container = CreateThumbnailContainer(fileInfo);
                    this._thumbnailPanel.Controls.Add(container);
                }
                catch (OutOfMemoryException) { }
                catch (FileNotFoundException) { }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
            _thumbnailPanel.ResumeLayout(true);
            splash?.Close();
        }

        private Panel CreateThumbnailContainer(FileInfo fileInfo)
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
            picBox.MouseClick += OnThumbnailMouseClick;

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

            this._pictureMap[picBox] = fileInfo;

            return container;
        }

        private void OnNodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            if (sender is TreeView treeview && e.Button == MouseButtons.Right)
            {
                treeview.SelectedNode = e.Node;
                _fileContextMenuStrip?.Show(treeview, e.Location);
            }
        }

        private ToolStrip CreateTopToolbar()
        {
            var toolStrip = new ToolStrip { Dock = DockStyle.Top };

            // save button
            var saveButton = new ToolStripButton
            {
                Text = "Save image",
                Alignment = ToolStripItemAlignment.Left,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uE74e') // save icon
            };
            saveButton.Click += OnSaveButtonMenuItemClick;

            // toggle view button
            _toggleViewButton = new ToolStripButton
            {
                Text = "Show file list",
                Alignment = ToolStripItemAlignment.Left,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uE74e')
            };
            _toggleViewButton.Click += (s, e) =>
            {
                _viewMode = !_viewMode;
                UpdateViewState();
            };

            // toolstrip label
            _filesToolStripLabel = new ToolStripLabel
            {
                Alignment = ToolStripItemAlignment.Right,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ue91b')
            };

            // toolstrip separator
            var toolStripSeparator = new ToolStripSeparator { Alignment = ToolStripItemAlignment.Right };

            // settings button & drop-down items
            var settingsButton = CreateSettingsDropdown();

            toolStrip.Items.Add(saveButton);
            toolStrip.Items.Add(_toggleViewButton);
            toolStrip.Items.Add(settingsButton);
            toolStrip.Items.Add(toolStripSeparator);
            toolStrip.Items.Add(_filesToolStripLabel);

            return toolStrip;
        }

        private ToolStripDropDownButton CreateSettingsDropdown()
        {
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

            // show assets folder menu item
            var SettingsMenuItemFolder1 = new ToolStripMenuItem("Show Assets Folder")
            {
                CheckOnClick = true,
                Checked = FolderVisibilityStates[_assetsUserDirectory],
                Tag = _assetsUserDirectory
            };
            SettingsMenuItemFolder1.CheckedChanged += OnFolderMenuItemCheckedChanged;

            // show lockscreen folder menu item
            var SettingsMenuItemFolder2 = new ToolStripMenuItem("Show Lockscreen Folder")
            {
                CheckOnClick = true,
                Checked = FolderVisibilityStates[_assetsLockScreenDirectory],
                Tag = _assetsLockScreenDirectory
            };
            SettingsMenuItemFolder2.CheckedChanged += OnFolderMenuItemCheckedChanged;

            // show spotlight folder menu item
            var SettingsMenuItemFolder3 = new ToolStripMenuItem("Show Spotlight Folder")
            {
                CheckOnClick = true,
                Checked = FolderVisibilityStates[_assetsSpotLightDirectory],
                Tag = _assetsSpotLightDirectory
            };
            SettingsMenuItemFolder3.CheckedChanged += OnFolderMenuItemCheckedChanged;

            // show edit folder menu item
            var SettingsMenuItemEditFolders = new ToolStripMenuItem("Configure Folders")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ue90f') // Wrench icon
            };
            SettingsMenuItemEditFolders.Click += OnSettingsMenuItemEditFoldersClick;

            // show set quick save folder menu item
            var SettingsMenuItemSetQuickSave = new ToolStripMenuItem("Set Quick Save folder")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ue896') // Download icon
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
            settingsButton.DropDownItems.Add(SettingsMenuItemEditFolders);
            settingsButton.DropDownItems.Add(new ToolStripSeparator());
            settingsButton.DropDownItems.Add(SettingsMenuItemSetQuickSave);
            settingsButton.DropDownItems.Add(new ToolStripSeparator());
            settingsButton.DropDownItems.Add(SettingsMenuItemAbout);

            return settingsButton;
        }

        private Panel CreateContentPanel()
        {
            // file tree view image list
            _treeViewIcons = new ImageList { ImageSize = new Size(16, 16) };
            _treeViewIcons.Images.Add("folder", GetIconFromFont('\ue8b7'));
            _treeViewIcons.Images.Add("folderopen", GetIconFromFont('\ue838'));
            _treeViewIcons.Images.Add("file", GetIconFromFont('\ueb9f'));

            var contentPanel = new Panel
            {
                Dock = DockStyle.Fill
            };

            // file tree view
            _fileTreeView = new()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(230, 230, 230),
                AutoSize = true,
                ImageList = _treeViewIcons,
                ImageIndex = 0,
                SelectedImageIndex = 1,
                Visible = false
            };

            _fileTreeView.NodeMouseDoubleClick += OnFileTreeViewDoubleClick;
            _fileTreeView.NodeMouseClick += OnNodeMouseClick;

            // thumbnail panel
            this._thumbnailPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(20, 20, 5, 20),
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.DarkGray,
                Visible = true
            };

            _no_results_label = new Label
            {
                Text = "No files match selected filters",
                Dock = DockStyle.Top,
                Height = 30,
                Visible = false,
            };
            _thumbnailPanel.Controls.Add(_no_results_label);

            contentPanel.Controls.Add(_fileTreeView);
            contentPanel.Controls.Add(_thumbnailPanel);

            typeof(Panel).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic,
                null, _thumbnailPanel, new object[] { true });

            return contentPanel;
        }

        private StatusStrip CreateStatusStrip()
        {
            var statusStrip = new StatusStrip { ShowItemToolTips = true };
            statusStrip.Items.Add(_statusStripLabel);
            statusStrip.Items.Add(new ToolStripStatusLabel("") { Spring = true, TextAlign = ContentAlignment.MiddleRight });

            // orientation filters
            var orientationAllMenuItem = new ToolStripMenuItem("Show All")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ue8cc') // All images icon
            };
            orientationAllMenuItem.Click += OnThumbnailFilterButtonClick;

            var orientationPortraitMenuItem = new ToolStripMenuItem("Portrait")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uee64') // Portrait icon
            };
            orientationPortraitMenuItem.Click += OnThumbnailFilterButtonClick;

            var orientationLandscapeMenuItem = new ToolStripMenuItem("Landscape")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ue70a') // Landscape icon
            };
            orientationLandscapeMenuItem.Click += OnThumbnailFilterButtonClick;

            // resolution filters
            var resolutionAllMenuItem = new ToolStripMenuItem("All Resolutions")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ue8cc') // All Resolutions Icon
            };
            resolutionAllMenuItem.Click += OnResolutionFilterButtonClick;

            var resolutionFullHDMenuItem = new ToolStripMenuItem("1920x1080")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ue70a') // Landscape icon
            };
            resolutionFullHDMenuItem.Click += OnResolutionFilterButtonClick;

            var resolutionUltraHDMenuItem = new ToolStripMenuItem("3840x2160")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ue70a') // Landscape icon
            };
            resolutionUltraHDMenuItem.Click += OnResolutionFilterButtonClick;

            var resolutionvFullHDMenuItem = new ToolStripMenuItem("1080x1920")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uee64') // Portrait icon
            };
            resolutionvFullHDMenuItem.Click += OnResolutionFilterButtonClick;

            // filter label
            _statusStripFilterLabel = new ToolStripStatusLabel("Filter:");
            statusStrip.Items.Add(_statusStripFilterLabel);

            // orientation filter button
            _statusStripThumbnailFilterButton = new ToolStripDropDownButton
            {
                Text = "Show All",
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ue8cc')
            };
            _statusStripThumbnailFilterButton.DropDownItems.Add(orientationAllMenuItem);
            _statusStripThumbnailFilterButton.DropDownItems.Add(orientationPortraitMenuItem);
            _statusStripThumbnailFilterButton.DropDownItems.Add(orientationLandscapeMenuItem);
            statusStrip.Items.Add(_statusStripThumbnailFilterButton);

            // resolution filter button
            _statusStripResolutionFilterButton = new ToolStripDropDownButton
            {
                Text = "All Resolutions",
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ue8cc')
            };
            _statusStripResolutionFilterButton.DropDownItems.Add(resolutionAllMenuItem);
            _statusStripResolutionFilterButton.DropDownItems.Add(resolutionFullHDMenuItem);
            _statusStripResolutionFilterButton.DropDownItems.Add(resolutionUltraHDMenuItem);
            _statusStripResolutionFilterButton.DropDownItems.Add(resolutionvFullHDMenuItem);
            statusStrip.Items.Add(_statusStripResolutionFilterButton);

            return statusStrip;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Text = "Local Background Image Viewer";
        }

        private void SetupUI()
        {
            var toolbar = CreateTopToolbar();
            var contentpanel = CreateContentPanel();
            var statusbar = CreateStatusStrip();

            Controls.Add(contentpanel);  // content container
            Controls.Add(toolbar);     // top toolbar
            Controls.Add(statusbar);    // bottom statusbar

            InitializeFileContextMenuStrip();
        }
    }

    internal sealed class SplashForm : Form
    {
        private Label _splashLabel;

        public SplashForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(250, 80);
            this.BackColor = SystemColors.ControlDark;
            this.Padding = new Padding(2);
            this.ShowInTaskbar = false;

            var panel = new Panel
            {
                BackColor = SystemColors.Control,
                Dock = DockStyle.Fill
            };
            this.Controls.Add(panel);

            _splashLabel = new Label
            {
                Font = SystemFonts.DefaultFont,
                ForeColor = SystemColors.ControlText,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Height = 20
            };
            panel.Controls.Add(_splashLabel);
        }

        public void UpdateStatus(string message)
        {
            _splashLabel.Text = message.Replace("\\n", "\n");
            this.Refresh(); // force redraw
        }
    }

    internal sealed class SettingsDialog : Form
    {
        private readonly string[] _settingNames = ["Assets folder:", "Lockscreen folder:", "SpotLight folder:"];
        private readonly string[] _settingProperties = ["FolderAPath", "FolderBPath", "FolderCPath"];

        private readonly Label[] _folderLabels = new Label[3];
        private readonly Button[] _browseButtons = new Button[3];
        private readonly ToolTip _pathToolTip = new();

        private string[] _currentPaths = new string[3];
        private readonly string[] _defaultPaths = new string[3];

        public SettingsDialog()
        {
            CalculateDefaultPaths();
            InitializeForm();
            InitializeComponents();
            LoadSettings();
        }

        private void CalculateDefaultPaths()
        {
            _defaultPaths[0] = Form1.DefaultFolders.FolderA;
            _defaultPaths[1] = Form1.DefaultFolders.FolderB;
            _defaultPaths[2] = Form1.DefaultFolders.FolderC;
        }

        private void InitializeForm()
        {
            this.Text = "Edit Folders";
            this.ClientSize = new Size(300, 200);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
        }

        private void LoadSettings()
        {
            var settings = Settings.Default;

            for (int i = 0; i < 3; i++)
            {
                string propertyName = _settingProperties[i];
                string? savedPath = settings[propertyName] as string;

                // if the saved path is empty, use default path
                if (string.IsNullOrEmpty(savedPath))
                {
                    _currentPaths[i] = _defaultPaths[i];
                }
                else
                {
                    _currentPaths[i] = savedPath;
                }

                _pathToolTip.SetToolTip(_folderLabels[i], _currentPaths[i]);
                UpdateFolderStatusLabel(i);
            }
        }

        private void SaveSetting(int index, string path)
        {
            string propertyName = _settingProperties[index];

            // set setting dynamically by name
            Settings.Default[propertyName] = path;

            Settings.Default.Save();
        }

        private void InitializeComponents()
        {
            var folderPanel = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 3,
                Dock = DockStyle.Top,
                Padding = new Padding(10),
                AutoSize = true,
            };

            folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            // create the three folder setting rows
            for (int i = 0; i < 3; i++)
            {
                var pathLabel = new Label
                {
                    Text = _settingNames[i],
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    AutoSize = false,
                };
                _folderLabels[i] = pathLabel; // store the reference
                folderPanel.Controls.Add(pathLabel, 0, i); // place in column

                var selectButton = new Button
                {
                    Text = "Select folder",
                    Tag = i, // tag identifies which setting this button belongs to
                    AutoSize = true
                };
                selectButton.Click += BrowseButton_Click;
                _browseButtons[i] = selectButton; // store the reference
                folderPanel.Controls.Add(selectButton, 1, i);
            }

            this.Controls.Add(folderPanel);

            var buttonPanel = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Bottom,
                Padding = new Padding(10, 5, 10, 10),
                AutoSize = true,
            };
            buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            // reset button
            var resetButton = new Button
            {
                Text = "Reset to Defaults",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Right
            };

            resetButton.Click += ResetButton_Click;
            buttonPanel.Controls.Add(resetButton, 1, 0);

            this.Controls.Add(buttonPanel);
            this.ClientSize = new Size(300, folderPanel.Height + buttonPanel.Height);
        }

        private void UpdateFolderStatusLabel(int index)
        {
            string statusText;

            if (string.Equals(_currentPaths[index], _defaultPaths[index], StringComparison.OrdinalIgnoreCase))
            {
                statusText = " (default)";
                // set to empty when resetting to restore the default setting behavior
                if (string.Equals(Settings.Default[_settingProperties[index]] as string, _defaultPaths[index], StringComparison.OrdinalIgnoreCase))
                {
                    // to reset the modified setting to default, clear the saved value
                    Settings.Default[_settingProperties[index]] = string.Empty;
                }
            }
            else
            {
                statusText = " (modified)";
            }

            _folderLabels[index].Text = $"{_settingNames[index]}{statusText}";
        }

        private void BrowseButton_Click(object? sender, EventArgs e)
        {
            if (sender is Button button && button.Tag is int index)
            {
                using var dialog = new FolderBrowserDialog();
                dialog.InitialDirectory = _currentPaths[index];

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string selectedPath = dialog.SelectedPath;

                    _currentPaths[index] = selectedPath;
                    SaveSetting(index, selectedPath);

                    _pathToolTip.SetToolTip(_folderLabels[index], selectedPath);
                    UpdateFolderStatusLabel(index);
                }
            }
        }

        private void ResetButton_Click(object? sender, EventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to reset all folder paths to their default values?", "Confirm Reset", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                for (int i = 0; i < 3; i++)
                {
                    // reset current internal path to the default
                    string defaultPath = _defaultPaths[i];
                    _currentPaths[i] = defaultPath;

                    // force Settings.Default[propertyName] to return empty to load defaults next time
                    SaveSetting(i, string.Empty);

                    _pathToolTip.SetToolTip(_folderLabels[i], defaultPath);
                    UpdateFolderStatusLabel(i);
                }

                MessageBox.Show("Settings have been reset to default values and saved.", "Reset Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }

    internal sealed class AboutDialog : Form
    {
        private string _appname = string.Empty;
        private string _architecture = string.Empty;
        private string _buildtime = string.Empty;

        public AboutDialog(Icon? appIcon = null)
        {
            InitializeData();
            InitializeLayout(appIcon);
        }

        private static Bitmap? GetIconBitmapOfSize(Icon? icon, int size)
        {
            if (icon == null)
                return null;

            try
            {
                using Icon sized = new Icon(icon, new Size(size, size));
                return sized.ToBitmap();
            }
            catch
            {
                return icon.ToBitmap();
            }
        }

        public void InitializeData()
        {
            // get app name
            _appname = Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyProductAttribute>()
                ?.Product ?? "Local Wallpaper Viewer";

            DateTime buildTime;

            // get buildtime
            try
            {
#if DEBUG
                var assembly = Assembly.GetEntryAssembly();
                string? filePath = assembly?.Location;
#else
                string? filePath = Environment.ProcessPath;
#endif

                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    buildTime = File.GetLastWriteTime(filePath);
                }
                else
                {
                    buildTime = DateTime.MinValue;
                }
            }
            catch
            {
                buildTime = DateTime.MinValue;
            }

            _buildtime = buildTime == DateTime.MinValue ? "Unknown" : buildTime.ToString("yyyy-MM-dd HH:mm:ss");

            // get architecture
            if (Environment.Is64BitProcess)
            {
                _architecture = "64-bit";
            }
            else
            {
                _architecture = "32-bit";
            }
        }

        public void InitializeLayout(Icon? appIcon = null)
        {
            this.Text = $"About {_appname}";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(450, 335);

            TableLayoutPanel tableLayoutPanel = new()
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 2
            };
            tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
            tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            tableLayoutPanel.RowCount = 6;
            tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F)); // name
            tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F)); // version
            tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F)); // buildtime
            tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F)); // repo
            tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // license
            tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F)); // ok button

            // app icon picture
            Control iconControl = new Control
            {
                Width = 64,
                Height = 64,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            iconControl.Paint += (s, e) =>
            {
                if (appIcon != null)
                {
                    using var sized = new Icon(appIcon, new Size(64, 64));
                    e.Graphics.DrawIcon(sized, 0, 0);
                }
            };

            iconControl.Margin = new Padding(20, 14, 0, 0);
            tableLayoutPanel.Controls.Add(iconControl, 0, 0);
            tableLayoutPanel.SetRowSpan(iconControl, 4);

            // application name
            Label AppNameLabel = new()
            {
                Text = _appname,
                Font = new Font(this.Font.FontFamily, 14, FontStyle.Bold),
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleLeft
            };
            tableLayoutPanel.Controls.Add(AppNameLabel, 1, 0);

            // version label
            Label VersionLabel = new()
            {
                Text = $"Version {Application.ProductVersion[..Application.ProductVersion.LastIndexOf('+')]} ({_architecture})",
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleLeft
            };
            tableLayoutPanel.Controls.Add(VersionLabel, 1, 1);

            // description label
            Label BuildTimeLabel = new()
            {
                Text = $"Build Time: {_buildtime}",
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleLeft
            };
            tableLayoutPanel.Controls.Add(BuildTimeLabel, 1, 2);

            // github link label
            LinkLabel GithubLinkLabel = new LinkLabel
            {
                Text = "Github Repo",
                Tag = "https://github.com/narfel/LocalWallpaperViewer/",
                Dock = DockStyle.Top
            };
            GithubLinkLabel.LinkClicked += (s, args) =>
            {
                var filename = GithubLinkLabel.Tag.ToString();
                if (filename != null)
                {
                    Process.Start(new ProcessStartInfo(filename) { UseShellExecute = true });
                }
            };
            tableLayoutPanel.Controls.Add(GithubLinkLabel, 1, 3);

            // license
            var licenseGroupBox = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = "GNU General Public License",
                BackColor = SystemColors.Control
            };

            var licenseText = new RichTextBox
            {
                DetectUrls = true,
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Text = @"This program is free software; you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation; either version 3 of the License, or at your option any later version.
This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details. 
You should have received a copy of the GNU General Public License along with this program. If not, see <https://www.gnu.org/licenses/>."
            };
            licenseText.LinkClicked += (s, args) =>
            {
                var url = args.LinkText;
                if (!string.IsNullOrEmpty(url))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
            };

            licenseGroupBox.Controls.Add(licenseText);
            tableLayoutPanel.Controls.Add(licenseGroupBox, 0, 4);
            tableLayoutPanel.SetColumnSpan(licenseGroupBox, 2); // span both icon and info cols

            Button okButton = new Button
            {
                Text = "&OK",
                DialogResult = DialogResult.OK,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };

            FlowLayoutPanel buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill
            };
            buttonPanel.Controls.Add(okButton);

            tableLayoutPanel.Controls.Add(buttonPanel, 0, 5);
            tableLayoutPanel.SetColumnSpan(buttonPanel, 2); // span both icon and info cols

            this.Controls.Add(tableLayoutPanel);
        }
    }

    internal static class StatusLabelExtensions
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
}
