// ============================================================
// RTTStamp.cs - J-Link RTT 日志查看器（每行带主机时间戳）WPF 版
// 单文件 C#，Windows 自带 csc.exe 编译（引用 GAC 中的 WPF 程序集），零安装。
// RTT/DLL 连接逻辑与 WinForms 版一致（实测 V9.66）。
// ============================================================
// 分区:
//   1. 程序集版本信息
//   2. J-Link DLL 接口
//   3. 配置读写 / 日志消息 / 主题
//   4. 通用工具 (版本/构建时间/级别解析)
//   5. 资源 XAML (现代样式: 药丸按钮/无边框输入框/窗口按钮/日志行模板)
//   6. 主窗口 MainWindow (RTT 连接与后台读取 + 日志列表 + 状态栏)
//   7. 设置窗口 SettingsWindow
//   8. 快捷键窗口 ShortcutsWindow
//   9. 关于窗口 AboutWindow
//  10. 程序入口
// 编译: 见 build.bat
// ============================================================

// ===== 1. 程序集版本信息 =====
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Globalization;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;

[assembly: AssemblyTitle("RTTStamp")]
[assembly: AssemblyDescription("J-Link RTT 日志查看器（带时间戳）")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// ===== 2. J-Link DLL 接口 =====
static class JLinkDll
{
    public const string DefaultDir = @"C:\Program Files\SEGGER\JLink_V966";
    public static string DllDir = DefaultDir;

    [DllImport("kernel32", SetLastError = true)]
    public static extern bool SetDllDirectory(string path);

    [DllImport("JLink_x64.dll")] public static extern int JLINK_GetDLLVersion();
    [DllImport("JLink_x64.dll")] public static extern int JLINK_Open();
    [DllImport("JLink_x64.dll", EntryPoint = "JLINK_ExecCommand", CharSet = CharSet.Ansi)]
    public static extern int JLINK_ExecCommand(string cmd, byte[] reply, int replyLen);
    [DllImport("JLink_x64.dll")] public static extern int JLINK_TIF_Select(int tif);
    [DllImport("JLink_x64.dll")] public static extern int JLINK_SetSpeed(int kHz);
    [DllImport("JLink_x64.dll")] public static extern int JLINK_Connect();
    [DllImport("JLink_x64.dll")] public static extern int JLINK_IsConnected();
    [DllImport("JLink_x64.dll")] public static extern int JLINK_IsHalted();
    [DllImport("JLink_x64.dll")] public static extern int JLINK_Go();
    [DllImport("JLink_x64.dll")] public static extern int JLINK_Close();
    [DllImport("JLink_x64.dll")] public static extern int JLINK_RTTERMINAL_Control(int cmd, IntPtr p);
    [DllImport("JLink_x64.dll")] public static extern int JLINK_RTTERMINAL_Read(int channel, byte[] buf, int size);

    public static int Exec(string s)
    {
        byte[] reply = new byte[256];
        return JLINK_ExecCommand(s, reply, reply.Length);
    }

    public static string DllVersionText()
    {
        try { return (JLINK_GetDLLVersion() / 10000.0).ToString("F2"); }
        catch { return "?"; }
    }
}

// ===== 3. 配置读写 / 日志消息 / 主题 =====
public class Msg
{
    public DateTime Ts;
    public string Text;
    public bool Sys;
}

public class Settings
{
    public string Device = "GD32F103CBT6";
    public string IfName = "SWD";
    public int Speed = 4000;
    public int Channel = 0;
    public string ProbeUsb = "";
    public string ProbeIp = "";
    public bool AutoRe = true;
    public bool StripAnsi = true;
    public bool AutoScroll = true;
    public bool RealtimeOn = false;
    public int TsFormat = 0;
    public float FontSize = 13F;
    public string FontName = "Consolas";
    public int Theme = 0;
    public string JLinkDir = "";
    public string SaveDir = "";
    public static string IniSuffix = "";

    public static string IniPath()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RTTStamp" + IniSuffix + ".ini");
    }

    public string EffectiveSaveDir()
    {
        // 目录不存在（例如项目被整体移动过）时，回退到当前软件目录下的 logs
        if (SaveDir.Length > 0 && Directory.Exists(SaveDir)) return SaveDir;
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
    }

    public void Save()
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[RTTStamp]");
            sb.AppendLine("Device=" + Device);
            sb.AppendLine("If=" + IfName);
            sb.AppendLine("Speed=" + Speed);
            sb.AppendLine("Channel=" + Channel);
            sb.AppendLine("ProbeUsb=" + ProbeUsb);
            sb.AppendLine("ProbeIp=" + ProbeIp);
            sb.AppendLine("AutoRe=" + (AutoRe ? "1" : "0"));
            sb.AppendLine("StripAnsi=" + (StripAnsi ? "1" : "0"));
            sb.AppendLine("AutoScroll=" + (AutoScroll ? "1" : "0"));
            sb.AppendLine("RealtimeOn=" + (RealtimeOn ? "1" : "0"));
            sb.AppendLine("TsFormat=" + TsFormat);
            sb.AppendLine("FontSize=" + FontSize.ToString("F1"));
            sb.AppendLine("FontName=" + FontName);
            sb.AppendLine("Theme=" + Theme);
            sb.AppendLine("JLinkDir=" + JLinkDir);
            sb.AppendLine("SaveDir=" + SaveDir);
            File.WriteAllText(IniPath(), sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }

    public static Settings Load()
    {
        Settings s = new Settings();
        try
        {
            if (!File.Exists(IniPath())) return s;
            string[] lines = File.ReadAllLines(IniPath(), Encoding.UTF8);
            foreach (string ln in lines)
            {
                int eq = ln.IndexOf('=');
                if (eq <= 0 || ln.StartsWith("[")) continue;
                string k = ln.Substring(0, eq).Trim();
                string v = ln.Substring(eq + 1).Trim();
                try
                {
                    if (k == "Device") s.Device = v;
                    else if (k == "If") s.IfName = v;
                    else if (k == "Speed") s.Speed = int.Parse(v);
                    else if (k == "Channel") s.Channel = int.Parse(v);
                    else if (k == "ProbeUsb") s.ProbeUsb = v;
                    else if (k == "ProbeIp") s.ProbeIp = v;
                    else if (k == "AutoRe") s.AutoRe = v == "1";
                    else if (k == "StripAnsi") s.StripAnsi = v == "1";
                    else if (k == "AutoScroll") s.AutoScroll = v == "1";
                    else if (k == "RealtimeOn") s.RealtimeOn = v == "1";
                    else if (k == "TsFormat") s.TsFormat = int.Parse(v);
                    else if (k == "FontSize") { float f; if (float.TryParse(v, out f) && f >= 6 && f <= 30) s.FontSize = f; }
                    else if (k == "FontName" && v.Length > 0) s.FontName = v;
                    else if (k == "Theme") s.Theme = v == "1" ? 1 : 0;
                    else if (k == "JLinkDir") s.JLinkDir = v;
                    else if (k == "SaveDir") s.SaveDir = v;
                }
                catch { }
            }
        }
        catch { }
        return s;
    }
}

public class ThemeColors
{
    public Brush WinBg, CardBg, CardBorder, LogBg, LogBorder;
    public Brush TextMain, TextSub, TsBrush, SysBrush, InfoBrush, DebugBrush, WarnBrush, ErrorBrush;
    public Brush NavBtnBg, NavBtnBorder, NavBtnFg;
    public Brush CtrlBg, CtrlBorder, CtrlFg, HoverBg, Accent, SelBg;
    public bool Dark;

    static Brush B(byte r, byte g, byte b)
    {
        SolidColorBrush s = new SolidColorBrush(Color.FromRgb(r, g, b));
        s.Freeze();
        return s;
    }

    public static ThemeColors Get(int theme)
    {
        ThemeColors c = new ThemeColors();
        c.Dark = theme == 1;
        if (c.Dark)
        {
            c.WinBg = B(14, 20, 30);
            c.CardBg = B(24, 32, 46);
            c.CardBorder = B(38, 50, 72);
            c.LogBg = B(18, 25, 38);
            c.LogBorder = B(38, 50, 72);
            c.TextMain = B(222, 230, 240);
            c.TextSub = B(138, 151, 171);
            c.TsBrush = B(100, 114, 139);
            c.SysBrush = B(248, 113, 113);
            c.InfoBrush = B(74, 222, 128);
            c.DebugBrush = B(56, 189, 248);
            c.WarnBrush = B(251, 191, 36);
            c.ErrorBrush = B(248, 113, 113);
            c.NavBtnBg = B(30, 38, 54);
            c.NavBtnBorder = B(44, 58, 82);
            c.NavBtnFg = B(222, 230, 240);
            c.CtrlBg = B(26, 34, 48);
            c.CtrlBorder = B(44, 58, 82);
            c.CtrlFg = B(222, 230, 240);
            c.HoverBg = B(36, 48, 68);
            c.Accent = B(76, 141, 255);
            c.SelBg = B(50, 66, 94);
        }
        else
        {
            c.WinBg = B(240, 245, 243);
            c.CardBg = B(255, 255, 255);
            c.CardBorder = B(226, 232, 230);
            c.LogBg = B(255, 255, 255);
            c.LogBorder = B(226, 232, 230);
            c.TextMain = B(23, 37, 43);
            c.TextSub = B(107, 122, 141);
            c.TsBrush = B(148, 163, 184);
            c.SysBrush = B(220, 38, 38);
            c.InfoBrush = B(22, 163, 74);
            c.DebugBrush = B(14, 154, 184);
            c.WarnBrush = B(217, 119, 6);
            c.ErrorBrush = B(220, 38, 38);
            c.NavBtnBg = B(245, 247, 250);
            c.NavBtnBorder = B(200, 205, 215);
            c.NavBtnFg = B(23, 37, 43);
            c.CtrlBg = B(255, 255, 255);
            c.CtrlBorder = B(201, 207, 217);
            c.CtrlFg = B(23, 37, 43);
            c.HoverBg = B(233, 237, 244);
            c.Accent = B(59, 130, 246);
            c.SelBg = B(189, 215, 238);
        }
        return c;
    }
}

// ===== 4. 通用工具 =====
public class UiUtil
{
    public static string AppVersion()
    {
        try
        {
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            return v.ToString(3);
        }
        catch { return "1.0.0"; }
    }

    public static string BuildTime()
    {
        try { return File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location).ToString("yyyy-MM-dd HH:mm"); }
        catch { return "-"; }
    }

    public static string ArchText() { return IntPtr.Size == 8 ? "Windows x64" : "Windows x86"; }

    public static string DotNetText()
    {
        try
        {
            object v = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release", null);
            if (v != null)
            {
                int rel = Convert.ToInt32(v);
                if (rel >= 528040) return ".NET Framework 4.8";
                if (rel >= 461808) return ".NET Framework 4.7.2";
                if (rel >= 460798) return ".NET Framework 4.7";
                if (rel >= 394802) return ".NET Framework 4.6.2";
            }
        }
        catch { }
        return ".NET Framework " + Environment.Version;
    }

    public static string FmtBytes(long b)
    {
        if (b < 1024) return b + " 字节";
        if (b < 1024 * 1024) return (b / 1024.0).ToString("F1") + " KB";
        return (b / (1024.0 * 1024.0)).ToString("F2") + " MB";
    }

    // 行首级别符号: 0=普通 1=INFO 2=DEBUG 3=WARN 4=ERROR
    public static int LevelKindOf(string line)
    {
        string t = line.TrimStart();
        if (t.StartsWith("E/", StringComparison.Ordinal) || t.StartsWith("[E]", StringComparison.Ordinal) ||
            t.IndexOf("ERROR", StringComparison.Ordinal) >= 0) return 4;
        if (t.StartsWith("W/", StringComparison.Ordinal) || t.StartsWith("[W]", StringComparison.Ordinal) ||
            t.IndexOf("WARN", StringComparison.Ordinal) >= 0) return 3;
        if (t.StartsWith("D/", StringComparison.Ordinal) || t.StartsWith("[D]", StringComparison.Ordinal) ||
            t.IndexOf("DEBUG", StringComparison.Ordinal) >= 0) return 2;
        if (t.StartsWith("I/", StringComparison.Ordinal) || t.StartsWith("[I]", StringComparison.Ordinal) ||
            t.IndexOf("INFO", StringComparison.Ordinal) >= 0) return 1;
        return 0;
    }

    public static string LevelTag(int kind)
    {
        switch (kind)
        {
            case 1: return "[INFO]";
            case 2: return "[DEBUG]";
            case 3: return "[WARN]";
            case 4: return "[ERROR]";
            default: return "";
        }
    }
}

