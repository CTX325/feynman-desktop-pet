using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
        public const int HtTransparent = -1;
        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoMove = 0x0002;
        public const uint SwpNoActivate = 0x0010;
        public const uint SwpShowWindow = 0x0040;
        public static readonly IntPtr HwndTopmost = new IntPtr(-1);

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
    }

    internal sealed class StateDefinition
    {
        public string Name;
        public int FrameDelayMs;
        public bool Loop;
        public int AutoReturnMs;
        public string DefaultBubble;
        public string[] FramePaths;
        public List<SpriteFrame> Frames;
    }

    internal sealed class AssetCatalog
    {
        public string AssetRoot;
        public Dictionary<string, StateDefinition> States =
            new Dictionary<string, StateDefinition>(StringComparer.OrdinalIgnoreCase);

        public static AssetCatalog Load(string projectRoot)
        {
            AssetCatalog catalog = new AssetCatalog();
            catalog.AssetRoot = Path.Combine(projectRoot, "assets", "feynman");
            if (!Directory.Exists(catalog.AssetRoot))
            {
                throw new DirectoryNotFoundException("找不到素材目录: " + catalog.AssetRoot);
            }

            catalog.Add("idle", 4, 290, true, 0, "今天想观察点什么？");
            catalog.Add("blink", 6, 90, false, 0, null);
            catalog.Add("walk", 6, 110, true, 8000, "去桌面上散散步。");
            catalog.Add("jump", 6, 100, false, 0, "起跳！");
            catalog.Add("sleep", 3, 440, true, 0, "别吵，我在梦里做思想实验……");
            catalog.Add("pet", 5, 110, false, 0, "摸头收到，好奇心上涨了！");
            catalog.Add("think", 4, 280, true, 9000, "这个问题，还能再想深一点。");
            catalog.Add("explain", 6, 210, true, 12000,
                "E = mc²。质量和能量，其实是一枚硬币的两面。");
            return catalog;
        }

        private void Add(
            string name,
            int frameCount,
            int delay,
            bool loop,
            int autoReturn,
            string bubble)
        {
            StateDefinition definition = new StateDefinition();
            definition.Name = name;
            definition.FrameDelayMs = delay;
            definition.Loop = loop;
            definition.AutoReturnMs = autoReturn;
            definition.DefaultBubble = bubble;
            definition.FramePaths = new string[frameCount];
            definition.Frames = new List<SpriteFrame>();
            for (int index = 0; index < frameCount; index++)
            {
                string relative = String.Format(
                    CultureInfo.InvariantCulture,
                    "{0}/frame_{1:00}.png",
                    name,
                    index);
                string path = Path.Combine(AssetRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                definition.FramePaths[index] = path;
                definition.Frames.Add(LoadFrame(path));
            }
            States[name] = definition;
        }

        private static SpriteFrame LoadFrame(string path)
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
                bitmap,
                PixelFormats.Bgra32,
                null,
                0);
            converted.Freeze();

            SpriteFrame frame = new SpriteFrame();
            frame.Image = converted;
            frame.Width = converted.PixelWidth;
            frame.Height = converted.PixelHeight;
            frame.Bgra = new byte[frame.Width * frame.Height * 4];
            converted.CopyPixels(frame.Bgra, frame.Width * 4, 0);
            return frame;
        }
    }

    internal static class RuntimeLog
    {
        private static string logPath;
        private static readonly object sync = new object();

        public static void Initialize(string projectRoot)
        {
            string directory = Path.Combine(projectRoot, "logs");
            Directory.CreateDirectory(directory);
            logPath = Path.Combine(directory, "runtime.log");
        }

        public static void Write(string message)
        {
            if (String.IsNullOrEmpty(logPath))
            {
                return;
            }
            try
            {
                lock (sync)
                {
                    string line = String.Format(
                        CultureInfo.InvariantCulture,
                        "{0:yyyy-MM-dd HH:mm:ss.fff}  {1}{2}",
                        DateTime.Now,
                        message,
                        Environment.NewLine);
                    File.AppendAllText(logPath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never prevent the desktop pet from running.
            }
        }
    }

    internal sealed class PetWindow : Window
    {
        private readonly AssetCatalog catalog;
        private readonly string projectRoot;
        private readonly Image characterImage;
        private readonly Grid bubbleHost;
        private readonly TextBlock bubbleText;
        private readonly ScaleTransform characterFlip;
        private readonly ScaleTransform bubbleFlip;
        private readonly DispatcherTimer frameTimer;
        private readonly DispatcherTimer autoReturnTimer;
        private readonly DispatcherTimer bubbleTimer;
        private readonly DispatcherTimer behaviorTimer;
        private readonly DispatcherTimer demoTimer;
        private readonly DispatcherTimer exitTimer;
        private readonly ContextMenu stateMenu;

        private StateDefinition currentState;
        private SpriteFrame currentFrame;
        private int frameIndex;
        private int idleBehaviorTicks;
        private int movementDirection = -1;
        private bool isMouseDown;
        private bool didDrag;
        private bool autoWalkEnabled = true;
        private Point mouseDownScreen;
        private Point mouseDownWindow;
        private DateTime lastClickUtc = DateTime.MinValue;
        private Point lastClickImagePoint;
        private HwndSource hwndSource;
        private Action demoCompletion;
        private int demoIndex;

        public PetWindow(
            string projectRoot,
            AssetCatalog catalog,
            string initialState,
            bool enableAutoBehavior)
        {
            this.projectRoot = projectRoot;
            this.catalog = catalog;
            this.autoWalkEnabled = enableAutoBehavior;

            Title = "Feynman Desktop Pet";
            Width = 340;
            Height = 410;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FocusVisualStyle = null;

            characterFlip = new ScaleTransform(1, 1);
            bubbleFlip = new ScaleTransform(1, 1);
            frameTimer = new DispatcherTimer(DispatcherPriority.Render);
            autoReturnTimer = new DispatcherTimer(DispatcherPriority.Background);
            bubbleTimer = new DispatcherTimer(DispatcherPriority.Background);
            behaviorTimer = new DispatcherTimer(DispatcherPriority.Background);
            demoTimer = new DispatcherTimer(DispatcherPriority.Background);
            exitTimer = new DispatcherTimer(DispatcherPriority.Background);

            Grid root = new Grid();
            root.Background = Brushes.Transparent;
            root.ClipToBounds = false;
            Content = root;

            characterImage = new Image();
            characterImage.Width = 280;
            characterImage.Height = 320;
            characterImage.Stretch = Stretch.Fill;
            characterImage.HorizontalAlignment = HorizontalAlignment.Center;
            characterImage.VerticalAlignment = VerticalAlignment.Bottom;
            characterImage.Margin = new Thickness(0, 0, 0, 2);
            characterImage.RenderTransformOrigin = new Point(0.5, 0.5);
            characterImage.RenderTransform = characterFlip;
            characterImage.Cursor = Cursors.Hand;
            RenderOptions.SetBitmapScalingMode(characterImage, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(characterImage, EdgeMode.Aliased);
            root.Children.Add(characterImage);

            bubbleHost = BuildBubble(out bubbleText);
            bubbleHost.Visibility = Visibility.Collapsed;
            root.Children.Add(bubbleHost);

            stateMenu = BuildContextMenu();
            ContextMenu = stateMenu;

            frameTimer.Tick += FrameTimerTick;
            autoReturnTimer.Tick += AutoReturnTick;
            bubbleTimer.Tick += BubbleTimerTick;
            behaviorTimer.Tick += BehaviorTimerTick;
            demoTimer.Tick += DemoTimerTick;
            exitTimer.Tick += ExitTimerTick;
            Loaded += PetWindowLoaded;
            Closed += PetWindowClosed;
            SourceInitialized += PetWindowSourceInitialized;
            MouseLeftButtonDown += PetMouseDown;
            MouseMove += PetMouseMove;
            MouseLeftButtonUp += PetMouseUp;
            MouseRightButtonUp += delegate
            {
                stateMenu.PlacementTarget = this;
                stateMenu.IsOpen = true;
            };

            SetState(String.IsNullOrEmpty(initialState) ? "idle" : initialState, null, false);
        }

        public string CurrentStateName
        {
            get { return currentState == null ? null : currentState.Name; }
        }

        private static Grid BuildBubble(out TextBlock textBlock)
        {
            StackPanel stack = new StackPanel();
            stack.HorizontalAlignment = HorizontalAlignment.Center;

            Border border = new Border();
            border.Width = 284;
            border.MinHeight = 42;
            border.Padding = new Thickness(15, 10, 15, 11);
            border.CornerRadius = new CornerRadius(16);
            border.Background = new SolidColorBrush(Color.FromArgb(255, 255, 250, 237));
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(48, 45, 44));
            border.BorderThickness = new Thickness(2);
            DropShadowEffect shadow = new DropShadowEffect();
            shadow.BlurRadius = 11;
            shadow.ShadowDepth = 3;
            shadow.Direction = 270;
            shadow.Opacity = 0.24;
            shadow.Color = Colors.Black;
            border.Effect = shadow;

            TextBlock text = new TextBlock();
            text.FontFamily = new FontFamily("Microsoft YaHei UI");
            text.FontSize = 14;
            text.FontWeight = FontWeights.SemiBold;
            text.Foreground = new SolidColorBrush(Color.FromRgb(48, 45, 44));
            text.TextWrapping = TextWrapping.Wrap;
            text.TextAlignment = TextAlignment.Center;
            text.LineHeight = 20;
            border.Child = text;
            textBlock = text;
            stack.Children.Add(border);

            System.Windows.Shapes.Polygon tail = new System.Windows.Shapes.Polygon();
            tail.Points = new PointCollection
            {
                new Point(0, 0),
                new Point(16, 0),
                new Point(5, 11)
            };
            tail.Fill = new SolidColorBrush(Color.FromRgb(255, 250, 237));
            tail.Stroke = new SolidColorBrush(Color.FromRgb(48, 45, 44));
            tail.StrokeThickness = 2;
            tail.Margin = new Thickness(-70, -1, 0, 0);
            tail.HorizontalAlignment = HorizontalAlignment.Center;
            tail.RenderTransformOrigin = new Point(0.5, 0.5);
            stack.Children.Add(tail);

            Grid host = new Grid();
            host.Width = 300;
            host.HorizontalAlignment = HorizontalAlignment.Center;
            host.VerticalAlignment = VerticalAlignment.Top;
            host.Margin = new Thickness(20, 12, 20, 0);
            host.RenderTransformOrigin = new Point(0.5, 0.5);
            host.Children.Add(stack);
            return host;
        }

        private ContextMenu BuildContextMenu()
        {
            ContextMenu menu = new ContextMenu();
            menu.FontFamily = new FontFamily("Microsoft YaHei UI");
            menu.FontSize = 13;
            menu.Items.Add(CreateStateItem("待机", "idle"));
            menu.Items.Add(CreateStateItem("眨眼", "blink"));
            menu.Items.Add(CreateStateItem("行走", "walk"));
            menu.Items.Add(CreateStateItem("跳跃", "jump"));
            menu.Items.Add(CreateStateItem("睡觉", "sleep"));
            menu.Items.Add(CreateStateItem("摸头回应", "pet"));
            menu.Items.Add(CreateStateItem("思考", "think"));
            menu.Items.Add(CreateStateItem("讲解物理", "explain"));
            menu.Items.Add(new Separator());

            MenuItem autoWalk = new MenuItem();
            autoWalk.Header = "自动漫步";
            autoWalk.IsCheckable = true;
            autoWalk.IsChecked = autoWalkEnabled;
            autoWalk.Click += delegate
            {
                autoWalkEnabled = autoWalk.IsChecked;
                ShowBubble(autoWalkEnabled ? "我会偶尔出去转一圈。" : "好，我就待在这里。", 2400);
            };
            menu.Items.Add(autoWalk);

            menu.Items.Add(new Separator());
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
            item.Click += delegate { SetState(state, null, true); };
            return item;
        }

        private void PetWindowSourceInitialized(object sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            hwndSource = HwndSource.FromHwnd(handle);
            if (hwndSource != null)
            {
                hwndSource.AddHook(WindowProcedure);
            }

            Rect workArea = SystemParameters.WorkArea;
            Left = Math.Max(workArea.Left, workArea.Right - Width - 24);
            Top = Math.Max(workArea.Top, workArea.Bottom - Height - 12);
            RuntimeLog.Write("Window created. Handle=" + handle.ToString() + " bounds=" +
                Left.ToString("0") + "," + Top.ToString("0") + "," +
                Width.ToString("0") + "x" + Height.ToString("0") +
                " transparent=" + AllowsTransparency + " topmost=" + Topmost);
        }

        private void PetWindowLoaded(object sender, RoutedEventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowPos(
                handle,
                NativeMethods.HwndTopmost,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoMove |
                NativeMethods.SwpNoSize |
                NativeMethods.SwpNoActivate |
                NativeMethods.SwpShowWindow);
            RuntimeLog.Write(
                "Window shown. image=" +
                characterImage.ActualWidth.ToString("0") + "x" +
                characterImage.ActualHeight.ToString("0") +
                " bubble=" + bubbleHost.ActualWidth.ToString("0") + "x" +
                bubbleHost.ActualHeight.ToString("0") +
                " source=" + (characterImage.Source == null ? "null" : "loaded"));
            behaviorTimer.Interval = TimeSpan.FromSeconds(7);
            behaviorTimer.Start();
            ShowBubble("你好，我是费曼。今天也保持好奇吧。", 4400);
        }

        private void PetWindowClosed(object sender, EventArgs e)
        {
            frameTimer.Stop();
            autoReturnTimer.Stop();
            bubbleTimer.Stop();
            behaviorTimer.Stop();
            demoTimer.Stop();
            exitTimer.Stop();
            if (hwndSource != null)
            {
                hwndSource.RemoveHook(WindowProcedure);
            }
            RuntimeLog.Write("Window closed.");
        }

        public void SetState(string stateName, string bubble, bool userInitiated)
        {
            StateDefinition definition;
            if (!catalog.States.TryGetValue(stateName, out definition))
            {
                throw new ArgumentException("未知状态: " + stateName, "stateName");
            }

            currentState = definition;
            currentFrame = definition.Frames[0];
            frameIndex = 0;
            characterImage.Source = currentFrame.Image;
            characterFlip.ScaleX = 1;
            frameTimer.Stop();
            frameTimer.Interval = TimeSpan.FromMilliseconds(definition.FrameDelayMs);
            frameTimer.Start();
            autoReturnTimer.Stop();
            if (definition.AutoReturnMs > 0)
            {
                autoReturnTimer.Interval = TimeSpan.FromMilliseconds(definition.AutoReturnMs);
                autoReturnTimer.Start();
            }

            if (String.Equals(definition.Name, "walk", StringComparison.OrdinalIgnoreCase))
            {
                movementDirection = ChooseWalkDirection();
                characterFlip.ScaleX = movementDirection < 0 ? -1 : 1;
            }

            RuntimeLog.Write(
                "State=" + definition.Name +
                (userInitiated ? " (user)" : " (auto)") +
                " frames=" + definition.Frames.Count +
                " delay=" + definition.FrameDelayMs + "ms");

            if (!String.IsNullOrEmpty(bubble))
            {
                ShowBubble(bubble, definition.Name == "explain" ? 6000 : 3400);
            }
            else if (!String.IsNullOrEmpty(definition.DefaultBubble))
            {
                ShowBubble(definition.DefaultBubble, definition.Name == "explain" ? 6000 : 3200);
            }
        }

        private int ChooseWalkDirection()
        {
            Rect workArea = SystemParameters.WorkArea;
            if (Left - workArea.Left < 80)
            {
                return 1;
            }
            if (workArea.Right - (Left + Width) < 80)
            {
                return -1;
            }
            return new Random().Next(0, 2) == 0 ? -1 : 1;
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
                    SetState("idle", null, false);
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
            Rect workArea = SystemParameters.WorkArea;
            double nextLeft = Left + movementDirection * 2.2;
            if (nextLeft < workArea.Left)
            {
                nextLeft = workArea.Left;
                movementDirection = 1;
                characterFlip.ScaleX = 1;
            }
            else if (nextLeft + Width > workArea.Right)
            {
                nextLeft = workArea.Right - Width;
                movementDirection = -1;
                characterFlip.ScaleX = -1;
            }
            Left = nextLeft;
        }

        private void AutoReturnTick(object sender, EventArgs e)
        {
            autoReturnTimer.Stop();
            if (currentState != null && currentState.Name != "sleep")
            {
                SetState("idle", null, false);
            }
        }

        private void BehaviorTimerTick(object sender, EventArgs e)
        {
            if (!IsVisible || currentState == null)
            {
                return;
            }
            if (currentState.Name != "idle")
            {
                idleBehaviorTicks = 0;
                return;
            }

            idleBehaviorTicks++;
            Random random = new Random(Guid.NewGuid().GetHashCode());
            if (idleBehaviorTicks >= 3 && autoWalkEnabled && random.NextDouble() < 0.42)
            {
                SetState("walk", null, false);
                idleBehaviorTicks = 0;
            }
            else if (random.NextDouble() < 0.55)
            {
                SetState("blink", null, false);
            }
        }

        private void ShowBubble(string text, int durationMs)
        {
            if (String.IsNullOrEmpty(text))
            {
                bubbleHost.Visibility = Visibility.Collapsed;
                bubbleTimer.Stop();
                return;
            }
            bubbleText.Text = text;
            bubbleHost.Visibility = Visibility.Visible;
            bubbleTimer.Stop();
            bubbleTimer.Interval = TimeSpan.FromMilliseconds(durationMs);
            bubbleTimer.Start();
        }

        private void BubbleTimerTick(object sender, EventArgs e)
        {
            bubbleTimer.Stop();
            bubbleHost.Visibility = Visibility.Collapsed;
        }

        private void PetMouseDown(object sender, MouseButtonEventArgs e)
        {
            isMouseDown = true;
            didDrag = false;
            mouseDownScreen = PointToScreen(e.GetPosition(this));
            mouseDownWindow = new Point(Left, Top);
            if (String.Equals(
                Environment.GetEnvironmentVariable("FEYNMAN_HIT_LOG"),
                "1",
                StringComparison.Ordinal))
            {
                RuntimeLog.Write(
                    "MouseDown window=" + e.GetPosition(this).X.ToString("0.0") + "," +
                    e.GetPosition(this).Y.ToString("0.0") +
                    " image=" + e.GetPosition(characterImage).X.ToString("0.0") + "," +
                    e.GetPosition(characterImage).Y.ToString("0.0"));
            }
            CaptureMouse();
            e.Handled = true;
        }

        private void PetMouseMove(object sender, MouseEventArgs e)
        {
            if (!isMouseDown || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }
            Point current = PointToScreen(e.GetPosition(this));
            Vector delta = current - mouseDownScreen;
            if (!didDrag && delta.Length > 4)
            {
                didDrag = true;
                if (currentState != null && currentState.Name == "walk")
                {
                    SetState("idle", null, false);
                }
            }
            if (didDrag)
            {
                Rect workArea = SystemParameters.WorkArea;
                double nextLeft = mouseDownWindow.X + delta.X;
                double nextTop = mouseDownWindow.Y + delta.Y;
                Left = Math.Max(workArea.Left, Math.Min(workArea.Right - Width, nextLeft));
                Top = Math.Max(workArea.Top, Math.Min(workArea.Bottom - Height, nextTop));
            }
        }

        private void PetMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!isMouseDown)
            {
                return;
            }
            isMouseDown = false;
            ReleaseMouseCapture();
            if (didDrag)
            {
                ShowBubble("这个位置不错。", 1700);
                e.Handled = true;
                return;
            }

            Point imagePoint = e.GetPosition(characterImage);
            if (imagePoint.X < 0 || imagePoint.Y < 0 ||
                imagePoint.X > characterImage.ActualWidth ||
                imagePoint.Y > characterImage.ActualHeight)
            {
                e.Handled = true;
                return;
            }

            DateTime now = DateTime.UtcNow;
            bool doubleClick =
                (now - lastClickUtc).TotalMilliseconds < 390 &&
                (imagePoint - lastClickImagePoint).Length < 22;
            lastClickUtc = doubleClick ? DateTime.MinValue : now;
            lastClickImagePoint = imagePoint;

            if (currentState != null && currentState.Name == "sleep")
            {
                SetState("idle", "早上好！刚才想到一个有趣的问题。", true);
            }
            else if (doubleClick)
            {
                SetState("explain", null, true);
            }
            else if (IsHeadPoint(imagePoint))
            {
                SetState("pet", null, true);
            }
            else
            {
                SetState("think", null, true);
            }
            e.Handled = true;
        }

        private bool IsHeadPoint(Point point)
        {
            int width = currentFrame == null ? 280 : currentFrame.Width;
            int height = currentFrame == null ? 320 : currentFrame.Height;
            double normalizedX = point.X / Math.Max(1.0, characterImage.ActualWidth);
            double normalizedY = point.Y / Math.Max(1.0, characterImage.ActualHeight);
            double centerX = currentState != null &&
                String.Equals(currentState.Name, "explain", StringComparison.OrdinalIgnoreCase)
                ? 0.74
                : 0.50;
            double dx = (normalizedX - centerX) / 0.245;
            double dy = (normalizedY - 0.36) / 0.255;
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
                Point screenPoint = new Point(x, y);
                bool transparent = IsTransparentScreenPoint(screenPoint);
                if (String.Equals(
                    Environment.GetEnvironmentVariable("FEYNMAN_HIT_LOG"),
                    "1",
                    StringComparison.Ordinal))
                {
                    Point windowPoint = PointFromScreen(screenPoint);
                    Point imagePoint = characterImage.PointFromScreen(screenPoint);
                    RuntimeLog.Write(
                        "HitTest screen=" + screenPoint.X + "," + screenPoint.Y +
                        " window=" + windowPoint.X.ToString("0.0") + "," +
                        windowPoint.Y.ToString("0.0") +
                        " image=" + imagePoint.X.ToString("0.0") + "," +
                        imagePoint.Y.ToString("0.0") +
                        " size=" + characterImage.ActualWidth.ToString("0") + "x" +
                        characterImage.ActualHeight.ToString("0") +
                        " transparent=" + transparent);
                }
                if (transparent)
                {
                    handled = true;
                    return new IntPtr(NativeMethods.HtTransparent);
                }
            }
            return IntPtr.Zero;
        }

        private bool IsTransparentScreenPoint(Point screenPoint)
        {
            if (bubbleHost.Visibility == Visibility.Visible)
            {
                Point bubblePoint = bubbleHost.PointFromScreen(screenPoint);
                if (bubblePoint.X >= 0 && bubblePoint.Y >= 0 &&
                    bubblePoint.X <= bubbleHost.ActualWidth &&
                    bubblePoint.Y <= bubbleHost.ActualHeight)
                {
                    return false;
                }
            }

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
            int xPixel = (int)(imagePoint.X / characterImage.ActualWidth * currentFrame.Width);
            int yPixel = (int)(imagePoint.Y / characterImage.ActualHeight * currentFrame.Height);
            return currentFrame.AlphaAt(xPixel, yPixel) < 12;
        }

        public void StartAnimationTest(Action completion)
        {
            demoCompletion = completion;
            demoIndex = 0;
            string[] sequence = new string[]
            {
                "idle", "blink", "walk", "jump", "sleep", "pet", "think", "explain"
            };
            SetState(sequence[demoIndex], null, false);
            demoTimer.Interval = TimeSpan.FromMilliseconds(1350);
            demoTimer.Tick += delegate
            {
                demoIndex++;
                if (demoIndex >= sequence.Length)
                {
                    demoTimer.Stop();
                    RuntimeLog.Write("Animation test completed: all 8 states switched successfully.");
                    if (demoCompletion != null)
                    {
                        demoCompletion();
                    }
                    return;
                }
                SetState(sequence[demoIndex], null, true);
            };
            demoTimer.Start();
        }

        public void CloseAfterSeconds(int seconds)
        {
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
            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(Width),
                (int)Math.Ceiling(Height),
                96,
                96,
                PixelFormats.Pbgra32);
            bitmap.Render(this);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (FileStream stream = File.Create(path))
            {
                encoder.Save(stream);
            }
            RuntimeLog.Write("Render snapshot saved: " + path);
        }

        private void DemoTimerTick(object sender, EventArgs e)
        {
        }

        private void ExitTimerTick(object sender, EventArgs e)
        {
        }

        public static string ValidateWindowConfiguration(
            string root,
            AssetCatalog catalog)
        {
            PetWindow window = new PetWindow(root, catalog, "idle", false);
            StringBuilder report = new StringBuilder();
            bool passed = true;

            passed &= Check(report, "borderless", window.WindowStyle == WindowStyle.None);
            passed &= Check(report, "per-pixel-transparency", window.AllowsTransparency);
            passed &= Check(report, "transparent-background", window.Background == Brushes.Transparent);
            passed &= Check(report, "topmost", window.Topmost);
            passed &= Check(report, "taskbar-hidden", !window.ShowInTaskbar);
            passed &= Check(report, "no-resize", window.ResizeMode == ResizeMode.NoResize);
            passed &= Check(
                report,
                "known-start-state",
                String.Equals(window.CurrentStateName, "idle", StringComparison.OrdinalIgnoreCase));
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
                AssetCatalog catalog = AssetCatalog.Load(root);
                RuntimeLog.Write("Asset catalog loaded from " + catalog.AssetRoot +
                    " (" + CountFrames(catalog) + " frames)");

                string initialState = ReadOption(args, "--state");
                int autoExit = ReadIntOption(args, "--auto-exit", 0);
                bool selfTest = HasFlag(args, "--self-test");
                bool animationTest = HasFlag(args, "--animation-test");
                bool solidTest = HasFlag(args, "--solid-test");
                bool renderTest = HasFlag(args, "--render-test");

                if (selfTest)
                {
                    string report = AuditAssets(catalog);
                    report += PetWindow.ValidateWindowConfiguration(root, catalog);
                    string reportPath = Path.Combine(root, "logs", "self-test.txt");
                    Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
                    File.WriteAllText(reportPath, report, Encoding.UTF8);
                    RuntimeLog.Write("Self-test finished.");
                    return report.Contains("=FAIL") ? 2 : 0;
                }

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                PetWindow window = new PetWindow(
                    root,
                    catalog,
                    String.IsNullOrEmpty(initialState) ? "idle" : initialState,
                    true);
                if (solidTest)
                {
                    window.Background = Brushes.Red;
                }
                if (renderTest)
                {
                    window.Loaded += delegate
                    {
                        window.Dispatcher.BeginInvoke(
                            DispatcherPriority.ApplicationIdle,
                            new Action(delegate
                            {
                                window.SaveRenderSnapshot(
                                    Path.Combine(root, "logs", "render-test.png"));
                                window.Close();
                            }));
                    };
                }
                if (animationTest)
                {
                    window.StartAnimationTest(delegate { window.Close(); });
                }
                if (autoExit > 0)
                {
                    window.CloseAfterSeconds(autoExit);
                }
                app.Run(window);
                return 0;
            }
            catch (Exception exception)
            {
                RuntimeLog.Write("FATAL: " + exception);
                try
                {
                    File.WriteAllText(
                        Path.Combine(FindProjectRoot(), "logs", "fatal.txt"),
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
            report.AppendLine("StateCount=" + catalog.States.Count);
            int frameCount = 0;
            bool passed = catalog.States.Count == 8;
            foreach (KeyValuePair<string, StateDefinition> pair in catalog.States)
            {
                StateDefinition definition = pair.Value;
                report.AppendLine("State " + pair.Key + " frames=" + definition.Frames.Count);
                if (definition.Frames.Count < 3)
                {
                    passed = false;
                }
                foreach (SpriteFrame frame in definition.Frames)
                {
                    frameCount++;
                    bool hasAlpha = false;
                    bool hasTransparent = false;
                    int transparentPixels = 0;
                    for (int index = 3; index < frame.Bgra.Length; index += 4)
                    {
                        byte alpha = frame.Bgra[index];
                        if (alpha > 0)
                        {
                            hasAlpha = true;
                        }
                        if (alpha < 12)
                        {
                            hasTransparent = true;
                            transparentPixels++;
                        }
                    }
                    double transparentRatio = transparentPixels / (double)(frame.Width * frame.Height);
                    bool cornersTransparent =
                        frame.AlphaAt(0, 0) == 0 &&
                        frame.AlphaAt(frame.Width - 1, 0) == 0 &&
                        frame.AlphaAt(0, frame.Height - 1) == 0 &&
                        frame.AlphaAt(frame.Width - 1, frame.Height - 1) == 0;
                    if (frame.Width != 280 || frame.Height != 320 ||
                        !hasAlpha || !hasTransparent || !cornersTransparent ||
                        transparentRatio < 0.15 || transparentRatio > 0.96)
                    {
                        passed = false;
                        report.AppendLine(
                            "  Frame FAIL " + definition.Name + " " + frame.Width + "x" +
                            frame.Height + " alpha=" + transparentRatio.ToString("P1"));
                    }
                }
            }
            report.AppendLine("FrameCount=" + frameCount);
            report.AppendLine("AlphaAudit=" + (passed ? "PASS" : "FAIL"));
            report.AppendLine(passed ? "ASSET_TEST=PASS" : "ASSET_TEST=FAIL");
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
            string value = ReadOption(args, name);
            int parsed;
            return Int32.TryParse(value, out parsed) ? parsed : fallback;
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

        private static int CountFrames(AssetCatalog catalog)
        {
            int count = 0;
            foreach (StateDefinition definition in catalog.States.Values)
            {
                count += definition.Frames.Count;
            }
            return count;
        }
    }
}
