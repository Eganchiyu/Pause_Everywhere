using System;
using System.Windows;
using Microsoft.Win32;

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
                // General
                OpacitySlider.Value = Properties.Settings.Default.DimOpacity;
                ThresholdSlider.Value = Properties.Settings.Default.Threshold;
                MuteCheckBox.IsChecked = Properties.Settings.Default.IsMute;
                DimCheckBox.IsChecked = Properties.Settings.Default.dim_flag;

                // Audio
                EnableStartSoundCheckBox.IsChecked = Properties.Settings.Default.EnableStartSound;
                EnableEndSoundCheckBox.IsChecked = Properties.Settings.Default.EnableEndSound;
                StartSoundPathText.Text = string.IsNullOrEmpty(Properties.Settings.Default.StartSoundPath) ? "未选择" : Properties.Settings.Default.StartSoundPath;
                EndSoundPathText.Text = string.IsNullOrEmpty(Properties.Settings.Default.EndSoundPath) ? "未选择" : Properties.Settings.Default.EndSoundPath;

                // Image
                EnableImageOverlayCheckBox.IsChecked = Properties.Settings.Default.EnableImageOverlay;
                ImagePathText.Text = string.IsNullOrEmpty(Properties.Settings.Default.ImagePath) ? "未选择" : Properties.Settings.Default.ImagePath;
                ImageFillModeComboBox.SelectedIndex = Properties.Settings.Default.ImageFillMode;
                ImageOpacitySlider.Value = Properties.Settings.Default.ImageOpacity;

                // Text
                EnableTextOverlayCheckBox.IsChecked = Properties.Settings.Default.EnableTextOverlay;
                OverlayTextBox.Text = Properties.Settings.Default.OverlayText;
                TextFontSizeTextBox.Text = Properties.Settings.Default.TextFontSize.ToString();

                // For color, find matching item
                string savedColor = Properties.Settings.Default.TextColor;
                foreach (System.Windows.Controls.ComboBoxItem item in TextColorComboBox.Items)
                {
                    if (item.Tag.ToString() == savedColor)
                    {
                        TextColorComboBox.SelectedItem = item;
                        break;
                    }
                }

                // Effects
                EnableAdvancedEffectsCheckBox.IsChecked = Properties.Settings.Default.EnableAdvancedEffects;
                EffectTypeComboBox.SelectedIndex = Properties.Settings.Default.EffectType;
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
            // General
            Properties.Settings.Default.DimOpacity = OpacitySlider.Value;
            Properties.Settings.Default.Threshold = (int)ThresholdSlider.Value;
            Properties.Settings.Default.IsMute = MuteCheckBox.IsChecked ?? true;
            Properties.Settings.Default.dim_flag = DimCheckBox.IsChecked ?? true;

            // Audio
            Properties.Settings.Default.EnableStartSound = EnableStartSoundCheckBox.IsChecked ?? false;
            Properties.Settings.Default.EnableEndSound = EnableEndSoundCheckBox.IsChecked ?? false;
            Properties.Settings.Default.StartSoundPath = StartSoundPathText.Text == "未选择" ? "" : StartSoundPathText.Text;
            Properties.Settings.Default.EndSoundPath = EndSoundPathText.Text == "未选择" ? "" : EndSoundPathText.Text;

            // Image
            Properties.Settings.Default.EnableImageOverlay = EnableImageOverlayCheckBox.IsChecked ?? false;
            Properties.Settings.Default.ImagePath = ImagePathText.Text == "未选择" ? "" : ImagePathText.Text;
            Properties.Settings.Default.ImageFillMode = ImageFillModeComboBox.SelectedIndex;
            Properties.Settings.Default.ImageOpacity = ImageOpacitySlider.Value;

            // Text
            Properties.Settings.Default.EnableTextOverlay = EnableTextOverlayCheckBox.IsChecked ?? false;
            Properties.Settings.Default.OverlayText = OverlayTextBox.Text;
            if (double.TryParse(TextFontSizeTextBox.Text, out double fontSize))
            {
                Properties.Settings.Default.TextFontSize = fontSize;
            }
            if (TextColorComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem colorItem && colorItem.Tag != null)
            {
                Properties.Settings.Default.TextColor = colorItem.Tag.ToString();
            }

            // Effects
            Properties.Settings.Default.EnableAdvancedEffects = EnableAdvancedEffectsCheckBox.IsChecked ?? false;
            Properties.Settings.Default.EffectType = EffectTypeComboBox.SelectedIndex;

            // 真正写入硬盘持久化存储
            Properties.Settings.Default.Save();

            // 同时更新 Main 类中正在运行的静态变量，实现“即时生效”
            Main.DIM_OPACITY = Properties.Settings.Default.DimOpacity;
            Main.DIFF_ENERGY_THRESHOLD = Properties.Settings.Default.Threshold;
            Main.isMute = Properties.Settings.Default.IsMute;
            Main.dim_flag = Properties.Settings.Default.dim_flag;

            // 提示并关闭窗口
            System.Windows.MessageBox.Show("配置已更新！", "Pause Everywhere", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();
        }

        public void SelectStartSound_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "Audio Files|*.wav;*.mp3|All Files|*.*";
            if (openFileDialog.ShowDialog() == true)
            {
                StartSoundPathText.Text = openFileDialog.FileName;
            }
        }

        public void SelectEndSound_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "Audio Files|*.wav;*.mp3|All Files|*.*";
            if (openFileDialog.ShowDialog() == true)
            {
                EndSoundPathText.Text = openFileDialog.FileName;
            }
        }

        public void SelectImage_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "Image Files|*.jpg;*.jpeg;*.png|All Files|*.*";
            if (openFileDialog.ShowDialog() == true)
            {
                ImagePathText.Text = openFileDialog.FileName;
            }
        }
    }
}