// 日志行（绑定到模板）
public class LogRow
{
    public string TsText { get; set; }
    public string TagText { get; set; }
    public string BodyText { get; set; }
    public Brush TsBrush { get; set; }
    public Brush TagBrush { get; set; }
    public Brush BodyBrush { get; set; }
    public string Raw { get; set; }
}

// ===== 窗口基类：自定义无边框 + 共享 UI 工具 =====
public class BaseWin : Window
{
    protected static void ApplyChrome(Window w)
    {
        w.WindowStyle = WindowStyle.None;
        w.AllowsTransparency = true;
        w.Background = Brushes.Transparent;
        System.Windows.Shell.WindowChrome chrome = new System.Windows.Shell.WindowChrome();
        chrome.CaptionHeight = 42;
        chrome.ResizeBorderThickness = new Thickness(8);
        chrome.CornerRadius = new CornerRadius(10);
        chrome.GlassFrameThickness = new Thickness(0);
        chrome.UseAeroCaptionButtons = false;
        System.Windows.Shell.WindowChrome.SetWindowChrome(w, chrome);
    }

    protected static Brush Frozen(byte r, byte g, byte b)
    {
        SolidColorBrush s = new SolidColorBrush(Color.FromRgb(r, g, b));
        s.Freeze();
        return s;
    }

    protected static TextBlock T(string text, Brush fore, double size = 12, string font = null, bool bold = false)
    {
        TextBlock tb = new TextBlock();
        tb.Text = text;
        tb.Foreground = fore;
        tb.FontSize = size;
        tb.VerticalAlignment = VerticalAlignment.Center;
        if (font != null) tb.FontFamily = new FontFamily(font);
        if (bold) tb.FontWeight = FontWeights.Bold;
        return tb;
    }

    protected static Label L(string text, Brush fore, double size = 12)
    {
        Label l = new Label();
        l.Content = text;
        l.Foreground = fore;
        l.FontSize = size;
        l.VerticalContentAlignment = VerticalAlignment.Center;
        l.Padding = new Thickness(0);
        return l;
    }

    protected static Button Pill(string text, Brush bg, Brush fg, Brush border, double fontSize = 12)
    {
        Button b = new Button();
        b.Content = text;
        b.Style = (Style)Application.Current.Resources["PillBtn"];
        b.Background = bg;
        b.Foreground = fg;
        b.BorderBrush = border;
        b.FontSize = fontSize;
        b.Cursor = Cursors.Hand;
        return b;
    }

    // 图标 + 文字的药丸按钮（图标来自系统字体 Segoe MDL2 Assets，零资源文件）
    protected static Button IconPill(string glyph, string text, Brush bg, Brush fg, Brush border)
    {
        Button b = Pill("", bg, fg, border);
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal };
        TextBlock ico = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        TextBlock tx = new TextBlock
        {
            Text = text,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 0, 0)
        };
        sp.Children.Add(ico);
        sp.Children.Add(tx);
        b.Content = sp;
        return b;
    }

    // 用代码现画应用图标（深色圆角块 + ">_"），同时用于窗口与任务栏
    protected static ImageSource MakeAppIcon()
    {
        DrawingVisual dv = new DrawingVisual();
        DrawingContext dc = dv.RenderOpen();
        try
        {
            dc.DrawRoundedRectangle(Frozen(27, 31, 39), null, new Rect(0, 0, 32, 32), 7, 7);
            FormattedText ft = new FormattedText(
                ">_",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                13, Brushes.White);
            dc.DrawText(ft, new Point(4, 7));
        }
        finally { dc.Close(); }
        RenderTargetBitmap rtb = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}

// ===== 5. 资源 XAML =====
static class ResLib
{
    public const string Dict = @"<ResourceDictionary
                    xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Style x:Key=""PillBtn"" TargetType=""{x:Type Button}"">
    <Setter Property=""Background"" Value=""#FFFFFF""/>
    <Setter Property=""BorderBrush"" Value=""#C9CFD9""/>
    <Setter Property=""Foreground"" Value=""#1F2430""/>
    <Setter Property=""Padding"" Value=""12,5""/>
    <Setter Property=""Cursor"" Value=""Hand""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""{x:Type Button}"">
          <Border x:Name=""bd"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}""
                  BorderThickness=""1"" CornerRadius=""15"" Padding=""{TemplateBinding Padding}"">
            <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""bd"" Property=""Opacity"" Value=""0.82""/>
            </Trigger>
            <Trigger Property=""IsPressed"" Value=""True"">
              <Setter TargetName=""bd"" Property=""Opacity"" Value=""0.65""/>
            </Trigger>
            <Trigger Property=""IsEnabled"" Value=""False"">
              <Setter TargetName=""bd"" Property=""Opacity"" Value=""0.4""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key=""WinBtn"" TargetType=""{x:Type Button}"">
    <Setter Property=""Width"" Value=""42""/>
    <Setter Property=""Height"" Value=""30""/>
    <Setter Property=""Background"" Value=""Transparent""/>
    <Setter Property=""Foreground"" Value=""{DynamicResource Text.Main}""/>
    <Setter Property=""FontFamily"" Value=""Segoe MDL2 Assets""/>
    <Setter Property=""FontSize"" Value=""10""/>
    <Setter Property=""Cursor"" Value=""Hand""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""{x:Type Button}"">
          <Border x:Name=""bd"" Background=""{TemplateBinding Background}"" CornerRadius=""6"">
            <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""bd"" Property=""Background"" Value=""{DynamicResource Hover.Bg}""/>
            </Trigger>
            <Trigger Property=""IsPressed"" Value=""True"">
              <Setter TargetName=""bd"" Property=""Background"" Value=""{DynamicResource Ctrl.Border}""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key=""WinBtnClose"" TargetType=""{x:Type Button}"" BasedOn=""{StaticResource WinBtn}"">
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""{x:Type Button}"">
          <Border x:Name=""bd"" Background=""{TemplateBinding Background}"" CornerRadius=""6"">
            <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""bd"" Property=""Background"" Value=""#E81123""/>
              <Setter Property=""Foreground"" Value=""White""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType=""TextBox"">
    <Setter Property=""Background"" Value=""{DynamicResource Ctrl.Bg}""/>
    <Setter Property=""Foreground"" Value=""{DynamicResource Ctrl.Fg}""/>
    <Setter Property=""CaretBrush"" Value=""{DynamicResource Ctrl.Fg}""/>
    <Setter Property=""BorderBrush"" Value=""{DynamicResource Ctrl.Border}""/>
    <Setter Property=""SelectionBrush"" Value=""{DynamicResource Accent}""/>
    <Setter Property=""Padding"" Value=""6,3""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""{x:Type TextBox}"">
          <Border x:Name=""bd"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}""
                  BorderThickness=""1"" CornerRadius=""6"">
            <ScrollViewer x:Name=""PART_ContentHost"" Margin=""{TemplateBinding Padding}""/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsKeyboardFocusWithin"" Value=""True"">
              <Setter TargetName=""bd"" Property=""BorderBrush"" Value=""{DynamicResource Accent}""/>
            </Trigger>
            <Trigger Property=""IsEnabled"" Value=""False"">
              <Setter TargetName=""bd"" Property=""Background"" Value=""{DynamicResource Hover.Bg}""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType=""ComboBox"">
    <Setter Property=""Foreground"" Value=""{DynamicResource Ctrl.Fg}""/>
    <Setter Property=""Height"" Value=""26""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""{x:Type ComboBox}"">
          <Grid>
            <ToggleButton x:Name=""ToggleButton"" Focusable=""False"" ClickMode=""Press""
                          IsChecked=""{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"">
              <ToggleButton.Template>
                <ControlTemplate TargetType=""{x:Type ToggleButton}"">
                  <Border x:Name=""bd"" Background=""{DynamicResource Ctrl.Bg}"" BorderBrush=""{DynamicResource Ctrl.Border}""
                          BorderThickness=""1"" CornerRadius=""6"">
                    <Path Data=""M 0 0 L 4 4 L 8 0 Z"" Fill=""{DynamicResource Text.Sub}"" HorizontalAlignment=""Right""
                          VerticalAlignment=""Center"" Margin=""0,0,8,0""/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property=""IsMouseOver"" Value=""True"">
                      <Setter TargetName=""bd"" Property=""BorderBrush"" Value=""{DynamicResource Accent}""/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter Margin=""8,4,24,4"" VerticalAlignment=""Center"" HorizontalAlignment=""Left"" IsHitTestVisible=""False""
                              Content=""{TemplateBinding SelectionBoxItem}""
                              ContentTemplate=""{TemplateBinding SelectionBoxItemTemplate}""
                              ContentTemplateSelector=""{TemplateBinding ItemTemplateSelector}""/>
            <TextBox x:Name=""PART_EditableTextBox"" Visibility=""Hidden"" Margin=""8,3,24,3"" VerticalAlignment=""Center""
                     BorderThickness=""0"" Background=""Transparent"" Padding=""0""
                     Foreground=""{DynamicResource Ctrl.Fg}"" CaretBrush=""{DynamicResource Ctrl.Fg}""/>
            <Popup x:Name=""PART_Popup"" AllowsTransparency=""True"" Placement=""Bottom"" PopupAnimation=""Slide""
                   IsOpen=""{TemplateBinding IsDropDownOpen}"">
              <Border CornerRadius=""8"" Background=""{DynamicResource Card.Bg}"" BorderBrush=""{DynamicResource Ctrl.Border}""
                      BorderThickness=""1"" Margin=""0,4,0,0"" MinWidth=""{TemplateBinding ActualWidth}"">
                <ScrollViewer MaxHeight=""220"" VerticalScrollBarVisibility=""Auto"">
                  <ItemsPresenter KeyboardNavigation.DirectionalNavigation=""Contained""/>
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsEditable"" Value=""True"">
              <Setter TargetName=""PART_EditableTextBox"" Property=""Visibility"" Value=""Visible""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Setter Property=""ItemContainerStyle"">
      <Setter.Value>
        <Style TargetType=""{x:Type ComboBoxItem}"">
          <Setter Property=""Template"">
            <Setter.Value>
              <ControlTemplate TargetType=""{x:Type ComboBoxItem}"">
                <Border x:Name=""bd"" Padding=""10,5"" CornerRadius=""4"" Background=""Transparent"">
                  <ContentPresenter VerticalAlignment=""Center""/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property=""IsMouseOver"" Value=""True"">
                    <Setter TargetName=""bd"" Property=""Background"" Value=""{DynamicResource Hover.Bg}""/>
                  </Trigger>
                  <Trigger Property=""IsSelected"" Value=""True"">
                    <Setter TargetName=""bd"" Property=""Background"" Value=""{DynamicResource Hover.Bg}""/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
        </Style>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType=""CheckBox"">
    <Setter Property=""Foreground"" Value=""{DynamicResource Text.Main}""/>
    <Setter Property=""Cursor"" Value=""Hand""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""{x:Type CheckBox}"">
          <StackPanel Orientation=""Horizontal"" VerticalAlignment=""Center"">
            <Border x:Name=""bd"" Width=""15"" Height=""15"" CornerRadius=""4"" VerticalAlignment=""Center""
                    Background=""{DynamicResource Ctrl.Bg}"" BorderBrush=""{DynamicResource Ctrl.Border}"" BorderThickness=""1"">
              <Path x:Name=""chk"" Data=""M 2 7 L 6 11 L 12 3"" Stroke=""White"" StrokeThickness=""2"" Visibility=""Collapsed""
                    StrokeStartLineCap=""Round"" StrokeEndLineCap=""Round""/>
            </Border>
            <ContentPresenter Margin=""6,0,0,0"" VerticalAlignment=""Center"" RecognizesAccessKey=""False""/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsChecked"" Value=""True"">
              <Setter TargetName=""bd"" Property=""Background"" Value=""{DynamicResource Accent}""/>
              <Setter TargetName=""bd"" Property=""BorderBrush"" Value=""{DynamicResource Accent}""/>
              <Setter TargetName=""chk"" Property=""Visibility"" Value=""Visible""/>
            </Trigger>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""bd"" Property=""BorderBrush"" Value=""{DynamicResource Accent}""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <DataTemplate x:Key=""LogRowTpl"">
    <Grid>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width=""Auto""/>
        <ColumnDefinition Width=""Auto""/>
        <ColumnDefinition Width=""*""/>
      </Grid.ColumnDefinitions>
      <TextBlock Grid.Column=""0"" Text=""{Binding TsText}"" Foreground=""{Binding TsBrush}"" FontFamily=""Consolas"" Padding=""8,0,12,0""/>
      <TextBlock Grid.Column=""1"" Text=""{Binding TagText}"" Foreground=""{Binding TagBrush}"" FontFamily=""Consolas"" FontWeight=""Bold"" Padding=""0,0,12,0""/>
      <TextBlock Grid.Column=""2"" Text=""{Binding BodyText}"" Foreground=""{Binding BodyBrush}"" FontFamily=""Consolas""/>
    </Grid>
  </DataTemplate>
  <Style x:Key=""LogItemStyle"" TargetType=""{x:Type ListBoxItem}"">
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""{x:Type ListBoxItem}"">
          <Border x:Name=""bd"" Background=""Transparent"" SnapsToDevicePixels=""True"">
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""bd"" Property=""Background"" Value=""{DynamicResource Hover.Bg}""/>
            </Trigger>
            <Trigger Property=""IsSelected"" Value=""True"">
              <Setter TargetName=""bd"" Property=""Background"" Value=""{DynamicResource Sel.Bg}""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";
}

// ===== 6. 主窗口 =====
public class MainWindow : BaseWin
{
    Settings S;
    ThemeColors tc;      // 窗口界面配色（固定浅色，保证可读性）
    ThemeColors tcLog;   // 日志区配色（跟随主题设置）

