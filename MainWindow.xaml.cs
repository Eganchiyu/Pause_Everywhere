using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Pause_Everywhere
{
    public partial class MainWindow : System.Windows.Window
    {
        const int HOTKEY_ID = 1;
        const uint MOD_CONTROL = 0x0002;
        const uint MOD_ALT = 0x0001;
        const uint VK_P = 0x50;

        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var handle = new WindowInteropHelper(this).Handle;
            RegisterHotKey(handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_P);

            HwndSource source = HwndSource.FromHwnd(handle);
            source.AddHook(WndProc);
        }

        private IntPtr WndProc(
            IntPtr hwnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                if (this.Visibility != Visibility.Visible)
                {
                    this.Visibility = Visibility.Visible;
                    this.Activate();

                    // 在 WndProc 中（UI 线程）
                    var handle = new WindowInteropHelper(this).Handle;
                    var screen = System.Windows.Forms.Screen.FromHandle(handle);
                    var bounds = screen.Bounds; // System.Drawing.Rectangle

                    Task.Run(() =>
                    {
                        var image = CaptureAndProcessScreen(bounds); // 不再访问 `this`
                        Dispatcher.Invoke(() =>
                        {
                            FrontImage.Source = image;
                            FrontImage.Opacity = 0;

                            var fade = new DoubleAnimation
                            {
                                From = 0.0,
                                To = 1.0,
                                Duration = TimeSpan.FromMilliseconds(180),
                                EasingFunction = new CubicEase
                                {
                                    EasingMode = EasingMode.EaseOut
                                }
                            };

                            fade.Completed += (_, __) =>
                            {
                                // 动画完成后，把新图“固化”为旧图
                                BackImage.Source = FrontImage.Source;
                                FrontImage.Source = null;
                                FrontImage.Opacity = 0;
                            };

                            FrontImage.BeginAnimation(UIElement.OpacityProperty, fade);
                        });

                    });
                }
                else
                {
                    this.Visibility = Visibility.Hidden;
                }

                handled = true;
            }

            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, HOTKEY_ID);
            base.OnClosed(e);
        }

        private BitmapSource CaptureAndProcessScreen(System.Drawing.Rectangle bounds)
        {
            using Bitmap bmp = new Bitmap(bounds.Width, bounds.Height);
            using Graphics g = Graphics.FromImage(bmp);
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bmp.Size);

            var mat = OpenCvSharp.Extensions.BitmapConverter.ToMat(bmp);
            Cv2.GaussianBlur(mat, mat, new OpenCvSharp.Size(0, 0), 20);

            var source = mat.ToBitmapSource();
            source.Freeze();
            return source;
        }

        

    }
}
