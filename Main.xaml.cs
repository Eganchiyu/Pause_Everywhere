using Microsoft.VisualBasic;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using OpenCvSharp.WpfExtensions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;


namespace Pause_Everywhere
{
    public partial class Main : System.Windows.Window
    {

        // ===== 亮度判断参数 =====
        public const double DIM_OPACITY = 0.25;          // 变暗强度
        public static volatile bool _needDim = false;           // 是否需要变暗
        // ===== 亮度判断参数 =====


        //===== 热键注册相关 =====
        const int HOTKEY_ID = 1;// 热键ID
        const uint MOD_CONTROL = 0x0002;// Ctrl键
        const uint MOD_ALT = 0x0001;// Alt键
        const uint VK_P = 0x50;// P键
        //===== 热键注册相关 =====

        // ===== 预计算模糊图像相关 =====
        public static volatile bool _precomputeRunning = true;// 控制后台任务运行
        public static volatile bool WindowIsVisible = false;
        // ===== 预计算模糊图像相关 =====



        public static Rectangle _screenBounds; // 屏幕区域的缓存
        public static volatile bool _isProcessingHotkey = false;// 防止热键重复处理




        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        //初始化阶段
        public Main()
        {
            System.Media.SystemSounds.Beep.Play();
            Debug.WriteLine("初始化窗口");
            InitializeComponent();
            Debug.WriteLine("注册窗口加载事件");
            Loaded += MainWindow_Loaded;// 窗口加载完成后启动后台任务
        }

        // 窗口加载完成事件处理
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("开始运行预渲染主函数");
            BGPreCompute.StartBackgroundPrecompute();
        }


        // 注册热键
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var handle = new WindowInteropHelper(this).Handle;
            RegisterHotKey(handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_P);

            // 获取屏幕区域的初始值的缓存
            var screen = Screen.FromHandle(handle);
            _screenBounds = screen.Bounds;

            HwndSource source = HwndSource.FromHwnd(handle);
            source.AddHook(WndProc);
        }

        /// <summary>
        /// 处理图像：包括调整大小、高斯模糊和恢复原尺寸，用于降噪或减少细节
        /// </summary>
        /// <remarks>
        /// 此方法还会根据图像亮度更新是否需要变暗的标志(_needDim)
        /// 调用方负责在使用完毕后释放返回的<see cref="Mat"/>对象。
        /// </remarks>
        /// <param name="input">输入的原始图像。</param>
        /// <returns>处理后的图像<see cref="Mat"/>对象。</returns>


        /// <summary>
        /// 窗口消息处理主函数，主要处理热键消息以控制窗口显示和更新背景
        /// </summary>
        /// <remarks>
        /// 此方法负责处理Windows消息循环中的WM_HOTKEY热键信息，
        /// 当检测到指定热键被按下时，会切换窗口的显示状态
        /// 消息处理后会将handled标志设为true，表示消息已被处理，不需要进一步传递。
        /// </remarks>
        /// <param name="hwnd">当前窗口的句柄</param>
        /// <param name="msg">Windows消息标识符</param>
        /// <param name="wParam">消息参数，对于热键消息表示热键ID</param>
        /// <param name="lParam">消息参数，内容取决于消息类型</param>
        /// <param name="handled">输出参数，指示消息是否已被处理</param>
        /// <returns>消息处理结果，此方法始终返回IntPtr.Zero</returns>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312; // 热键消息
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                _isProcessingHotkey = true;
                //_needsRecalculation = true; // 标记需要重新计算模糊图像

                Dispatcher.Invoke(() =>
                {
                    WindowIsVisible = IsVisible;
                    if (WindowIsVisible)
                    {
                        if (_needDim)
                            DimLayer.Opacity = DIM_OPACITY;
                        else
                            DimLayer.Opacity = 0.0;

                        // 立即显示窗口
                        Visibility = Visibility.Visible;
                        Activate();

                        // 使用预计算的模糊图像（如果有）
                        BitmapSource? src = null;

                        // 读取预计算图像时加锁,防止数据竞争
                        lock (BGPreCompute._frameLock)
                        {
                            if (!BGPreCompute._preparedMat.Empty())// 检查预计算图像是否有效
                            {
                                src = BGPreCompute._preparedMat.ToBitmapSource(); // 转换为WPF图像
                            }
                        }

                        // 如果预计算图像可用，直接使用
                        if (src != null)
                        {
                            src.Freeze();
                            BackImage.Source = src;// 设置背景图像
                        }

                        else
                        {
                            // 否则实时计算（后备方案）
                            var handle = new WindowInteropHelper(this).Handle;
                            var screen = System.Windows.Forms.Screen.FromHandle(handle);
                            var bounds = screen.Bounds;
                            using var mat = Gaussian_processor.Process(CapScreen.Capture(bounds));
                            src = mat.ToBitmapSource();
                            src.Freeze();
                            BackImage.Source = src;
                        }
                    }
                    else
                    {
                        Visibility = Visibility.Hidden;
                    }
                });

                Task.Delay(300).ContinueWith(_ => _isProcessingHotkey = false);
                handled = true;
            }
            return IntPtr.Zero;
        }


        /// <summary>
        /// 窗口关闭时执行清理操作
        /// </summary>
        /// <remarks>
        /// 此方法在窗口关闭时释放资源、注销热键并清理预计算数据。
        /// 作为窗口关闭流程的一部分被调用，确保资源正确释放。
        /// </remarks>
        /// <param name="e">包含事件数据的<see cref="EventArgs"/>对象</param>
        protected override void OnClosed(EventArgs e)
        {
            _precomputeRunning = false;

            try { BGPreCompute._precomputeTask?.Wait(500); } catch { }

            var handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, HOTKEY_ID);

            lock (BGPreCompute._frameLock)
            {
                BGPreCompute._preparedMat?.Dispose();
                BGPreCompute._preparedMat = new Mat();
            }

            base.OnClosed(e);
        }
    }
}