    // 控件
    Border rootBorder;
    ComboBox cmbDevice, cmbIf, cmbSpeed, cmbTs;
    CheckBox chkAutoRe, chkAdv, chkAnsi, chkScroll, chkRealtime;
    TextBox txtUsb, txtIp, txtChannel, txtDir, txtFind;
    Label lblUsb, lblIp, lblCh, lblFindMsg;
    StackPanel advPanel, findBar;
    Button btnConnect, btnClear, btnExport, btnFind, btnBrowseDir;
    Button btnMin, btnMax, btnClose, btnSettings, btnKeys, btnAbout;
    RichTextBox rtb;
    Border logBorder, stBorder;
    Label lblDev, lblIf2, lblSpeed, lblTs2, lblDirTip, lblFindTip;
    int findAnchorIdx = -1;
    TextBlock tbConn, tbDll, tbBytes, tbLines, tbClock, tbVer;
    System.Windows.Shapes.Ellipse dotConn;

    // 运行状态
    Thread worker;
    volatile bool stopReq;
    volatile string workerStatus = "未连接";
    ConcurrentQueue<Msg> q = new ConcurrentQueue<Msg>();
    List<Msg> session = new List<Msg>();
    long bytesTotal, lineCount;
    string dllVer = "?";
    DateTime connStart;
    StreamWriter fileWriter;
    object lastRow;
    bool atBottom = true;
    string wDev, wIf, wUsb, wIp;
    int wSpeed, wCh;
    bool wAutoRe, wStripAnsi;
    static Regex ansiRx = new Regex("\u001B\\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);
    static string[] DevicePresets = new string[] {
        "GD32F103CBT6", "APM32E103CE", "GD32F303CR", "STM32F303"
    };
    const string ConnectText = "▶  连接";
    const string DisconnectText = "■  断开";
    static readonly Brush BrOk = Frozen(34, 165, 89);
    static readonly Brush BrStop = Frozen(204, 65, 65);
    static readonly Brush BrWarn = Frozen(214, 158, 46);

    public MainWindow()
    {
        S = Settings.Load();
        ResolveJLinkDir();
        JLinkDll.SetDllDirectory(JLinkDll.DllDir);
        dllVer = JLinkDll.DllVersionText();
        tc = ThemeColors.Get(S.Theme);
        tcLog = tc;

        Title = "RTTStamp v" + UiUtil.AppVersion() + " - J-Link RTT 日志查看器"
            + (Settings.IniSuffix.Length > 0 ? "  [" + Settings.IniSuffix.TrimStart('_') + "]" : "");
        Width = 1060; Height = 700; MinWidth = 880; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 12;
        Icon = MakeAppIcon();
        ApplyChrome(this);
        Icon = MakeAppIcon();
        BuildUI();
        ApplyTheme();
        LoadInitialValues();
        HookLiveParams();
        PushSys("RTTStamp v" + UiUtil.AppVersion() + " 启动 | J-Link DLL " + dllVer + " | " + JLinkDll.DllDir);

        DispatcherTimer t = new DispatcherTimer();
        t.Interval = TimeSpan.FromMilliseconds(30);
        t.Tick += new EventHandler(OnTick);
        t.Start();
    }

    void LoadInitialValues()
    {
        cmbDevice.Text = S.Device;
        cmbIf.SelectedItem = S.IfName == "JTAG" ? "JTAG" : "SWD";
        cmbSpeed.Text = S.Speed.ToString();
        txtChannel.Text = S.Channel.ToString();
        txtUsb.Text = S.ProbeUsb;
        txtIp.Text = S.ProbeIp;
        chkAutoRe.IsChecked = S.AutoRe;
        chkAnsi.IsChecked = S.StripAnsi;
        chkScroll.IsChecked = S.AutoScroll;
        chkRealtime.IsChecked = S.RealtimeOn;
        txtDir.Text = S.EffectiveSaveDir();
        cmbTs.SelectedIndex = Math.Max(0, Math.Min(2, S.TsFormat));
        rtb.Document.FontSize = S.FontSize;
        advPanel.Visibility = Visibility.Collapsed;
    }

    // ---------- J-Link 目录定位 ----------
    public static bool IsValidJLinkDir(string d)
    {
        if (d == null || d.Length == 0) return false;
        try { return File.Exists(Path.Combine(d, "JLink_x64.dll")); }
        catch { return false; }
    }

    static string FindJLinkDirFromRegistry()
    {
        try
        {
            string[] keys = new string[] {
                @"HKEY_LOCAL_MACHINE\SOFTWARE\SEGGER\JLink",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\SEGGER\JLink",
                @"HKEY_CURRENT_USER\SOFTWARE\SEGGER\JLink"
            };
            foreach (string k in keys)
            {
                object v = Registry.GetValue(k, "InstallPath", null);
                if (v != null && IsValidJLinkDir(v.ToString())) return v.ToString();
            }
        }
        catch { }
        return null;
    }

    internal static string AskJLinkDir(Window owner)
    {
        Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
        dlg.Title = "选择 J-Link 安装目录下的 JLink_x64.dll";
        dlg.Filter = "JLink_x64.dll|JLink_x64.dll|所有文件 (*.*)|*.*";
        try { dlg.InitialDirectory = JLinkDll.DefaultDir; } catch { }
        bool? ok = dlg.ShowDialog(owner);
        if (ok == true)
        {
            string dir = Path.GetDirectoryName(dlg.FileName);
            if (IsValidJLinkDir(dir)) return dir;
            MessageBox.Show(owner, "所选目录中没有 JLink_x64.dll", "RTTStamp");
        }
        return null;
    }

    void ResolveJLinkDir()
    {
        string dir = null;
        if (S.JLinkDir.Length > 0 && IsValidJLinkDir(S.JLinkDir)) dir = S.JLinkDir;
        if (dir == null) dir = FindJLinkDirFromRegistry();
        if (dir == null && IsValidJLinkDir(JLinkDll.DefaultDir)) dir = JLinkDll.DefaultDir;
        if (dir == null)
        {
            MessageBox.Show("未找到 J-Link 软件安装目录（需要其中的 JLink_x64.dll）。\r\n\r\n" +
                "点击确定后请手动选择 JLink_x64.dll 所在的目录（即 J-Link 软件包安装目录）。",
                "RTTStamp", MessageBoxButton.OK, MessageBoxImage.Information);
            dir = AskJLinkDir(null);
            if (dir == null) dir = JLinkDll.DefaultDir;
        }
        JLinkDll.DllDir = dir;
        if (S.JLinkDir != dir) { S.JLinkDir = dir; S.Save(); }
    }

    // ---------- 小工具 ----------
    static ComboBox Combo(bool editable)
    {
        ComboBox c = new ComboBox();
        c.IsEditable = editable;
        c.VerticalContentAlignment = VerticalAlignment.Center;
        return c;
    }

    // ---------- 界面构建 ----------
    void BuildUI()
    {
        rootBorder = new Border();
        rootBorder.CornerRadius = new CornerRadius(10);
        rootBorder.BorderThickness = new Thickness(1);
        rootBorder.Margin = new Thickness(10);
        rootBorder.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black };
        rootBorder.Child = mainGrid;
        Content = rootBorder;

        // 窗口大小变化时处理圆角/阴影（最大化时贴边）
        StateChanged += new EventHandler(delegate
        {
            if (WindowState == WindowState.Maximized)
            {
                rootBorder.Margin = new Thickness(0);
                rootBorder.CornerRadius = new CornerRadius(0);
                rootBorder.BorderThickness = new Thickness(0);
            }
            else
            {
                rootBorder.Margin = new Thickness(10);
                rootBorder.CornerRadius = new CornerRadius(10);
                rootBorder.BorderThickness = new Thickness(1);
            }
        });
    }

    Grid mainGrid
    {
        get
        {
            Grid g = new Grid();
            for (int i = 0; i < 8; i++) g.RowDefinitions.Add(new RowDefinition());
            g.RowDefinitions[0].Height = new GridLength(42);                  // 标题栏
            g.RowDefinitions[1].Height = GridLength.Auto;                     // 配置行1
            g.RowDefinitions[2].Height = GridLength.Auto;                     // 配置行2(时间戳)
            g.RowDefinitions[3].Height = GridLength.Auto;                     // 高级(折叠)
            g.RowDefinitions[4].Height = GridLength.Auto;                     // 工具栏
            g.RowDefinitions[5].Height = GridLength.Auto;                     // 查找栏
            g.RowDefinitions[6].Height = new GridLength(1, GridUnitType.Star);// 日志
            g.RowDefinitions[7].Height = GridLength.Auto;                     // 状态栏

            g.Children.Add(BuildTitleBar());
            Grid.SetRow(titleBar, 0);
            Grid cfg = BuildConfigRow();
            g.Children.Add(cfg); Grid.SetRow(cfg, 1);
            Grid ts = BuildTsRow();
            g.Children.Add(ts); Grid.SetRow(ts, 2);
            advPanel = BuildAdvRow();
            g.Children.Add(advPanel); Grid.SetRow(advPanel, 3);
            Grid bar = BuildToolbar();
            g.Children.Add(bar); Grid.SetRow(bar, 4);
            findBar = BuildFindBar();
            findBar.Visibility = Visibility.Collapsed;
            g.Children.Add(findBar); Grid.SetRow(findBar, 5);
            Border logB = BuildLogArea();
            g.Children.Add(logB); Grid.SetRow(logB, 6);
            Border st = BuildStatusBar();
            g.Children.Add(st); Grid.SetRow(st, 7);
            return g;
        }
    }

    Grid titleBar;
    Grid BuildTitleBar()
    {
        Grid g = new Grid();
        titleBar = g;

        StackPanel left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Border logo = new Border();
        logo.Width = 24; logo.Height = 24;
        logo.CornerRadius = new CornerRadius(6);
        logo.Background = Frozen(27, 31, 39);
        logo.Child = T(">_", Brushes.White, 10, "Consolas", true);
        logo.VerticalAlignment = VerticalAlignment.Center;
        left.Children.Add(logo);
        TextBlock tt = T(Title, tc.TextMain, 13, null, true);
        tt.Tag = "title";
        tt.Margin = new Thickness(9, 0, 0, 0);
        left.Children.Add(tt);
        g.Children.Add(left);

        StackPanel right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        btnSettings = IconPill("", "设置", tc.NavBtnBg, tc.NavBtnFg, tc.NavBtnBorder);
        btnKeys = IconPill("", "快捷键", tc.NavBtnBg, tc.NavBtnFg, tc.NavBtnBorder);
        btnAbout = IconPill("", "关于", tc.NavBtnBg, tc.NavBtnFg, tc.NavBtnBorder);
        btnSettings.Click += new RoutedEventHandler(delegate { ShowSettings(); });
        btnKeys.Click += new RoutedEventHandler(delegate { ShortcutsWindow sw = new ShortcutsWindow(); sw.Owner = this; sw.ShowDialog(); });
        btnAbout.Click += new RoutedEventHandler(delegate { AboutWindow aw = new AboutWindow(dllVer); aw.Owner = this; aw.ShowDialog(); });
        foreach (Button b in new Button[] { btnSettings, btnKeys, btnAbout })
        {
            b.Margin = new Thickness(4, 0, 0, 0);
            right.Children.Add(b);
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        }
        btnMin = WinCtl("\uE921", "WinBtn", delegate { WindowState = WindowState.Minimized; });
        btnMax = WinCtl("\uE922", "WinBtn", delegate
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        });
        btnClose = WinCtl("\uE8BB", "WinBtnClose", delegate { Close(); });
        right.Children.Add(btnMin); right.Children.Add(btnMax); right.Children.Add(btnClose);
        g.Children.Add(right);
        return g;
    }

