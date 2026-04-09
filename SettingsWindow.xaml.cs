using System;
using System.Windows;

namespace Pause_Everywhere
{
    /// <summary>
    /// SettingsWindow.xaml 的交互逻辑
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();

            // 窗口启动时加载当前保存的配置
            LoadCurrentSettings();
        }

        /// <summary>
        /// 从持久化设置中读取值并显示在 UI 控件上
        /// </summary>
        private void LoadCurrentSettings()
        {
            try
            {
                // 从配置文件读取值并赋给滑动条 (Slider)
                OpacitySlider.Value = Properties.Settings.Default.DimOpacity;
                ThresholdSlider.Value = Properties.Settings.Default.Threshold;

                // 从配置文件读取值并赋给复选框 (CheckBox)
                MuteCheckBox.IsChecked = Properties.Settings.Default.IsMute;
                DimCheckBox.IsChecked = Properties.Settings.Default.dim_flag;
            }
            catch (Exception ex)
            {
                // 如果是第一次运行或配置出错，可以使用 Main 中的默认值
                OpacitySlider.Value = Main.DIM_OPACITY;
                ThresholdSlider.Value = Main.DIFF_ENERGY_THRESHOLD;
                MuteCheckBox.IsChecked = true;
                DimCheckBox.IsChecked = true;
            }
        }

        /// <summary>
        /// 点击保存按钮时的逻辑
        /// </summary>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // 1. 将 UI 控件当前的值写入配置文件（内存快照）
            Properties.Settings.Default.DimOpacity = OpacitySlider.Value;
            Properties.Settings.Default.Threshold = (int)ThresholdSlider.Value;
            Properties.Settings.Default.IsMute = MuteCheckBox.IsChecked ?? true;
            Properties.Settings.Default.dim_flag = DimCheckBox.IsChecked ?? true;

            // 2. 真正写入硬盘持久化存储
            Properties.Settings.Default.Save();

            // 3. 同时更新 Main 类中正在运行的静态变量，实现“即时生效”
            Main.DIM_OPACITY = Properties.Settings.Default.DimOpacity;
            Main.DIFF_ENERGY_THRESHOLD = Properties.Settings.Default.Threshold;
            Main.isMute = Properties.Settings.Default.IsMute;
            Main.dim_flag = Properties.Settings.Default.dim_flag;

            // 4. 提示并关闭窗口
            System.Windows.MessageBox.Show("配置已更新！", "Pause Everywhere", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();
        }
    }
}