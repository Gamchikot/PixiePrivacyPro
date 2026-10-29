using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Microsoft.Win32;

// Design system of the WPF interface: theme colours, control styles and small builders shared by the window and the control center.
// Colours are dynamic resources, so switching the theme repaints open windows at once.
public static class Ui {
    public const int ThemeSystem=0,ThemeLight=1,ThemeDark=2;
    public static readonly Color Green=C("#30D158"),Red=C("#FF453A"),Blue=C("#0A84FF"),Orange=C("#FF9F0A"),Purple=C("#BF5AF2"),Pink=C("#FF375F"),Teal=C("#40C8E0"),Gray=C("#8E8E93"),Yellow=C("#FFD60A");
    public static Color C(string hex) {return (Color)ColorConverter.ConvertFromString(hex);}
    public static SolidColorBrush B(Color c) {var b=new SolidColorBrush(c);b.Freeze();return b;}
    public static SolidColorBrush B(string hex) {return B(C(hex));}
    public static Color Alpha(Color c,byte a) {return Color.FromArgb(a,c.R,c.G,c.B);}

    public static bool SystemDark {get {
        try {
            using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) {
                object value=key==null?null:key.GetValue("AppsUseLightTheme");
                return value!=null && Convert.ToInt32(value)==0;
            }
        } catch {return false;}
    }}
    public static int ThemeChoice {get {return Preferences.GetInt(Preferences.Theme,ThemeSystem);}}
    public static bool Dark {get {int t=ThemeChoice;return t==ThemeDark||(t==ThemeSystem&&SystemDark);}}

    public static void ApplyTheme(ResourceDictionary r,bool dark) {
        Set(r,"Text",dark?"#F5F6F8":"#111318");Set(r,"Sub",dark?"#9AA1AD":"#5D6470");Set(r,"Faint",dark?"#737A87":"#8A9099");
        Set(r,"Content",dark?"#FF14161B":"#FFF4F5F7");Set(r,"Sidebar",dark?"#FF1B1D23":"#FFECEDF0");Set(r,"SideLine",dark?"#FF26282E":"#FFDFE1E5");
        Set(r,"Card",dark?"#FF202329":"#FFFFFFFF");Set(r,"CardLine",dark?"#FF2C2F36":"#FFE3E5E9");Set(r,"Line",dark?"#FF2A2D34":"#FFEEEFF2");
        Set(r,"Hover",dark?"#12FFFFFF":"#0A000000");Set(r,"Press",dark?"#20FFFFFF":"#16000000");
        Set(r,"NavOn",dark?"#FF2B2E35":"#FFFFFFFF");Set(r,"Field",dark?"#FF2A2D34":"#FFEDEEF1");Set(r,"SegOn",dark?"#FF0F1013":"#FFFFFFFF");
        Set(r,"PillOff",dark?"#FF3A3C42":"#FFD5D7DC");Set(r,"Key",dark?"#FF2E3138":"#FFF3F4F6");Set(r,"KeyLine",dark?"#FF41444C":"#FFD3D6DB");
        Set(r,"Preview",dark?"#FF0B0C0F":"#FF1B1D22");Set(r,"Flyout",dark?"#F51C1F26":"#F7F6F7F9");Set(r,"Tile",dark?"#FF2C2F37":"#FFE6E8EC");
        Set(r,"TileOn",dark?"#FFF5F6F8":"#FFFFFFFF");Set(r,"Chip",dark?"#FF2A2D34":"#FFEEF0F3");Set(r,"Track",dark?"#FF3A3D45":"#FFD9DBE0");
        Set(r,"BannerBg",dark?"#2EFF453A":"#17FF453A");Set(r,"BannerLine",dark?"#70FF453A":"#55FF453A");
        Set(r,"InfoBg",dark?"#260A84FF":"#140A84FF");Set(r,"InfoLine",dark?"#660A84FF":"#4D0A84FF");
    }
    static void Set(ResourceDictionary r,string key,string hex) {r[key]=B(hex);}

    // Shared templates; colours come from the dynamic resources above.
    public static ResourceDictionary Styles() {
        var styles=(ResourceDictionary)XamlReader.Parse(StylesXaml);
        ApplyTheme(styles,Dark);
        return styles;
    }
    const string StylesXaml=@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='Pill' TargetType='ToggleButton'>
    <Setter Property='Cursor' Value='Hand'/><Setter Property='Focusable' Value='False'/><Setter Property='VerticalAlignment' Value='Center'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='ToggleButton'>
        <Grid Width='51' Height='31' Background='Transparent'>
          <Border CornerRadius='15.5' Background='{DynamicResource PillOff}'/>
          <Border x:Name='On' CornerRadius='15.5' Background='#30D158' Opacity='0'/>
          <Ellipse Width='27' Height='27' Fill='White' HorizontalAlignment='Left' Margin='2,0,0,0'>
            <Ellipse.Effect><DropShadowEffect BlurRadius='6' ShadowDepth='1' Opacity='0.3'/></Ellipse.Effect>
            <Ellipse.RenderTransform><TranslateTransform x:Name='Move'/></Ellipse.RenderTransform>
          </Ellipse>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property='IsChecked' Value='True'>
            <Trigger.EnterActions><BeginStoryboard><Storyboard>
              <DoubleAnimation Storyboard.TargetName='Move' Storyboard.TargetProperty='X' To='20' Duration='0:0:0.32'><DoubleAnimation.EasingFunction><BackEase Amplitude='0.5' EasingMode='EaseOut'/></DoubleAnimation.EasingFunction></DoubleAnimation>
              <DoubleAnimation Storyboard.TargetName='On' Storyboard.TargetProperty='Opacity' To='1' Duration='0:0:0.2'/>
            </Storyboard></BeginStoryboard></Trigger.EnterActions>
            <Trigger.ExitActions><BeginStoryboard><Storyboard>
              <DoubleAnimation Storyboard.TargetName='Move' Storyboard.TargetProperty='X' To='0' Duration='0:0:0.32'><DoubleAnimation.EasingFunction><BackEase Amplitude='0.5' EasingMode='EaseOut'/></DoubleAnimation.EasingFunction></DoubleAnimation>
              <DoubleAnimation Storyboard.TargetName='On' Storyboard.TargetProperty='Opacity' To='0' Duration='0:0:0.2'/>
            </Storyboard></BeginStoryboard></Trigger.ExitActions>
          </Trigger>
          <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='Seg' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='{DynamicResource Sub}'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FontSize' Value='12.5'/><Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='RadioButton'>
        <Border x:Name='Bd' CornerRadius='8' Background='Transparent' Padding='{TemplateBinding Padding}'>
          <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
          <Trigger Property='IsChecked' Value='True'>
            <Setter TargetName='Bd' Property='Background' Value='{DynamicResource SegOn}'/><Setter Property='Foreground' Value='{DynamicResource Text}'/><Setter Property='FontWeight' Value='SemiBold'/>
            <Setter TargetName='Bd' Property='Effect'><Setter.Value><DropShadowEffect BlurRadius='6' ShadowDepth='1' Opacity='0.18'/></Setter.Value></Setter>
          </Trigger>
          <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='Nav' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FontSize' Value='14'/><Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='RadioButton'>
        <Border x:Name='Bd' CornerRadius='10' Background='Transparent' Padding='10,8' Margin='0,1'>
          <ContentPresenter VerticalAlignment='Center'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
          <Trigger Property='IsChecked' Value='True'>
            <Setter TargetName='Bd' Property='Background' Value='{DynamicResource NavOn}'/>
            <Setter TargetName='Bd' Property='Effect'><Setter.Value><DropShadowEffect BlurRadius='8' ShadowDepth='1' Opacity='0.12'/></Setter.Value></Setter>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='Row' TargetType='Button'>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Focusable' Value='False'/>
    <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='Button'>
        <Border x:Name='Bd' Background='Transparent' Padding='{TemplateBinding Padding}'>
          <ContentPresenter HorizontalAlignment='Stretch' VerticalAlignment='Center'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
          <Trigger Property='IsPressed' Value='True'><Setter TargetName='Bd' Property='Background' Value='{DynamicResource Press}'/></Trigger>
          <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='Soft' TargetType='Button'>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/><Setter Property='Background' Value='{DynamicResource Tile}'/>
    <Setter Property='Cursor' Value='Hand'/><Setter Property='Focusable' Value='False'/><Setter Property='FontSize' Value='13'/><Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Padding' Value='14,8'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='Button'>
        <Border x:Name='Bd' CornerRadius='10' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}' RenderTransformOrigin='0.5,0.5'>
          <Border.RenderTransform><ScaleTransform x:Name='Scale'/></Border.RenderTransform>
          <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Bd' Property='Opacity' Value='0.86'/></Trigger>
          <Trigger Property='IsPressed' Value='True'>
            <Trigger.EnterActions><BeginStoryboard><Storyboard>
              <DoubleAnimation Storyboard.TargetName='Scale' Storyboard.TargetProperty='ScaleX' To='0.96' Duration='0:0:0.08'/>
              <DoubleAnimation Storyboard.TargetName='Scale' Storyboard.TargetProperty='ScaleY' To='0.96' Duration='0:0:0.08'/>
            </Storyboard></BeginStoryboard></Trigger.EnterActions>
            <Trigger.ExitActions><BeginStoryboard><Storyboard>
              <DoubleAnimation Storyboard.TargetName='Scale' Storyboard.TargetProperty='ScaleX' To='1' Duration='0:0:0.3'><DoubleAnimation.EasingFunction><BackEase Amplitude='0.6' EasingMode='EaseOut'/></DoubleAnimation.EasingFunction></DoubleAnimation>
              <DoubleAnimation Storyboard.TargetName='Scale' Storyboard.TargetProperty='ScaleY' To='1' Duration='0:0:0.3'><DoubleAnimation.EasingFunction><BackEase Amplitude='0.6' EasingMode='EaseOut'/></DoubleAnimation.EasingFunction></DoubleAnimation>
            </Storyboard></BeginStoryboard></Trigger.ExitActions>
          </Trigger>
          <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='Bare' TargetType='Button'>
    <Setter Property='Cursor' Value='Hand'/><Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='Button'><ContentPresenter/></ControlTemplate>
    </Setter.Value></Setter>
  </Style>