    Button WinCtl(string glyph, string styleKey, RoutedEventHandler onClick)
    {
        Button b = new Button();
        b.Content = glyph;
        b.Style = (Style)Application.Current.Resources[styleKey];
        b.Click += onClick;
        b.Margin = new Thickness(3, 0, 0, 0);
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        return b;
    }

    Grid BuildConfigRow()
    {
        Grid g = new Grid();
        g.Margin = new Thickness(10, 2, 10, 2);
        ColumnDefinition c0 = new ColumnDefinition(); c0.Width = GridLength.Auto;
        ColumnDefinition c1 = new ColumnDefinition(); c1.Width = new GridLength(150);
        ColumnDefinition c2 = new ColumnDefinition(); c2.Width = GridLength.Auto;
        ColumnDefinition c3 = new ColumnDefinition(); c3.Width = new GridLength(80);
        ColumnDefinition c4 = new ColumnDefinition(); c4.Width = GridLength.Auto;
        ColumnDefinition c5 = new ColumnDefinition(); c5.Width = new GridLength(80);
        ColumnDefinition c6 = new ColumnDefinition(); c6.Width = GridLength.Auto;
        ColumnDefinition c7 = new ColumnDefinition(); c7.Width = GridLength.Auto;
        ColumnDefinition c8 = new ColumnDefinition(); c8.Width = new GridLength(1, GridUnitType.Star);
        g.ColumnDefinitions.Add(c0); g.ColumnDefinitions.Add(c1); g.ColumnDefinitions.Add(c2);
        g.ColumnDefinitions.Add(c3); g.ColumnDefinitions.Add(c4); g.ColumnDefinitions.Add(c5);
        g.ColumnDefinitions.Add(c6); g.ColumnDefinitions.Add(c7); g.ColumnDefinitions.Add(c8);

        Label l1 = L("设备:", tc.TextSub); l1.Margin = new Thickness(0, 0, 8, 0); lblDev = l1; Grid.SetColumn(l1, 0); g.Children.Add(l1);
        cmbDevice = Combo(true); Grid.SetColumn(cmbDevice, 1);
        foreach (string d in DevicePresets) cmbDevice.Items.Add(d);
        g.Children.Add(cmbDevice);
        Label l2 = L("接口:", tc.TextSub); l2.Margin = new Thickness(14, 0, 8, 0); lblIf2 = l2; Grid.SetColumn(l2, 2); g.Children.Add(l2);
        cmbIf = Combo(false); Grid.SetColumn(cmbIf, 3);
        cmbIf.Items.Add("SWD"); cmbIf.Items.Add("JTAG");
        g.Children.Add(cmbIf);
        Label l3 = L("速率kHz:", tc.TextSub); l3.Margin = new Thickness(14, 0, 8, 0); lblSpeed = l3; Grid.SetColumn(l3, 4); g.Children.Add(l3);
        cmbSpeed = Combo(true); Grid.SetColumn(cmbSpeed, 5);
        foreach (string sp in new string[] { "1000", "2000", "4000", "8000", "10000" }) cmbSpeed.Items.Add(sp);
        g.Children.Add(cmbSpeed);
        chkAutoRe = new CheckBox { Content = "断线自动重连", VerticalContentAlignment = VerticalAlignment.Center };
        chkAutoRe.Margin = new Thickness(14, 0, 0, 0);
        chkAutoRe.Foreground = tc.TextMain;
        chkAutoRe.ToolTip = "目标板复位/掉电后每 2 秒自动重连 J-Link";
        Grid.SetColumn(chkAutoRe, 6); g.Children.Add(chkAutoRe);
        return g;
    }

    Grid BuildTsRow()
    {
        Grid g = new Grid();
        g.Margin = new Thickness(10, 4, 10, 2);
        ColumnDefinition c0 = new ColumnDefinition(); c0.Width = GridLength.Auto;
        ColumnDefinition c1 = new ColumnDefinition(); c1.Width = new GridLength(1, GridUnitType.Star);
        ColumnDefinition c2 = new ColumnDefinition(); c2.Width = GridLength.Auto;
        g.ColumnDefinitions.Add(c0); g.ColumnDefinitions.Add(c1); g.ColumnDefinitions.Add(c2);

        Label l = L("时间戳:", tc.TextSub); l.Margin = new Thickness(0, 0, 8, 0); lblTs2 = l; Grid.SetColumn(l, 0); g.Children.Add(l);
        cmbTs = Combo(false); Grid.SetColumn(cmbTs, 1);
        cmbTs.Items.Add("年-月-日 时:分:秒.毫秒 (1970-01-01 01:01:01.001)");
        cmbTs.Items.Add("时:分:秒.毫秒 (01:01:01.001)");
        cmbTs.Items.Add("相对连接秒数 (+12.345s)");
        g.Children.Add(cmbTs);
        chkAdv = new CheckBox { Content = "高级选项", VerticalContentAlignment = VerticalAlignment.Center };
        chkAdv.Margin = new Thickness(14, 0, 4, 0);
        chkAdv.Foreground = tc.TextMain;
        chkAdv.Checked += new RoutedEventHandler(delegate { advPanel.Visibility = Visibility.Visible; });
        chkAdv.Unchecked += new RoutedEventHandler(delegate { advPanel.Visibility = Visibility.Collapsed; });
        Grid.SetColumn(chkAdv, 2); g.Children.Add(chkAdv);
        return g;
    }

    StackPanel BuildAdvRow()
    {
        StackPanel p = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 2, 10, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        chkAnsi = new CheckBox { Content = "剥离ANSI颜色码", VerticalContentAlignment = VerticalAlignment.Center, Foreground = tc.TextMain, Margin = new Thickness(0, 0, 16, 0) };
        chkAnsi.ToolTip = "固件日志常带 ANSI 颜色序列(如 ESC[36;22m)，勾选后剥掉只显示纯文本；取消则原样显示";
        p.Children.Add(chkAnsi);
        chkScroll = new CheckBox { Content = "自动滚动", VerticalContentAlignment = VerticalAlignment.Center, Foreground = tc.TextMain, Margin = new Thickness(0, 0, 16, 0) };
        p.Children.Add(chkScroll);
        lblUsb = L("USB序列号:", tc.TextSub); p.Children.Add(lblUsb);
        txtUsb = new TextBox { Width = 120, Margin = new Thickness(4, 0, 12, 0) };
        txtUsb.ToolTip = "同一电脑插多台 J-Link 时，填要使用的那台的序列号(SN)；单台留空";
        p.Children.Add(txtUsb);
        lblIp = L("IP地址:", tc.TextSub); p.Children.Add(lblIp);
        txtIp = new TextBox { Width = 120, Margin = new Thickness(4, 0, 12, 0) };
        txtIp.ToolTip = "使用网络 J-Link(远程调试)时填 IP 地址，USB 直连留空";
        p.Children.Add(txtIp);
        lblCh = L("通道:", tc.TextSub); p.Children.Add(lblCh);
        txtChannel = new TextBox { Width = 40, Margin = new Thickness(4, 0, 0, 0) };
        txtChannel.ToolTip = "RTT 逻辑通道号(0~15)，固件 printf 默认走通道 0，一般不用改";
        p.Children.Add(txtChannel);
        return p;
    }

    Grid BuildToolbar()
    {
        Grid g = new Grid();
        g.Margin = new Thickness(10, 6, 10, 4);
        for (int i = 0; i < 9; i++)
        {
            ColumnDefinition c = new ColumnDefinition();
            c.Width = GridLength.Auto;
            g.ColumnDefinitions.Add(c);
        }
        ColumnDefinition stretch = new ColumnDefinition();
        stretch.Width = new GridLength(1, GridUnitType.Star);
        g.ColumnDefinitions[7] = stretch;

        btnConnect = Pill(ConnectText, BrOk, Brushes.White, Brushes.Transparent, 13);
        btnConnect.Padding = new Thickness(18, 6, 18, 6);
        btnConnect.Click += new RoutedEventHandler(OnConnectClick);
        g.Children.Add(btnConnect);

        btnClear = Pill("🗑  清空", tc.CardBg, tc.TextMain, tc.CardBorder);
        btnClear.Click += new RoutedEventHandler(delegate { ClearLog(); });
        btnClear.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(btnClear, 1);
        g.Children.Add(btnClear);

        btnExport = Pill("💾  保存日志", tc.CardBg, tc.TextMain, tc.CardBorder);
        btnExport.Click += new RoutedEventHandler(delegate { ExportLog(); });
        btnExport.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(btnExport, 2);
        g.Children.Add(btnExport);

        btnFind = Pill("🔍  查找", tc.CardBg, tc.TextMain, tc.CardBorder);
        btnFind.Click += new RoutedEventHandler(delegate
        {
            findBar.Visibility = findBar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (findBar.Visibility == Visibility.Visible) txtFind.Focus();
        });
        btnFind.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(btnFind, 3);
        g.Children.Add(btnFind);

        chkRealtime = new CheckBox { Content = "实时保存", VerticalContentAlignment = VerticalAlignment.Center, Foreground = tc.TextMain };
        chkRealtime.Margin = new Thickness(14, 0, 0, 0);
        chkRealtime.Checked += new RoutedEventHandler(delegate
        {
            if (IsLoaded && fileWriter == null) OpenWriter();
        });
        chkRealtime.Unchecked += new RoutedEventHandler(delegate
        {
            if (IsLoaded && fileWriter != null) { CloseWriter(); PushSys("实时保存已关闭"); }
        });
        chkRealtime.ToolTip = "开启后每条日志实时写入目录里的 real_log_*.log，清空窗口不影响";
        Grid.SetColumn(chkRealtime, 4); g.Children.Add(chkRealtime);

        Label ldir = L("目录:", tc.TextSub); ldir.Margin = new Thickness(14, 0, 8, 0); lblDirTip = ldir;
        Grid.SetColumn(ldir, 5); g.Children.Add(ldir);
        txtDir = new TextBox(); Grid.SetColumn(txtDir, 7);
        txtDir.ToolTip = "如需更改默认目录：设置 → 日志设置（默认为本程序目录下的 logs）";
        g.Children.Add(txtDir);
        btnBrowseDir = Pill("...", tc.CardBg, tc.TextMain, tc.CardBorder);
        btnBrowseDir.Padding = new Thickness(10, 4, 10, 4);
        btnBrowseDir.Margin = new Thickness(6, 0, 0, 0);
        btnBrowseDir.Click += new RoutedEventHandler(OnBrowseDir);
        Grid.SetColumn(btnBrowseDir, 8); g.Children.Add(btnBrowseDir);
        return g;
    }

    StackPanel BuildFindBar()
    {
        StackPanel p = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 2, 10, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        Label lf = L("查找:", tc.TextSub); lblFindTip = lf; p.Children.Add(lf);
        txtFind = new TextBox { Width = 260, Margin = new Thickness(6, 0, 0, 0) };
        p.Children.Add(txtFind);
        CheckBox caseBox = new CheckBox { Content = "区分大小写", VerticalContentAlignment = VerticalAlignment.Center, Foreground = tc.TextMain, Margin = new Thickness(12, 0, 0, 0) };
        caseBox.Tag = "case";
        p.Children.Add(caseBox);
        Button fNext = Pill("下一个", tc.CardBg, tc.TextMain, tc.CardBorder);
        fNext.Margin = new Thickness(12, 0, 0, 0);
        fNext.Click += new RoutedEventHandler(delegate { FindNext(1, caseBox); });
        p.Children.Add(fNext);
        Button fPrev = Pill("上一个", tc.CardBg, tc.TextMain, tc.CardBorder);
        fPrev.Margin = new Thickness(8, 0, 0, 0);
        fPrev.Click += new RoutedEventHandler(delegate { FindNext(-1, caseBox); });
        p.Children.Add(fPrev);
        lblFindMsg = new Label { Foreground = tc.SysBrush, Padding = new Thickness(0) };
        lblFindMsg.Margin = new Thickness(14, 0, 0, 0);
        p.Children.Add(lblFindMsg);
        return p;
    }


    CheckBox FindCaseBox(StackPanel p)
    {
        foreach (object c in p.Children)
        {
            CheckBox cb = c as CheckBox;
            if (cb != null && (string)cb.Tag == "case") return cb;
        }
        return null;
    }

