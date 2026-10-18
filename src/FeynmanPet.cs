using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FeynmanPet
{
    internal static class NativeMethods
    {
        public const int WmNcHitTest = 0x0084;
        public const int WmMouseMove = 0x0200;
        public const int WmLeftButtonDown = 0x0201;
        public const int WmLeftButtonUp = 0x0202;
        public const int WmRightButtonUp = 0x0205;
        public const int WmCaptureChanged = 0x0215;
        public const int HtTransparent = -1;
        public const int GwlExStyle = -20;
        public const int WsExToolWindow = 0x00000080;
        public const int WsExTransparent = 0x00000020;
        public const int WsExNoActivate = 0x08000000;
        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoMove = 0x0002;
        public const uint SwpNoActivate = 0x0010;
        public const uint SwpShowWindow = 0x0040;
        public static readonly IntPtr HwndTopmost = new IntPtr(-1);
        public static readonly IntPtr HwndNotTopmost = new IntPtr(-2);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int index, int newStyle);

        [DllImport("user32.dll")]
        public static extern IntPtr SetCapture(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }
    }

    internal sealed class AppSettings
    {
        public string Profile = "64x96";
        public double Left = -1;
        public double Top = -1;
        public bool Topmost = true;
        public bool AutoWalk = true;

        public static string GetPath(string projectRoot)
        {
            return Path.Combine(projectRoot, "data", "pet-settings.ini");
        }

        public static AppSettings Load(string projectRoot)
        {
            AppSettings settings = new AppSettings();
            string path = GetPath(projectRoot);
            if (!File.Exists(path))
            {
                return settings;
            }
            try
            {
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int split = line.IndexOf('=');
                    if (split <= 0)
                    {
                        continue;
                    }
                    string key = line.Substring(0, split).Trim().ToLowerInvariant();
                    string value = line.Substring(split + 1).Trim();
                    if (key == "profile" && IsKnownProfile(value))
                    {
                        settings.Profile = value;
                    }
                    else if (key == "left")
                    {
                        settings.Left = ParseDouble(value, settings.Left);
                    }
                    else if (key == "top")
                    {
                        settings.Top = ParseDouble(value, settings.Top);
                    }
                    else if (key == "topmost")
                    {
                        settings.Topmost = ParseBool(value, settings.Topmost);
                    }
                    else if (key == "auto_walk")
                    {
                        settings.AutoWalk = ParseBool(value, settings.AutoWalk);
                    }
                }
            }
            catch (Exception exception)
            {
                RuntimeLog.Write("Settings read failed: " + exception.Message);
            }
            return settings;
        }

        public void Save(string projectRoot)
        {
            try
            {
                string path = GetPath(projectRoot);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                StringBuilder text = new StringBuilder();
                text.AppendLine("profile=" + Profile);
                text.AppendLine("left=" + Left.ToString("0.###", CultureInfo.InvariantCulture));
                text.AppendLine("top=" + Top.ToString("0.###", CultureInfo.InvariantCulture));
                text.AppendLine("topmost=" + (Topmost ? "1" : "0"));
                text.AppendLine("auto_walk=" + (AutoWalk ? "1" : "0"));
                File.WriteAllText(path, text.ToString(), Encoding.UTF8);
            }
            catch (Exception exception)
            {
                RuntimeLog.Write("Settings write failed: " + exception.Message);
            }
        }

        public static bool IsKnownProfile(string profile)
        {
            return profile == "48x72" || profile == "64x96" || profile == "96x144";
        }

        private static double ParseDouble(string value, double fallback)
        {
            double parsed;
            return Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : fallback;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            return value == "1" || String.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                ? true
                : (value == "0" || String.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
                    ? false
                    : fallback);
        }
    }

    internal sealed class SpriteFrame
    {
        public BitmapSource Image;
        public byte[] Bgra;
        public int Width;
        public int Height;

        public byte AlphaAt(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return 0;
            }
            return Bgra[(y * Width + x) * 4 + 3];
        }

        public int[] ContentBounds()
        {
            int minX = Width;
            int minY = Height;
            int maxX = -1;
            int maxY = -1;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (AlphaAt(x, y) <= 8)
                    {
                        continue;
                    }
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
            if (maxX < 0)
            {
                return new int[] { 0, 0, 0, 0 };
            }
            return new int[] { minX, minY, maxX - minX + 1, maxY - minY + 1 };
        }
    }

    internal sealed class StateDefinition
    {
        public string Name;
        public int FrameDelayMs;
        public bool Loop;
        public int AutoReturnMs;
        public string DefaultBubble;
        public List<SpriteFrame> Frames = new List<SpriteFrame>();
        public int UnionMinX;
        public int UnionMinY;
        public int UnionWidth;
        public int UnionHeight;
    }

    internal sealed class SizeProfile
    {
        public string Name;
        public int Width;
        public int Height;
        public Dictionary<string, StateDefinition> States =
            new Dictionary<string, StateDefinition>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class AssetCatalog
    {
        public string AssetRoot;
        public Dictionary<string, SizeProfile> Profiles =
            new Dictionary<string, SizeProfile>(StringComparer.OrdinalIgnoreCase);

        public static AssetCatalog Load(string projectRoot)
        {
            AssetCatalog catalog = new AssetCatalog();
            catalog.AssetRoot = Path.Combine(projectRoot, "assets", "feynman");
            if (!Directory.Exists(catalog.AssetRoot))
            {
                throw new DirectoryNotFoundException("找不到素材目录: " + catalog.AssetRoot);
            }
            catalog.AddProfile("48x72", 48, 72);
            catalog.AddProfile("64x96", 64, 96);
            catalog.AddProfile("96x144", 96, 144);
            return catalog;
        }

        private void AddProfile(string name, int width, int height)
        {
            SizeProfile profile = new SizeProfile();
            profile.Name = name;
            profile.Width = width;
            profile.Height = height;
            profile.States["idle"] = AddState(profile, name, "idle", 6, 260, true, 0, null);
            profile.States["blink"] = AddState(profile, name, "blink", 6, 80, false, 0, null);
            profile.States["walk"] = AddState(
                profile, name, "walk", 8, 100, true, 8000, "去桌面上转转。");
            profile.States["jump"] = AddState(
                profile, name, "jump", 6, 100, false, 0, "起跳！");
            profile.States["sleep"] = AddState(
                profile, name, "sleep", 4, 450, true, 0, "想个问题，打个盹。");
            profile.States["think"] = AddState(
                profile, name, "think", 6, 260, true, 9000, "这里有个有趣的问题。");
            profile.States["explain"] = AddState(
                profile, name, "explain", 6, 220, true, 12000,
                "F = ma。再想想，这个公式为什么成立？");
            profile.States["pet"] = AddState(
                profile, name, "pet", 6, 120, false, 0, "嘿，好奇心收到。");
            profile.States["click"] = AddState(
                profile, name, "click", 4, 100, false, 0, "嗯？你也对物理感兴趣？");
            Profiles[name] = profile;
        }

        private StateDefinition AddState(
            SizeProfile profile,
            string profileName,
            string stateName,
            int count,
            int delay,
            bool loop,
            int autoReturn,
            string defaultBubble)
        {
            StateDefinition state = new StateDefinition();
            state.Name = stateName;
            state.FrameDelayMs = delay;
            state.Loop = loop;
            state.AutoReturnMs = autoReturn;
            state.DefaultBubble = defaultBubble;

            int unionMinX = profile.Width;
            int unionMinY = profile.Height;
            int unionMaxX = -1;
            int unionMaxY = -1;
            for (int index = 0; index < count; index++)
            {
                string relative = String.Format(
                    CultureInfo.InvariantCulture,
                    "{0}/{1}/frame_{2:00}.png",
                    profileName,
                    stateName,
                    index);
                string path = Path.Combine(
                    AssetRoot,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                SpriteFrame frame = LoadFrame(path, profile.Width, profile.Height);
                state.Frames.Add(frame);
                int[] bounds = frame.ContentBounds();
                unionMinX = Math.Min(unionMinX, bounds[0]);
                unionMinY = Math.Min(unionMinY, bounds[1]);
                unionMaxX = Math.Max(unionMaxX, bounds[0] + bounds[2] - 1);
                unionMaxY = Math.Max(unionMaxY, bounds[1] + bounds[3] - 1);
            }
            state.UnionMinX = unionMinX;
            state.UnionMinY = unionMinY;
            state.UnionWidth = unionMaxX - unionMinX + 1;
            state.UnionHeight = unionMaxY - unionMinY + 1;
            return state;
        }

        private static SpriteFrame LoadFrame(string path, int expectedWidth, int expectedHeight)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("缺少动画帧", path);
            }
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            FormatConvertedBitmap converted = new FormatConvertedBitmap(
                bitmap, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            if (converted.PixelWidth != expectedWidth || converted.PixelHeight != expectedHeight)
            {
                throw new InvalidDataException(
                    "素材尺寸错误: " + path + " = " +
                    converted.PixelWidth + "x" + converted.PixelHeight);
            }
            SpriteFrame frame = new SpriteFrame();
            frame.Image = converted;
            frame.Width = converted.PixelWidth;
            frame.Height = converted.PixelHeight;
            frame.Bgra = new byte[frame.Width * frame.Height * 4];
            converted.CopyPixels(frame.Bgra, frame.Width * 4, 0);
            return frame;
        }

        public int CountFrames()
        {
            int count = 0;
            foreach (SizeProfile profile in Profiles.Values)
            {
                foreach (StateDefinition state in profile.States.Values)
                {
                    count += state.Frames.Count;
                }
            }
            return count;
        }
    }

    internal static class RuntimeLog
    {
        private static string logPath;
        private static readonly object Sync = new object();

        public static void Initialize(string projectRoot)
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, "logs"));
            logPath = Path.Combine(projectRoot, "logs", "runtime.log");
        }

        public static void Write(string message)
        {
            if (String.IsNullOrEmpty(logPath))
            {
                return;
            }
            try
            {
                lock (Sync)
                {
                    File.AppendAllText(
                        logPath,
                        String.Format(
                            CultureInfo.InvariantCulture,
                            "{0:yyyy-MM-dd HH:mm:ss.fff}  {1}{2}",
                            DateTime.Now,
                            message,
                            Environment.NewLine),
                        Encoding.UTF8);
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class PetWindow : Window
    {
        private readonly string projectRoot;
        private readonly AssetCatalog catalog;
        private readonly AppSettings settings;
        private readonly bool persistSettings;
        private readonly Image characterImage;
        private readonly ScaleTransform characterFlip;
        private readonly DispatcherTimer frameTimer;
        private readonly DispatcherTimer autoReturnTimer;
        private readonly DispatcherTimer behaviorTimer;
        private readonly DispatcherTimer bubbleTimer;
        private readonly DispatcherTimer demoTimer;
        private readonly DispatcherTimer exitTimer;
        private readonly ContextMenu mainMenu;
        private readonly Dictionary<string, MenuItem> sizeMenuItems =
            new Dictionary<string, MenuItem>(StringComparer.OrdinalIgnoreCase);
        private readonly MenuItem topmostMenuItem;
        private readonly MenuItem autoWalkMenuItem;
        private readonly SpeechBubbleWindow bubbleWindow;
        private AssistantSettings assistantSettings;
        private AssistantWindow assistantWindow;
        private MicrophoneButtonWindow microphoneWindow;

        private System.Windows.Forms.NotifyIcon trayIcon;
        private System.Windows.Forms.ContextMenuStrip trayMenu;
        private System.Windows.Forms.ToolStripMenuItem trayShowItem;
        private System.Windows.Forms.ToolStripMenuItem trayHideItem;
        private System.Windows.Forms.ToolStripMenuItem trayTopmostItem;
        private System.Windows.Forms.ToolStripMenuItem trayAutoWalkItem;
        private readonly Dictionary<string, System.Windows.Forms.ToolStripMenuItem> traySizeItems =
            new Dictionary<string, System.Windows.Forms.ToolStripMenuItem>(
                StringComparer.OrdinalIgnoreCase);

        private SizeProfile currentProfile;
        private StateDefinition currentState;
        private SpriteFrame currentFrame;
        private string currentStateName;
        private int frameIndex;
        private int idleTicks;
        private int movementDirection = -1;
        private bool isMouseDown;
        private bool didDrag;
        private bool autoWalkEnabled;
        private double dpiScale = 1.0;
        private Point mouseDownScreen;
        private Point mouseDownWindow;
        private DateTime lastClickUtc = DateTime.MinValue;
        private Point lastClickImagePoint;
        private HwndSource hwndSource;
        private Action demoCompletion;
        private int demoIndex;
        private bool assistantInteractionActive;
        private bool petClosing;

        public PetWindow(
            string projectRoot,
            AssetCatalog catalog,
            AppSettings settings,
            string profileOverride,
            bool persistSettings)
        {
            this.projectRoot = projectRoot;
            this.catalog = catalog;
            this.settings = settings;
            this.persistSettings = persistSettings;
            this.autoWalkEnabled = settings.AutoWalk;
            this.assistantSettings = AssistantSettings.Load(projectRoot);

            string profileName = !String.IsNullOrEmpty(profileOverride)
                ? profileOverride
                : settings.Profile;
            if (!catalog.Profiles.ContainsKey(profileName))
            {
                profileName = "64x96";
            }
            currentProfile = catalog.Profiles[profileName];

            Title = "Feynman Desktop Pet";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            Topmost = settings.Topmost;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = currentProfile.Width;
            Height = currentProfile.Height;
            FocusVisualStyle = null;

            frameTimer = new DispatcherTimer(DispatcherPriority.Render);
            autoReturnTimer = new DispatcherTimer(DispatcherPriority.Background);
            behaviorTimer = new DispatcherTimer(DispatcherPriority.Background);
            bubbleTimer = new DispatcherTimer(DispatcherPriority.Background);
            demoTimer = new DispatcherTimer(DispatcherPriority.Background);
            exitTimer = new DispatcherTimer(DispatcherPriority.Background);
            characterFlip = new ScaleTransform(1, 1);

            Grid root = new Grid();
            root.Background = Brushes.Transparent;
            root.ClipToBounds = true;
            Content = root;

            characterImage = new Image();
            characterImage.Stretch = Stretch.Fill;
            characterImage.HorizontalAlignment = HorizontalAlignment.Stretch;
            characterImage.VerticalAlignment = VerticalAlignment.Stretch;
            characterImage.RenderTransformOrigin = new Point(0.5, 0.5);
            characterImage.RenderTransform = characterFlip;
            characterImage.Cursor = Cursors.Hand;
            RenderOptions.SetBitmapScalingMode(characterImage, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(characterImage, EdgeMode.Aliased);
            root.Children.Add(characterImage);

            bubbleWindow = new SpeechBubbleWindow();

            mainMenu = BuildContextMenu(
                out topmostMenuItem,
                out autoWalkMenuItem);
            ContextMenu = mainMenu;

            frameTimer.Tick += FrameTimerTick;
            autoReturnTimer.Tick += AutoReturnTick;
            behaviorTimer.Tick += BehaviorTimerTick;
            bubbleTimer.Tick += BubbleTimerTick;
            demoTimer.Tick += DemoTimerTick;
            exitTimer.Tick += ExitTimerTick;
            SourceInitialized += PetWindowSourceInitialized;
            Loaded += PetWindowLoaded;
            Closed += PetWindowClosed;
            IsVisibleChanged += PetWindowVisibilityChanged;
            LocationChanged += delegate
            {
                if (bubbleWindow.IsVisible)
                {
                    bubbleWindow.PositionAbove(this);
                }
                if (microphoneWindow != null)
                {
                    microphoneWindow.PositionNearPet();
                }
            };
            SizeChanged += delegate
            {
                if (bubbleWindow.IsVisible)
                {
                    bubbleWindow.PositionAbove(this);
                }
                if (microphoneWindow != null)
                {
                    microphoneWindow.PositionNearPet();
                }
            };

            SetState("idle", null, false, false);
            UpdateMenuChecks();
        }

        public string CurrentStateName
        {
            get { return currentStateName; }
        }

        public string CurrentProfileName
        {
            get { return currentProfile.Name; }
        }

        public bool OpenAssistantOnStartup
        {
            get;
            set;
        }

        public bool HasState(string stateName)
        {
            return currentProfile.States.ContainsKey(stateName);
        }

        public void OpenAssistant(bool startRecording)
        {
            if (assistantWindow == null)
            {
                assistantWindow = new AssistantWindow(projectRoot, this, persistSettings);
            }
            if (startRecording)
            {
                assistantWindow.OpenAndStartRecording();
            }
            else
            {
                assistantWindow.OpenOnly();
            }
        }

        public void ApplyAssistantSettings(AssistantSettings updatedSettings)
        {
            assistantSettings = updatedSettings;
            UpdateMicrophoneButtonVisibility();
            if (assistantWindow != null)
            {
                assistantWindow.SetTopmost(Topmost);
            }
        }

        public void OnAssistantClosed()
        {
            assistantWindow = null;
            assistantInteractionActive = false;
            SetMicButtonMode(MicButtonMode.Idle);
            if (!petClosing && IsLoaded)
            {
                SetState("idle", null, false, false);
            }
        }

        public void SetMicButtonMode(MicButtonMode mode)
        {
            if (microphoneWindow != null)
            {
                microphoneWindow.SetMode(mode);
            }
        }

        public void ShowAssistantBubble(string text, int durationMs)
        {
            if (!petClosing && !String.IsNullOrWhiteSpace(text))
            {
                string excerpt = text.Replace("\r", " ").Replace("\n", " ");
                if (excerpt.Length > 170)
                {
                    excerpt = excerpt.Substring(0, 170) + "...";
                }
                ShowBubble(excerpt, durationMs);
            }
        }

        public void ApplyAssistantUiState(AssistantUiState state, string message)
        {
            if (petClosing || !IsLoaded)
            {
                return;
            }
            bool hold = state == AssistantUiState.Listening ||
                state == AssistantUiState.Recognizing ||
                state == AssistantUiState.Thinking ||
                state == AssistantUiState.Speaking;
            assistantInteractionActive = hold;
            string petState = "idle";
            if (state == AssistantUiState.Thinking)
            {
                petState = "think";
            }
            else if (state == AssistantUiState.Speaking)
            {
                petState = "explain";
            }
            SetState(petState, null, false, false);
            if (hold)
            {
                autoReturnTimer.Stop();
            }
            string bubble = message;
            if (state == AssistantUiState.Ready)
            {
                bubble = null;
            }
            if (!String.IsNullOrEmpty(bubble))
            {
                bubble = bubble.Replace("\r", " ").Replace("\n", " ");
                if (bubble.Length > 90)
                {
                    bubble = bubble.Substring(0, 90) + "...";
                }
                ShowBubble(bubble, hold ? 12000 : 2200);
            }
        }

        private void UpdateMicrophoneButtonVisibility()
        {
            if (!assistantSettings.ShowMicButton || !IsVisible)
            {
                if (microphoneWindow != null)
                {
                    microphoneWindow.Hide();
                }
                return;
            }
            if (microphoneWindow == null)
            {
                microphoneWindow = new MicrophoneButtonWindow(this);
                microphoneWindow.ToggleRequested += delegate
                {
                    if (assistantWindow != null && assistantWindow.IsVisible)
                    {
                        assistantWindow.ToggleRecordingFromShortcut();
                    }
                    else
                    {
                        OpenAssistant(true);
                    }
                };
            }
            microphoneWindow.SetTopmost(Topmost);
            if (!microphoneWindow.IsVisible)
            {
                microphoneWindow.Show();
            }
            microphoneWindow.PositionNearPet();
        }

        private ContextMenu BuildContextMenu(
            out MenuItem topmostItem,
            out MenuItem autoWalkItem)
        {
            ContextMenu menu = new ContextMenu();
            menu.FontFamily = new FontFamily("Microsoft YaHei UI");
            menu.FontSize = 13;
            menu.Items.Add(CreateStateItem("待机", "idle"));
            menu.Items.Add(CreateStateItem("眨眼", "blink"));
            menu.Items.Add(CreateStateItem("行走", "walk"));
            menu.Items.Add(CreateStateItem("跳跃", "jump"));
            menu.Items.Add(CreateStateItem("睡觉", "sleep"));
            menu.Items.Add(CreateStateItem("思考", "think"));
            menu.Items.Add(CreateStateItem("讲解物理", "explain"));
            menu.Items.Add(CreateStateItem("点击回应", "click"));
            menu.Items.Add(CreateStateItem("摸头回应", "pet"));
            menu.Items.Add(new Separator());
            MenuItem voiceChat = new MenuItem();
            voiceChat.Header = "AI 语音对话…";
            voiceChat.Click += delegate { OpenAssistant(false); };
            menu.Items.Add(voiceChat);
            menu.Items.Add(new Separator());

            MenuItem sizeRoot = new MenuItem();
            sizeRoot.Header = "显示尺寸";
            string[] profiles = new string[] { "48x72", "64x96", "96x144" };
            foreach (string profile in profiles)
            {
                MenuItem item = new MenuItem();
                item.Header = profile + " 像素";
                item.IsCheckable = true;
                item.Tag = profile;
                item.Click += delegate(object sender, RoutedEventArgs e)
                {
                    MenuItem clicked = (MenuItem)sender;
                    SetDisplayProfile((string)clicked.Tag);
                };
                sizeMenuItems[profile] = item;
                sizeRoot.Items.Add(item);
            }
            menu.Items.Add(sizeRoot);

            MenuItem topmostLocal = new MenuItem();
            topmostLocal.Header = "始终置顶";
            topmostLocal.IsCheckable = true;
            topmostLocal.Click += delegate
            {
                SetTopmost(topmostLocal.IsChecked);
            };
            menu.Items.Add(topmostLocal);
            topmostItem = topmostLocal;

            MenuItem autoWalkLocal = new MenuItem();
            autoWalkLocal.Header = "自动漫步";
            autoWalkLocal.IsCheckable = true;
            autoWalkLocal.Click += delegate
            {
                SetAutoWalk(autoWalkLocal.IsChecked);
            };
            menu.Items.Add(autoWalkLocal);
            autoWalkItem = autoWalkLocal;

            menu.Items.Add(new Separator());
            MenuItem hide = new MenuItem();
            hide.Header = "隐藏到系统托盘";
            hide.Click += delegate { HidePet(); };
            menu.Items.Add(hide);

            MenuItem exit = new MenuItem();
            exit.Header = "退出";
            exit.Click += delegate { Close(); };
            menu.Items.Add(exit);
            return menu;
        }

        private MenuItem CreateStateItem(string label, string state)
        {
            MenuItem item = new MenuItem();
            item.Header = label;
            item.Click += delegate { SetState(state, null, true, true); };
            return item;
        }

        private void CreateTrayIcon()
        {
            trayIcon = new System.Windows.Forms.NotifyIcon();
            trayIcon.Text = "费曼桌面宠物";
            try
            {
                string iconPath = Path.Combine(projectRoot, "assets", "feynman", "feynman.ico");
                trayIcon.Icon = new System.Drawing.Icon(iconPath);
            }
            catch
            {
                trayIcon.Icon = System.Drawing.SystemIcons.Application;
            }
            trayIcon.DoubleClick += delegate { ShowPet(); };

            trayMenu = new System.Windows.Forms.ContextMenuStrip();
            trayMenu.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            trayShowItem = new System.Windows.Forms.ToolStripMenuItem("显示费曼");
            trayShowItem.Click += delegate { ShowPet(); };
            trayHideItem = new System.Windows.Forms.ToolStripMenuItem("隐藏");
            trayHideItem.Click += delegate { HidePet(); };
            trayMenu.Items.Add(trayShowItem);
            trayMenu.Items.Add(trayHideItem);
            System.Windows.Forms.ToolStripMenuItem voiceChat =
                new System.Windows.Forms.ToolStripMenuItem("AI 语音对话");
            voiceChat.Click += delegate
            {
                ShowPet();
                OpenAssistant(false);
            };
            trayMenu.Items.Add(voiceChat);
            trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            System.Windows.Forms.ToolStripMenuItem sizeRoot =
                new System.Windows.Forms.ToolStripMenuItem("显示尺寸");
            string[] profiles = new string[] { "48x72", "64x96", "96x144" };
            foreach (string profile in profiles)
            {
                System.Windows.Forms.ToolStripMenuItem item =
                    new System.Windows.Forms.ToolStripMenuItem(profile + " 像素");
                item.Tag = profile;
                item.Click += delegate(object sender, EventArgs e)
                {
                    System.Windows.Forms.ToolStripMenuItem clicked =
                        (System.Windows.Forms.ToolStripMenuItem)sender;
                    SetDisplayProfile((string)clicked.Tag);
                };
                traySizeItems[profile] = item;
                sizeRoot.DropDownItems.Add(item);
            }
            trayMenu.Items.Add(sizeRoot);

            trayTopmostItem = new System.Windows.Forms.ToolStripMenuItem("始终置顶");
            trayTopmostItem.Click += delegate
            {
                SetTopmost(!settings.Topmost);
            };
            trayMenu.Items.Add(trayTopmostItem);
            trayAutoWalkItem = new System.Windows.Forms.ToolStripMenuItem("自动漫步");
            trayAutoWalkItem.Click += delegate
            {
                SetAutoWalk(!autoWalkEnabled);
            };
            trayMenu.Items.Add(trayAutoWalkItem);
            trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            System.Windows.Forms.ToolStripMenuItem exit =
                new System.Windows.Forms.ToolStripMenuItem("退出");
            exit.Click += delegate { Close(); };
            trayMenu.Items.Add(exit);

            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = true;
            UpdateMenuChecks();
        }

        private void PetWindowSourceInitialized(object sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            hwndSource = HwndSource.FromHwnd(handle);
            if (hwndSource != null)
            {
                hwndSource.AddHook(WindowProcedure);
            }

            int exStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExStyle);
            exStyle |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
            NativeMethods.SetWindowLong(handle, NativeMethods.GwlExStyle, exStyle);

            dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            if (dpiScale <= 0)
            {
                dpiScale = 1.0;
            }
            ApplyWindowPixelSize();
            RestoreWindowPosition();
            ApplyTopmostToNativeWindow();
            if (OpenAssistantOnStartup)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(delegate { OpenAssistant(false); }));
            }

            RuntimeLog.Write(
                "Window created. handle=" + handle.ToString() +
                " profile=" + currentProfile.Name +
                " windowDip=" + Width.ToString("0.##") + "x" + Height.ToString("0.##") +
                " dpiScale=" + dpiScale.ToString("0.##") +
                " physicalTarget=" + currentProfile.Width + "x" + currentProfile.Height);
        }

        private void PetWindowLoaded(object sender, RoutedEventArgs e)
        {
            CreateTrayIcon();
            UpdateMicrophoneButtonVisibility();
            behaviorTimer.Interval = TimeSpan.FromSeconds(6.5);
            behaviorTimer.Start();
            if (String.Equals(currentStateName, "idle", StringComparison.OrdinalIgnoreCase))
            {
                ShowBubble("嗨，我是费曼。保持好奇。", 3900);
            }
            RuntimeLog.Write(
                "Window shown. image=" + characterImage.ActualWidth.ToString("0.##") + "x" +
                characterImage.ActualHeight.ToString("0.##") +
                " state=" + currentStateName);
        }

        private void PetWindowClosed(object sender, EventArgs e)
        {
            petClosing = true;
            SaveWindowState();
            frameTimer.Stop();
            autoReturnTimer.Stop();
            behaviorTimer.Stop();
            bubbleTimer.Stop();
            demoTimer.Stop();
            exitTimer.Stop();
            HideBubble();
            if (hwndSource != null)
            {
                hwndSource.RemoveHook(WindowProcedure);
            }
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayIcon = null;
            }
            if (assistantWindow != null)
            {
                assistantWindow.Close();
                assistantWindow = null;
            }
            if (microphoneWindow != null)
            {
                microphoneWindow.Close();
                microphoneWindow = null;
            }
            bubbleWindow.Close();
            RuntimeLog.Write("Window closed.");
        }

        private void PetWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                if (currentState != null)
                {
                    frameTimer.Interval = TimeSpan.FromMilliseconds(currentState.FrameDelayMs);
                    frameTimer.Start();
                }
                behaviorTimer.Start();
                UpdateMicrophoneButtonVisibility();
                UpdateTrayVisibilityItems();
            }
            else
            {
                frameTimer.Stop();
                autoReturnTimer.Stop();
                behaviorTimer.Stop();
                bubbleTimer.Stop();
                HideBubble();
                if (microphoneWindow != null)
                {
                    microphoneWindow.Hide();
                }
                UpdateTrayVisibilityItems();
            }
        }

        private void ApplyWindowPixelSize()
        {
            double widthDip = currentProfile.Width / dpiScale;
            double heightDip = currentProfile.Height / dpiScale;
            MinWidth = 0;
            MinHeight = 0;
            Width = widthDip;
            Height = heightDip;
            characterImage.Width = Double.NaN;
            characterImage.Height = Double.NaN;
        }

        private void RestoreWindowPosition()
        {
            Rect work = SystemParameters.WorkArea;
            double left;
            double top;
            if (settings.Left >= 0 && settings.Top >= 0)
            {
                left = settings.Left;
                top = settings.Top;
            }
            else
            {
                left = work.Right - Width - 20;
                top = work.Bottom - Height - 18;
            }
            Left = Math.Max(work.Left, Math.Min(work.Right - Width, left));
            Top = Math.Max(work.Top, Math.Min(work.Bottom - Height, top));
            SnapWindowToPixelGrid();
        }

        private void SnapWindowToPixelGrid()
        {
            if (dpiScale <= 0)
            {
                return;
            }
            Left = Math.Round((double)(Left * dpiScale)) / dpiScale;
            Top = Math.Round((double)(Top * dpiScale)) / dpiScale;
        }

        private void ClampToWorkArea()
        {
            Rect work = SystemParameters.WorkArea;
            Left = Math.Max(work.Left, Math.Min(work.Right - Width, Left));
            Top = Math.Max(work.Top, Math.Min(work.Bottom - Height, Top));
        }

        public void SetDisplayProfile(string profileName)
        {
            SizeProfile profile;
            if (!catalog.Profiles.TryGetValue(profileName, out profile))
            {
                return;
            }
            if (currentProfile != null && profileName == currentProfile.Name)
            {
                return;
            }
            double centerX = Left + Width / 2.0;
            double bottom = Top + Height;
            currentProfile = profile;
            settings.Profile = profile.Name;
            ApplyWindowPixelSize();
            Left = centerX - Width / 2.0;
            Top = bottom - Height;
            ClampToWorkArea();
            SnapWindowToPixelGrid();

            string stateName = String.IsNullOrEmpty(currentStateName) ? "idle" : currentStateName;
            SetState(stateName, null, true, false);
            UpdateMenuChecks();
            if (microphoneWindow != null)
            {
                microphoneWindow.PositionNearPet();
            }
            SaveWindowState();
            RuntimeLog.Write(
                "Profile=" + profile.Name + " window=" + Width.ToString("0.##") +
                "x" + Height.ToString("0.##") + " dip");
        }

        public void SetTopmost(bool enabled)
        {
            Topmost = enabled;
            settings.Topmost = enabled;
            bubbleWindow.Topmost = enabled;
            ApplyTopmostToNativeWindow();
            if (bubbleWindow.IsVisible)
            {
                bubbleWindow.ApplyWindowStyles();
            }
            if (microphoneWindow != null)
            {
                microphoneWindow.SetTopmost(enabled);
            }
            if (assistantWindow != null)
            {
                assistantWindow.SetTopmost(enabled);
            }
            UpdateMenuChecks();
            SaveWindowState();
            RuntimeLog.Write("Topmost=" + enabled);
        }

        private void ApplyTopmostToNativeWindow()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }
            NativeMethods.SetWindowPos(
                handle,
                Topmost ? NativeMethods.HwndTopmost : NativeMethods.HwndNotTopmost,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        }

        public void SetAutoWalk(bool enabled)
        {
            autoWalkEnabled = enabled;
            settings.AutoWalk = enabled;
            UpdateMenuChecks();
            SaveWindowState();
        }

        private void UpdateMenuChecks()
        {
            if (topmostMenuItem != null)
            {
                topmostMenuItem.IsChecked = Topmost;
            }
            if (autoWalkMenuItem != null)
            {
                autoWalkMenuItem.IsChecked = autoWalkEnabled;
            }
            foreach (KeyValuePair<string, MenuItem> pair in sizeMenuItems)
            {
                pair.Value.IsChecked =
                    String.Equals(pair.Key, currentProfile.Name, StringComparison.OrdinalIgnoreCase);
            }

            if (trayTopmostItem != null)
            {
                trayTopmostItem.Checked = Topmost;
            }
            if (trayAutoWalkItem != null)
            {
                trayAutoWalkItem.Checked = autoWalkEnabled;
            }
            foreach (KeyValuePair<string, System.Windows.Forms.ToolStripMenuItem> pair in traySizeItems)
            {
                pair.Value.Checked =
                    String.Equals(pair.Key, currentProfile.Name, StringComparison.OrdinalIgnoreCase);
            }
            UpdateTrayVisibilityItems();
        }

        private void UpdateTrayVisibilityItems()
        {
            if (trayShowItem != null)
            {
                trayShowItem.Enabled = !IsVisible;
            }
            if (trayHideItem != null)
            {
                trayHideItem.Enabled = IsVisible;
            }
        }

        public void SetState(
            string stateName,
            string bubble,
            bool userInitiated,
            bool allowDefaultBubble)
        {
            StateDefinition state;
            if (!currentProfile.States.TryGetValue(stateName, out state))
            {
                throw new ArgumentException("未知状态: " + stateName, "stateName");
            }

            currentState = state;
            currentStateName = state.Name;
            frameIndex = 0;
            currentFrame = state.Frames[0];
            characterImage.Source = currentFrame.Image;
            characterFlip.ScaleX = 1;

            frameTimer.Stop();
            frameTimer.Interval = TimeSpan.FromMilliseconds(state.FrameDelayMs);
            if (IsVisible)
            {
                frameTimer.Start();
            }
            autoReturnTimer.Stop();
            if (state.AutoReturnMs > 0)
            {
                autoReturnTimer.Interval = TimeSpan.FromMilliseconds(state.AutoReturnMs);
                autoReturnTimer.Start();
            }
            if (state.Name == "walk")
            {
                movementDirection = ChooseWalkDirection();
                characterFlip.ScaleX = movementDirection < 0 ? -1 : 1;
            }

            if (!String.IsNullOrEmpty(bubble))
            {
                ShowBubble(bubble, state.Name == "explain" ? 5200 : 3000);
            }
            else if (allowDefaultBubble && !String.IsNullOrEmpty(state.DefaultBubble))
            {
                ShowBubble(state.DefaultBubble, state.Name == "explain" ? 5200 : 3000);
            }

            RuntimeLog.Write(
                "State=" + state.Name +
                (userInitiated ? " (user)" : " (auto)") +
                " profile=" + currentProfile.Name +
                " frames=" + state.Frames.Count);
        }

        private void FrameTimerTick(object sender, EventArgs e)
        {
            if (currentState == null || currentState.Frames.Count == 0)
            {
                return;
            }
            int next = frameIndex + 1;
            if (next >= currentState.Frames.Count)
            {
                if (!currentState.Loop)
                {
                    frameTimer.Stop();
                    SetState("idle", null, false, false);
                    return;
                }
                next = 0;
            }
            frameIndex = next;
            currentFrame = currentState.Frames[frameIndex];
            characterImage.Source = currentFrame.Image;

            if (String.Equals(currentState.Name, "walk", StringComparison.OrdinalIgnoreCase))
            {
                MoveWalkStep();
            }
        }

        private void MoveWalkStep()
        {
            Rect work = SystemParameters.WorkArea;
            double physicalStep = Math.Max(1.0, currentProfile.Width / 48.0);
            double step = physicalStep / dpiScale;
            double next = Left + movementDirection * step;
            if (next < work.Left)
            {
                next = work.Left;
                movementDirection = 1;
                characterFlip.ScaleX = 1;
            }
            else if (next + Width > work.Right)
            {
                next = work.Right - Width;
                movementDirection = -1;
                characterFlip.ScaleX = -1;
            }
            Left = next;
        }

        private int ChooseWalkDirection()
        {
            Rect work = SystemParameters.WorkArea;
            if (Left - work.Left < 30)
            {
                return 1;
            }
            if (work.Right - (Left + Width) < 30)
            {
                return -1;
            }
            return new Random(Guid.NewGuid().GetHashCode()).Next(0, 2) == 0 ? -1 : 1;
        }

        private void AutoReturnTick(object sender, EventArgs e)
        {
            autoReturnTimer.Stop();
            if (assistantInteractionActive)
            {
                return;
            }
            if (currentState != null && currentState.Name != "sleep")
            {
                SaveWindowState();
                SetState("idle", null, false, false);
            }
        }

        private void BehaviorTimerTick(object sender, EventArgs e)
        {
            if (!IsVisible || assistantInteractionActive ||
                currentState == null || currentState.Name != "idle")
            {
                idleTicks = 0;
                return;
            }
            idleTicks++;
            Random random = new Random(Guid.NewGuid().GetHashCode());
            if (idleTicks >= 3 && autoWalkEnabled && random.NextDouble() < 0.5)
            {
                idleTicks = 0;
                SetState("walk", null, false, true);
            }
            else if (random.NextDouble() < 0.6)
            {
                SetState("blink", null, false, false);
            }
        }

        private void ShowBubble(string text, int durationMs)
        {
            if (String.IsNullOrEmpty(text) || !IsVisible)
            {
                return;
            }
            bubbleWindow.Owner = this;
            bubbleWindow.Text = text;
            bubbleWindow.Topmost = Topmost;
            if (!bubbleWindow.IsVisible)
            {
                bubbleWindow.Opacity = 0;
                bubbleWindow.Show();
            }
            bubbleWindow.ShowAbove(this);
            RuntimeLog.Write("Bubble=" + text);
            bubbleTimer.Stop();
            bubbleTimer.Interval = TimeSpan.FromMilliseconds(durationMs);
            bubbleTimer.Start();
        }

        private void HideBubble()
        {
            bubbleTimer.Stop();
            bubbleWindow.Hide();
        }

        private void BubbleTimerTick(object sender, EventArgs e)
        {
            HideBubble();
        }

        private void BeginPointerDrag(IntPtr hwnd)
        {
            isMouseDown = true;
            didDrag = false;
            NativeMethods.POINT cursor;
            NativeMethods.GetCursorPos(out cursor);
            mouseDownScreen = new Point(cursor.X, cursor.Y);
            mouseDownWindow = new Point(Left, Top);
            NativeMethods.SetCapture(hwnd);
        }

        private void ContinuePointerDrag()
        {
            if (!isMouseDown)
            {
                return;
            }
            NativeMethods.POINT cursor;
            NativeMethods.GetCursorPos(out cursor);
            Vector deltaPixels = new Point(cursor.X, cursor.Y) - mouseDownScreen;
            if (!didDrag && deltaPixels.Length / Math.Max(dpiScale, 0.1) > 3)
            {
                didDrag = true;
                if (currentState != null && currentState.Name == "walk")
                {
                    SetState("idle", null, false, false);
                }
            }
            if (didDrag)
            {
                Left = mouseDownWindow.X + deltaPixels.X / dpiScale;
                Top = mouseDownWindow.Y + deltaPixels.Y / dpiScale;
                ClampToWorkArea();
            }
        }

        private void EndPointerDrag(Point clientPoint)
        {
            if (!isMouseDown)
            {
                return;
            }
            isMouseDown = false;
            NativeMethods.ReleaseCapture();

            if (didDrag)
            {
                SnapWindowToPixelGrid();
                SaveWindowState();
                ShowBubble("这个位置不错。", 1600);
                return;
            }

            Point normalized;
            if (!TryGetOpaqueNormalized(clientPoint, out normalized))
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            bool doubleClick =
                (now - lastClickUtc).TotalMilliseconds < 390 &&
                (normalized - lastClickImagePoint).Length < 0.28;
            lastClickUtc = doubleClick ? DateTime.MinValue : now;
            lastClickImagePoint = normalized;

            if (currentState != null && currentState.Name == "sleep")
            {
                SetState("idle", "早上好。刚才想到一个有意思的问题。", true, false);
            }
            else if (doubleClick)
            {
                SetState("explain", null, true, true);
            }
            else if (IsHeadPointNormalized(normalized))
            {
                SetState("pet", null, true, true);
            }
            else
            {
                SetState("click", null, true, true);
            }
        }

        private bool IsHeadPointNormalized(Point normalized)
        {
            double centerX = currentState != null &&
                String.Equals(currentState.Name, "sleep", StringComparison.OrdinalIgnoreCase)
                ? 0.515
                : 0.5;
            double centerY = currentState != null &&
                String.Equals(currentState.Name, "sleep", StringComparison.OrdinalIgnoreCase)
                ? 0.46
                : 0.245;
            double dx = (normalized.X - centerX) / 0.22;
            double dy = (normalized.Y - centerY) / 0.22;
            return dx * dx + dy * dy <= 1.0;
        }

        private IntPtr WindowProcedure(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message == NativeMethods.WmNcHitTest && !isMouseDown)
            {
                long packed = lParam.ToInt64();
                int x = unchecked((short)(packed & 0xFFFF));
                int y = unchecked((short)((packed >> 16) & 0xFFFF));
                if (IsTransparentScreenPoint(new Point(x, y)))
                {
                    handled = true;
                    return new IntPtr(NativeMethods.HtTransparent);
                }
            }
            if (message == NativeMethods.WmLeftButtonDown)
            {
                Point client = ClientPointFromLParam(lParam);
                if (IsOpaqueClientPoint(client))
                {
                    BeginPointerDrag(hwnd);
                    handled = true;
                }
                return IntPtr.Zero;
            }
            if (message == NativeMethods.WmMouseMove && isMouseDown)
            {
                ContinuePointerDrag();
                handled = true;
                return IntPtr.Zero;
            }
            if (message == NativeMethods.WmLeftButtonUp && isMouseDown)
            {
                EndPointerDrag(ClientPointFromLParam(lParam));
                handled = true;
                return IntPtr.Zero;
            }
            if (message == NativeMethods.WmRightButtonUp)
            {
                mainMenu.PlacementTarget = this;
                mainMenu.IsOpen = true;
                handled = true;
                return IntPtr.Zero;
            }
            if (message == NativeMethods.WmCaptureChanged)
            {
                isMouseDown = false;
                didDrag = false;
            }
            return IntPtr.Zero;
        }

        private static Point ClientPointFromLParam(IntPtr lParam)
        {
            long packed = lParam.ToInt64();
            int x = unchecked((short)(packed & 0xFFFF));
            int y = unchecked((short)((packed >> 16) & 0xFFFF));
            return new Point(x, y);
        }

        private bool IsOpaqueClientPoint(Point point)
        {
            Point normalized;
            return TryGetOpaqueNormalized(point, out normalized);
        }

        private bool TryGetOpaqueNormalized(Point point, out Point normalized)
        {
            normalized = new Point(0, 0);
            if (currentFrame == null)
            {
                return false;
            }
            Point[] candidates = dpiScale > 1.01
                ? new Point[]
                {
                    point,
                    new Point(point.X / dpiScale, point.Y / dpiScale),
                    new Point(point.X * dpiScale, point.Y * dpiScale)
                }
                : new Point[] { point };
            foreach (Point candidate in candidates)
            {
                if (candidate.X < 0 || candidate.Y < 0 ||
                    candidate.X >= currentProfile.Width ||
                    candidate.Y >= currentProfile.Height)
                {
                    continue;
                }
                int x = (int)(candidate.X / currentProfile.Width * currentFrame.Width);
                int y = (int)(candidate.Y / currentProfile.Height * currentFrame.Height);
                if (currentFrame.AlphaAt(x, y) >= 12)
                {
                    normalized = new Point(
                        candidate.X / currentProfile.Width,
                        candidate.Y / currentProfile.Height);
                    return true;
                }
            }
            return false;
        }

        private bool IsTransparentScreenPoint(Point screenPoint)
        {
            if (currentFrame == null || characterImage.Source == null)
            {
                return true;
            }
            Point imagePoint = characterImage.PointFromScreen(screenPoint);
            if (imagePoint.X < 0 || imagePoint.Y < 0 ||
                imagePoint.X >= characterImage.ActualWidth ||
                imagePoint.Y >= characterImage.ActualHeight)
            {
                return true;
            }
            int x = (int)(imagePoint.X / characterImage.ActualWidth * currentFrame.Width);
            int y = (int)(imagePoint.Y / characterImage.ActualHeight * currentFrame.Height);
            return currentFrame.AlphaAt(x, y) < 12;
        }

        public void HidePet()
        {
            SaveWindowState();
            if (assistantWindow != null)
            {
                assistantWindow.SuspendForHide();
                assistantWindow.Hide();
            }
            if (microphoneWindow != null)
            {
                microphoneWindow.Hide();
            }
            Hide();
            RuntimeLog.Write("Pet hidden to tray.");
        }

        public void ShowPet()
        {
            Show();
            ApplyWindowPixelSize();
            RestoreWindowPosition();
            ApplyTopmostToNativeWindow();
            UpdateMenuChecks();
            UpdateMicrophoneButtonVisibility();
            ShowBubble("我回来了。", 1800);
            RuntimeLog.Write("Pet restored from tray.");
        }

        public void StartAnimationTest(Action completion)
        {
            demoCompletion = completion;
            demoIndex = 0;
            string[] sequence = new string[]
            {
                "idle", "blink", "walk", "jump", "sleep",
                "think", "explain", "pet", "click"
            };
            SetState(sequence[demoIndex], null, false, false);
            demoTimer.Interval = TimeSpan.FromMilliseconds(1150);
            demoTimer.Tick += delegate
            {
                demoIndex++;
                if (demoIndex >= sequence.Length)
                {
                    demoTimer.Stop();
                    RuntimeLog.Write(
                        "Animation test completed: all 9 states switched successfully.");
                    if (demoCompletion != null)
                    {
                        demoCompletion();
                    }
                    return;
                }
                SetState(sequence[demoIndex], null, true, false);
            };
            demoTimer.Start();
        }

        public void CloseAfterSeconds(int seconds)
        {
            if (seconds <= 0)
            {
                return;
            }
            exitTimer.Interval = TimeSpan.FromSeconds(seconds);
            exitTimer.Tick += delegate
            {
                exitTimer.Stop();
                RuntimeLog.Write("Auto-exit after " + seconds + " seconds.");
                Close();
            };
            exitTimer.Start();
        }

        public void SaveRenderSnapshot(string path)
        {
            UpdateLayout();
            int width = Math.Max(1, currentProfile.Width);
            int height = Math.Max(1, currentProfile.Height);
            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (FileStream stream = File.Create(path))
            {
                encoder.Save(stream);
            }
        }

        public void SaveWindowState()
        {
            if (!persistSettings)
            {
                return;
            }
            settings.Profile = currentProfile.Name;
            settings.Topmost = Topmost;
            settings.AutoWalk = autoWalkEnabled;
            settings.Left = Left;
            settings.Top = Top;
            settings.Save(projectRoot);
        }

        private void DemoTimerTick(object sender, EventArgs e)
        {
        }

        private void ExitTimerTick(object sender, EventArgs e)
        {
        }

        public static string ValidateWindowConfiguration(
            string projectRoot,
            AssetCatalog catalog,
            AppSettings settings)
        {
            PetWindow window = new PetWindow(
                projectRoot, catalog, settings, "64x96", false);
            StringBuilder report = new StringBuilder();
            bool passed = true;
            passed &= Check(report, "borderless", window.WindowStyle == WindowStyle.None);
            passed &= Check(report, "per-pixel-transparency", window.AllowsTransparency);
            passed &= Check(
                report,
                "transparent-background",
                ReferenceEquals(window.Background, Brushes.Transparent));
            passed &= Check(report, "taskbar-hidden", !window.ShowInTaskbar);
            passed &= Check(report, "no-resize", window.ResizeMode == ResizeMode.NoResize);
            passed &= Check(report, "startup-does-not-activate", !window.ShowActivated);
            passed &= Check(report, "default-64x96",
                String.Equals(window.CurrentProfileName, "64x96", StringComparison.Ordinal));
            passed &= Check(report, "all-nine-states",
                window.currentProfile.States.Count == 9);
            window.Close();
            report.AppendLine(passed ? "WINDOW_CONFIG=PASS" : "WINDOW_CONFIG=FAIL");
            return report.ToString();
        }

        private static bool Check(StringBuilder report, string name, bool passed)
        {
            report.AppendLine(name + "=" + (passed ? "PASS" : "FAIL"));
            return passed;
        }
    }

    internal sealed class SpeechBubbleWindow : Window
    {
        private readonly TextBlock textBlock;
        private readonly System.Windows.Shapes.Polygon tail;
        private Window anchor;

        public SpeechBubbleWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SizeToContent = SizeToContent.WidthAndHeight;
            MaxWidth = 260;
            MaxHeight = 170;
            Title = "Feynman speech";

            StackPanel stack = new StackPanel();
            stack.Background = Brushes.Transparent;
            stack.IsHitTestVisible = false;

            Border border = new Border();
            border.MinWidth = 124;
            border.MaxWidth = 238;
            border.Padding = new Thickness(9, 7, 9, 8);
            border.CornerRadius = new CornerRadius(9);
            border.Background = new SolidColorBrush(Color.FromRgb(255, 250, 237));
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(49, 44, 42));
            border.BorderThickness = new Thickness(1.2);
            DropShadowEffect shadow = new DropShadowEffect();
            shadow.BlurRadius = 7;
            shadow.ShadowDepth = 2;
            shadow.Direction = 270;
            shadow.Opacity = 0.22;
            shadow.Color = Colors.Black;
            border.Effect = shadow;

            TextBlock label = new TextBlock();
            label.FontFamily = new FontFamily("Microsoft YaHei UI");
            label.FontSize = 12;
            label.FontWeight = FontWeights.Medium;
            label.Foreground = new SolidColorBrush(Color.FromRgb(48, 44, 42));
            label.TextWrapping = TextWrapping.Wrap;
            label.TextAlignment = TextAlignment.Center;
            label.LineHeight = 17;
            label.MaxWidth = 220;
            border.Child = label;
            textBlock = label;
            stack.Children.Add(border);

            tail = new System.Windows.Shapes.Polygon();
            tail.Points = new PointCollection
            {
                new Point(0, 0),
                new Point(11, 0),
                new Point(3, 8)
            };
            tail.Fill = new SolidColorBrush(Color.FromRgb(255, 250, 237));
            tail.Stroke = new SolidColorBrush(Color.FromRgb(49, 44, 42));
            tail.StrokeThickness = 1.2;
            tail.Margin = new Thickness(0, -1, 0, 0);
            tail.HorizontalAlignment = HorizontalAlignment.Left;
            tail.IsHitTestVisible = false;
            stack.Children.Add(tail);

            Content = stack;
            SourceInitialized += delegate { ApplyWindowStyles(); };
            Loaded += delegate { PositionAfterLayout(); };
            SizeChanged += delegate
            {
                if (IsVisible)
                {
                    PositionAfterLayout();
                }
            };
        }

        public string Text
        {
            set
            {
                textBlock.Text = value;
                textBlock.UpdateLayout();
            }
        }

        public void PositionAbove(Window owner)
        {
            anchor = owner;
            UpdateLayout();
            double width = Math.Max(ActualWidth, 1);
            double height = Math.Max(ActualHeight, 1);
            Rect work = SystemParameters.WorkArea;
            double left = owner.Left + owner.Width / 2.0 - width / 2.0;
            double top = owner.Top - height - 5;
            if (top < work.Top)
            {
                top = owner.Top + owner.Height + 5;
            }
            left = Math.Max(work.Left, Math.Min(work.Right - width, left));
            top = Math.Max(work.Top, Math.Min(work.Bottom - height, top));
            Left = left;
            Top = top;
            double targetCenter = owner.Left + owner.Width / 2.0 - left;
            double tailLeft = Math.Max(10, Math.Min(width - 22, targetCenter - 5.5));
            tail.Margin = new Thickness(tailLeft, -1, 0, 0);
        }

        public void ShowAbove(Window owner)
        {
            anchor = owner;
            UpdateLayout();
            PositionAfterLayout();
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(PositionAfterLayout));
        }

        private void PositionAfterLayout()
        {
            if (anchor == null)
            {
                return;
            }
            UpdateLayout();
            PositionAbove(anchor);
            ApplyWindowStyles();
            if (IsVisible)
            {
                Opacity = 1;
            }
        }

        public void ApplyWindowStyles()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }
            int exStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExStyle);
            exStyle |= NativeMethods.WsExToolWindow |
                NativeMethods.WsExNoActivate |
                NativeMethods.WsExTransparent;
            NativeMethods.SetWindowLong(handle, NativeMethods.GwlExStyle, exStyle);
            NativeMethods.SetWindowPos(
                handle,
                Topmost ? NativeMethods.HwndTopmost : NativeMethods.HwndNotTopmost,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoMove |
                NativeMethods.SwpNoSize |
                NativeMethods.SwpNoActivate |
                NativeMethods.SwpShowWindow);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                try
                {
                    NativeMethods.SetProcessDPIAware();
                }
                catch
                {
                }

                string root = FindProjectRoot();
                RuntimeLog.Initialize(root);
                AppSettings settings = AppSettings.Load(root);
                AssetCatalog catalog = AssetCatalog.Load(root);
                RuntimeLog.Write(
                    "Assets loaded. frames=" + catalog.CountFrames() +
                    " profiles=" + catalog.Profiles.Count +
                    " root=" + catalog.AssetRoot);

                bool selfTest = HasFlag(args, "--self-test");
                bool animationTest = HasFlag(args, "--animation-test");
                bool voiceDiagnostics = HasFlag(args, "--voice-diagnostics");
                bool espeakPlaybackTest = HasFlag(args, "--espeak-playback-test");
                bool ttsTextTest = HasFlag(args, "--tts-text-test");
                bool openAssistant = HasFlag(args, "--open-assistant");
                bool persist = !HasFlag(args, "--no-save");
                int autoExit = ReadIntOption(args, "--auto-exit", 0);
                string state = ReadOption(args, "--state");
                string profile = ReadOption(args, "--size");
                if (profile == "48")
                {
                    profile = "48x72";
                }
                else if (profile == "64")
                {
                    profile = "64x96";
                }
                else if (profile == "96")
                {
                    profile = "96x144";
                }
                if (!String.IsNullOrEmpty(profile) && !catalog.Profiles.ContainsKey(profile))
                {
                    profile = null;
                }

                if (selfTest)
                {
                    string report = AuditAssets(catalog);
                    report += PetWindow.ValidateWindowConfiguration(root, catalog, settings);
                    report += AuditAssistantConfiguration();
                    string reportPath = Path.Combine(root, "logs", "self-test.txt");
                    Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
                    File.WriteAllText(reportPath, report, Encoding.UTF8);
                    RuntimeLog.Write("Self-test finished.");
                    return report.Contains("=FAIL") ? 2 : 0;
                }

                if (voiceDiagnostics)
                {
                    string report = VoiceDiagnostics.Run(root);
                    string reportPath = Path.Combine(root, "logs", "voice-diagnostics.txt");
                    Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
                    File.WriteAllText(reportPath, report, Encoding.UTF8);
                    RuntimeLog.Write("Voice diagnostics finished.");
                    return 0;
                }

                if (espeakPlaybackTest)
                {
                    return RunESpeakPlaybackTest(root);
                }
                if (ttsTextTest)
                {
                    return RunTtsTextTests(root);
                }

                bool createdNew;
                using (Mutex instanceMutex = new Mutex(
                    true,
                    "Local\\FeynmanPet.SingleInstance",
                    out createdNew))
                {
                    if (!createdNew)
                    {
                        MessageBox.Show(
                            "费曼桌宠已经在运行。请使用现有窗口或系统托盘图标。",
                            "费曼桌宠",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return 0;
                    }
                    Application app = new Application();
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    PetWindow window = new PetWindow(
                        root,
                        catalog,
                        settings,
                        profile,
                        persist);
                    window.OpenAssistantOnStartup = openAssistant;
                    if (String.IsNullOrEmpty(state))
                    {
                        state = "idle";
                    }
                    if (!window.HasState(state))
                    {
                        state = "idle";
                    }
                    window.SetState(state, null, false, false);
                    if (animationTest)
                    {
                        window.StartAnimationTest(delegate { window.Close(); });
                    }
                    if (autoExit > 0)
                    {
                        window.CloseAfterSeconds(autoExit);
                    }
                    app.Run(window);
                }
                return 0;
            }
            catch (Exception exception)
            {
                RuntimeLog.Write("FATAL: " + exception);
                try
                {
                    string logs = Path.Combine(FindProjectRoot(), "logs");
                    Directory.CreateDirectory(logs);
                    File.WriteAllText(
                        Path.Combine(logs, "fatal.txt"),
                        exception.ToString(),
                        Encoding.UTF8);
                }
                catch
                {
                }
                return 1;
            }
        }

        private static string FindProjectRoot()
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string candidate = directory;
            for (int depth = 0; depth < 5 && !String.IsNullOrEmpty(candidate); depth++)
            {
                if (Directory.Exists(Path.Combine(candidate, "assets", "feynman")))
                {
                    return candidate;
                }
                candidate = Path.GetDirectoryName(candidate);
            }
            return directory;
        }

        private static string AuditAssets(AssetCatalog catalog)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("FEYNMAN PET SELF TEST");
            report.AppendLine("AssetRoot=" + catalog.AssetRoot);
            report.AppendLine("Profiles=" + catalog.Profiles.Count);
            bool passed = catalog.Profiles.Count == 3;
            int totalFrames = 0;
            Dictionary<string, int> expectedCounts = new Dictionary<string, int>();
            expectedCounts["idle"] = 6;
            expectedCounts["blink"] = 6;
            expectedCounts["walk"] = 8;
            expectedCounts["jump"] = 6;
            expectedCounts["sleep"] = 4;
            expectedCounts["think"] = 6;
            expectedCounts["explain"] = 6;
            expectedCounts["pet"] = 6;
            expectedCounts["click"] = 4;

            foreach (KeyValuePair<string, SizeProfile> profilePair in catalog.Profiles)
            {
                SizeProfile profile = profilePair.Value;
                report.AppendLine(
                    "Profile " + profile.Name + " " +
                    profile.Width + "x" + profile.Height +
                    " states=" + profile.States.Count);
                if (profile.States.Count != 9)
                {
                    passed = false;
                }
                foreach (KeyValuePair<string, StateDefinition> statePair in profile.States)
                {
                    StateDefinition state = statePair.Value;
                    int expected;
                    expectedCounts.TryGetValue(statePair.Key, out expected);
                    if (state.Frames.Count != expected)
                    {
                        passed = false;
                        report.AppendLine("  FRAME_COUNT_FAIL " + state.Name);
                    }
                    double widthCoverage =
                        state.UnionWidth / (double)profile.Width;
                    double heightCoverage =
                        state.UnionHeight / (double)profile.Height;
                    if (widthCoverage < 0.48 || heightCoverage < 0.58)
                    {
                        passed = false;
                        report.AppendLine(
                            "  CONTENT_COVERAGE_FAIL " + state.Name +
                            " width=" + widthCoverage.ToString("P1") +
                            " height=" + heightCoverage.ToString("P1"));
                    }
                    foreach (SpriteFrame frame in state.Frames)
                    {
                        totalFrames++;
                        if (frame.Width != profile.Width || frame.Height != profile.Height)
                        {
                            passed = false;
                        }
                        bool cornersTransparent =
                            frame.AlphaAt(0, 0) == 0 &&
                            frame.AlphaAt(frame.Width - 1, 0) == 0 &&
                            frame.AlphaAt(0, frame.Height - 1) == 0 &&
                            frame.AlphaAt(frame.Width - 1, frame.Height - 1) == 0;
                        if (!cornersTransparent)
                        {
                            passed = false;
                            report.AppendLine(
                                "  ALPHA_CORNER_FAIL " + profile.Name + "/" + state.Name);
                        }
                    }
                }
            }
            report.AppendLine("FrameCount=" + totalFrames);
            report.AppendLine("ExpectedFrameCount=156");
            if (totalFrames != 156)
            {
                passed = false;
            }
            report.AppendLine("AlphaAndSizeAudit=" + (passed ? "PASS" : "FAIL"));
            report.AppendLine(passed ? "ASSET_TEST=PASS" : "ASSET_TEST=FAIL");
            return report.ToString();
        }

        private static int RunESpeakPlaybackTest(string root)
        {
            StringBuilder report = new StringBuilder();
            ESpeakTtsService service = new ESpeakTtsService();
            try
            {
                IList<VoiceDescriptor> voices = service.GetVoices();
                VoiceDescriptor chinese = null;
                VoiceDescriptor english = null;
                foreach (VoiceDescriptor voice in voices)
                {
                    report.AppendLine(
                        "Voice=" + voice.Name + " | " + voice.CultureName +
                        " | " + voice.Gender);
                    if (chinese == null &&
                        voice.CultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                    {
                        chinese = voice;
                    }
                    if (english == null &&
                        voice.CultureName.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                    {
                        english = voice;
                    }
                }
                bool passed = chinese != null && english != null;
                if (chinese != null)
                {
                    TtsPreparedText chineseText = TtsTextPreprocessor.Prepare(
                        "## 光\n- 光具有**波粒二象性**，频率约为 50%（来源）。",
                        "zh");
                    service.SpeakAsync(
                        chineseText.Text,
                        chineseText.Language,
                        0,
                        chinese.Name,
                        CancellationToken.None).GetAwaiter().GetResult();
                    report.AppendLine("ChinesePlayback=PASS");
                }
                else
                {
                    report.AppendLine("ChinesePlayback=UNAVAILABLE_NO_VOICE");
                }
                if (english != null)
                {
                    TtsPreparedText englishText = TtsTextPreprocessor.Prepare(
                        "## Light\n- **Interference** is about 50% of the story " +
                        "([source](https://example.com)). Formula: E=mc^2.",
                        "en");
                    service.SpeakAsync(
                        englishText.Text,
                        englishText.Language,
                        0,
                        english.Name,
                        CancellationToken.None).GetAwaiter().GetResult();
                    report.AppendLine("EnglishPlayback=PASS");
                }
                else
                {
                    report.AppendLine("EnglishPlayback=UNAVAILABLE_NO_VOICE");
                }
                bool stopped = false;
                if (chinese != null)
                {
                    CancellationTokenSource cancellation = new CancellationTokenSource();
                    Task active = service.SpeakAsync(
                        "这是一个用于验证停止功能的较长中文测试句子。请持续朗读。",
                        "zh",
                        -3,
                        chinese.Name,
                        cancellation.Token);
                    System.Threading.Thread.Sleep(450);
                    cancellation.Cancel();
                    try
                    {
                        stopped = active.Wait(5000);
                    }
                    catch (AggregateException)
                    {
                        stopped = active.IsCanceled;
                    }
                }
                report.AppendLine(stopped ? "StopTest=PASS" : "StopTest=FAIL");
                passed &= stopped;
                report.AppendLine(passed ? "ESPEAK_PLAYBACK=PASS" : "ESPEAK_PLAYBACK=FAIL");
                RuntimeLog.Write("eSpeak playback test finished. passed=" + passed);
                return passed ? 0 : 2;
            }
            catch (Exception exception)
            {
                report.AppendLine("ESPEAK_PLAYBACK=FAIL");
                report.AppendLine(exception.ToString());
                RuntimeLog.Write("eSpeak playback test failed: " + exception);
                return 2;
            }
            finally
            {
                service.Dispose();
                string path = Path.Combine(root, "logs", "espeak-playback-test.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, report.ToString(), Encoding.UTF8);
            }
        }

        private static int RunTtsTextTests(string root)
        {
            StringBuilder report = new StringBuilder();
            bool passed = true;

            string englishSource =
                "## Why is the sky blue?\n" +
                "- **Rayleigh scattering** explains this ([source](https://example.com)).\n" +
                "- About 25% of light is scattered. Formula: E=mc^2.";
            TtsPreparedText english = TtsTextPreprocessor.Prepare(englishSource, "en");
            passed &= Check(report, "EnglishLanguage", english.Language == "en");
            passed &= Check(report, "EnglishNoMarkdown",
                !english.Text.Contains("#") &&
                !english.Text.Contains("**") &&
                !english.Text.Contains("](") &&
                !english.Text.Contains("http"));
            passed &= Check(report, "EnglishPercent",
                english.Text.Contains("25 percent"));
            passed &= Check(report, "EnglishFormula",
                english.Text.Contains("squared"));
            report.AppendLine("EnglishCleaned=" + english.Text);

            string chineseSource =
                "## 光\n- 光具有**波粒二象性**，频率约为 50%（参见[资料](https://example.com)）。";
            TtsPreparedText chinese = TtsTextPreprocessor.Prepare(chineseSource, "zh");
            passed &= Check(report, "ChineseLanguage", chinese.Language == "zh");
            passed &= Check(report, "ChineseNoMarkdown",
                !chinese.Text.Contains("#") &&
                !chinese.Text.Contains("**") &&
                !chinese.Text.Contains("](") &&
                !chinese.Text.Contains("http"));
            passed &= Check(report, "ChinesePercent",
                chinese.Text.Contains("百分之50"));
            passed &= Check(report, "ChinesePunctuation",
                chinese.Text.Contains("，") && chinese.Text.Contains("。"));
            report.AppendLine("ChineseCleaned=" + chinese.Text);

            TtsPreparedText symbols = TtsTextPreprocessor.Prepare("*** --- ||| @@@ ###", "en");
            passed &= Check(report, "PureSymbolsRejected", symbols.WasEmpty);

            TtsPreparedText code = TtsTextPreprocessor.Prepare(
                "Before.\n```csharp\nConsole.WriteLine(\"x\");\n```\nAfter.",
                "en");
            passed &= Check(report, "CodeRemoved",
                code.RemovedCode &&
                !code.Text.Contains("Console") &&
                code.Text.Contains("Before") &&
                code.Text.Contains("After"));

            AssistantSettings languageSettings = new AssistantSettings();
            string englishPrompt = DeepSeekChatService.BuildSystemPrompt(languageSettings, "en");
            string chinesePrompt = DeepSeekChatService.BuildSystemPrompt(languageSettings, "zh");
            passed &= Check(report, "EnglishSystemDirective",
                englishPrompt.IndexOf(
                    "natural, fluent English",
                    StringComparison.OrdinalIgnoreCase) >= 0 &&
                englishPrompt.IndexOf(
                    "HIGHEST PRIORITY",
                    StringComparison.OrdinalIgnoreCase) >= 0);
            passed &= Check(report, "ChineseSystemDirective",
                chinesePrompt.IndexOf(
                    "Simplified Chinese",
                    StringComparison.OrdinalIgnoreCase) >= 0);
            passed &= Check(report, "AnswerLanguageOverride",
                LanguageTools.Resolve("en", "解释一下什么是光的干涉") == "en" &&
                LanguageTools.Resolve("zh", "What is quantum entanglement?") == "zh");
            passed &= Check(report, "AutoLanguage",
                LanguageTools.Resolve("auto", "解释一下什么是光的干涉") == "zh" &&
                LanguageTools.Resolve("auto", "What is quantum entanglement?") == "en");

            report.AppendLine(passed ? "TTS_TEXT_TESTS=PASS" : "TTS_TEXT_TESTS=FAIL");
            string path = Path.Combine(root, "logs", "tts-text-test.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, report.ToString(), Encoding.UTF8);
            return passed ? 0 : 2;
        }

        private static string AuditAssistantConfiguration()
        {
            StringBuilder report = new StringBuilder();
            bool passed = true;
            AssistantSettings assistant = new AssistantSettings();
            WindowsSpeechRecognitionService recognition =
                new WindowsSpeechRecognitionService();
            try
            {
                int recognizers = recognition.GetInstalledRecognizers().Count;
                report.AppendLine("AssistantRecognizers=" + recognizers);
                report.AppendLine("AssistantRecognitionService=" +
                    (recognizers > 0 ? "PASS" : "UNAVAILABLE"));
            }
            finally
            {
                recognition.Dispose();
            }

            WindowsTtsService tts = new WindowsTtsService();
            try
            {
                bool chinese = tts.SupportsLanguage("zh");
                bool english = tts.SupportsLanguage("en");
                report.AppendLine("AssistantChineseTts=" + (chinese ? "PASS" : "UNAVAILABLE"));
                report.AppendLine("AssistantEnglishTts=" + (english ? "PASS" : "UNAVAILABLE"));
                passed &= chinese;
            }
            finally
            {
                tts.Dispose();
            }

            DeepSeekChatService chat = new DeepSeekChatService();
            try
            {
                report.AppendLine("AssistantDeepSeekKey=" +
                    (chat.IsConfigured ? "CONFIGURED" : "MISSING"));
                if (!chat.IsConfigured)
                {
                    try
                    {
                        chat.AskAsync(
                            "configuration test",
                            assistant,
                            LanguageTools.Resolve(
                                assistant.OutputLanguage,
                                "configuration test"),
                            CancellationToken.None).GetAwaiter().GetResult();
                        passed = false;
                        report.AppendLine("AssistantMissingKeyHandling=FAIL");
                    }
                    catch (ChatServiceException exception)
                    {
                        bool clear = exception.Code == "missing_key" &&
                            exception.Message.IndexOf(
                                "DEEPSEEK_API_KEY",
                                StringComparison.Ordinal) >= 0;
                        report.AppendLine(
                            "AssistantMissingKeyHandling=" + (clear ? "PASS" : "FAIL"));
                        passed &= clear;
                    }
                }
                else
                {
                    report.AppendLine("AssistantMissingKeyHandling=NOT_APPLICABLE");
                }
            }
            finally
            {
                chat.Dispose();
            }
            report.AppendLine(passed ? "ASSISTANT_CONFIG=PASS" : "ASSISTANT_CONFIG=FAIL");
            return report.ToString();
        }

        private static string ReadOption(string[] args, string name)
        {
            string prefix = name + "=";
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return args[index].Substring(prefix.Length);
                }
                if (String.Equals(args[index], name, StringComparison.OrdinalIgnoreCase) &&
                    index + 1 < args.Length)
                {
                    return args[index + 1];
                }
            }
            return null;
        }

        private static int ReadIntOption(string[] args, string name, int fallback)
        {
            int parsed;
            return Int32.TryParse(ReadOption(args, name), out parsed) ? parsed : fallback;
        }

        private static bool Check(StringBuilder report, string name, bool passed)
        {
            report.AppendLine(name + "=" + (passed ? "PASS" : "FAIL"));
            return passed;
        }

        private static bool HasFlag(string[] args, string flag)
        {
            foreach (string argument in args)
            {
                if (String.Equals(argument, flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
