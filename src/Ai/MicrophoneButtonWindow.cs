using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace FeynmanPet
{
    internal enum MicButtonMode
    {
        Idle,
        Recording,
        Busy,
        Disabled
    }

    internal sealed class MicrophoneButtonWindow : Window
    {
        private readonly PetWindow pet;
        private readonly Border border;
        private readonly TextBlock glyph;
        private HwndSource hwndSource;
        private MicButtonMode mode;

        public event EventHandler ToggleRequested;

        public MicrophoneButtonWindow(PetWindow pet)
        {
            this.pet = pet;
            Title = "Feynman microphone";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            Topmost = pet.Topmost;
            Width = 31;
            Height = 31;
            WindowStartupLocation = WindowStartupLocation.Manual;

            border = new Border();
            border.CornerRadius = new CornerRadius(15.5);
            border.BorderThickness = new Thickness(1.4);
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(47, 42, 39));
            border.Background = new SolidColorBrush(Color.FromRgb(255, 248, 232));
            DropShadowEffect shadow = new DropShadowEffect();
            shadow.BlurRadius = 7;
            shadow.ShadowDepth = 2;
            shadow.Opacity = 0.25;
            shadow.Color = Colors.Black;
            border.Effect = shadow;

            glyph = new TextBlock();
            glyph.FontFamily = new FontFamily("Segoe MDL2 Assets");
            glyph.FontSize = 15;
            glyph.Text = "\uE720";
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.Foreground = new SolidColorBrush(Color.FromRgb(47, 42, 39));
            border.Child = glyph;
            Content = border;
            ToolTip = "开始语音对话";

            SourceInitialized += delegate
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
                PositionNearPet();
            };
            Closed += delegate
            {
                if (hwndSource != null)
                {
                    hwndSource.RemoveHook(WindowProcedure);
                }
            };
            SetMode(MicButtonMode.Idle);
        }

        public MicButtonMode Mode
        {
            get { return mode; }
        }

        public void SetMode(MicButtonMode value)
        {
            mode = value;
            if (value == MicButtonMode.Recording)
            {
                border.Background = new SolidColorBrush(Color.FromRgb(181, 69, 76));
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(96, 32, 38));
                glyph.Foreground = Brushes.White;
                ToolTip = "结束录音并识别";
            }
            else if (value == MicButtonMode.Busy)
            {
                border.Background = new SolidColorBrush(Color.FromRgb(219, 176, 85));
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(94, 76, 40));
                glyph.Foreground = new SolidColorBrush(Color.FromRgb(42, 34, 26));
                ToolTip = "语音任务处理中";
            }
            else if (value == MicButtonMode.Disabled)
            {
                border.Background = new SolidColorBrush(Color.FromRgb(196, 191, 182));
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(108, 104, 98));
                glyph.Foreground = new SolidColorBrush(Color.FromRgb(84, 80, 74));
                ToolTip = "语音服务不可用";
            }
            else
            {
                border.Background = new SolidColorBrush(Color.FromRgb(255, 248, 232));
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(47, 42, 39));
                glyph.Foreground = new SolidColorBrush(Color.FromRgb(47, 42, 39));
                ToolTip = "开始语音对话";
            }
        }

        public void PositionNearPet()
        {
            if (!pet.IsVisible)
            {
                return;
            }
            Rect work = SystemParameters.WorkArea;
            double left = pet.Left - Width - 6;
            if (left < work.Left)
            {
                left = pet.Left + pet.Width + 6;
            }
            double top = pet.Top + pet.Height * 0.22;
            Left = Math.Max(work.Left, Math.Min(work.Right - Width, left));
            Top = Math.Max(work.Top, Math.Min(work.Bottom - Height, top));
        }

        public void SetTopmost(bool topmost)
        {
            Topmost = topmost;
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(
                    handle,
                    topmost ? NativeMethods.HwndTopmost : NativeMethods.HwndNotTopmost,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SwpNoMove |
                    NativeMethods.SwpNoSize |
                    NativeMethods.SwpNoActivate);
            }
        }

        private IntPtr WindowProcedure(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message == NativeMethods.WmLeftButtonUp && mode != MicButtonMode.Disabled)
            {
                handled = true;
                EventHandler handler = ToggleRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
            return IntPtr.Zero;
        }
    }
}