    Border BuildLogArea()
    {
        Border b = new Border();
        b.Background = tc.LogBg;
        b.BorderBrush = tc.LogBorder;
        b.BorderThickness = new Thickness(1, 1, 1, 1);
        b.CornerRadius = new CornerRadius(8);
        b.Margin = new Thickness(10, 2, 10, 4);
        b.Child = rtb = new RichTextBox();
        rtb.Background = Brushes.Transparent;
        rtb.BorderThickness = new Thickness(0);
        rtb.FontFamily = new FontFamily(S.FontName.Length > 0 ? S.FontName : "Consolas");
        rtb.FontSize = S.FontSize;
        rtb.Document.PagePadding = new Thickness(8, 4, 8, 4);
        rtb.Document.PageWidth = 4000; // 页面固定较宽：长行不折行，横向滚动查看
        rtb.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        rtb.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        rtb.SelectionBrush = tc.Accent;
        rtb.SelectionOpacity = 0.4;
        rtb.PreviewMouseWheel += new MouseWheelEventHandler(OnLogWheel);
        rtb.ContextMenu = BuildLogContextMenu();
        logBorder = b;
        return b;
    }

    ContextMenu BuildLogContextMenu()
    {
        ContextMenu m = new ContextMenu();
        MenuItem miCopy = new MenuItem { Header = "复制 (Ctrl+C)" };
        miCopy.Click += new RoutedEventHandler(delegate { CopySelected(); });
        m.Items.Add(miCopy);
        MenuItem miAll = new MenuItem { Header = "全选 (Ctrl+A)" };
        miAll.Click += new RoutedEventHandler(delegate { rtb.SelectAll(); });
        m.Items.Add(miAll);
        MenuItem miFind = new MenuItem { Header = "查找 (Ctrl+F)" };
        miFind.Click += new RoutedEventHandler(delegate { ShowFind(); });
        m.Items.Add(miFind);
        m.Items.Add(new Separator());
        MenuItem miClear = new MenuItem { Header = "清空 (Ctrl+L)" };
        miClear.Click += new RoutedEventHandler(delegate { ClearLog(); });
        m.Items.Add(miClear);
        MenuItem miSave = new MenuItem { Header = "保存日志 (Ctrl+S)" };
        miSave.Click += new RoutedEventHandler(delegate { ExportLog(); });
        m.Items.Add(miSave);
        m.Items.Add(new Separator());
        MenuItem miBig = new MenuItem { Header = "字体增大 (Ctrl+滚轮)" };
        miBig.Click += new RoutedEventHandler(delegate { ZoomFont(1); });
        m.Items.Add(miBig);
        MenuItem miSmall = new MenuItem { Header = "字体减小 (Ctrl+滚轮)" };
        miSmall.Click += new RoutedEventHandler(delegate { ZoomFont(-1); });
        m.Items.Add(miSmall);
        MenuItem miReset = new MenuItem { Header = "恢复默认字号 (Ctrl+0)" };
        miReset.Click += new RoutedEventHandler(delegate { SetLogFont(13); });
        m.Items.Add(miReset);
        return m;
    }