</ResourceDictionary>";

    // Stroke icons on a 24×24 grid (rounded ends); the optional second part is filled.
    static readonly Dictionary<string,string[]> Icons=new Dictionary<string,string[]> {
        {"shield",new[]{"M12,2.6 L19.5,5.5 V11.2 C19.5,16 16.3,19.6 12,21.2 C7.7,19.6 4.5,16 4.5,11.2 V5.5 Z",null}},
        {"shieldcheck",new[]{"M12,2.6 L19.5,5.5 V11.2 C19.5,16 16.3,19.6 12,21.2 C7.7,19.6 4.5,16 4.5,11.2 V5.5 Z M8.8,11.8 L11,14 L15.4,9.6",null}},
        {"lock",new[]{"M6.5,10.5 H17.5 A2,2 0 0 1 19.5,12.5 V18.5 A2,2 0 0 1 17.5,20.5 H6.5 A2,2 0 0 1 4.5,18.5 V12.5 A2,2 0 0 1 6.5,10.5 Z M8,10.5 V7.5 A4,4 0 0 1 16,7.5 V10.5","M10.4,15.5 A1.6,1.6 0 1 0 13.6,15.5 A1.6,1.6 0 1 0 10.4,15.5 Z"}},
        {"eye",new[]{"M2,12 C4.5,7.2 8,5 12,5 C16,5 19.5,7.2 22,12 C19.5,16.8 16,19 12,19 C8,19 4.5,16.8 2,12 Z","M9,12 A3,3 0 1 0 15,12 A3,3 0 1 0 9,12 Z"}},
        {"video",new[]{"M4.5,6 H14.5 A1.5,1.5 0 0 1 16,7.5 V16.5 A1.5,1.5 0 0 1 14.5,18 H4.5 A1.5,1.5 0 0 1 3,16.5 V7.5 A1.5,1.5 0 0 1 4.5,6 Z M16,10.5 L21,7.5 V16.5 L16,13.5",null}},
        {"mic",new[]{"M9,5 A3,3 0 0 1 15,5 V11 A3,3 0 0 1 9,11 Z M5,11 A7,7 0 0 0 19,11 M12,18 V21",null}},
        {"micoff",new[]{"M9,5 A3,3 0 0 1 15,5 V11 A3,3 0 0 1 9,11 Z M5,11 A7,7 0 0 0 19,11 M12,18 V21 M3.5,3.5 L20.5,20.5",null}},
        {"target",new[]{"M4,12 A8,8 0 1 0 20,12 A8,8 0 1 0 4,12 Z M12,2 V5 M12,19 V22 M2,12 H5 M19,12 H22","M9.5,12 A2.5,2.5 0 1 0 14.5,12 A2.5,2.5 0 1 0 9.5,12 Z"}},
        {"timer",new[]{"M5,13.5 A7,7 0 1 0 19,13.5 A7,7 0 1 0 5,13.5 Z M12,13.5 V9.5 M9.5,3 H14.5",null}},
        {"volume",new[]{"M11,5 L6,9 H3 V15 H6 L11,19 Z M15.5,8.5 A5,5 0 0 1 15.5,15.5 M18.5,5.5 A9,9 0 0 1 18.5,18.5",null}},
        {"mute",new[]{"M11,5 L6,9 H3 V15 H6 L11,19 Z M16,9.5 L21,14.5 M21,9.5 L16,14.5",null}},
        {"image",new[]{"M5,4 H19 A1,1 0 0 1 20,5 V19 A1,1 0 0 1 19,20 H5 A1,1 0 0 1 4,19 V5 A1,1 0 0 1 5,4 Z M4,16 L9,11 L13,15 L15,13 L20,18","M14.5,8.5 A1.5,1.5 0 1 0 17.5,8.5 A1.5,1.5 0 1 0 14.5,8.5 Z"}},
        {"journal",new[]{"M6,3 H15 L19,7 V20 A1,1 0 0 1 18,21 H6 A1,1 0 0 1 5,20 V4 A1,1 0 0 1 6,3 Z M9,12 H15 M9,16 H15 M9,8 H11",null}},
        {"alert",new[]{"M12,3.5 L21.5,20 H2.5 Z M12,10 V14.5 M12,17.4 V17.5",null}},
        {"power",new[]{"M12,3 V11 M6.3,6.3 A8,8 0 1 0 17.7,6.3",null}},
        {"window",new[]{"M4,5 H20 V19 H4 Z M4,9 H20",null}},
        {"check",new[]{"M5,12.5 L9.5,17 L19,7.5",null}},
        {"chevron",new[]{"M9.5,6 L15.5,12 L9.5,18",null}},
        {"play",new[]{null,"M8,5 V19 L19,12 Z"}},
        {"stop",new[]{null,"M7,7 H17 V17 H7 Z"}},
        {"x",new[]{"M6,6 L18,18 M18,6 L6,18",null}},
        {"search",new[]{"M4,10.5 A6.5,6.5 0 1 0 17,10.5 A6.5,6.5 0 1 0 4,10.5 Z M15.5,15.5 L20.5,20.5",null}},
        {"folder",new[]{"M3.5,6.5 A1.5,1.5 0 0 1 5,5 H9.5 L11.5,7 H19 A1.5,1.5 0 0 1 20.5,8.5 V17.5 A1.5,1.5 0 0 1 19,19 H5 A1.5,1.5 0 0 1 3.5,17.5 Z",null}},
        {"keyboard",new[]{"M3.5,6.5 H20.5 V17.5 H3.5 Z M7,10 H7.1 M10.5,10 H10.6 M14,10 H14.1 M17.5,10 H17.6 M8,14 H16",null}},
        {"apps",new[]{"M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z",null}},
        {"sun",new[]{"M8,12 A4,4 0 1 0 16,12 A4,4 0 1 0 8,12 Z M12,2.5 V4.5 M12,19.5 V21.5 M2.5,12 H4.5 M19.5,12 H21.5 M5.3,5.3 L6.7,6.7 M17.3,17.3 L18.7,18.7 M5.3,18.7 L6.7,17.3 M17.3,6.7 L18.7,5.3",null}},
        {"question",new[]{"M9,9 A3,3 0 1 1 13.2,11.7 C12.4,12.1 12,12.7 12,13.6 V14.5 M12,17.6 V17.7",null}},
    };
    public static FrameworkElement Icon(string name,double size,Brush brush,double thickness) {
        var canvas=new Canvas {Width=24,Height=24};
        string[] parts=Icons[name];
        if(parts[0]!=null) canvas.Children.Add(new Path {Data=Geometry.Parse(parts[0]),Stroke=brush,StrokeThickness=thickness,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round});
        if(parts[1]!=null) canvas.Children.Add(new Path {Data=Geometry.Parse(parts[1]),Fill=brush});
        return new Viewbox {Width=size,Height=size,Child=canvas};
    }
    public static FrameworkElement Icon(string name,double size,string brushKey) {
        var canvas=new Canvas {Width=24,Height=24};
        string[] parts=Icons[name];
        if(parts[0]!=null) {var p=new Path {Data=Geometry.Parse(parts[0]),StrokeThickness=2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round};p.SetResourceReference(Shape.StrokeProperty,brushKey);canvas.Children.Add(p);}
        if(parts[1]!=null) {var p=new Path {Data=Geometry.Parse(parts[1])};p.SetResourceReference(Shape.FillProperty,brushKey);canvas.Children.Add(p);}
        return new Viewbox {Width=size,Height=size,Child=canvas};
    }
    // Coloured rounded square with a white icon, as in the iOS settings list.
    public static Border IconTile(string name,Color color,double size) {
        var tile=new Border {Width=size,Height=size,CornerRadius=new CornerRadius(size*.27),
            Background=new LinearGradientBrush(Lighter(color,.18),color,new Point(0,0),new Point(0,1)),
            Child=Icon(name,size*.6,Brushes.White,2.1)};
        return tile;
    }
    public static Color Lighter(Color c,double amount) {
        return Color.FromRgb((byte)(c.R+(255-c.R)*amount),(byte)(c.G+(255-c.G)*amount),(byte)(c.B+(255-c.B)*amount));
    }

    public static TextBlock Text(string text,double size,string brushKey) {return Text(text,size,brushKey,FontWeights.Normal);}
    public static TextBlock Text(string text,double size,string brushKey,FontWeight weight) {
        var t=new TextBlock {Text=text,FontSize=size,FontWeight=weight,TextTrimming=TextTrimming.CharacterEllipsis};
        t.SetResourceReference(TextBlock.ForegroundProperty,brushKey);
        return t;
    }
    public static TextBlock Header(string text) {
        var t=Text(text.ToUpperInvariant(),11.5,"Faint",FontWeights.SemiBold);
        t.Margin=new Thickness(14,20,0,7);
        return t;
    }
    public static Border Card(UIElement content) {
        var card=new Border {CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),Child=content,ClipToBounds=false};
        card.SetResourceReference(Border.BackgroundProperty,"Card");card.SetResourceReference(Border.BorderBrushProperty,"CardLine");
        return card;
    }
    // A grouped list: rows separated by hairlines; the card clips the hover of the first and last row.
    public static Border List(params UIElement[] rows) {
        var stack=new StackPanel();
        bool first=true;
        foreach(var row in rows) {
            if(row==null) continue;
            if(!first) {var line=new Border {Height=1,Margin=new Thickness(16,0,0,0)};line.SetResourceReference(Border.BackgroundProperty,"Line");stack.Children.Add(line);}
            stack.Children.Add(row);first=false;
        }
        var card=Card(stack);
        card.Loaded+=delegate {ClipRounded(card,stack,13);};
        card.SizeChanged+=delegate {ClipRounded(card,stack,13);};
        return card;
    }
    static void ClipRounded(Border card,FrameworkElement inner,double radius) {
        inner.Clip=new RectangleGeometry(new Rect(0,0,inner.ActualWidth,inner.ActualHeight),radius,radius);
    }
    // Row with a title, an optional subtitle and an accessory on the right; clickable when an action is given.
    public const double RowHeight=76;
    public static Control Row(string title,string subtitle,UIElement accessory,Action click) {
        TextBlock subtitleText;
        return Row(title,subtitle,accessory,click,out subtitleText);
    }
    public static Control Row(string title,string subtitle,UIElement accessory,Action click,out TextBlock subtitleText) {
        var grid=new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
        var texts=new StackPanel {VerticalAlignment=VerticalAlignment.Center};
        texts.Children.Add(Text(title,14,"Text",FontWeights.SemiBold));
        subtitleText=Text(subtitle??"",12.5,"Sub");subtitleText.Margin=new Thickness(0,2,0,0);
        // Up to two lines of hint; every row has the same fixed height, so changing text never moves the page.
        subtitleText.TextWrapping=TextWrapping.Wrap;subtitleText.TextTrimming=TextTrimming.CharacterEllipsis;subtitleText.MaxHeight=34;
        subtitleText.Visibility=string.IsNullOrEmpty(subtitle)?Visibility.Collapsed:Visibility.Visible;
        texts.Children.Add(subtitleText);
        grid.Children.Add(texts);
        if(accessory!=null) {var a=accessory as FrameworkElement;if(a!=null){a.VerticalAlignment=VerticalAlignment.Center;a.Margin=new Thickness(12,0,0,0);}Grid.SetColumn(accessory,1);grid.Children.Add(accessory);}
        var button=new Button {Style=Style("Row"),Content=grid,Padding=new Thickness(16,10,16,10),Height=RowHeight};
        if(click!=null) button.Click+=delegate {click();};
        else {button.Cursor=Cursors.Arrow;button.IsHitTestVisible=accessory!=null;}
        return button;
    }
    public static FrameworkElement Value(string text) {
        var panel=new StackPanel {Orientation=Orientation.Horizontal};
        var t=Text(text,13,"Sub");t.VerticalAlignment=VerticalAlignment.Center;panel.Children.Add(t);
        var chevron=Icon("chevron",14,"Faint");chevron.Margin=new Thickness(4,1,0,0);panel.Children.Add(chevron);
        panel.Tag=t;
        return panel;
    }
    public static void SetValue(FrameworkElement value,string text) {((TextBlock)value.Tag).Text=text;}
    public static FrameworkElement Check() {return Icon("check",18,B(Blue),2.4);}

    public static ToggleButton Pill(Action<bool> changed) {
        var pill=new ToggleButton {Style=Style("Pill")};
        pill.Click+=delegate {changed(pill.IsChecked==true);};
        return pill;
    }
    public static Button Soft(string text,Action click) {
        var b=new Button {Style=Style("Soft"),Content=text};
        b.Click+=delegate {click();};
        return b;
    }
    public static Button Soft(string icon,string text,Action click) {
        var content=new StackPanel {Orientation=Orientation.Horizontal};
        var i=Icon(icon,15,"Text");i.Margin=new Thickness(0,0,7,0);content.Children.Add(i);
        content.Children.Add(new TextBlock {Text=text,VerticalAlignment=VerticalAlignment.Center});
        var b=new Button {Style=Style("Soft"),Content=content};
        b.Click+=delegate {click();};
        return b;
    }
    public static Border KeyCap(string text) {
        var cap=new Border {CornerRadius=new CornerRadius(6),BorderThickness=new Thickness(1),Padding=new Thickness(8,3,8,4),Margin=new Thickness(0,0,6,0),
            Child=Text(text,12,"Text",FontWeights.SemiBold)};
        cap.SetResourceReference(Border.BackgroundProperty,"Key");cap.SetResourceReference(Border.BorderBrushProperty,"KeyLine");
        return cap;
    }
    // Key caps for "Win+Alt+Пробел" with small pluses between them.
    public static void FillKeys(Panel panel,string combo) {
        panel.Children.Clear();
        string[] keys=combo.Split('+');
        for(int i=0;i<keys.Length;i++) {
            if(i>0) {var plus=Text("+",12,"Faint");plus.Margin=new Thickness(0,0,6,0);plus.VerticalAlignment=VerticalAlignment.Center;panel.Children.Add(plus);}
            panel.Children.Add(KeyCap(keys[i]));
        }
    }
    public static Border Chip(UIElement content) {
        var chip=new Border {CornerRadius=new CornerRadius(14),Padding=new Thickness(10,5,12,5),Margin=new Thickness(4,0,4,0),Child=content,BorderThickness=new Thickness(1)};
        chip.SetResourceReference(Border.BackgroundProperty,"Chip");chip.SetResourceReference(Border.BorderBrushProperty,"CardLine");
        return chip;
    }
    public static Style Style(string key) {return (Style)Shared[key];}
    static ResourceDictionary shared;
    static ResourceDictionary Shared {get {if(shared==null) shared=(ResourceDictionary)XamlReader.Parse(StylesXaml);return shared;}}

    // Pulse used by orbs and badges: a ring that grows and fades, forever.
    public static void Pulse(UIElement ring,double seconds,double to) {
        var scale=new ScaleTransform(1,1);ring.RenderTransform=scale;ring.RenderTransformOrigin=new Point(.5,.5);
        var grow=new DoubleAnimation(.92,to,TimeSpan.FromSeconds(seconds)) {RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new QuadraticEase {EasingMode=EasingMode.EaseOut}};
        var fade=new DoubleAnimation(.9,0,TimeSpan.FromSeconds(seconds)) {RepeatBehavior=RepeatBehavior.Forever};
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,grow);scale.BeginAnimation(ScaleTransform.ScaleYProperty,grow);
        ring.BeginAnimation(UIElement.OpacityProperty,fade);
    }
    public static void FadeIn(UIElement element,double offsetY) {
        var move=new TranslateTransform(0,offsetY);element.RenderTransform=move;
        var ease=new CubicEase {EasingMode=EasingMode.EaseOut};
        move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(offsetY,0,TimeSpan.FromSeconds(.25)) {EasingFunction=ease});
        element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromSeconds(.22)));
    }

    // Windows 11 backdrop: Mica behind the window and the dark or light title bar.
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int value,int size);
    public static void Backdrop(Window window,bool dark,bool mica) {
        var hwnd=new WindowInteropHelper(window).Handle;
        if(hwnd==IntPtr.Zero) return;
        int on=dark?1:0;DwmSetWindowAttribute(hwnd,20,ref on,4);
        if(mica) {
            var source=HwndSource.FromHwnd(hwnd);
            if(source!=null) source.CompositionTarget.BackgroundColor=Colors.Transparent;
            int kind=2;DwmSetWindowAttribute(hwnd,38,ref kind,4);
        }
    }
    // Rounded corners for borderless windows (DWMWA_WINDOW_CORNER_PREFERENCE = round).
    public static void RoundCorners(Window window) {
        var hwnd=new WindowInteropHelper(window).Handle;
        int round=2;DwmSetWindowAttribute(hwnd,33,ref round,4);
    }

    // GDI+ bitmap to a WPF image, reusing the writeable bitmap while the size stays the same.
    public static WriteableBitmapHolder Frames() {return new WriteableBitmapHolder();}
}

