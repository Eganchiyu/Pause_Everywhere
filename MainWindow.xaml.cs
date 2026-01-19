using OpenCvSharp;
using OpenCvSharp.Extensions;
using OpenCvSharp.WpfExtensions;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Pause_Everywhere
{
    public partial class MainWindow : System.Windows.Window
    {
        private BitmapSource _preparedFrame;
        private readonly object _frameLock = new();
        private Task _precomputeTask;
        private volatile bool _precomputeRunning = true;

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
            StartBackgroundPrecompute();
        }

        private void StartBackgroundPrecompute()
        {
            _precomputeTask = Task.Run(() =>
            {
                while (_precomputeRunning)
                {
                    try
                    {
                        // 获取当前窗口所在的屏幕
                        var handle = new WindowInteropHelper(this).Handle;
                        var screen = System.Windows.Forms.Screen.FromHandle(handle);
                        var bounds = screen.Bounds;

                        // 捕获并处理屏幕
                        var frame = CaptureAndProcessScreen_Precompute(bounds);

                        // 更新预备帧
                        lock (_frameLock)
                        {
                            _preparedFrame = frame;
                        }

                        // 如果窗口可见，立即更新显示
                        if (this.Dispatcher.Invoke(() => this.Visibility == Visibility.Visible))
                        {
                            this.Dispatcher.Invoke(() =>
                            {
                                if (this.Visibility == Visibility.Visible)
                                {
                                    BackImage.Source = frame;
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"预计算错误: {ex.Message}");
                    }

                    // 稍微延迟一下，避免过度消耗CPU
                    System.Threading.Thread.Sleep(50);
                }
            });
        }

        private BitmapSource CaptureAndProcessScreen_Precompute(System.Drawing.Rectangle bounds)
        {
            using (Bitmap bmp = new Bitmap(bounds.Width, bounds.Height))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                // 捕获整个屏幕
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bmp.Size);

                using (Mat mat = BitmapConverter.ToMat(bmp))
                {
                    // 1️⃣ 降采样（性能核心）
                    Cv2.Resize(mat, mat, new OpenCvSharp.Size(), 0.5, 0.5,
                        InterpolationFlags.Linear);

                    // 2️⃣ 模糊（在小图上）
                    Cv2.GaussianBlur(mat, mat, new OpenCvSharp.Size(0, 0), 20);

                    // 3️⃣ 放大回原分辨率
                    Cv2.Resize(
                        mat,
                        mat,
                        new OpenCvSharp.Size(bounds.Width, bounds.Height),
                        0, 0,
                        InterpolationFlags.Linear);

                    var source = mat.ToBitmapSource();
                    source.Freeze();
                    return source;
                }
            }
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
                    // 先显示窗口并激活
                    this.Visibility = Visibility.Visible;
                    this.Activate();
                    this.Topmost = true; // 确保在最前面
                    this.Topmost = false; // 恢复正常状态

                    // 立即更新显示为最新的预备帧
                    Dispatcher.Invoke(() =>
                    {
                        BitmapSource frame;
                        lock (_frameLock)
                        {
                            frame = _preparedFrame;
                        }

                        if (frame != null)
                        {
                            BackImage.Source = frame;
                        }
                        else
                        {
                            // 如果没有预备帧，立即捕获一次
                            var handle = new WindowInteropHelper(this).Handle;
                            var screen = System.Windows.Forms.Screen.FromHandle(handle);
                            var bounds = screen.Bounds;

                            var newFrame = CaptureAndProcessScreen_Precompute(bounds);
                            BackImage.Source = newFrame;
                        }
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

            _precomputeRunning = false;
            _precomputeTask?.Wait(1000); // 等待任务结束

            base.OnClosed(e);
        }
    }
}