    Border BuildStatusBar()
    {
        Border b = new Border();
        b.Background = Brushes.Transparent;
        b.BorderBrush = tc.CardBorder;
        b.BorderThickness = new Thickness(0, 1, 0, 0);
        b.Padding = new Thickness(14, 5, 14, 7);
        Grid g = new Grid();
        ColumnDefinition c0 = new ColumnDefinition(); c0.Width = GridLength.Auto;
        ColumnDefinition c1 = new ColumnDefinition(); c1.Width = GridLength.Auto;
        ColumnDefinition c2 = new ColumnDefinition(); c2.Width = GridLength.Auto;
        ColumnDefinition c3 = new ColumnDefinition(); c3.Width = GridLength.Auto;
        ColumnDefinition c4 = new ColumnDefinition(); c4.Width = new GridLength(1, GridUnitType.Star);
        ColumnDefinition c5 = new ColumnDefinition(); c5.Width = GridLength.Auto;
        ColumnDefinition c6 = new ColumnDefinition(); c6.Width = GridLength.Auto;
        g.ColumnDefinitions.Add(c0); g.ColumnDefinitions.Add(c1); g.ColumnDefinitions.Add(c2);
        g.ColumnDefinitions.Add(c3); g.ColumnDefinitions.Add(c4); g.ColumnDefinitions.Add(c5);
        g.ColumnDefinitions.Add(c6);

        dotConn = new System.Windows.Shapes.Ellipse();
        dotConn.Width = 8; dotConn.Height = 8;
        dotConn.Fill = Frozen(150, 150, 150);
        dotConn.VerticalAlignment = VerticalAlignment.Center;
        dotConn.Margin = new Thickness(0, 0, 5, 0);
        Grid.SetColumn(dotConn, 0); g.Children.Add(dotConn);
        tbConn = T("未连接", tc.TextSub, 12); Grid.SetColumn(tbConn, 1); g.Children.Add(tbConn);
        tbDll = T("J-Link DLL: " + dllVer, tc.TextSub, 12);
        tbDll.Margin = new Thickness(16, 0, 0, 0); Grid.SetColumn(tbDll, 2); g.Children.Add(tbDll);
        tbBytes = T("接收: 0 字节", tc.TextSub, 12);
        tbBytes.Margin = new Thickness(16, 0, 0, 0); Grid.SetColumn(tbBytes, 3); g.Children.Add(tbBytes);
        tbLines = T("行数: 0", tc.TextSub, 12);
        tbLines.Margin = new Thickness(16, 0, 0, 0); Grid.SetColumn(tbLines, 4); g.Children.Add(tbLines);
        tbClock = T(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), tc.TextSub, 12);
        Grid.SetColumn(tbClock, 5); g.Children.Add(tbClock);
        tbVer = T("RTTStamp v" + UiUtil.AppVersion(), tc.TextSub, 12);
        tbVer.Margin = new Thickness(16, 0, 0, 0); Grid.SetColumn(tbVer, 6); g.Children.Add(tbVer);
        stBorder = b;
        b.Child = g;
        return b;
    }

    // ---------- 主题 ----------
    void ApplyTheme()
    {
        if (rootBorder == null) return;
        tc = ThemeColors.Get(S.Theme);
        tcLog = tc;
        // 同步动态资源（输入框/下拉框/复选框的样式跟随主题）
        Application.Current.Resources["Ctrl.Bg"] = tc.CtrlBg;
        Application.Current.Resources["Ctrl.Border"] = tc.CtrlBorder;
        Application.Current.Resources["Ctrl.Fg"] = tc.CtrlFg;
        Application.Current.Resources["Text.Main"] = tc.TextMain;
        Application.Current.Resources["Text.Sub"] = tc.TextSub;
        Application.Current.Resources["Hover.Bg"] = tc.HoverBg;
        Application.Current.Resources["Card.Bg"] = tc.CardBg;
        Application.Current.Resources["Card.Border"] = tc.CardBorder;
        Application.Current.Resources["Accent"] = tc.Accent;
        Application.Current.Resources["Sel.Bg"] = tc.SelBg;
        rootBorder.Background = tc.WinBg;
        rootBorder.BorderBrush = tc.CardBorder;
        if (titleBar != null)
            foreach (object c in titleBar.Children)
            {
                StackPanel sp = c as StackPanel;
                if (sp == null) continue;
                foreach (object o in sp.Children)
                {
                    TextBlock t2 = o as TextBlock;
                    if (t2 != null && (string)t2.Tag == "title") t2.Foreground = tc.TextMain;
                }
            }
        // 日志容器（深浅主题只作用于日志显示区）
        if (logBorder != null) { logBorder.Background = tcLog.LogBg; logBorder.BorderBrush = tcLog.LogBorder; }
        if (stBorder != null) stBorder.BorderBrush = tc.CardBorder;
        if (rtb != null) { rtb.Background = tcLog.LogBg; rtb.Foreground = tcLog.TextMain; rtb.SelectionBrush = tcLog.Accent; }
        // 各行标签
        foreach (Label l in new Label[] { lblDev, lblIf2, lblSpeed, lblTs2, lblDirTip, lblFindTip, lblUsb, lblIp, lblCh })
            if (l != null) l.Foreground = tc.TextSub;
        // 复选框
        foreach (CheckBox c in new CheckBox[] { chkAutoRe, chkAdv, chkAnsi, chkScroll, chkRealtime })
            if (c != null) c.Foreground = tc.TextMain;
        // 次要药丸按钮
        foreach (Button b in new Button[] { btnClear, btnExport, btnFind, btnBrowseDir })
            if (b != null) { b.Background = tc.CardBg; b.Foreground = tc.TextMain; b.BorderBrush = tc.CardBorder; }
        // 导航按钮
        foreach (Button b in new Button[] { btnSettings, btnKeys, btnAbout })
            if (b != null) { b.Background = tc.NavBtnBg; b.Foreground = tc.NavBtnFg; b.BorderBrush = tc.NavBtnBorder; }
        // 状态栏文字
        foreach (TextBlock t in new TextBlock[] { tbConn, tbDll, tbBytes, tbLines, tbClock, tbVer })
            if (t != null) t.Foreground = tc.TextSub;
        RedrawAll();
    }

    void RedrawAll()
    {
        if (rtb == null) return;
        rtb.Document.Blocks.Clear();
        foreach (Msg m in session) rtb.Document.Blocks.Add(MakeParagraph(m));
        if (chkScroll.IsChecked == true) rtb.ScrollToEnd();
    }

    // ---------- J-Link 连接与后台读取 ----------
    // 连接中修改参数也实时同步到工作快照：剥离ANSI/自动重连立即生效，
    // 设备/接口/速率/通道/序列号/IP 在断线重连（自动或手动）时生效
    void SyncWorkerParams()
    {
        if (worker == null || !worker.IsAlive) return;
        int sp, ch;
        if (!int.TryParse(cmbSpeed.Text.Trim(), out sp) || sp <= 0) sp = 4000;
        if (!int.TryParse(txtChannel.Text.Trim(), out ch) || ch < 0) ch = 0;
        wDev = cmbDevice.Text.Trim();
        string ifSel = cmbIf.SelectedItem as string;
        if (ifSel == "SWD" || ifSel == "JTAG") wIf = ifSel;
        wSpeed = sp; wCh = ch;
        wUsb = txtUsb.Text.Trim(); wIp = txtIp.Text.Trim();
        wAutoRe = chkAutoRe.IsChecked == true;
        wStripAnsi = chkAnsi.IsChecked == true;
    }

    void HookLiveParams()
    {
        RoutedEventHandler re = new RoutedEventHandler(delegate { SyncWorkerParams(); });
        SelectionChangedEventHandler se = new SelectionChangedEventHandler(delegate { SyncWorkerParams(); });
        EventHandler te = new EventHandler(delegate { SyncWorkerParams(); });
        System.Windows.Controls.TextChangedEventHandler tce = new System.Windows.Controls.TextChangedEventHandler(delegate { SyncWorkerParams(); });
        // 可编辑下拉框的文本变化经内部 TextBox 冒泡上来的 TextChanged 路由事件捕获
        cmbDevice.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, tce);
        cmbDevice.SelectionChanged += se;
        cmbIf.SelectionChanged += se;
        cmbSpeed.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, tce);
        txtChannel.TextChanged += tce;
        txtUsb.TextChanged += tce;
        txtIp.TextChanged += tce;
        chkAutoRe.Checked += re; chkAutoRe.Unchecked += re;
        chkAnsi.Checked += re; chkAnsi.Unchecked += re;
    }

    void PushSys(string text)
    {
        Msg m = new Msg(); m.Ts = DateTime.Now; m.Text = text; m.Sys = true;
        q.Enqueue(m);
    }

    void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (worker != null && worker.IsAlive)
        {
            stopReq = true;
            workerStatus = "断开中...";
            btnConnect.Content = "断开中...";
            return;
        }
        StartWorker();
    }

    void StartWorker()
    {
        string dev = cmbDevice.Text.Trim();
        if (dev.Length == 0) { MessageBox.Show(this, "请填写设备名称", "RTTStamp"); return; }
        ReadSettingsFromUI();
        S.Save();

        wDev = dev; wIf = S.IfName; wSpeed = S.Speed; wCh = S.Channel;
        wUsb = S.ProbeUsb; wIp = S.ProbeIp;
        wAutoRe = S.AutoRe; wStripAnsi = S.StripAnsi;

        CloseWriter();
        if (chkRealtime.IsChecked == true) OpenWriter();

        bytesTotal = 0; lineCount = 0;
        connStart = DateTime.Now;
        stopReq = false;
        worker = new Thread(new ThreadStart(WorkerRun));
        worker.IsBackground = true;
        worker.Start();

        btnConnect.Content = DisconnectText;
        btnConnect.Background = BrStop;
    }

    void OpenWriter()
    {
        string dir = txtDir.Text.Trim();
        if (dir.Length == 0) dir = S.EffectiveSaveDir();
        try
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string p = Path.Combine(dir, "real_log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
            fileWriter = new StreamWriter(p, false, Encoding.UTF8);
            fileWriter.AutoFlush = true;
            PushSys("实时保存已开启: " + p);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法创建日志文件:\r\n" + ex.Message, "RTTStamp");
            fileWriter = null;
        }
    }

    void CloseWriter()
    {
        if (fileWriter != null)
        {
            try { fileWriter.Flush(); fileWriter.Close(); } catch { }
            fileWriter = null;
        }
    }

    void ReadSettingsFromUI()
    {
        int sp, ch;
        if (!int.TryParse(cmbSpeed.Text.Trim(), out sp) || sp <= 0) sp = 4000;
        if (!int.TryParse(txtChannel.Text.Trim(), out ch) || ch < 0) ch = 0;
        S.Device = cmbDevice.Text.Trim();
        S.IfName = cmbIf.SelectedItem as string ?? cmbIf.Text;
        if (S.IfName != "SWD" && S.IfName != "JTAG") S.IfName = "SWD";
        S.Speed = sp; S.Channel = ch;
        S.ProbeUsb = txtUsb.Text.Trim(); S.ProbeIp = txtIp.Text.Trim();
        S.AutoRe = chkAutoRe.IsChecked == true;
        S.StripAnsi = chkAnsi.IsChecked == true;
        S.AutoScroll = chkScroll.IsChecked == true;
        S.RealtimeOn = chkRealtime.IsChecked == true;
        S.TsFormat = cmbTs.SelectedIndex;
        // 目录以 设置→日志设置 里的配置为准，工具栏路径仅本会话使用，不写回配置
    }

    void WorkerRun()
    {
        bool rttOn = false;
        try
        {
            while (!stopReq)
            {
                workerStatus = "连接中...";
                JLinkDll.JLINK_Open();
                JLinkDll.Exec("Device = " + wDev);
                if (wUsb.Length > 0) JLinkDll.Exec("USB = " + wUsb);
                if (wIp.Length > 0) JLinkDll.Exec("IP = " + wIp);
                JLinkDll.JLINK_TIF_Select(wIf == "SWD" ? 1 : 0);
                JLinkDll.JLINK_SetSpeed(wSpeed);
                int rc = JLinkDll.JLINK_Connect();
                if (JLinkDll.JLINK_IsConnected() == 0)
                {
                    PushSys("连接失败 (Connect=" + rc + ")：请检查 J-Link USB 连接、目标板供电、调试口接线");
                    if (wAutoRe && !stopReq) { workerStatus = "2秒后重试..."; Thread.Sleep(2000); continue; }
                    break;
                }
                int halt = JLinkDll.JLINK_IsHalted();
                if (halt != 0)
                {
                    JLinkDll.JLINK_Go();
                    PushSys("目标 CPU 处于暂停状态，已恢复运行");
                }
                rttOn = false;
                if (JLinkDll.JLINK_RTTERMINAL_Control(0, IntPtr.Zero) == 0) rttOn = true; // START
                if (!rttOn)
                {
                    PushSys("RTT 启动失败：未找到 RTT 控制块（确认固件包含 SEGGER RTT 且目标正在运行）");
                    if (wAutoRe && !stopReq) { workerStatus = "2秒后重试..."; Thread.Sleep(2000); continue; }
                    break;
                }
                workerStatus = "已连接";
                PushSys("── 已连接 " + wDev + " (" + wIf + " " + wSpeed + "kHz)，通道 " + wCh + " ──");

                List<byte> pending = new List<byte>(1024);
                DateTime pendTs = DateTime.Now;
                byte[] buf = new byte[16384];
                int lastCheck = Environment.TickCount;

                while (!stopReq)
                {
                    int n = JLinkDll.JLINK_RTTERMINAL_Read(wCh, buf, buf.Length);
                    if (n > 0)
                    {
                        Interlocked.Add(ref bytesTotal, n);
                        for (int i = 0; i < n; i++)
                        {
                            byte b = buf[i];
                            if (b == 10 || b == 13)
                            {
                                if (pending.Count > 0) Emit(pendTs, pending);
                                pending.Clear();
                            }
                            else if (b == 9 || b == 27 || b >= 32) // 27=ESC，颜色码开头，必须放行
                            {
                                if (pending.Count == 0) pendTs = DateTime.Now;
                                pending.Add(b);
                                if (pending.Count > 8192) { Emit(pendTs, pending); pending.Clear(); }
                            }
                        }
                    }
                    else if (n < 0)
                    {
                        PushSys("RTT 读取错误 code=" + n + "（目标可能已断开/复位）");
                        break;
                    }
                    else
                    {
                        Thread.Sleep(1);
                        int now = Environment.TickCount;
                        if (now - lastCheck > 1000)
                        {
                            lastCheck = now;
                            if (JLinkDll.JLINK_IsConnected() == 0)
                            {
                                PushSys("与目标失去连接");
                                break;
                            }
                        }
                    }
                }

                JLinkDll.JLINK_RTTERMINAL_Control(1, IntPtr.Zero); // STOP
                rttOn = false;
                if (stopReq) break;
                if (!wAutoRe) { PushSys("── 已断开 ──"); break; }
                workerStatus = "2秒后自动重连...";
                PushSys("── 连接断开，自动重连中... ──");
                Thread.Sleep(2000);
            }
        }
        finally
        {
            try { if (rttOn) JLinkDll.JLINK_RTTERMINAL_Control(1, IntPtr.Zero); } catch { }
            try { JLinkDll.JLINK_Close(); } catch { }
            workerStatus = "已断开";
        }
    }

    void Emit(DateTime ts, List<byte> bytes)
    {
        string line = Encoding.Default.GetString(bytes.ToArray());
        if (wStripAnsi)
        {
            line = ansiRx.Replace(line, "");
            line = line.Replace("\u001B", "");
        }
        if (line.Length == 0) return;
        Interlocked.Increment(ref lineCount);
        Msg m = new Msg(); m.Ts = ts; m.Text = line; m.Sys = false;
        q.Enqueue(m);
    }

    // ---------- 5. 日志缓存与处理 ----------
    Paragraph MakeParagraph(Msg m)
    {
        Paragraph p = new Paragraph();
        p.Margin = new Thickness(0);
        if (m.Sys)
        {
            p.Inlines.Add(new Run("[" + FormatTs(m.Ts) + "]  ") { Foreground = tcLog.TsBrush });
            p.Inlines.Add(new Run(m.Text) { Foreground = tcLog.SysBrush });
            return p;
        }
        int kind = UiUtil.LevelKindOf(m.Text);
        string tag = UiUtil.LevelTag(kind);
        Brush kbr = tcLog.TextMain;
        if (kind == 1) kbr = tcLog.InfoBrush;
        else if (kind == 2) kbr = tcLog.DebugBrush;
        else if (kind == 3) kbr = tcLog.WarnBrush;
        else if (kind == 4) kbr = tcLog.ErrorBrush;
        p.Inlines.Add(new Run("[" + FormatTs(m.Ts) + "]  ") { Foreground = tcLog.TsBrush });
        if (tag.Length > 0) p.Inlines.Add(new Run(tag + "  ") { Foreground = kbr, FontWeight = FontWeights.Bold });
        p.Inlines.Add(new Run(m.Text) { Foreground = kind == 0 ? tcLog.TextMain : kbr });
        return p;
    }

    public void OnTick(object sender, EventArgs e)
    {
        Msg m;
        bool stick = chkScroll.IsChecked == true || rtbAtBottom();
        while (q.TryDequeue(out m))
        {
            rtb.Document.Blocks.Add(MakeParagraph(m));
            session.Add(m);
            if (session.Count > 100000)
            {
                session.RemoveRange(0, 20000);
                TrimDoc(20000);
            }
            if (fileWriter != null)
            {
                try { fileWriter.WriteLine("[" + FormatTs(m.Ts) + "] " + m.Text); }
                catch { fileWriter = null; }
            }
        }
        if (stick) rtb.ScrollToEnd();

        // worker 退出检测
        string btnText = btnConnect.Content as string;
        if (worker != null && !worker.IsAlive && btnText != ConnectText)
        {
            worker = null;
            btnConnect.Content = ConnectText;
            btnConnect.Background = BrOk;
            CloseWriter();
        }
        // 状态栏
        string st = workerStatus;
        Brush dot = Frozen(150, 150, 150), tbr = tc.TextSub;
        if (st == "已连接") { dot = BrOk; }
        else if (st.IndexOf("连接中", StringComparison.Ordinal) >= 0 || st.IndexOf("重试", StringComparison.Ordinal) >= 0) { dot = BrWarn; }
        else if (st == "已断开") st = "未连接";
        dotConn.Fill = dot;
        tbConn.Text = st;
        tbConn.Foreground = tbr;
        tbBytes.Text = "接收: " + UiUtil.FmtBytes(Interlocked.Read(ref bytesTotal));
        tbLines.Text = "行数: " + Interlocked.Read(ref lineCount);
        tbClock.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    string FormatTs(DateTime ts)
    {
        int f = cmbTs.SelectedIndex;
        if (f == 1) return ts.ToString("HH:mm:ss.fff");
        if (f == 2)
        {
            double s = (ts - connStart).TotalSeconds;
            return "+" + s.ToString("F3") + "s";
        }
        return ts.ToString("yyyy-MM-dd HH:mm:ss.fff");
    }

    void ClearLog()
    {
        rtb.Document.Blocks.Clear();
        session.Clear();
    }

    void ExportLog()
    {
        if (session.Count == 0)
        {
            PushSys("当前没有可保存的日志");
            return;
        }
        string dir = txtDir.Text.Trim();
        if (dir.Length == 0) dir = S.EffectiveSaveDir();
        try
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
            StringBuilder sb = new StringBuilder();
            foreach (Msg m in session)
                sb.AppendLine("[" + FormatTs(m.Ts) + "] " + m.Text);
            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            PushSys("已保存 " + session.Count + " 行到 " + file);
        }
        catch (Exception ex)
        {
            PushSys("保存失败: " + ex.Message);
        }
    }

    void OnBrowseDir(object sender, RoutedEventArgs e)
    {
        System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog();
        dlg.Description = "选择日志保存目录";
        try { dlg.SelectedPath = txtDir.Text; } catch { }
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) txtDir.Text = dlg.SelectedPath;
    }

    void OpenSaveDirDialog(TextBox target)
    {
        System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog();
        dlg.Description = "选择日志保存目录";
        try { dlg.SelectedPath = target.Text; } catch { }
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) target.Text = dlg.SelectedPath;
    }

    // ---------- 查找 / 字号 / 快捷键 ----------
    void ShowFind()
    {
        findBar.Visibility = Visibility.Visible;
        txtFind.Focus();
        txtFind.SelectAll();
    }

    void HideFind()
    {
        findBar.Visibility = Visibility.Collapsed;
        Keyboard.ClearFocus();
        rtb.Focus();
    }

    void FindNext(int dir, CheckBox caseBox)
    {
        string needle = txtFind.Text;
        if (needle.Length == 0) { txtFind.Focus(); return; }
        StringComparison cmp = caseBox != null && caseBox.IsChecked == true
            ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (session.Count == 0) { lblFindMsg.Content = "没有日志"; return; }
        int cur = findAnchorIdx;
        int found = -1;
        if (dir > 0)
        {
            for (int i = cur + 1; i < session.Count; i++)
                if (session[i].Text.IndexOf(needle, cmp) >= 0) { found = i; break; }
            if (found < 0)
                for (int i = 0; i <= cur && i < session.Count; i++)
                    if (session[i].Text.IndexOf(needle, cmp) >= 0) { found = i; break; }
        }
        else
        {
            for (int i = cur - 1; i >= 0; i--)
                if (session[i].Text.IndexOf(needle, cmp) >= 0) { found = i; break; }
            if (found < 0)
                for (int i = session.Count - 1; i > cur; i--)
                    if (session[i].Text.IndexOf(needle, cmp) >= 0) { found = i; break; }
        }
        if (found < 0) { lblFindMsg.Content = "未找到: " + needle; return; }
        lblFindMsg.Content = "";
        SelectParagraph(found);
    }

    // 选中第 i 条日志所在段落
    void SelectParagraph(int i)
    {
        int k = 0;
        foreach (Block blk in rtb.Document.Blocks)
        {
            if (k == i)
            {
                Paragraph pp = blk as Paragraph;
                if (pp != null) rtb.Selection.Select(pp.ContentStart, pp.ContentEnd);
                break;
            }
            k++;
        }
        rtb.ScrollToEnd();
    }

    // 无选中时复制全部
    void TrimDoc(int n)
    {
        for (int i = 0; i < n && rtb.Document.Blocks.Count > 0; i++)
            rtb.Document.Blocks.Remove(rtb.Document.Blocks.FirstBlock);
    }

    bool rtbAtBottom()
    {
        if (!rtb.IsLoaded) return true;
        return rtb.VerticalOffset + rtb.ViewportHeight >= rtb.ExtentHeight - 6;
    }


    void CopySelected()
    {
        string t = rtb.Selection.Text;
        if (string.IsNullOrEmpty(t))
            t = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd).Text; // 无选中时复制全部
        if (t.Length > 0) { try { Clipboard.SetText(t); } catch { } }
    }

    void ZoomFont(int delta) { SetLogFont(rtb.Document.FontSize + delta); }

    void SetLogFont(double sz)
    {
        if (sz < 6) sz = 6;
        if (sz > 30) sz = 30;
        rtb.Document.FontSize = sz;
        S.FontSize = (float)sz;
    }

    void OnLogWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            SetLogFont(rtb.Document.FontSize + (e.Delta > 0 ? 1 : -1));
            e.Handled = true;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (findBar.Visibility == Visibility.Visible && e.Key == Key.Escape)
        {
            HideFind(); e.Handled = true; base.OnPreviewKeyDown(e); return;
        }
        if (findBar.Visibility == Visibility.Visible && e.Key == Key.Enter && txtFind.IsKeyboardFocused)
        {
            FindNext(1, FindCaseBox(findBar)); e.Handled = true; base.OnPreviewKeyDown(e); return;
        }
        if (ctrl && e.Key == Key.F) { ShowFind(); e.Handled = true; }
        else if (ctrl && e.Key == Key.L) { ClearLog(); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { ExportLog(); e.Handled = true; }
        else if (e.Key == Key.F3) { FindNext(shift ? -1 : 1, FindCaseBox(findBar)); e.Handled = true; }
        else if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add)) { ZoomFont(1); e.Handled = true; }
        else if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract)) { ZoomFont(-1); e.Handled = true; }
        else if (ctrl && (e.Key == Key.D0 || e.Key == Key.NumPad0)) { SetLogFont(13); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }

    void ShowSettings()
    {
        ReadSettingsFromUI();
        SettingsWindow dlg = new SettingsWindow(S, dllVer);
        dlg.Owner = this;
        bool? ok = dlg.ShowDialog();
        if (ok == true)
        {
            ApplySettingsFromDialog();
        }
    }

    void ApplySettingsFromDialog()
    {
        cmbDevice.Text = S.Device;
        cmbIf.SelectedItem = S.IfName;
        if (cmbIf.SelectedIndex < 0) cmbIf.SelectedIndex = 0;
        cmbSpeed.Text = S.Speed.ToString();
        txtChannel.Text = S.Channel.ToString();
        chkAutoRe.IsChecked = S.AutoRe;
        chkAnsi.IsChecked = S.StripAnsi;
        chkScroll.IsChecked = S.AutoScroll;
        chkRealtime.IsChecked = S.RealtimeOn;
        txtDir.Text = S.EffectiveSaveDir();
        cmbTs.SelectedIndex = Math.Max(0, Math.Min(2, S.TsFormat));
        SetLogFont(S.FontSize);
        ApplyTheme(); // 全量应用主题（界面 + 日志区）
        S.Save();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        ReadSettingsFromUI();
        S.FontSize = (float)rtb.Document.FontSize;
        S.Save();
        stopReq = true;
        if (worker != null && worker.IsAlive) worker.Join(800);
        CloseWriter();
        base.OnClosing(e);
    }
}

