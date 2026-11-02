using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BreathingRoom
{
    // Shared palette and control styles. Warm paper colors, nothing clinical.
    public static class Ui
    {
        public static readonly Brush Paper = B("#FFF8EF");
        public static readonly Brush PaperDeep = B("#F7EADB");
        public static readonly Brush Ink = B("#3A2A20");
        public static readonly Brush Soft = B("#8C776A");
        public static readonly Brush Orange = B("#F2A04E");
        public static readonly Brush OrangeDeep = B("#D9823A");
        public static readonly Brush Line = B("#EBD9C6");
        public static readonly FontFamily Font = new FontFamily("Segoe UI");

        public static Color C(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        public static SolidColorBrush B(string hex)
        {
            var b = new SolidColorBrush(C(hex));
            b.Freeze();
            return b;
        }

        const string StylesXaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='Pill' TargetType='Button'>
    <Setter Property='Foreground' Value='#3A2A20'/>
    <Setter Property='Background' Value='#FFE9D0'/>
    <Setter Property='BorderBrush' Value='#F2C08A'/>
    <Setter Property='FontFamily' Value='Segoe UI'/>
    <Setter Property='FontSize' Value='12.5'/>
    <Setter Property='Padding' Value='11,4'/>
    <Setter Property='Margin' Value='0,5,6,0'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='1' CornerRadius='11' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='False'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#FFD7A8'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Background' Value='#F7BE7E'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='Primary' TargetType='Button'>
    <Setter Property='Foreground' Value='#2B1D14'/>
    <Setter Property='Background' Value='#F2A04E'/>
    <Setter Property='FontFamily' Value='Segoe UI'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Padding' Value='20,8'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='14' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='False'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#F6B26B'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Background' Value='#D9823A'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='Link' TargetType='Button'>
    <Setter Property='Foreground' Value='#B5631F'/>
    <Setter Property='FontFamily' Value='Segoe UI'/>
    <Setter Property='FontSize' Value='12.5'/>
    <Setter Property='Margin' Value='0,7,0,0'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='HorizontalAlignment' Value='Left'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border Background='Transparent'>
            <TextBlock x:Name='t' TextWrapping='Wrap' Text='{Binding Content, RelativeSource={RelativeSource TemplatedParent}}'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='t' Property='TextDecorations' Value='Underline'/>
              <Setter Property='Foreground' Value='#8A4510'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";

        static ResourceDictionary styles;

        public static Style Style(string key)
        {
            if (styles == null) styles = (ResourceDictionary)XamlReader.Parse(StylesXaml);
            return (Style)styles[key];
        }

        public static Button Pill(string text, RoutedEventHandler click)
        {
            var b = new Button { Content = text, Style = Style("Pill") };
            if (click != null) b.Click += click;
            return b;
        }

        public static Button Primary(string text, RoutedEventHandler click)
        {
            var b = new Button { Content = text, Style = Style("Primary") };
            if (click != null) b.Click += click;
            return b;
        }

        public static Button Link(string text, RoutedEventHandler click)
        {
            var b = new Button { Content = text, Style = Style("Link") };
            if (click != null) b.Click += click;
            return b;
        }

        public static TextBlock Text(string text, double size, Brush color)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = color,
                FontFamily = Font,
                TextWrapping = TextWrapping.Wrap
            };
        }

        public static TextBlock Heading(string text, double size)
        {
            var t = Text(text, size, Ink);
            t.FontWeight = FontWeights.SemiBold;
            return t;
        }

        // A plain window with the warm paper look used by Welcome, Insights and Settings.
        public static Window PaperWindow(string title, double width, double height)
        {
            // Never taller or wider than the usable screen (e.g. a laptop at 150% scaling), so the
            // title bar and buttons stay reachable; every page scrolls.
            Rect wa = SystemParameters.WorkArea;
            var w = new Window
            {
                Title = title,
                Width = Math.Min(width, wa.Width - 20),
                Height = Math.Min(height, wa.Height - 20),
                Background = Paper,
                FontFamily = Font,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanMinimize
            };
            var icon = AppIcon();
            if (icon != null) w.Icon = icon;
            return w;
        }

        static ImageSource appIcon;

        public static ImageSource AppIcon()
        {
            if (appIcon != null) return appIcon;
            try
            {
                using (Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico"))
                {
                    if (s != null)
                    {
                        appIcon = BitmapFrame.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        return appIcon;
                    }
                }
                using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetEntryAssembly().Location))
                {
                    appIcon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                }
            }
            catch { }
            return appIcon;
        }
    }
}