public class WriteableBitmapHolder {
    public System.Windows.Media.Imaging.WriteableBitmap Bitmap;
    public System.Windows.Media.Imaging.WriteableBitmap Update(System.Drawing.Bitmap source) {
        if(source==null) return Bitmap;
        int w=source.Width,h=source.Height;
        if(Bitmap==null || Bitmap.PixelWidth!=w || Bitmap.PixelHeight!=h)
            Bitmap=new System.Windows.Media.Imaging.WriteableBitmap(w,h,96,96,PixelFormats.Bgra32,null);
        var data=source.LockBits(new System.Drawing.Rectangle(0,0,w,h),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try {Bitmap.WritePixels(new Int32Rect(0,0,w,h),data.Scan0,data.Stride*h,data.Stride);}
        finally {source.UnlockBits(data);}
        return Bitmap;
    }
}

// Segmented control (iOS UISegmentedControl): one choice of a few, the selected one raised.
public class Segments : Border {
    readonly List<RadioButton> items=new List<RadioButton>();
    static int groups;
    public Segments(string[] names,Action<int> chosen) {
        CornerRadius=new CornerRadius(10);Padding=new Thickness(3);
        SetResourceReference(BackgroundProperty,"Field");
        var grid=new System.Windows.Controls.Primitives.UniformGrid {Rows=1};
        string group="seg"+(++groups);
        for(int i=0;i<names.Length;i++) {
            int index=i;
            var item=new RadioButton {Style=Ui.Style("Seg"),Content=names[i],GroupName=group,Padding=new Thickness(names.Length>4?2:8,6,names.Length>4?2:8,6)};
            item.Click+=delegate {chosen(index);};
            items.Add(item);grid.Children.Add(item);
        }
        Child=grid;
    }
    public void Select(int index) {for(int i=0;i<items.Count;i++) items[i].IsChecked=i==index;}
}

// Slider with a thin track and a white knob; reports values while dragging and once when released.
public class Thin : Grid {
    readonly Border fill=new Border {Height=4,CornerRadius=new CornerRadius(2),HorizontalAlignment=HorizontalAlignment.Left,Background=Ui.B(Ui.Blue)};
    readonly Ellipse knob=new Ellipse {Width=22,Height=22,Fill=Brushes.White,HorizontalAlignment=HorizontalAlignment.Left,Effect=new DropShadowEffect {BlurRadius=6,ShadowDepth=1,Opacity=.3}};
    readonly double min,max; double value;
    public Action<double> Changed,Released;
    public Thin(double min,double max) {
        this.min=min;this.max=max;Height=26;Background=Brushes.Transparent;Cursor=Cursors.Hand;
        var track=new Border {Height=4,CornerRadius=new CornerRadius(2)};track.SetResourceReference(Border.BackgroundProperty,"Track");
        Children.Add(track);Children.Add(fill);Children.Add(knob);
        SizeChanged+=delegate {Place();};
        MouseLeftButtonDown+=(s,e)=>{CaptureMouse();Drag(e.GetPosition(this).X);};
        MouseMove+=(s,e)=>{if(IsMouseCaptured) Drag(e.GetPosition(this).X);};
        MouseLeftButtonUp+=(s,e)=>{if(!IsMouseCaptured) return;ReleaseMouseCapture();if(Released!=null) Released(value);};
    }
    public double Value {get {return value;} set {this.value=Math.Max(min,Math.Min(max,value));Place();}}
    void Drag(double x) {
        double usable=Math.Max(1,ActualWidth-22);
        Value=min+(max-min)*Math.Max(0,Math.Min(1,(x-11)/usable));
        if(Changed!=null) Changed(value);
    }
    void Place() {
        double usable=Math.Max(0,ActualWidth-22),f=(value-min)/(max-min);
        knob.Margin=new Thickness(usable*f,0,0,0);fill.Width=usable*f+11;
    }
}