// ===== 7. 设置窗口 =====
public class SettingsWindow : BaseWin
{
    Settings S;
    string dllVer;
    ListBox lvCat;
    StackPanel[] pages;
    TextBox txtDir, txtSaveDir, txtFont;
    TextBlock lblDetect;
    ComboBox cmbFont, cmbTheme, cmbTs;
    CheckBox chkScroll, chkAnsi, chkRealtime;
    Border root;

    public SettingsWindow(Settings s, string dllVersion)
    {
        S = s; dllVer = dllVersion;
        Title = "设置";
        Width = 720; Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ApplyChrome(this);
        Icon = MakeAppIcon();
        ThemeColors t = ThemeColors.Get(S.Theme); // 跟随主题

        root = new Border();
        root.CornerRadius = new CornerRadius(10);
        root.Background = t.CardBg;
        root.BorderBrush = t.CardBorder;
        root.BorderThickness = new Thickness(1);
        root.Margin = new Thickness(10);
        root.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black };
        Content = root;

        Grid g = new Grid();
        RowDefinition r0 = new RowDefinition(); r0.Height = new GridLength(44);
        RowDefinition r1 = new RowDefinition(); r1.Height = new GridLength(1, GridUnitType.Star);
        RowDefinition r2 = new RowDefinition(); r2.Height = GridLength.Auto;
        g.RowDefinitions.Add(r0); g.RowDefinitions.Add(r1); g.RowDefinitions.Add(r2);
        root.Child = g;

        // 标题行
        Grid title = new Grid();
        TextBlock tt = T("⚙  设置", t.TextMain, 14, null, true);
        tt.Margin = new Thickness(16, 0, 0, 0);
        title.Children.Add(tt);
        Button x = new Button();
        x.Content = "\uE8BB";
        x.Style = (Style)Application.Current.Resources["WinBtnClose"];
        x.HorizontalAlignment = HorizontalAlignment.Right;
        x.Margin = new Thickness(0, 0, 12, 0);
        x.Click += new RoutedEventHandler(delegate { DialogResult = false; });
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(x, true);
        title.Children.Add(x);
        Grid.SetRow(title, 0);
        g.Children.Add(title);

        // 左导航 + 右页面
        Grid body = new Grid();
        ColumnDefinition cn = new ColumnDefinition(); cn.Width = new GridLength(150);
        ColumnDefinition cf = new ColumnDefinition(); cf.Width = new GridLength(1, GridUnitType.Star);
        body.ColumnDefinitions.Add(cn); body.ColumnDefinitions.Add(cf);
        lvCat = new ListBox();
        foreach (string it in new string[] { "J-Link 配置", "日志设置", "显示设置", "其他设置" }) lvCat.Items.Add(it);
        lvCat.SelectedIndex = 0;
        lvCat.BorderThickness = new Thickness(0);
        lvCat.Background = Brushes.Transparent;
        lvCat.Foreground = t.TextMain;
        Style navItem = new Style(typeof(ListBoxItem));
        navItem.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(12, 7, 12, 7)));
        lvCat.ItemContainerStyle = navItem;
        lvCat.SelectionChanged += new SelectionChangedEventHandler(delegate
        {
            if (lvCat.SelectedIndex >= 0)
                for (int i = 0; i < pages.Length; i++)
                    pages[i].Visibility = i == lvCat.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
        });
        Grid.SetColumn(lvCat, 0); body.Children.Add(lvCat);

        pages = new StackPanel[4];
        for (int i = 0; i < 4; i++)
        {
            StackPanel p = new StackPanel();
            p.Margin = new Thickness(8, 4, 8, 4);
            Visibility vis = i == 0 ? Visibility.Visible : Visibility.Collapsed;
            p.Visibility = vis;
            Grid.SetColumn(p, 1);
            body.Children.Add(p);
            pages[i] = p;
        }
        Grid.SetRow(body, 1);
        g.Children.Add(body);

        BuildJLinkPage(pages[0], t);
        BuildLogPage(pages[1], t);
        BuildDisplayPage(pages[2], t);
        BuildOtherPage(pages[3], t);

        // 底部按钮
        StackPanel btns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 14, 12)
        };
        Button btnDef = Pill("恢复默认", t.NavBtnBg, t.NavBtnFg, t.NavBtnBorder);
        btnDef.Click += new RoutedEventHandler(delegate { ResetDefaults(); });
        btns.Children.Add(btnDef);
        Button btnOk = Pill("确定", Frozen(35, 110, 225), Brushes.White, Brushes.Transparent);
        btnOk.Margin = new Thickness(10, 0, 0, 0);
        btnOk.Click += new RoutedEventHandler(delegate { ApplyAndClose(); });
        btns.Children.Add(btnOk);
        Button btnCancel = Pill("取消", t.CardBg, t.TextMain, t.CardBorder);
        btnCancel.Margin = new Thickness(10, 0, 0, 0);
        btnCancel.Click += new RoutedEventHandler(delegate { DialogResult = false; });
        btns.Children.Add(btnCancel);
        Grid.SetRow(btns, 2);
        g.Children.Add(btns);
    }


    static StackPanel HRow(double top)
    {
        StackPanel p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, top, 8, 0) };
        return p;
    }

    void BuildJLinkPage(StackPanel p, ThemeColors t)
    {
        p.Children.Clear();
        StackPanel row0 = HRow(10);
        row0.Children.Add(T("J-Link 状态:  ", t.TextSub, 12));
        lblDetect = T("", t.TextMain, 12, null, true);
        row0.Children.Add(lblDetect);
        p.Children.Add(row0);
        StackPanel row0b = HRow(6);
        Button btnRe = Pill("重新检测", t.NavBtnBg, t.NavBtnFg, t.NavBtnBorder);
        btnRe.Click += new RoutedEventHandler(delegate { UpdateDetectLabel(); });
        row0b.Children.Add(btnRe);
        p.Children.Add(row0b);

        StackPanel row1 = HRow(16);
        row1.Children.Add(L("J-Link 安装目录:", t.TextSub, 12));
        txtDir = new TextBox { Width = 280, Margin = new Thickness(8, 0, 0, 0) };
        txtDir.Text = JLinkDll.DllDir;
        row1.Children.Add(txtDir);
        Button btnBrowse = Pill("浏览...", t.CardBg, t.TextMain, t.CardBorder);
        btnBrowse.Margin = new Thickness(8, 0, 0, 0);
        btnBrowse.Click += new RoutedEventHandler(delegate
        {
            string d = MainWindow.AskJLinkDir(this);
            if (d != null) txtDir.Text = d;
        });
        row1.Children.Add(btnBrowse);
        p.Children.Add(row1);

        StackPanel row2 = HRow(6);
        Label hint = L("路径修改后需要重启 RTTStamp 才能生效。", t.Accent, 12);
        row2.Children.Add(hint);
        p.Children.Add(row2);
        UpdateDetectLabel();
    }

    void UpdateDetectLabel()
    {
        bool ok = MainWindow.IsValidJLinkDir(txtDir == null ? JLinkDll.DllDir : txtDir.Text);
        lblDetect.Text = ok ? "✔ 已识别 JLink_x64.dll · DLL " + dllVer : "✘ 未找到 JLink_x64.dll";
        lblDetect.Foreground = ok ? Frozen(16, 124, 16) : Frozen(204, 41, 54);
    }

    void BuildLogPage(StackPanel p, ThemeColors t)
    {
        p.Children.Clear();
        StackPanel row1 = HRow(16);
        row1.Children.Add(L("日志保存目录:", t.TextSub, 12));
        txtSaveDir = new TextBox { Width = 280, Margin = new Thickness(8, 0, 0, 0) };
        txtSaveDir.Text = S.EffectiveSaveDir();
        row1.Children.Add(txtSaveDir);
        Button btnBrowse = Pill("浏览...", t.CardBg, t.TextMain, t.CardBorder);
        btnBrowse.Margin = new Thickness(8, 0, 0, 0);
        btnBrowse.Click += new RoutedEventHandler(delegate { OpenSaveDirDialog(txtSaveDir); });
        row1.Children.Add(btnBrowse);
        p.Children.Add(row1);
        StackPanel row2 = HRow(8);
        row2.Children.Add(L("实时保存与“保存日志”按钮的文件都存放在此目录，文件名自动生成:", t.TextSub, 12));
        p.Children.Add(row2);
        StackPanel row3 = HRow(2);
        row3.Children.Add(L("real_log_年月日_时分秒.log / log_年月日_时分秒.log", t.TextSub, 12));
        p.Children.Add(row3);
        StackPanel row4 = HRow(12);
        chkRealtime = new CheckBox { Content = "启动时默认开启实时保存", Foreground = t.TextMain, VerticalContentAlignment = VerticalAlignment.Center };
        chkRealtime.IsChecked = S.RealtimeOn;
        row4.Children.Add(chkRealtime);
        p.Children.Add(row4);
    }

    void BuildDisplayPage(StackPanel p, ThemeColors t)
    {
        p.Children.Clear();
        StackPanel row1 = HRow(16);
        row1.Children.Add(L("字体:", t.TextSub, 12));
        cmbFont = new ComboBox { Width = 160, Margin = new Thickness(8, 0, 0, 0) };
        cmbFont.Items.Add("Consolas"); cmbFont.Items.Add("Courier New");
        cmbFont.SelectedIndex = S.FontName == "Courier New" ? 1 : 0;
        row1.Children.Add(cmbFont);
        row1.Children.Add(L("字号:", t.TextSub, 12));
        txtFont = new TextBox { Width = 60, Margin = new Thickness(8, 0, 0, 0) };
        txtFont.Text = S.FontSize.ToString("F1");
        row1.Children.Add(txtFont);
        p.Children.Add(row1);
        StackPanel row2 = HRow(14);
        row2.Children.Add(L("日志区配色:", t.TextSub, 12));
        cmbTheme = new ComboBox { Width = 120, Margin = new Thickness(8, 0, 0, 0) };
        cmbTheme.Items.Add("浅色"); cmbTheme.Items.Add("深色");
        cmbTheme.SelectedIndex = S.Theme == 1 ? 1 : 0;
        row2.Children.Add(cmbTheme);
        p.Children.Add(row2);
        StackPanel row3 = HRow(14);
        chkScroll = new CheckBox { Content = "自动滚动到最新日志", Foreground = t.TextMain, VerticalContentAlignment = VerticalAlignment.Center };
        chkScroll.IsChecked = S.AutoScroll;
        row3.Children.Add(chkScroll);
        p.Children.Add(row3);
    }

    void BuildOtherPage(StackPanel p, ThemeColors t)
    {
        p.Children.Clear();
        StackPanel row1 = HRow(16);
        row1.Children.Add(L("时间戳格式:", t.TextSub, 12));
        cmbTs = new ComboBox { Width = 320, Margin = new Thickness(8, 0, 0, 0) };
        cmbTs.Items.Add("年-月-日 时:分:秒.毫秒 (1970-01-01 01:01:01.001)");
        cmbTs.Items.Add("时:分:秒.毫秒 (01:01:01.001)");
        cmbTs.Items.Add("相对连接秒数 (+12.345s)");
        cmbTs.SelectedIndex = Math.Max(0, Math.Min(2, S.TsFormat));
        row1.Children.Add(cmbTs);
        p.Children.Add(row1);
        StackPanel row2 = HRow(14);
        chkAnsi = new CheckBox { Content = "剥离 ANSI 颜色码（固件颜色序列不显示为乱码）", Foreground = t.TextMain, VerticalContentAlignment = VerticalAlignment.Center };
        chkAnsi.IsChecked = S.StripAnsi;
        row2.Children.Add(chkAnsi);
        p.Children.Add(row2);
    }

    void OpenSaveDirDialog(TextBox target)
    {
        System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog();
        dlg.Description = "选择日志保存目录";
        try { dlg.SelectedPath = target.Text; } catch { }
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) target.Text = dlg.SelectedPath;
    }

    void ResetDefaults()
    {
        txtDir.Text = JLinkDll.DefaultDir;
        txtSaveDir.Text = "";
        cmbFont.SelectedIndex = 0;
        txtFont.Text = "13.0";
        cmbTheme.SelectedIndex = 0;
        chkScroll.IsChecked = true;
        chkRealtime.IsChecked = false;
        cmbTs.SelectedIndex = 0;
        chkAnsi.IsChecked = true;
    }

    void ApplyAndClose()
    {
        S.JLinkDir = txtDir.Text.Trim();
        S.SaveDir = txtSaveDir.Text.Trim();
        S.FontName = cmbFont.SelectedItem as string;
        float fs;
        if (!float.TryParse(txtFont.Text.Trim(), out fs) || fs < 6 || fs > 30) fs = 13;
        S.FontSize = fs;
        S.Theme = cmbTheme.SelectedIndex;
        S.AutoScroll = chkScroll.IsChecked == true;
        S.RealtimeOn = chkRealtime.IsChecked == true;
        S.TsFormat = cmbTs.SelectedIndex;
        S.StripAnsi = chkAnsi.IsChecked == true;
        S.Save();
        DialogResult = true;
        Close();
    }
}

