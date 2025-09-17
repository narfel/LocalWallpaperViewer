using System;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace LocalWallpaperViewer
{
    public partial class Form1 : Form
    {
        private FileInfo[] assets = Array.Empty<FileInfo>();
        private Dictionary<PictureBox, FileInfo> pictureMap = [];

        private ToolStrip? _toolStrip;
        private ToolStripButton? _toggleViewButton;
        private FlowLayoutPanel? _thumbnailPanel;
        private TreeView? _fileTreeView;
        private ToolStripDropDownButton? _statusStripThumbnailFilterButton;
        private ToolStripDropDownButton? _statusStripResolutionFilterButton;
        private PictureBox? selectedThumbnail = null;
        private ToolStripStatusLabel? _statusStripFilterLabel;
        private ContextMenuStrip? _fileContextMenuStrip;

        private string assetsUserDirectory = string.Empty;
        private string assetsLockScreenDirectory = string.Empty;
        private string assetsSpotLightDirectory = string.Empty;
        private bool ViewMode;
        private bool portraitFilterActive = false;
        private OrientationFilterStates OrientationFilter;
        private ResolutionFilterStates ResolutionFilter;
        private Dictionary<string, bool> folderVisibilityStates = [];

        enum OrientationFilterStates { All, Portrait, Landscape }
        enum ResolutionFilterStates { All, HD, FullHD, UltraHD }

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
            this.assets = GetAssets();
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
            return GetAssetsFromDirectories(directories);
        }

        private static FileInfo[] GetAssetsFromDirectories(IEnumerable<string> directories)
        {
            var results = new List<FileInfo>();
            foreach (var directory in directories)
            {
                if (!Directory.Exists(directory))
                {
                    MessageBox.Show($"Directory '{directory}' does not exist!", "Warning",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }
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

        private void ApplyFilter()
        {
            if (_thumbnailPanel == null)
            {
                UpdateFilterButtonText();
                return;
            }

            foreach (var container in _thumbnailPanel.Controls.OfType<Panel>())
            {
                var picBox = container.Controls.OfType<PictureBox>().FirstOrDefault();
                if (picBox == null)
                {
                    container.Visible = OrientationFilter == 0; // hide if missing
                    continue;
                }

                if (container.Tag is ThumbnailMetadata meta)
                {
                    bool visible = true;

                    // Orientation filter
                    if (OrientationFilter == OrientationFilterStates.Portrait)
                    {
                        visible &= meta.Height > meta.Width;
                        ResolutionFilter = ResolutionFilterStates.All;
                    }
                    else if (OrientationFilter == OrientationFilterStates.Landscape)
                    {
                        visible &= meta.Width > meta.Height;
                    }
                    else if (OrientationFilter == OrientationFilterStates.All)
                    {
                        ResolutionFilter = ResolutionFilterStates.All;
                    }

                    // Resolution filter
                    if (ResolutionFilter == ResolutionFilterStates.FullHD)
                        visible &= meta.Width == 1920 && meta.Height == 1080;
                    else if (ResolutionFilter == ResolutionFilterStates.UltraHD)
                        visible &= meta.Width == 3840 && meta.Height == 2160;

                    // Folder filter:
                    if (meta.Folder != null)
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

                    container.Visible = visible;
                }
                portraitFilterActive = OrientationFilter == OrientationFilterStates.Landscape;
            }

            UpdateFilterButtonText();
            UpdateViewState();
        }

        private void UpdateFilterButtonText()
        {
            if (_statusStripThumbnailFilterButton != null)
            {
                switch (OrientationFilter)
                {
                    case OrientationFilterStates.All:
                        _statusStripThumbnailFilterButton.Text = "All";
                        _statusStripThumbnailFilterButton.Image = GetIconFromFont('\uE8a9'); // All images icon
                        break;
                    case OrientationFilterStates.Portrait:
                        _statusStripThumbnailFilterButton.Text = "Portrait";
                        _statusStripThumbnailFilterButton.Image = GetIconFromFont('\uF573'); // Portrait icon
                        break;
                    case OrientationFilterStates.Landscape:
                        _statusStripThumbnailFilterButton.Text = "Landscape";
                        _statusStripThumbnailFilterButton.Image = GetIconFromFont('\uF577'); // Landscape icon
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

            // expected sizes: 3840x2160, 1920x1080 (landscape) or 1080x1920 (portrait)
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

                // The context menu was opened on a PictureBox in the FlowLayoutPanel
                if (contextMenu.SourceControl is PictureBox pictureBox &&
                    pictureMap.TryGetValue(pictureBox, out var pictureFileInfo))
                {
                    fileInfo = pictureFileInfo;
                }
                // The context menu was opened on the TreeView
                else if (contextMenu.SourceControl is TreeView treeView &&
                        treeView.SelectedNode?.Tag is FileInfo nodeFileInfo)
                {
                    fileInfo = nodeFileInfo;
                }

                // Show the image if a FileInfo was found
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

                // If PictureBox in the FlowLayoutPanel
                if (contextMenu.SourceControl is PictureBox pictureBox &&
                    pictureMap.TryGetValue(pictureBox, out var pictureFileInfo))
                {
                    fileInfo = pictureFileInfo;
                }
                // If Node in TreeView
                else if (contextMenu.SourceControl is TreeView treeView &&
                        treeView.SelectedNode?.Tag is FileInfo nodeFileInfo)
                {
                    fileInfo = nodeFileInfo;
                }

                // Open folder if a FileInfo was found
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

        private void OnSettingsMenuItemAboutClick(object? sender, EventArgs e)
        {
            MessageBox.Show("Version 0.01 alpha", "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnFolderMenuItemCheckedChanged(object? sender, EventArgs e)
        {
            var menuItem = (ToolStripMenuItem)sender!;

            // Update the dictionary
            if (menuItem.Tag is string folderPath)
            {
                folderVisibilityStates[folderPath] = menuItem.Checked;
            }

            ApplyFilter();
        }

        private void OnSaveButtonClick(object? sender, EventArgs e)
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
            _statusStripResolutionFilterButton.Visible = portraitFilterActive;
        }

        private void SaveSettings()
        {
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
            // Cast the sender to a ToolStripMenuItem
            ToolStripMenuItem? clickedItem = sender as ToolStripMenuItem;

            if (clickedItem != null)
            {
                ResolutionFilter = clickedItem.Text switch
                {
                    "All Resolutions" => ResolutionFilterStates.All,
                    "1920x1080" => ResolutionFilterStates.FullHD,
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
            _fileContextMenuStrip.Items.Add("Save image", GetIconFromFont('\uE74e'), OnSaveButtonClick);
            _fileContextMenuStrip.Items.Add(new ToolStripSeparator());
            _fileContextMenuStrip.Items.Add("Open image folder", GetIconFromFont('\uE838'), OnOpenFolderMenuItemClick);
        }

        public static Bitmap GetIconFromFont(char iconChar)
        {
            int fontSize = 16;
            int padding = (int)(fontSize * 0.6); // padding to prevent clipping 
            int bitmapSize = fontSize + padding;

            Bitmap bitmap = new Bitmap(bitmapSize, bitmapSize);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                // Set up drawing properties for quality
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                // Use a StringFormat to center the glyph and allow for full rendering
                StringFormat format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.NoWrap
                };

                // Create the font and brush
                Font font = new("Segoe Fluent Icons", fontSize);
                SolidBrush brush = new(Color.Black);

                // Define a rectangle for the drawing, including padding
                RectangleF rect = new(0, 0, bitmapSize, bitmapSize);

                // Draw the character onto the bitmap within the defined rectangle
                g.DrawString(iconChar.ToString(), font, brush, rect, format);

                return bitmap;
            }
        }

        private void SetupUI()
        {
            // Top Toolbar
            _toolStrip = new ToolStrip
            {
                Dock = DockStyle.Top
            };

            // Save button
            var saveButton = new ToolStripButton
            {
                Text = "Save to disk",
                Alignment = ToolStripItemAlignment.Left,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uE74e') // save icon
            };

            // Toggle View button
            _toggleViewButton = new ToolStripButton
            {
                Text = "Show file list",
                Alignment = ToolStripItemAlignment.Left,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uE74e')
            };

            // Settings button
            var settingsButton = new ToolStripDropDownButton
            {
                Text = "Settings",
                ShowDropDownArrow = false,
                ToolTipText = "",
                Alignment = ToolStripItemAlignment.Right,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uE713')
            };

            // Show Assets Folder menu item
            var SettingsMenuItemFolder1 = new ToolStripMenuItem("Show Assets Folder")
            {
                CheckOnClick = true,
                Checked = true,
                Tag = assetsUserDirectory
            };
            SettingsMenuItemFolder1.CheckedChanged += OnFolderMenuItemCheckedChanged;

            // Show Lockscreen Folder menu item
            var SettingsMenuItemFolder2 = new ToolStripMenuItem("Show Lockscreen Folder")
            {
                CheckOnClick = true,
                Checked = true,
                Tag = assetsLockScreenDirectory
            };
            SettingsMenuItemFolder2.CheckedChanged += OnFolderMenuItemCheckedChanged;

            // Show Spotlight Folder menu item
            var SettingsMenuItemFolder3 = new ToolStripMenuItem("Show Spotlight Folder")
            {
                CheckOnClick = true,
                Checked = true,
                Tag = assetsSpotLightDirectory
            };
            SettingsMenuItemFolder3.CheckedChanged += OnFolderMenuItemCheckedChanged;

            // About menu item
            var SettingsMenuItemAbout = new ToolStripMenuItem("About")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uE946') // Info icon
            };
            SettingsMenuItemAbout.Click += OnSettingsMenuItemAboutClick;

            folderVisibilityStates[assetsUserDirectory] = true;
            folderVisibilityStates[assetsLockScreenDirectory] = true;
            folderVisibilityStates[assetsSpotLightDirectory] = true;

            settingsButton.DropDownItems.Add(SettingsMenuItemFolder1);
            settingsButton.DropDownItems.Add(SettingsMenuItemFolder2);
            settingsButton.DropDownItems.Add(SettingsMenuItemFolder3);
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
                Image = GetIconFromFont('\uF573') // Portrait icon
            };

            var toolStripMenuItem3 = new ToolStripMenuItem("Landscape")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uF577') // Landscape icon
            };

            var toolStripMenuItem4 = new ToolStripMenuItem("All Resolutions")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ue740') // All images icon
            };

            var toolStripMenuItem5 = new ToolStripMenuItem("1920x1080")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uF577') // Portrait icon
            };

            var toolStripMenuItem6 = new ToolStripMenuItem("3840x2160")
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uF577') // Portrait icon
            };

            _toolStrip.Items.Add(saveButton);
            _toolStrip.Items.Add(_toggleViewButton);
            _toolStrip.Items.Add(settingsButton);

            saveButton.Click += OnSaveButtonClick;

            _toggleViewButton.Click += (s, e) =>
            {
                ViewMode = !_thumbnailPanel?.Visible ?? true; // null reference nonsense
                UpdateViewState();
            };

            toolStripMenuItem1.Click += OnThumbnailFilterButtonClick;
            toolStripMenuItem2.Click += OnThumbnailFilterButtonClick;
            toolStripMenuItem3.Click += OnThumbnailFilterButtonClick;
            toolStripMenuItem4.Click += OnResolutionFilterButtonClick;
            toolStripMenuItem5.Click += OnResolutionFilterButtonClick;
            toolStripMenuItem6.Click += OnResolutionFilterButtonClick;

            // === FILE TREE VIEW ===
            _fileTreeView = new()
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.DarkGray
            };

            TreeNode generalNode = new("Assets folder");
            TreeNode lockscreenNode = new("Lockscreen folder");
            TreeNode spotLightNode = new("SpotLight folder");

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

            foreach (var fileInfo in assets)
            {
                TreeNode fileNode = new(fileInfo.Name) { Tag = fileInfo };

                if (fileInfo.FullName.StartsWith(assetsLockScreenDirectory, StringComparison.OrdinalIgnoreCase))
                    lockscreenNode.Nodes.Add(fileNode);
                else if (fileInfo.FullName.StartsWith(assetsSpotLightDirectory, StringComparison.OrdinalIgnoreCase))
                    spotLightNode.Nodes.Add(fileNode);
                else
                    generalNode.Nodes.Add(fileNode);
            }

            if (assets.Length == 0)
            {
                MessageBox.Show("No files found");
            }

            // === THUMBNAIL PANEL ===
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

            foreach (var fileInfo in this.assets)
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
                        Cursor = Cursors.Hand
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
                            // Display the context menu at the cursor's location
                            if (_fileContextMenuStrip != null)
                            {
                                _fileContextMenuStrip.Show(picBox, e.Location);
                            }
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
                            File = fileInfo
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

            // === STATUS STRIP (BOTTOM) ===
            var statusStrip = new StatusStrip { ShowItemToolTips = true };
            var statusStripLabel = new ToolStripStatusLabel("Double click an image from the list to view");
            statusStrip.Items.Add(statusStripLabel);
            statusStrip.Items.Add(new ToolStripStatusLabel("") { Spring = true, TextAlign = ContentAlignment.MiddleRight });

            _statusStripFilterLabel = new ToolStripStatusLabel("Filter:");
            statusStrip.Items.Add(_statusStripFilterLabel);

            _statusStripThumbnailFilterButton = new ToolStripDropDownButton
            {
                Text = "Show All",
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\uE8a9') // All images icon
            };

            _statusStripResolutionFilterButton = new ToolStripDropDownButton
            {
                Text = "All Resolutions",
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Image = GetIconFromFont('\ue740')
            };

            // Add controls to form
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
            _statusStripResolutionFilterButton.DropDownItems.Add(toolStripMenuItem6);
            statusStrip.Items.Add(_statusStripResolutionFilterButton);

            InitializeFileContextMenuStrip();
            UpdateViewState();
            ApplyFilter();
        }
    }
}