// ===== 8. 快捷键窗口 =====
public class ShortcutsWindow : BaseWin
{
    public ShortcutsWindow()
    {
        Title = "快捷键";
        Width = 460; Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ApplyChrome(this);
        Icon = MakeAppIcon();
        ThemeColors t = ThemeColors.Get(SettingsLoadTheme()); // 跟随主题

        Border root = new Border();
        root.CornerRadius = new CornerRadius(10);
        root.Background = t.CardBg;
        root.BorderBrush = t.CardBorder;
        root.BorderThickness = new Thickness(1);
        root.Margin = new Thickness(10);
        root.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black };
        Content = root;

        Grid g = new Grid();
        RowDefinition r0 = new RowDefinition(); r0.Height = new GridLength(44);
        RowDefinition r1 = new RowDefinition(); r1.Height = new GridLength(1, GridUnitType.Star);
        RowDefinition r2 = new RowDefinition(); r2.Height = GridLength.Auto;
        g.RowDefinitions.Add(r0); g.RowDefinitions.Add(r1); g.RowDefinitions.Add(r2);
        root.Child = g;

        Grid title = new Grid();
        TextBlock tt = T("⌨  快捷键", t.TextMain, 14, null, true);
        tt.Margin = new Thickness(16, 0, 0, 0);
        title.Children.Add(tt);
        Button x = new Button();
        x.Content = "\uE8BB";
        x.Style = (Style)Application.Current.Resources["WinBtnClose"];
        x.HorizontalAlignment = HorizontalAlignment.Right;
        x.Margin = new Thickness(0, 0, 12, 0);
        x.Click += new RoutedEventHandler(delegate { Close(); });
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(x, true);
        title.Children.Add(x);
        Grid.SetRow(title, 0); g.Children.Add(title);

        StackPanel body = new StackPanel { Margin = new Thickness(18, 6, 18, 4) };
        AddGroup(body, "日志操作", t);
        AddRow(body, "清空日志", "Ctrl + L", t);
        AddRow(body, "保存日志", "Ctrl + S", t);
        AddGroup(body, "查找操作", t);
        AddRow(body, "打开查找", "Ctrl + F", t);
        AddRow(body, "查找下一个", "F3", t);
        AddRow(body, "查找上一个", "Shift + F3", t);
        AddRow(body, "关闭查找", "Esc", t);
        AddGroup(body, "显示操作", t);
        AddRow(body, "增大 / 减小字体", "Ctrl + +/− 或 Ctrl + 滚轮", t);
        AddRow(body, "恢复默认字号", "Ctrl + 0", t);
        AddRow(body, "复制 / 全选", "Ctrl + C / Ctrl + A", t);
        Grid.SetRow(body, 1); g.Children.Add(body);

        StackPanel btns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 14, 12)
        };
        Button btnClose = Pill("关闭", t.NavBtnBg, t.NavBtnFg, t.NavBtnBorder);
        btnClose.Click += new RoutedEventHandler(delegate { Close(); });
        btns.Children.Add(btnClose);
        Grid.SetRow(btns, 2); g.Children.Add(btns);
    }

    static int SettingsLoadTheme()
    {
        Settings s = Settings.Load();
        return s.Theme;
    }

    static void AddGroup(StackPanel p, string text, ThemeColors t)
    {
        TextBlock tb = T(text, t.Accent, 12, null, true);
        tb.Margin = new Thickness(0, 10, 0, 2);
        p.Children.Add(tb);
    }

    static void AddRow(StackPanel p, string func, string keys, ThemeColors t)
    {
        Grid g = new Grid();
        ColumnDefinition c0 = new ColumnDefinition(); c0.Width = new GridLength(1, GridUnitType.Star);
        ColumnDefinition c1 = new ColumnDefinition(); c1.Width = GridLength.Auto;
        g.ColumnDefinitions.Add(c0); g.ColumnDefinitions.Add(c1);
        TextBlock a = T(func, t.TextMain, 12); Grid.SetColumn(a, 0); g.Children.Add(a);
        TextBlock b2 = T(keys, t.TextSub, 12); b2.FontFamily = new FontFamily("Consolas");
        Grid.SetColumn(b2, 1); g.Children.Add(b2);
        Border line = new Border();
        line.BorderBrush = t.CardBorder;
        line.BorderThickness = new Thickness(0, 0, 0, 1);
        line.Padding = new Thickness(0, 5, 0, 5);
        line.Child = g;
        p.Children.Add(line);
    }
}

// ===== 9. 关于窗口 =====
public class AboutWindow : BaseWin
{
    public AboutWindow(string dllVer)
    {
        Title = "关于 RTTStamp";
        Width = 420; Height = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ApplyChrome(this);
        Icon = MakeAppIcon();
        Settings s0 = Settings.Load();
        ThemeColors t = ThemeColors.Get(s0.Theme); // 跟随主题

        Border root = new Border();
        root.CornerRadius = new CornerRadius(10);
        root.Background = t.CardBg;
        root.BorderBrush = t.CardBorder;
        root.BorderThickness = new Thickness(1);
        root.Margin = new Thickness(10);
        root.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black };
        Content = root;

        Grid g = new Grid();
        RowDefinition r0 = new RowDefinition(); r0.Height = new GridLength(44);
        RowDefinition r1 = new RowDefinition(); r1.Height = new GridLength(1, GridUnitType.Star);
        RowDefinition r2 = new RowDefinition(); r2.Height = GridLength.Auto;
        g.RowDefinitions.Add(r0); g.RowDefinitions.Add(r1); g.RowDefinitions.Add(r2);
        root.Child = g;

        Grid title = new Grid();
        TextBlock tt = T("ℹ  关于 RTTStamp", t.TextMain, 14, null, true);
        tt.Margin = new Thickness(16, 0, 0, 0);
        title.Children.Add(tt);
        Button x = new Button();
        x.Content = "\uE8BB";
        x.Style = (Style)Application.Current.Resources["WinBtnClose"];
        x.HorizontalAlignment = HorizontalAlignment.Right;
        x.Margin = new Thickness(0, 0, 12, 0);
        x.Click += new RoutedEventHandler(delegate { Close(); });
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(x, true);
        title.Children.Add(x);
        Grid.SetRow(title, 0); g.Children.Add(title);

        StackPanel body = new StackPanel { Margin = new Thickness(20, 6, 20, 4) };
        StackPanel head = new StackPanel { Orientation = Orientation.Horizontal };
        Border logo = new Border();
        logo.Width = 58; logo.Height = 58;
        logo.CornerRadius = new CornerRadius(12);
        logo.Background = Frozen(27, 31, 39);
        logo.Child = T(">_", Brushes.White, 24, "Consolas", true);
        head.Children.Add(logo);
        StackPanel names = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(T("RTTStamp", t.TextMain, 22, null, true));
        TextBlock vt = T("v" + UiUtil.AppVersion(), t.Accent, 15, null, true);
        vt.Margin = new Thickness(0, 2, 0, 0);
        names.Children.Add(vt);
        head.Children.Add(names);
        body.Children.Add(head);
        TextBlock desc = T("J-Link RTT 日志查看器（带时间戳）", t.TextSub, 12);
        desc.Margin = new Thickness(0, 10, 0, 6);
        body.Children.Add(desc);

        string[] rows = new string[] {
            "软件版本|v" + UiUtil.AppVersion(),
            "J-Link DLL|" + dllVer,
            "程序架构|" + UiUtil.ArchText(),
            "开发框架|C# / WPF",
            "运行环境|" + UiUtil.DotNetText(),
            "构建时间|" + UiUtil.BuildTime()
        };
        foreach (string rr in rows)
        {
            int sep = rr.IndexOf('|');
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            TextBlock k = T(rr.Substring(0, sep), t.TextSub, 12); k.Width = 110;
            row.Children.Add(k);
            row.Children.Add(T(rr.Substring(sep + 1), t.TextMain, 12));
            body.Children.Add(row);
        }
        TextBlock foot = T("RTTStamp · 独立的 J-Link RTT 日志辅助工具", t.TextSub, 11);
        foot.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(foot);
        Grid.SetRow(body, 1); g.Children.Add(body);

        StackPanel btns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 14, 12)
        };
        Button btnOk = Pill("确定", Frozen(35, 110, 225), Brushes.White, Brushes.Transparent);
        btnOk.Click += new RoutedEventHandler(delegate { Close(); });
        btns.Children.Add(btnOk);
        Grid.SetRow(btns, 2); g.Children.Add(btns);
    }
}

// ===== 10. 程序入口 =====
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].Trim().Length > 0)
            Settings.IniSuffix = "_" + args[0].Trim();
        Application app = new Application();
        app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(ResLib.Dict));
        MainWindow win = new MainWindow();
        app.Run(win);
    }
}
