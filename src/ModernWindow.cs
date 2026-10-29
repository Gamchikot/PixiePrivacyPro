using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using Keys=System.Windows.Forms.Keys;
using Path=System.Windows.Shapes.Path;

// The main window (WPF): sidebar sections as in iOS Settings, the privacy orb, the live preview and grouped settings.
// It owns no logic: it reads MainWindow's state on a timer and calls its actions.
public class ModernWindow : Window {
    readonly MainWindow app;
    readonly DispatcherTimer refresh=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(400)};
    readonly Grid pageHost=new Grid();
    readonly TextBlock pageTitle=Ui.Text("",28,"Text",FontWeights.SemiBold),pageSubtitle=Ui.Text("",13.5,"Sub");
    readonly Dictionary<string,FrameworkElement> pages=new Dictionary<string,FrameworkElement>();
    readonly Dictionary<string,Action> renderers=new Dictionary<string,Action>();
    readonly Dictionary<string,RadioButton> nav=new Dictionary<string,RadioButton>();
    readonly Dictionary<string,string[]> titles=new Dictionary<string,string[]>();
    readonly List<Action> always=new List<Action>();
    string current;
    bool shuttingDown,recording;
    DateTime? closedSince;
    int lastMode=-2;

    public ModernWindow(MainWindow app) {
        this.app=app;
        Title="Pixie Privacy Pro";Width=1120;Height=760;MinWidth=1000;MinHeight=660;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        FontFamily=new FontFamily("Segoe UI Variable Text, Segoe UI");
        SetResourceReference(BackgroundProperty,"Content");
        UseLayoutRounding=true;
        try {Icon=BitmapFrame.Create(new Uri(System.Windows.Forms.Application.ExecutablePath.Replace(".exe",".ico")));} catch {}
        Resources.MergedDictionaries.Add(new ResourceDictionary());
        Ui.ApplyTheme(Resources,Ui.Dark);
        // The system caption buttons were drawn under the content and barely showed, so the window draws its own.
        // One pixel of glass keeps the Windows shadow without bringing the system buttons back.
        WindowChrome.SetWindowChrome(this,new WindowChrome {GlassFrameThickness=new Thickness(0,0,0,1),CaptionHeight=38,ResizeBorderThickness=new Thickness(6),UseAeroCaptionButtons=false,CornerRadius=new CornerRadius(0)});
        SourceInitialized+=delegate {
            Ui.Backdrop(this,Ui.Dark,false);Ui.RoundCorners(this);
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(CaptionHook);
        };
        StateChanged+=delegate {
            // A maximized chrome window hangs over the screen edge by its resize frame.
            double frame=0;
            if(WindowState==WindowState.Maximized) frame=(GetSystemMetrics(32)+GetSystemMetrics(92))/VisualTreeHelper.GetDpi(this).DpiScaleX;
            ((FrameworkElement)Content).Margin=new Thickness(frame);
            maxGlyph.Text=WindowState==WindowState.Maximized?"":"";
        };
        System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(this);

        var root=new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(236)});
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.Children.Add(Sidebar());
        var content=new Border();content.SetResourceReference(Border.BackgroundProperty,"Content");
        Grid.SetColumn(content,1);root.Children.Add(content);
        var main=new DockPanel {Margin=new Thickness(34,40,34,0)};
        var head=new StackPanel {Margin=new Thickness(0,0,0,8)};
        head.Children.Add(pageTitle);pageSubtitle.Margin=new Thickness(0,2,0,0);head.Children.Add(pageSubtitle);
        DockPanel.SetDock(head,Dock.Top);main.Children.Add(head);
        // Not focusable: a click on empty space must not draw the dotted keyboard-focus frame around the page.
        var scroll=new ScrollViewer {Content=pageHost,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,Padding=new Thickness(0,0,0,24),Focusable=false,FocusVisualStyle=null};
        main.Children.Add(scroll);
        content.Child=main;
        var caption=CaptionButtons();Grid.SetColumn(caption,1);root.Children.Add(caption);
        Content=root;

        AddPage("home",L.T("Главная"),L.T("Объектив, микрофон и кто сейчас читает камеру"),Home());
        AddPage("privacy",L.T("Приватность"),L.T("Когда закрывать объектив и кому его открывать"),PrivacyPage());
        AddPage("camera",L.T("Камера"),L.T("Настройки хранятся в самой камере и действуют во всех программах"),CameraPage());
        AddPage("mic",L.T("Микрофон"),L.T("Микрофон камеры и кто сейчас слушает"),MicrophonePage());
        AddPage("alert",L.T("Предупреждение"),L.T("«Я вижу тебя» — когда объектив открыли не через Pixie Pro"),AlertPage());
        AddPage("look",L.T("Оформление"),L.T("Тема, значки в трее и что видят приложения при приватности"),LookPage());
        AddPage("journal",L.T("Журнал"),L.T("Что происходило с камерой и микрофоном"),JournalPage());
        Navigate("home");

        refresh.Tick+=delegate {Render();};
        IsVisibleChanged+=delegate {if(IsVisible) {refresh.Start();Render();} else refresh.Stop();};
        PreviewKeyDown+=RecordKey;
        Deactivated+=delegate {StopRecording(null);};
        Microsoft.Win32.SystemEvents.UserPreferenceChanged+=SystemThemeChanged;
    }
    void SystemThemeChanged(object sender,Microsoft.Win32.UserPreferenceChangedEventArgs e) {
        if(e.Category==Microsoft.Win32.UserPreferenceCategory.General) Dispatcher.BeginInvoke((Action)ApplyTheme);
    }
    public void ApplyTheme() {
        bool dark=Ui.Dark;
        Ui.ApplyTheme(Resources,dark);
        Ui.Backdrop(this,dark,false);
    }

    // ---------- caption buttons ----------
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    Border maxButton;TextBlock maxGlyph;
    FrameworkElement CaptionButtons() {
        var panel=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top};
        TextBlock glyph;
        panel.Children.Add(CaptionButton("",false,out glyph,()=>{WindowState=WindowState.Minimized;}));
        panel.Children.Add(maxButton=CaptionButton("",false,out maxGlyph,ToggleMaximize));
        panel.Children.Add(CaptionButton("",true,out glyph,Close));
        return panel;
    }
    Border CaptionButton(string code,bool close,out TextBlock glyph,Action click) {
        glyph=new TextBlock {Text=code,FontFamily=new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),FontSize=10,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        glyph.SetResourceReference(TextBlock.ForegroundProperty,"Text");
        var button=new Border {Width=46,Height=32,Background=Brushes.Transparent,Child=glyph,Tag=close};
        WindowChrome.SetIsHitTestVisibleInChrome(button,true);
        button.MouseEnter+=delegate {Highlight(button,true);};
        button.MouseLeave+=delegate {Highlight(button,false);};
        button.MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e) {e.Handled=true;};
        button.MouseLeftButtonUp+=delegate(object s,MouseButtonEventArgs e) {e.Handled=true;Highlight(button,false);click();};
        return button;
    }
    void Highlight(Border button,bool on) {
        var glyph=(TextBlock)button.Child;
        if(!on) {button.Background=Brushes.Transparent;glyph.SetResourceReference(TextBlock.ForegroundProperty,"Text");return;}
        if((bool)button.Tag) {button.Background=new SolidColorBrush(Color.FromRgb(0xC4,0x2B,0x1C));glyph.Foreground=Brushes.White;}
        else button.SetResourceReference(Border.BackgroundProperty,"Hover");
    }
    void ToggleMaximize() {WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;}
    // The maximize button answers as HTMAXBUTTON so Windows 11 shows its snap layouts on hover.
    bool overMax;
    IntPtr CaptionHook(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled) {
        const int NcHitTest=0x84,NcLButtonDown=0xA1,NcLButtonUp=0xA2,NcMouseLeave=0x2A2,HtMaxButton=9;
        if(msg==NcHitTest && maxButton!=null && maxButton.IsVisible) {
            int value=unchecked((int)lParam.ToInt64());
            var p=maxButton.PointFromScreen(new Point((short)(value&0xFFFF),(short)(value>>16)));
            bool inside=p.X>=0 && p.Y>=0 && p.X<maxButton.ActualWidth && p.Y<maxButton.ActualHeight;
            if(inside!=overMax) {overMax=inside;Highlight(maxButton,inside);}
            if(inside) {handled=true;return new IntPtr(HtMaxButton);}
        }
        else if(msg==NcLButtonDown && wParam.ToInt64()==HtMaxButton) handled=true;
        else if(msg==NcLButtonUp && wParam.ToInt64()==HtMaxButton) {handled=true;ToggleMaximize();}
        else if(msg==NcMouseLeave && overMax) {overMax=false;Highlight(maxButton,false);}
        return IntPtr.Zero;
    }
    public void Open() {
        if(!IsVisible) Show();
        if(WindowState==WindowState.Minimized) WindowState=WindowState.Normal;
        Activate();
        Render();
    }
    public void Shutdown() {shuttingDown=true;Microsoft.Win32.SystemEvents.UserPreferenceChanged-=SystemThemeChanged;Close();}
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e) {
        // Closing the window keeps Pixie Pro in the tray.
        if(!shuttingDown) {e.Cancel=true;StopRecording(null);Hide();}
        base.OnClosing(e);
    }
    public bool PreviewShown {get {return IsVisible && WindowState!=WindowState.Minimized && current=="camera";}}

    // ---------- sidebar ----------
    TextBlock deviceText,footerText;
    FrameworkElement Sidebar() {
        var side=new Border {BorderThickness=new Thickness(0,0,1,0)};
        side.SetResourceReference(Border.BackgroundProperty,"Sidebar");side.SetResourceReference(Border.BorderBrushProperty,"SideLine");
        var dock=new DockPanel {Margin=new Thickness(14,44,14,18)};
        var brand=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(8,0,0,18)};
        var logo=new Border {Width=36,Height=36,CornerRadius=new CornerRadius(10),
            Background=new LinearGradientBrush(Ui.C("#34D399"),Ui.C("#0EA5E9"),new Point(0,0),new Point(1,1)),Child=Ui.Icon("shieldcheck",21,Brushes.White,2)};
        brand.Children.Add(logo);
        var names=new StackPanel {Margin=new Thickness(11,0,0,0),VerticalAlignment=VerticalAlignment.Center};
        names.Children.Add(Ui.Text("Pixie Privacy Pro",14.5,"Text",FontWeights.SemiBold));
        deviceText=Ui.Text("EMEET PIXY",11.5,"Sub");names.Children.Add(deviceText);
        brand.Children.Add(names);
        DockPanel.SetDock(brand,Dock.Top);dock.Children.Add(brand);
        footerText=Ui.Text("",11.5,"Faint");footerText.TextWrapping=TextWrapping.Wrap;footerText.Margin=new Thickness(10,0,6,0);
        DockPanel.SetDock(footerText,Dock.Bottom);dock.Children.Add(footerText);
        var list=new StackPanel();
        AddNav(list,"home",L.T("Главная"),"shield",Ui.Green);
        AddNav(list,"privacy",L.T("Приватность"),"lock",Ui.Blue);
        AddNav(list,"camera",L.T("Камера"),"video",Ui.Purple);
        AddNav(list,"mic",L.T("Микрофон"),"mic",Ui.Orange);
        AddNav(list,"alert",L.T("Предупреждение"),"eye",Ui.Pink);
        AddNav(list,"look",L.T("Оформление"),"image",Ui.Teal);
        AddNav(list,"journal",L.T("Журнал"),"journal",Ui.Gray);
        dock.Children.Add(list);
        side.Child=dock;
        always.Add(()=>{
            bool known=app.Mode>=0;
            deviceText.Text="EMEET PIXY · "+(known?L.T("подключена"):L.T("нет связи"));
            string mic=app.MicrophoneText;int dot=mic.LastIndexOf(" · ");
            string service=dot>0?L.T("служба: ")+mic.Substring(dot+3):"";
            footerText.Text=Version.Length>0?L.T("Версия ")+Version+(service.Length>0?" · "+service:""):service;
            ((Border)nav["home"].Tag).Background=new LinearGradientBrush(Ui.Lighter(OrbColor(app.Mode),.18),OrbColor(app.Mode),new Point(0,0),new Point(0,1));
        });
        return side;
    }
    static string Version {get {
        var attributes=typeof(ModernWindow).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute),false);
        return attributes.Length>0?((System.Reflection.AssemblyInformationalVersionAttribute)attributes[0]).InformationalVersion:"";
    }}
    void AddNav(Panel list,string key,string title,string icon,Color color) {
        var row=new StackPanel {Orientation=Orientation.Horizontal};
        var tile=Ui.IconTile(icon,color,28);row.Children.Add(tile);
        var text=Ui.Text(title,14,"Text");text.Margin=new Thickness(12,0,0,0);text.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(text);
        var item=new RadioButton {Style=Ui.Style("Nav"),Content=row,GroupName="nav",Tag=tile};
        item.Click+=delegate {Navigate(key);};
        nav[key]=item;list.Children.Add(item);
    }
    void AddPage(string key,string title,string subtitle,FrameworkElement page) {pages[key]=page;titles[key]=new[]{title,subtitle};}
    public void Navigate(string key) {
        if(current==key) return;
        current=key;nav[key].IsChecked=true;
        pageTitle.Text=titles[key][0];pageSubtitle.Text=titles[key][1];
        pageHost.Children.Clear();
        var page=pages[key];pageHost.Children.Add(page);
        Ui.FadeIn(page,12);
        if(key=="journal") LoadJournal(true);
        Render();
    }
    void Render() {
        if(!IsVisible) return;
        if(app.Mode==2 && lastMode!=2) closedSince=DateTime.UtcNow;
        if(app.Mode!=2) closedSince=null;
        lastMode=app.Mode;
        foreach(var a in always) a();
        Action page;
        if(current!=null && renderers.TryGetValue(current,out page)) page();
    }

    // ---------- layout helpers ----------
    static Grid Columns(UIElement left,UIElement right) {
        var grid=new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(20)});grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(left);Grid.SetColumn(right,2);grid.Children.Add(right);
        return grid;
    }
    static StackPanel Stack(params UIElement[] items) {
        var stack=new StackPanel();
        foreach(var i in items) if(i!=null) stack.Children.Add(i);
        return stack;
    }
    static void Show(UIElement element,bool visible) {element.Visibility=visible?Visibility.Visible:Visibility.Collapsed;}
    static string Join(List<string> apps) {return CapabilityUsage.Describe(apps);}
    static Color OrbColor(int mode) {return mode==2?Ui.Green:mode<0?Ui.Gray:Ui.Red;}
    static string ShortTimer(int seconds) {
        if(seconds<0) return "—";
        if(seconds==0) return L.T("Никогда");
        return seconds<60||seconds%60!=0?seconds+L.T(" с"):seconds/60+L.T(" мин");
    }

    // ---------- home ----------
    readonly GradientStop[] orbStops={new GradientStop(Ui.C("#5BEA86"),0),new GradientStop(Ui.C("#2DBE57"),.55),new GradientStop(Ui.C("#178A3A"),1)};
    Ellipse orbRing,orbHalo;DropShadowEffect orbGlow;ContentControl orbIcon;TextBlock orbTitle,orbSubtitle;
    Border readersChip,trackingChip;Ellipse readersDot;TextBlock readersText;StackPanel hotKeyChip;Border hotKeyChipBox;
    int orbState=-9;
    Image previewImage;Canvas cropLayer;Path cropShade,cropLine;TextBlock previewLabelText;Ellipse previewDot;Border previewStart,previewStop;
    readonly WriteableBitmapHolder liveFrames=new WriteableBitmapHolder(),stateFrames=new WriteableBitmapHolder();
    System.Drawing.Bitmap shownState;bool showingLive,liveWanted,renderingHooked;
    WriteableBitmap live;long liveStamp;
    Segments shapes;Thin cropSize;TextBlock cropPercent;
    ToggleButton trackingPill,micPill;TextBlock trackingTileSub,micTileSub,timerTileSub,cameraTileSub;FrameworkElement timerValue;TextBlock cameraTileValue;Border micTileIconBox;
    StackPanel banners;
    string lastHint,dismissedHint;DateTime hintShown;

    FrameworkElement Home() {
        var top=new Grid {Height=356};
        top.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        top.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(18)});
        top.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        top.Children.Add(OrbCard());
        var tiles=new UniformGrid {Rows=2,Columns=2,Margin=new Thickness(-7,0,-7,0)};
        Grid.SetColumn(tiles,2);top.Children.Add(tiles);
        tiles.Children.Add(Tile("target",Ui.Blue,L.T("Автослежение"),out trackingTileSub,trackingPill=Ui.Pill(on=>Run(app.SwitchTracking())),null));
        micPill=Ui.Pill(on=>Run(app.ChooseMicrophone(on?(app.AudioProcessing>=1&&app.AudioProcessing<=3?app.AudioProcessing:1):0)));
        var micTile=Tile("mic",Ui.Orange,L.T("Микрофон"),out micTileSub,micPill,null);
        micTileIconBox=(Border)((Grid)((StackPanel)((Border)micTile).Child).Children[0]).Children[0];
        tiles.Children.Add(micTile);
        timerValue=Ui.Value("");
        tiles.Children.Add(Tile("timer",Ui.Green,L.T("Таймер камеры"),out timerTileSub,timerValue,()=>Navigate("camera")));
        cameraTileValue=Ui.Text("",12.5,"Sub");
        tiles.Children.Add(Tile("video",Ui.Purple,L.T("Камера Pixie"),out cameraTileSub,cameraTileValue,()=>Navigate("privacy")));
        for(int i=0;i<4;i++) ((FrameworkElement)tiles.Children[i]).Margin=i<2?new Thickness(7,0,7,9):new Thickness(7,9,7,0);
        banners=new StackPanel {Margin=new Thickness(0,18,0,0)};
        renderers["home"]=RenderHome;
        return Stack(top,ActivityCard(),banners);
    }
    FrameworkElement OrbCard() {
        var grid=new Grid();
        var center=new StackPanel {VerticalAlignment=VerticalAlignment.Center};
        var orbBox=new Grid {Width=210,Height=210};
        orbHalo=new Ellipse {Width=188,Height=188,StrokeThickness=1.5};
        orbRing=new Ellipse {Width=180,Height=180,StrokeThickness=2.5};
        var brush=new RadialGradientBrush {GradientOrigin=new Point(.36,.3),Center=new Point(.42,.36),RadiusX=.72,RadiusY=.72};
        foreach(var s in orbStops) brush.GradientStops.Add(s);
        orbGlow=new DropShadowEffect {BlurRadius=70,ShadowDepth=0,Opacity=.6,Color=Ui.Green};
        var orb=new Ellipse {Width=164,Height=164,Fill=brush,Effect=orbGlow};
        var shine=new Ellipse {Width=164,Height=164,IsHitTestVisible=false,
            Fill=new LinearGradientBrush(Color.FromArgb(70,255,255,255),Color.FromArgb(0,255,255,255),new Point(.5,0),new Point(.5,.55))};
        orbIcon=new ContentControl {Width=66,Height=66,IsHitTestVisible=false};
        orbBox.Children.Add(orbHalo);orbBox.Children.Add(orbRing);orbBox.Children.Add(orb);orbBox.Children.Add(shine);orbBox.Children.Add(orbIcon);
        var press=new ScaleTransform(1,1);
        var button=new Button {Style=Ui.Style("Bare"),Content=orbBox,HorizontalAlignment=HorizontalAlignment.Center,RenderTransform=press,RenderTransformOrigin=new Point(.5,.5),ToolTip=L.T("Переключить приватность")};
        button.PreviewMouseLeftButtonDown+=delegate {Spring(press,.94,.1);};
        button.PreviewMouseLeftButtonUp+=delegate {Spring(press,1,.45);};
        button.MouseLeave+=delegate {Spring(press,1,.3);};
        button.Click+=delegate {Run(app.TogglePrivacy());};
        center.Children.Add(button);
        orbTitle=Ui.Text("",23,"Text",FontWeights.SemiBold);orbTitle.HorizontalAlignment=HorizontalAlignment.Center;orbTitle.Margin=new Thickness(0,4,0,0);
        orbSubtitle=Ui.Text("",13,"Sub");orbSubtitle.HorizontalAlignment=HorizontalAlignment.Center;orbSubtitle.Margin=new Thickness(0,4,0,0);
        center.Children.Add(orbTitle);center.Children.Add(orbSubtitle);
        var chips=new WrapPanel {HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,16,0,0)};
        var readers=new StackPanel {Orientation=Orientation.Horizontal};
        readersDot=new Ellipse {Width=7,Height=7,Margin=new Thickness(0,1,7,0),VerticalAlignment=VerticalAlignment.Center};
        readersText=Ui.Text("",12.5,"Sub");readers.Children.Add(readersDot);readers.Children.Add(readersText);
        readersChip=Ui.Chip(readers);chips.Children.Add(readersChip);
        trackingChip=Ui.Chip(Ui.Text(L.T("Автослежение"),12.5,"Sub"));chips.Children.Add(trackingChip);
        hotKeyChip=new StackPanel {Orientation=Orientation.Horizontal};
        hotKeyChipBox=Ui.Chip(hotKeyChip);hotKeyChipBox.Padding=new Thickness(8,4,2,4);hotKeyChipBox.ToolTip=L.T("Горячая клавиша приватности");
        chips.Children.Add(hotKeyChipBox);
        center.Children.Add(chips);
        grid.Children.Add(center);
        var card=Ui.Card(grid);card.ClipToBounds=true;
        return card;
    }
    static void Spring(ScaleTransform t,double to,double seconds) {
        var a=new DoubleAnimation(to,TimeSpan.FromSeconds(seconds)) {EasingFunction=to<1?(IEasingFunction)new QuadraticEase():new ElasticEase {Oscillations=1,Springiness=6}};
        t.BeginAnimation(ScaleTransform.ScaleXProperty,a);t.BeginAnimation(ScaleTransform.ScaleYProperty,a);
    }
    void SetOrb(int mode) {
        int state=mode==2?2:mode<0?-1:1;
        if(state==orbState) return;
        bool first=orbState==-9;orbState=state;
        Color c=OrbColor(mode);
        Color[] stops=state==2?new[]{Ui.C("#5BEA86"),Ui.C("#2DBE57"),Ui.C("#178A3A")}:state==1?new[]{Ui.C("#FF7B6F"),Ui.C("#E5392E"),Ui.C("#A51F17")}:new[]{Ui.C("#B4B7BE"),Ui.C("#80848C"),Ui.C("#565A62")};
        var duration=TimeSpan.FromSeconds(first?0:.45);
        for(int i=0;i<3;i++) orbStops[i].BeginAnimation(GradientStop.ColorProperty,new ColorAnimation(stops[i],duration));
        orbGlow.BeginAnimation(DropShadowEffect.ColorProperty,new ColorAnimation(c,duration));
        orbRing.Stroke=Ui.B(Ui.Alpha(c,150));orbHalo.Stroke=Ui.B(Ui.Alpha(c,60));
        Ui.Pulse(orbRing,state==1?1.3:2.4,1.22);
        // The lock turns into the eye: the old symbol shrinks away, the new one springs in.
        var icon=Ui.Icon(state==2?"lock":state==1?"eye":"question",66,Brushes.White,2.1);
        var scale=new ScaleTransform(.6,.6);icon.RenderTransform=scale;icon.RenderTransformOrigin=new Point(.5,.5);
        orbIcon.Content=icon;
        var grow=new DoubleAnimation(.6,1,TimeSpan.FromSeconds(first?0:.5)) {EasingFunction=new BackEase {Amplitude=.5,EasingMode=EasingMode.EaseOut}};
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,grow);scale.BeginAnimation(ScaleTransform.ScaleYProperty,grow);
        icon.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromSeconds(first?0:.25)));
    }
    FrameworkElement PreviewCard() {
        var stack=new DockPanel {Margin=new Thickness(14)};
        var screen=new Grid {Height=212};
        var screenBorder=new Border {CornerRadius=new CornerRadius(12),Child=screen};
        screenBorder.SetResourceReference(Border.BackgroundProperty,"Preview");
        screen.SizeChanged+=delegate {screen.Clip=new RectangleGeometry(new Rect(0,0,screen.ActualWidth,screen.ActualHeight),12,12);LayoutCrop();};
        // The crop layer above takes the mouse; the image itself must not catch clicks meant for the controls below.
        previewImage=new Image {Stretch=Stretch.Uniform,IsHitTestVisible=false};
        RenderOptions.SetBitmapScalingMode(previewImage,BitmapScalingMode.HighQuality);
        screen.Children.Add(previewImage);
        cropLayer=new Canvas {Background=Brushes.Transparent,Cursor=Cursors.SizeAll};
        cropShade=new Path {Fill=new SolidColorBrush(Color.FromArgb(130,0,0,0)),IsHitTestVisible=false};
        cropLine=new Path {Stroke=Brushes.White,StrokeThickness=2,IsHitTestVisible=false};
        cropLayer.Children.Add(cropShade);cropLayer.Children.Add(cropLine);
        cropLayer.MouseLeftButtonDown+=StartCropDrag;cropLayer.MouseMove+=MoveCropDrag;cropLayer.MouseLeftButtonUp+=EndCropDrag;
        screen.Children.Add(cropLayer);
        var label=new StackPanel {Orientation=Orientation.Horizontal};
        previewDot=new Ellipse {Width=7,Height=7,Margin=new Thickness(0,0,7,0),VerticalAlignment=VerticalAlignment.Center};
        previewLabelText=new TextBlock {FontSize=11.5,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White};
        label.Children.Add(previewDot);label.Children.Add(previewLabelText);
        screen.Children.Add(new Border {Background=new SolidColorBrush(Color.FromArgb(170,0,0,0)),CornerRadius=new CornerRadius(14),Padding=new Thickness(9,4,11,4),
            HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(10),Child=label,IsHitTestVisible=false});
        previewStart=OverlayButton("play",L.T("Включить предпросмотр"),()=>Run(app.SwitchPreview()));
        previewStart.HorizontalAlignment=HorizontalAlignment.Center;previewStart.VerticalAlignment=VerticalAlignment.Bottom;previewStart.Margin=new Thickness(0,0,0,14);
        screen.Children.Add(previewStart);
        previewStop=OverlayButton("x",L.T("Остановить"),()=>Run(app.SwitchPreview()));
        previewStop.HorizontalAlignment=HorizontalAlignment.Right;previewStop.VerticalAlignment=VerticalAlignment.Top;previewStop.Margin=new Thickness(10);
        screen.Children.Add(previewStop);
        DockPanel.SetDock(screenBorder,Dock.Top);stack.Children.Add(screenBorder);
        shapes=new Segments(new[]{"1:1","4:3","16:9",L.T(CropSettings.Circle)},i=>{app.SetCropShape(new[]{"1:1","4:3","16:9",CropSettings.Circle}[i]);LayoutCrop();});
        shapes.Margin=new Thickness(0,12,0,0);
        DockPanel.SetDock(shapes,Dock.Top);stack.Children.Add(shapes);
        var bottom=new Grid {Margin=new Thickness(0,14,0,0)};
        foreach(var w in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto}) bottom.ColumnDefinitions.Add(new ColumnDefinition {Width=w});
        var frameLabel=Ui.Text(L.T("Рамка"),13,"Sub");frameLabel.VerticalAlignment=VerticalAlignment.Center;bottom.Children.Add(frameLabel);
        cropSize=new Thin(20,100) {Margin=new Thickness(12,0,8,0)};
        cropSize.Changed=v=>{app.SetCropSize((int)Math.Round(v));cropPercent.Text=(int)Math.Round(v)+"%";LayoutCrop();};
        Grid.SetColumn(cropSize,1);bottom.Children.Add(cropSize);
        cropPercent=Ui.Text("",13,"Sub");cropPercent.VerticalAlignment=VerticalAlignment.Center;cropPercent.Width=40;Grid.SetColumn(cropPercent,2);bottom.Children.Add(cropPercent);
        DockPanel.SetDock(bottom,Dock.Top);stack.Children.Add(bottom);
        // The one mirror switch: it flips the picture in the camera itself, so every program and this preview see it.
        var mirrorRow=new Grid {Margin=new Thickness(0,14,0,0)};
        mirrorRow.ColumnDefinitions.Add(new ColumnDefinition());mirrorRow.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
        var mirrorText=Stack(Ui.Text(L.T("Зеркально"),13.5,"Text",FontWeights.SemiBold),Ui.Text(L.T("В самой камере — так вас увидят все программы"),12,"Sub"));
        mirrorText.VerticalAlignment=VerticalAlignment.Center;mirrorRow.Children.Add(mirrorText);
        hwMirrorPill=Ui.Pill(on=>Run(app.ChooseHardwareMirror(on)));hwMirrorPill.Margin=new Thickness(12,0,0,0);
        Grid.SetColumn(hwMirrorPill,1);mirrorRow.Children.Add(hwMirrorPill);
        stack.Children.Add(mirrorRow);
        var crop=app.CurrentCrop;
        shapes.Select(Array.IndexOf(new[]{"1:1","4:3","16:9",CropSettings.Circle},crop.Shape));
        cropSize.Value=app.CropSize;cropPercent.Text=app.CropSize+"%";
        return Ui.Card(stack);
    }
    Border OverlayButton(string icon,string text,Action click) {
        var content=new StackPanel {Orientation=Orientation.Horizontal};
        content.Children.Add(Ui.Icon(icon,13,Brushes.White,2.2));
        content.Children.Add(new TextBlock {Text=text,Foreground=Brushes.White,FontSize=12.5,FontWeight=FontWeights.SemiBold,Margin=new Thickness(7,0,0,0),VerticalAlignment=VerticalAlignment.Center});
        var button=new Button {Style=Ui.Style("Bare"),Content=content};
        button.Click+=delegate {click();};
        return new Border {Background=new SolidColorBrush(Color.FromArgb(190,20,22,28)),BorderBrush=new SolidColorBrush(Color.FromArgb(60,255,255,255)),BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(14),Padding=new Thickness(12,6,14,6),Child=button};
    }
    // Called by MainWindow every 120 ms while the home page is visible: null state means live video,
    // which RenderLive copies straight into the image on every frame the window draws.
    public void ShowLive(System.Drawing.Bitmap state) {
        liveWanted=state==null;
        if(!liveWanted) ShowFrame(null,state);
        if(liveWanted!=renderingHooked) {
            if(liveWanted) CompositionTarget.Rendering+=RenderLive; else CompositionTarget.Rendering-=RenderLive;
            renderingHooked=liveWanted;
        }
    }
    void RenderLive(object sender,EventArgs e) {
        if(!liveWanted || !PreviewShown) {CompositionTarget.Rendering-=RenderLive;renderingHooked=false;liveWanted=false;return;}
        if(live==null) live=new WriteableBitmap(VideoPipeline.PreviewWidth,VideoPipeline.PreviewHeight,96,96,PixelFormats.Bgr32,null);
        bool fresh;
        live.Lock();
        try {
            fresh=app.CopyPreview(live.BackBuffer,live.BackBufferStride,ref liveStamp);
            if(fresh) live.AddDirtyRect(new Int32Rect(0,0,live.PixelWidth,live.PixelHeight));
        } finally {live.Unlock();}
        if(fresh && !showingLive) {previewImage.Source=live;showingLive=true;shownState=null;LayoutCrop();}
    }
    public void ShowFrame(System.Drawing.Bitmap frame,System.Drawing.Bitmap state) {
        if(frame!=null) {previewImage.Source=liveFrames.Update(frame);showingLive=true;shownState=null;}
        else if(state!=null && (state!=shownState||showingLive)) {previewImage.Source=stateFrames.Update(state);shownState=state;showingLive=false;}
        LayoutCrop();
    }
    Rect ImageRect() {
        var source=previewImage.Source as BitmapSource;
        if(source==null||cropLayer.ActualWidth<=0) return Rect.Empty;
        double s=Math.Min(cropLayer.ActualWidth/source.PixelWidth,cropLayer.ActualHeight/source.PixelHeight);
        double w=source.PixelWidth*s,h=source.PixelHeight*s;
        return new Rect((cropLayer.ActualWidth-w)/2,(cropLayer.ActualHeight-h)/2,w,h);
    }
    void LayoutCrop() {
        if(cropLayer==null) return;
        var b=ImageRect();
        bool show=showingLive && !b.IsEmpty;
        cropShade.Visibility=cropLine.Visibility=show?Visibility.Visible:Visibility.Collapsed;
        cropLayer.IsHitTestVisible=show;
        if(!show) return;
        var crop=app.CurrentCrop;
        var r=new Rect(b.X+crop.X*b.Width,b.Y+crop.Y*b.Height,crop.W*b.Width,crop.H*b.Height);
        Geometry shape=crop.IsCircle?(Geometry)new EllipseGeometry(r):new RectangleGeometry(r,4,4);
        cropShade.Data=new CombinedGeometry(GeometryCombineMode.Exclude,new RectangleGeometry(b),shape);
        cropLine.Data=shape;
    }
    Point dragStart;float dragX,dragY;
    void StartCropDrag(object sender,MouseButtonEventArgs e) {dragStart=e.GetPosition(cropLayer);dragX=app.CurrentCrop.X;dragY=app.CurrentCrop.Y;cropLayer.CaptureMouse();}
    void MoveCropDrag(object sender,MouseEventArgs e) {
        if(!cropLayer.IsMouseCaptured) return;
        var b=ImageRect();if(b.IsEmpty) return;
        var p=e.GetPosition(cropLayer);
        app.MoveCrop(dragX+(float)((p.X-dragStart.X)/b.Width),dragY+(float)((p.Y-dragStart.Y)/b.Height),false);
        LayoutCrop();
    }
    void EndCropDrag(object sender,MouseButtonEventArgs e) {
        if(!cropLayer.IsMouseCaptured) return;
        cropLayer.ReleaseMouseCapture();
        app.MoveCrop(app.CurrentCrop.X,app.CurrentCrop.Y,true);
    }
    FrameworkElement Tile(string icon,Color color,string title,out TextBlock subtitle,UIElement accessory,Action click) {
        var stack=new StackPanel();
        var top=new Grid {Margin=new Thickness(0,0,0,12)};
        var iconBox=new Border {Width=36,Height=36,CornerRadius=new CornerRadius(10),Background=Ui.B(Ui.Alpha(color,48)),HorizontalAlignment=HorizontalAlignment.Left,
            Child=Ui.Icon(icon,19,Ui.B(color),2)};
        top.Children.Add(iconBox);
        if(accessory!=null) {var a=(FrameworkElement)accessory;a.HorizontalAlignment=HorizontalAlignment.Right;a.VerticalAlignment=VerticalAlignment.Center;top.Children.Add(accessory);}
        stack.Children.Add(top);
        stack.Children.Add(Ui.Text(title,14.5,"Text",FontWeights.SemiBold));
        subtitle=Ui.Text("",12.5,"Sub");subtitle.Margin=new Thickness(0,3,0,0);stack.Children.Add(subtitle);
        var card=Ui.Card(stack);card.Padding=new Thickness(16,14,16,14);card.Margin=new Thickness(7,0,7,0);
        if(click!=null) {card.Cursor=Cursors.Hand;card.MouseLeftButtonUp+=delegate {click();};}
        return card;
    }
    void RenderHome() {
        int mode=app.Mode;
        SetOrb(mode);
        orbTitle.Text=mode==2?L.T("Объектив закрыт"):mode<0?L.T("Нет связи с камерой"):L.T("Камера открыта");
        if(mode==2) orbSubtitle.Text=L.T("Приватность включена")+(closedSince==null?"":" · "+MainWindow.Duration(DateTime.UtcNow-closedSince.Value));
        else if(mode<0) orbSubtitle.Text=L.T("Камера не отвечает по USB");
        else orbSubtitle.Text=(app.VideoLive?L.T("Видео идёт"):mode==3?L.T("Ожидание видео"):L.T("Объектив поднят"))+(app.OpenSince==null?"":" · "+MainWindow.Duration(DateTime.UtcNow-app.OpenSince.Value));
        var readers=app.Readers;
        bool reading=readers.Count>0||app.ConsumerActive;
        readersDot.Fill=Ui.B(reading?Ui.Red:Ui.Green);
        readersText.Text=reading?L.T("Читает: ")+(readers.Count>0?Join(readers):L.T("приложение")):app.CameraUsers.Count>0?L.T("Камеру использует: ")+Join(app.CameraUsers):L.T("Никто не читает камеру");
        Show(trackingChip,mode==1);
        bool hotKey=Preferences.GetFlag(Preferences.HotKey,true);
        Show(hotKeyChipBox,hotKey);
        if(hotKey && (string)hotKeyChip.Tag!=HotKeySetting.Current) {Ui.FillKeys(hotKeyChip,HotKeySetting.Current);hotKeyChip.Tag=HotKeySetting.Current;}
        // Tiles.
        trackingPill.IsChecked=app.TrackingWanted;
        trackingPill.IsEnabled=!app.Busy && (mode==0||mode==1||mode==3);
        trackingTileSub.Text=mode==1?L.T("Включено"):mode==2?L.T("Объектив закрыт"):mode==3?(app.TrackingWanted?L.T("Включится с видео"):L.T("Выключено")):mode<0?L.T("Нет связи"):L.T("Выключено");
        int mic=app.MicrophoneChoice;
        micPill.IsChecked=mic>0;
        string micIcon=mic==0?"micoff":"mic";
        if((string)micTileIconBox.Tag!=micIcon) {micTileIconBox.Tag=micIcon;micTileIconBox.Child=Ui.Icon(micIcon,19,Ui.B(Ui.Orange),2);}
        micTileSub.Text=mic==0?L.T("Отключён полностью"):mic>0?MainWindow.MicrophoneNames[mic]:L.T("Проверка…");
        timerTileSub.Text=app.CameraTimer>0?L.T("Сама закроется"):app.CameraTimer==0?L.T("Выключен"):L.T("Нет данных");
        Ui.SetValue(timerValue,ShortTimer(app.CameraTimer));
        cameraTileValue.Text=app.MfCameraOn?"MF · DS":"DS";
        cameraTileSub.Text=app.ConsumerActive?L.T("Читает: ")+(app.Readers.Count>0?Join(app.Readers):L.T("приложение")):L.T("Готова для приложений");
        RenderActivity();
        RenderBanners();
    }
    // Preview: live picture with the crop frame, or what apps see right now.
    void RenderPreview() {
        bool live=app.CaptureRunning;
        previewDot.Fill=Ui.B(app.VideoLive?Ui.Red:Ui.Yellow);
        previewLabelText.Text=app.VideoLive?"LIVE · "+FrameSize():live?L.T("Камера запускается"):L.T("Заставка для приложений");
        Show(previewStart,!live && !app.PreviewOn);
        Show(previewStop,app.PreviewOn);
    }
    string FrameSize() {
        string size=app.LiveSize;
        if(size.Length==0) {var s=previewImage.Source as BitmapSource;size=s==null?"":s.PixelWidth+"×"+s.PixelHeight;}
        return size+(app.HardwareMirror==1?L.T(" · зеркально"):"");
    }
    void RenderBanners() {
        int mode=app.Mode;
        var wanted=new List<Tuple<string,string,string,string,Action>>();
        if(mode<0) wanted.Add(Tuple.Create("alert",L.T("Нет связи с камерой"),L.T("Проверьте USB-кабель. Пока камера не отвечает, переходы приватности не отслеживаются."),(string)null,(Action)null));
        else if(mode!=2 && !app.ConsumerActive && !app.PreviewOn && app.CameraUsers.Count==0 && !app.Busy)
            wanted.Add(Tuple.Create("alert",L.T("Объектив открыт, но камеру никто не использует"),
                Preferences.GetFlag(Preferences.AutoPrivacy,true)?L.T("Закроется сам через 15 с."):L.T("Автоматическое закрытие выключено в разделе «Приватность»."),L.T("Закрыть сейчас"),(Action)(()=>Run(app.TogglePrivacy()))));
        if(app.MicrophoneWarning) wanted.Add(Tuple.Create("mic",L.T("Защита микрофона"),app.MicrophoneText,(string)null,(Action)null));
        string hint=app.HintText;
        if(lastHint==null) lastHint=hint;
        if(hint!=lastHint) {lastHint=hint;hintShown=DateTime.UtcNow;dismissedHint=null;}
        if(hint!=dismissedHint && hintShown!=default(DateTime) && DateTime.UtcNow-hintShown<TimeSpan.FromSeconds(15))
            wanted.Add(Tuple.Create("info",(string)null,hint,L.T("Скрыть"),(Action)(()=>{dismissedHint=lastHint;})));
        // Rebuild only when the set of messages changes, so the entrance animation plays once.
        var key=new StringBuilder();
        foreach(var w in wanted) key.Append(w.Item2).Append('|').Append(w.Item3).Append('\n');
        if((string)banners.Tag==key.ToString()) return;
        banners.Tag=key.ToString();
        banners.Children.Clear();
        foreach(var w in wanted) banners.Children.Add(Banner(w.Item1,w.Item2,w.Item3,w.Item4,w.Item5));
    }
    FrameworkElement Banner(string icon,string title,string text,string button,Action click) {
        bool info=icon=="info";
        var grid=new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
        var i=Ui.Icon(info?"alert":icon=="mic"?"micoff":"alert",22,Ui.B(info?Ui.Blue:Ui.Red),2);i.Margin=new Thickness(0,0,14,0);i.VerticalAlignment=VerticalAlignment.Center;
        grid.Children.Add(i);
        var texts=new StackPanel {VerticalAlignment=VerticalAlignment.Center};
        if(title!=null) texts.Children.Add(Ui.Text(title,14,"Text",FontWeights.SemiBold));
        var body=Ui.Text(text,12.5,title==null?"Text":"Sub");body.TextWrapping=TextWrapping.Wrap;body.TextTrimming=TextTrimming.None;texts.Children.Add(body);
        Grid.SetColumn(texts,1);grid.Children.Add(texts);
        if(button!=null) {
            var b=Ui.Soft(button,()=>{click();banners.Tag=null;RenderBanners();});
            if(!info) {b.Background=Ui.B(Ui.Red);b.Foreground=Brushes.White;}
            b.Margin=new Thickness(14,0,0,0);b.VerticalAlignment=VerticalAlignment.Center;
            Grid.SetColumn(b,2);grid.Children.Add(b);
        }
        var border=new Border {CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),Padding=new Thickness(18,14,14,14),Margin=new Thickness(0,0,0,10),Child=grid};
        border.SetResourceReference(Border.BackgroundProperty,info?"InfoBg":"BannerBg");border.SetResourceReference(Border.BorderBrushProperty,info?"InfoLine":"BannerLine");
        Ui.FadeIn(border,-8);
        return border;
    }

    // ---------- privacy ----------
    FrameworkElement showPictureCheck,openAutoCheck;StackPanel trustedBox;string trustedShown;
    ToggleButton hotKeyPill,closeUnusedPill,lockPill,mfPill;StackPanel hotKeyKeys;TextBlock hotKeySub,mfSub;FrameworkElement cameraTimerValue;
    // Trusted apps: each with "Убрать", apps reading the camera now with "Добавить", and a file picker.
    // They matter only with "Открывать сразу"; otherwise the list is dimmed.
    FrameworkElement TrustedList(bool auto,List<string> trusted,List<string> readers) {
        var rows=new List<UIElement>();
        if(trusted.Count==0) rows.Add(Ui.Row(L.T("Любая программа"),auto?L.T("Добавьте программы, чтобы сразу открывать камеру только им"):L.T("Действует, когда выбрано «Открывать сразу»"),null,null));
        foreach(string name in trusted) {string n=name;rows.Add(Ui.Row(n,L.T("Сама открывает камеру"),SmallButton(L.T("Убрать"),()=>app.RemoveTrusted(n)),null));}
        foreach(string name in readers) if(!PixieReaders.Contains(trusted,name)) {string n=name;rows.Add(Ui.Row(n,L.T("Сейчас читает камеру Pixie"),SmallButton(L.T("Добавить"),()=>app.AddTrusted(n)),null));}
        rows.Add(Ui.Row(L.T("Добавить программу…"),L.T("Выберите её .exe на диске"),Ui.Value(""),()=>app.AddTrustedFromFile()));
        var list=Ui.List(rows.ToArray());
        list.IsEnabled=auto;list.Opacity=auto?1:.55;
        return list;
    }
    static Button SmallButton(string text,Action click) {
        var b=Ui.Soft(text,click);b.Padding=new Thickness(12,4,12,4);b.FontSize=12.5;
        return b;
    }
    FrameworkElement PrivacyPage() {
        showPictureCheck=Ui.Check();openAutoCheck=Ui.Check();trustedBox=new StackPanel();
        var left=Stack(
            Ui.Header(L.T("Когда приложение открывает камеру")),
            Ui.List(Ui.Row(L.T("Показывать заставку"),L.T("Объектив открываю я сам — безопаснее"),showPictureCheck,()=>app.SetAutoOpen(false)),
                    Ui.Row(L.T("Открывать сразу"),L.T("Видео идёт без нажатий — удобнее"),openAutoCheck,()=>app.SetAutoOpen(true))),
            Ui.Header(L.T("Доверенные программы")),
            trustedBox,
            Ui.Header(L.T("Горячая клавиша")),
            Ui.List(Ui.Row(L.T("Переключать приватность"),L.T("Работает в любой программе"),hotKeyPill=Ui.Pill(on=>app.SetHotKeyOn(on)),null),
                    HotKeyRow()));
        cameraTimerValue=Ui.Value("");
        var right=Stack(
            Ui.Header(L.T("Закрывать объектив")),
            Ui.List(Ui.Row(L.T("Когда камеру никто не использует"),L.T("Через 15 секунд"),closeUnusedPill=Ui.Pill(on=>app.SetFlag(Preferences.AutoPrivacy,on)),null),
                    Ui.Row(L.T("При блокировке и сне"),L.T("Win+L, сон, выключение экрана"),lockPill=Ui.Pill(on=>app.SetFlag(Preferences.LockPrivacy,on)),null),
                    Ui.Row(L.T("Таймер в самой камере"),L.T("Работает даже без Pixie Pro"),cameraTimerValue,()=>Navigate("camera")),
                    Ui.Row(L.T("При включении компьютера"),L.T("Объектив опускается сам ещё до входа в Windows"),Ui.Text(L.T("всегда"),13,"Sub"),null)),
            Ui.Header(L.T("Камеры для приложений")),
            Ui.List(Ui.Row(L.T("Виртуальная камера Pixie"),L.T("Выберите её в Zoom, Telegram, OBS вместо EMEET PIXY"),Ui.Text(L.T("готова"),13,"Sub"),null),
                    MfCamera.Supported?Ui.Row(L.T("Камера для приложений Windows"),L.T("Для «Камеры» Windows и нового Teams, если они не видят Pixie"),mfPill=Ui.Pill(on=>Run(app.ChooseMfCamera(on))),null,out mfSub):null));
        renderers["privacy"]=()=>{
            bool auto=Preferences.GetFlag(Preferences.AutoOpen,false);
            Show(showPictureCheck,!auto);Show(openAutoCheck,auto);
            var trusted=Preferences.TrustedApps();var readers=app.Readers;
            string shown=auto+"|"+string.Join("|",trusted.ToArray())+"#"+string.Join("|",readers.ToArray());
            if(shown!=trustedShown) {trustedShown=shown;trustedBox.Children.Clear();trustedBox.Children.Add(TrustedList(auto,trusted,readers));}
            hotKeyPill.IsChecked=Preferences.GetFlag(Preferences.HotKey,true);
            if(!recording) {
                if((string)hotKeyKeys.Tag!=HotKeySetting.Current) {Ui.FillKeys(hotKeyKeys,HotKeySetting.Current);hotKeyKeys.Tag=HotKeySetting.Current;}
                hotKeySub.Text=app.HotKeyBusy?L.T("Сочетание занято другой программой — запишите другое"):L.T("Нажмите, чтобы записать новое");}
            hotKeySub.Foreground=app.HotKeyBusy&&!recording?Ui.B(Ui.Red):(Brush)FindResource("Sub");
            closeUnusedPill.IsChecked=Preferences.GetFlag(Preferences.AutoPrivacy,true);
            lockPill.IsChecked=Preferences.GetFlag(Preferences.LockPrivacy,true);
            Ui.SetValue(cameraTimerValue,ShortTimer(app.CameraTimer));
            if(mfPill!=null) {
                // Shows the choice at once; while the camera starts or stops the switch waits.
                bool wanted=app.MfCameraWanted,busy=app.MfCameraBusy;
                mfPill.IsChecked=wanted;mfPill.IsEnabled=!busy;
                // No "starting…" text while it switches: the pill is just disabled for that moment.
                mfSub.Text=!busy && wanted && !app.MfCameraOn?L.T("Не запустилась: ")+(app.MfCameraError??L.T("ошибка Windows")):
                    L.T("Для «Камеры» Windows и нового Teams, если они не видят Pixie");
            }
        };
        return Columns(left,right);
    }
    Control HotKeyRow() {
        hotKeyKeys=new StackPanel {Orientation=Orientation.Horizontal};
        var row=Ui.Row(L.T("Сочетание"),"",hotKeyKeys,StartRecording,out hotKeySub);
        hotKeySub.Visibility=Visibility.Visible;
        return row;
    }
    void StartRecording() {
        if(recording) return;
        recording=true;app.PauseHotKey(true);
        hotKeyKeys.Children.Clear();hotKeyKeys.Tag=null;hotKeyKeys.Children.Add(Ui.KeyCap(L.T("Нажмите сочетание…")));
        hotKeySub.Text=L.T("Например Win+Alt+Пробел или Ctrl+Alt+P · Esc — отмена");
        Focus();Keyboard.Focus(this);
    }
    void StopRecording(string error) {
        if(!recording) return;
        recording=false;app.PauseHotKey(false);
        Render();
        if(error!=null) hotKeySub.Text=error;
    }
    void RecordKey(object sender,KeyEventArgs e) {
        if(!recording) return;
        e.Handled=true;
        Key key=e.Key==Key.System?e.SystemKey:e.Key;
        if(key==Key.Escape) {StopRecording(null);return;}
        if(key==Key.LeftAlt||key==Key.RightAlt||key==Key.LeftCtrl||key==Key.RightCtrl||key==Key.LeftShift||key==Key.RightShift||key==Key.LWin||key==Key.RWin) return;
        int modifiers=0;var held=Keyboard.Modifiers;
        if((held&ModifierKeys.Alt)!=0) modifiers|=1;
        if((held&ModifierKeys.Control)!=0) modifiers|=2;
        if((held&ModifierKeys.Shift)!=0) modifiers|=4;
        if((held&ModifierKeys.Windows)!=0||Keyboard.IsKeyDown(Key.LWin)||Keyboard.IsKeyDown(Key.RWin)) modifiers|=8;
        var winKey=(Keys)KeyInterop.VirtualKeyFromKey(key);
        if(Array.IndexOf(HotKeySetting.ModifierChoices,modifiers)<0||Array.IndexOf(HotKeySetting.KeyChoices,winKey)<0) {
            StopRecording(L.T("Нужно Win+Alt, Ctrl+Alt, Ctrl+Shift, Win+Shift или Ctrl+Win и буква, цифра, F1–F12 или пробел"));
            return;
        }
        recording=false;
        app.SetHotKeyCombo(modifiers,winKey);
        Render();
    }

    // ---------- camera ----------
    Border gestureHelp;ToggleButton hwMirrorPill,hwFlipPill,gesturesPill,trackingPill2;Segments timerSegments;TextBlock timerCaption;
    FrameworkElement CameraPage() {
        string[] names=new string[MainWindow.TimerChoices.Length];
        for(int i=0;i<names.Length;i++) names[i]=ShortTimer(MainWindow.TimerChoices[i]);
        timerSegments=new Segments(names,i=>Run(app.ChooseCameraTimer(MainWindow.TimerChoices[i])));
        timerCaption=Ui.Text(L.T("Если камеру не используют и никто не управляет ею по USB, она сама опускает объектив. Работает и без Pixie Pro, во время звонка не срабатывает."),12.5,"Sub");
        timerCaption.TextWrapping=TextWrapping.Wrap;timerCaption.TextTrimming=TextTrimming.None;timerCaption.Margin=new Thickness(4,10,4,2);
        var timerCard=Ui.Card(Stack(timerSegments,timerCaption));timerCard.Padding=new Thickness(12);
        var left=Stack(
            Ui.Header(L.T("Изображение")),
            Ui.List(Ui.Row(L.T("Перевернуть"),L.T("Если камера висит вверх ногами"),hwFlipPill=Ui.Pill(on=>Run(app.ChooseHardwareFlip(on))),null),
                    Ui.Row(L.T("Управление жестами"),L.T("Включать автослежение ладонью"),gesturesPill=Ui.Pill(on=>Run(app.ChooseGestures(on))),null)),
            GestureHelp(),
            Ui.Header(L.T("Автослежение")),
            Ui.List(Ui.Row(L.T("Следить за мной"),L.T("Камера поворачивается за человеком в кадре"),trackingPill2=Ui.Pill(on=>Run(app.SwitchTracking())),null)),
            Ui.Header(L.T("Таймер приватности")),timerCard);
        var right=Stack(Ui.Header(L.T("Предпросмотр и рамка для приложений")),PreviewCard());
        renderers["camera"]=()=>{
            hwMirrorPill.IsChecked=app.HardwareMirror==1;hwMirrorPill.IsEnabled=app.HardwareMirror>=0;
            hwFlipPill.IsChecked=app.HardwareFlip==1;hwFlipPill.IsEnabled=app.HardwareFlip>=0;
            gesturesPill.IsChecked=app.GesturesState==1;gesturesPill.IsEnabled=app.GesturesState>=0;
            Show(gestureHelp,app.GesturesState==1);
            timerSegments.Select(Array.IndexOf(MainWindow.TimerChoices,app.CameraTimer));timerSegments.IsEnabled=app.CameraTimer>=0;
            trackingPill2.IsChecked=app.TrackingWanted;trackingPill2.IsEnabled=!app.Busy && (app.Mode==0||app.Mode==1||app.Mode==3);
            RenderPreview();
        };
        return Columns(left,right);
    }

    // What the camera understands once gestures are on (EMEET PIXY recognises an open palm).
    FrameworkElement GestureHelp() {
        var text=Ui.Text(L.T("Поднимите открытую ладонь к камере и подержите 2–3 секунды — автослежение включится или выключится. ")+
            L.T("Когда слежение включено, индикатор на камере синий. Камера видит жест только при открытом объективе."),12.5,"Sub");
        text.TextWrapping=TextWrapping.Wrap;text.TextTrimming=TextTrimming.None;
        var row=new DockPanel();
        var icon=Ui.Icon("target",18,Ui.B(Ui.Blue),2);icon.Margin=new Thickness(0,1,10,0);icon.VerticalAlignment=VerticalAlignment.Top;
        DockPanel.SetDock(icon,Dock.Left);row.Children.Add(icon);row.Children.Add(text);
        gestureHelp=new Border {CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),Padding=new Thickness(14,10,14,10),Margin=new Thickness(0,10,0,0),Child=row};
        gestureHelp.SetResourceReference(Border.BackgroundProperty,"InfoBg");gestureHelp.SetResourceReference(Border.BorderBrushProperty,"InfoLine");
        return gestureHelp;
    }
    // Goes to the camera page and starts the preview there (once: a running preview stays on).
    public void OpenPreview() {
        Navigate("camera");
        if(!app.PreviewOn && !app.CaptureRunning) Run(app.SwitchPreview());
    }

    // ---------- microphone ----------
    readonly FrameworkElement[] micChecks=new FrameworkElement[4];readonly Control[] micRows=new Control[4];
    TextBlock micStatus,micApps,cameraApps;Border micStatusIcon;
    FrameworkElement MicrophonePage() {
        string[] subs={L.T("Его не видит ни одна программа"),L.T("Для звонков"),L.T("Для стримов и музыки"),L.T("Без обработки")};
        string[] titlesShort={L.T("Отключён полностью"),L.T("Шумоподавление"),L.T("Живой звук"),L.T("Оригинал")};
        for(int i=0;i<4;i++) {int choice=i;micChecks[i]=Ui.Check();micRows[i]=Ui.Row(titlesShort[i],subs[i],micChecks[i],()=>Run(app.ChooseMicrophone(choice)));}
        var left=Stack(Ui.Header(L.T("Микрофон камеры")),Ui.List(micRows));
        var statusGrid=new Grid();
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});statusGrid.ColumnDefinitions.Add(new ColumnDefinition());
        micStatusIcon=new Border {Width=36,Height=36,CornerRadius=new CornerRadius(10),Margin=new Thickness(0,0,14,0),VerticalAlignment=VerticalAlignment.Top};
        statusGrid.Children.Add(micStatusIcon);
        micStatus=Ui.Text("",13.5,"Text");micStatus.TextWrapping=TextWrapping.Wrap;micStatus.TextTrimming=TextTrimming.None;micStatus.VerticalAlignment=VerticalAlignment.Center;
        Grid.SetColumn(micStatus,1);statusGrid.Children.Add(micStatus);
        var statusCard=Ui.Card(statusGrid);statusCard.Padding=new Thickness(16,14,16,14);
        micApps=Ui.Text("",12.5,"Sub");cameraApps=Ui.Text("",12.5,"Sub");
        var right=Stack(Ui.Header(L.T("Защита")),statusCard,Ui.Header(L.T("Сейчас используют")),
            Ui.List(Ui.Row(L.T("Микрофон"),null,micApps,null),Ui.Row(L.T("Камеру"),null,cameraApps,null)));
        renderers["mic"]=()=>{
            int mic=app.MicrophoneChoice;
            for(int i=0;i<4;i++) {Show(micChecks[i],i==mic);micRows[i].IsEnabled=i==0||app.MicrophoneKnown;}
            micStatus.Text=app.MicrophoneText;
            bool warning=app.MicrophoneWarning;
            micStatusIcon.Background=Ui.B(Ui.Alpha(warning?Ui.Red:Ui.Green,48));
            micStatusIcon.Child=Ui.Icon(warning?"alert":mic==0?"micoff":"mic",19,Ui.B(warning?Ui.Red:Ui.Green),2);
            micApps.Text=Join(app.MicrophoneUsers);cameraApps.Text=Join(app.CameraUsers);
        };
        return Columns(left,right);
    }

    // ---------- alert ----------
    ToggleButton soundPill,picturePill;Button alertTest;bool alertTestShown=true;readonly FrameworkElement[] soundChecks=new FrameworkElement[3];TextBlock customSoundSub;
    FrameworkElement AlertPage() {
        var eye=new Image {Source=EyeAlert.EyeImageSource(),Height=160};
        RenderOptions.SetBitmapScalingMode(eye,BitmapScalingMode.HighQuality);
        var words=new TextBlock {Text=EyeAlert.Headline,FontSize=30,FontFamily=new FontFamily("Georgia"),FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,8,0,4),
            Foreground=new LinearGradientBrush(Ui.C("#FFE7A3"),Ui.C("#FF7A1A"),new Point(0,0),new Point(0,1)),
            Effect=new DropShadowEffect {Color=Ui.C("#FF4500"),BlurRadius=18,ShadowDepth=0,Opacity=.8}};
        var test=alertTest=Ui.Soft("play",L.T("Проверить"),()=>{app.TestAlert();Render();});
        test.Margin=new Thickness(0,10,0,0);test.HorizontalAlignment=HorizontalAlignment.Center;
        test.Background=new SolidColorBrush(Color.FromArgb(40,255,255,255));test.Foreground=Brushes.White;
        var show=Stack(eye,words,test);show.VerticalAlignment=VerticalAlignment.Center;
        var stage=new Border {CornerRadius=new CornerRadius(14),Child=show,
            Background=new RadialGradientBrush(Ui.C("#3A1206"),Ui.C("#0B0B0E")) {RadiusX=.7,RadiusY=.8}};
        var left=Stack(Ui.Header(L.T("Картинка")),
            Ui.List(Ui.Row(L.T("Картинка предупреждения"),L.T("Глаз на экране поверх всех окон"),picturePill=Ui.Pill(on=>app.SetFlag(Preferences.AlertPicture,on)),null)),
            Ui.Header(L.T("Как это выглядит")),stage);
        Control custom;
        string[] subs={L.T("Низкий гул и голос"),L.T("Звуковой эффект"),""};
        var rows=new Control[3];
        for(int i=0;i<3;i++) {
            int kind=i;soundChecks[i]=Ui.Check();
            string title=i==0?L.T("Гул и голос"):i==1?L.T("Зловещий голос"):L.T("Свой звук");
            if(i==2) {rows[i]=custom=Ui.Row(title,L.T("Файл не выбран"),soundChecks[i],()=>app.ChooseSoundKind(kind),out customSoundSub);}
            else rows[i]=Ui.Row(title,subs[i],soundChecks[i],()=>app.ChooseSoundKind(kind));
        }
        var sounds=Ui.List(rows[0],rows[1],rows[2],Ui.Row(L.T("Выбрать файл…"),L.T("MP3, WAV или WMA"),Ui.Value(""),()=>app.PickSound()));
        // The preview is as tall as the sound list next to it.
        sounds.SizeChanged+=delegate {stage.Height=sounds.ActualHeight;};
        var right=Stack(
            Ui.Header(L.T("Звук")),
            Ui.List(Ui.Row(L.T("Звук предупреждения"),L.T("Гул или голос, когда объектив открыли"),soundPill=Ui.Pill(on=>app.SetFlag(Preferences.AlertSound,on)),null)),
            Ui.Header(L.T("Какой звук")),
            sounds);
        renderers["alert"]=()=>{
            soundPill.IsChecked=Preferences.GetFlag(Preferences.AlertSound,false);
            picturePill.IsChecked=Preferences.GetFlag(Preferences.AlertPicture,true);
            // "Проверить" turns into "Остановить" while the test runs; with picture and sound off there is nothing to test.
            bool running=app.AlertRunning;
            if(running!=alertTestShown) {
                alertTestShown=running;
                var content=new StackPanel {Orientation=Orientation.Horizontal};
                var icon=Ui.Icon(running?"stop":"play",15,Brushes.White,2);icon.Margin=new Thickness(0,0,7,0);content.Children.Add(icon);
                content.Children.Add(new TextBlock {Text=running?L.T("Остановить"):L.T("Проверить"),VerticalAlignment=VerticalAlignment.Center});
                alertTest.Content=content;
            }
            alertTest.IsEnabled=running || app.AlertEnabled;
            alertTest.Opacity=alertTest.IsEnabled?1:.45;
            int kind=AlertSound.Kind;
            for(int i=0;i<3;i++) Show(soundChecks[i],i==kind);
            string file=AlertSound.CustomFile;
            customSoundSub.Text=file==null?L.T("Файл не выбран"):System.IO.Path.GetFileName(file);
        };
        return Columns(left,right);
    }

    // ---------- appearance ----------
    Segments themeSegments,languageSegments;ToggleButton trackingIconPill,autostartPill;Image pictureThumb;Button pictureReset;
    FrameworkElement LookPage() {
        themeSegments=new Segments(new[]{L.T("Как в Windows"),L.T("Светлая"),L.T("Тёмная")},i=>{
            try {Preferences.SetInt(Preferences.Theme,i);} catch {}
            app.ThemeChanged();
        });
        var themeCard=Ui.Card(themeSegments);themeCard.Padding=new Thickness(12);
        // Language names stay in their own language, so the choice is found whatever is shown now.
        languageSegments=new Segments(new[]{L.T("Как в Windows"),"Русский","English"},i=>Run(app.ChangeLanguage(i)));
        var languageCard=Ui.Card(languageSegments);languageCard.Padding=new Thickness(12);
        pictureThumb=new Image {Width=112,Height=63,Stretch=Stretch.UniformToFill,Clip=new RectangleGeometry(new Rect(0,0,112,63),8,8)};
        var thumbBox=new Border {CornerRadius=new CornerRadius(8),ClipToBounds=true,Child=pictureThumb,Background=Brushes.Black};
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(16,0,0,0)};
        buttons.Children.Add(Ui.Soft(L.T("Выбрать…"),()=>app.PickPicture()));
        pictureReset=Ui.Soft(L.T("Вернуть замок"),()=>app.ClearPicture());pictureReset.Margin=new Thickness(8,0,0,0);buttons.Children.Add(pictureReset);
        var pictureRow=new StackPanel {Orientation=Orientation.Horizontal};pictureRow.Children.Add(thumbBox);pictureRow.Children.Add(buttons);
        var pictureCard=Ui.Card(pictureRow);pictureCard.Padding=new Thickness(14);
        var left=Stack(Ui.Header(L.T("Тема")),themeCard,Ui.Header(L.English?"Language · Язык":"Язык · Language"),languageCard,Ui.Header(L.T("Картинка при приватности")),pictureCard,
            Ui.Header(L.T("Запуск")),Ui.List(Ui.Row(L.T("Запускать вместе с Windows"),L.T("Значок появится в трее после входа"),autostartPill=Ui.Pill(on=>app.SetAutostart(on)),null)));
        var dropIcon=new Image {Width=22,Height=22,Source=TrayIcons.PreviewSource(0)};
        var right=Stack(Ui.Header(L.T("Значки в трее")),
            Ui.List(Ui.Row(L.T("Значок приватности"),L.T("Всегда показан: зелёный — закрыт, красный — открыт"),dropIcon,null),
                    Ui.Row(L.T("Значок автослежения"),L.T("Второй значок, клик — включить или выключить"),trackingIconPill=Ui.Pill(on=>app.SetTrackingTrayIcon(on)),null),
                    Ui.Row(L.T("Левый клик"),L.T("Переключает приватность"),Ui.Text(L.T("приватность"),13,"Sub"),null),
                    Ui.Row(L.T("Правый клик"),L.T("Пункт управления; с Shift — классическое меню"),Ui.Text(L.T("пункт управления"),13,"Sub"),null)));
        renderers["look"]=()=>{
            themeSegments.Select(Ui.ThemeChoice);
            languageSegments.Select(Math.Max(0,Math.Min(2,Preferences.GetInt(Preferences.Language,0))));
            trackingIconPill.IsChecked=Preferences.GetFlag(Preferences.TrackingTrayIcon,false);
            try {autostartPill.IsChecked=Preferences.Autostart;} catch {}
            bool custom=Placeholder.HasCustom;pictureReset.IsEnabled=custom;
            string stamp=custom?File.GetLastWriteTimeUtc(Placeholder.CustomPath).Ticks.ToString():"lock";
            if((string)pictureThumb.Tag!=stamp) {pictureThumb.Tag=stamp;pictureThumb.Source=new WriteableBitmapHolder().Update(Placeholder.Privacy());}
        };
        return Columns(left,right);
    }

    // ---------- journal ----------
    ListBox journalList;TextBox journalSearch;long journalLength=-1;readonly List<string> journalLines=new List<string>();DateTime journalChecked;
    FrameworkElement JournalPage() {
        var bar=new Grid {Margin=new Thickness(0,12,0,12)};
        bar.ColumnDefinitions.Add(new ColumnDefinition());bar.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});bar.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
        var searchBox=new Border {CornerRadius=new CornerRadius(10),Padding=new Thickness(10,0,10,0),Height=36};
        searchBox.SetResourceReference(Border.BackgroundProperty,"Field");
        var searchRow=new DockPanel();
        var glass=Ui.Icon("search",16,"Faint");DockPanel.SetDock(glass,Dock.Left);searchRow.Children.Add(glass);
        journalSearch=new TextBox {BorderThickness=new Thickness(0),Background=Brushes.Transparent,FontSize=13.5,VerticalContentAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,0,0)};
        journalSearch.SetResourceReference(TextBox.ForegroundProperty,"Text");journalSearch.SetResourceReference(TextBox.CaretBrushProperty,"Text");
        journalSearch.TextChanged+=delegate {FillJournal();};
        searchRow.Children.Add(journalSearch);searchBox.Child=searchRow;
        bar.Children.Add(searchBox);
        var open=Ui.Soft("folder",L.T("Открыть файл"),()=>app.ShowJournalFile());open.Margin=new Thickness(10,0,0,0);Grid.SetColumn(open,1);bar.Children.Add(open);
        // Clearing asks once: the first click turns the button red for a few seconds, the second click clears.
        Button clear=null;bool armed=false;var disarm=new DispatcherTimer {Interval=TimeSpan.FromSeconds(4)};
        Action reset=()=>{armed=false;disarm.Stop();clear.Content=L.T("Очистить");clear.ClearValue(Control.BackgroundProperty);clear.ClearValue(Control.ForegroundProperty);};
        clear=Ui.Soft(L.T("Очистить"),()=>{
            if(!armed) {armed=true;clear.Content=L.T("Точно очистить?");clear.Background=Ui.B(Ui.Red);clear.Foreground=Brushes.White;disarm.Start();return;}
            reset();app.ClearJournal();LoadJournal(true);
        });
        disarm.Tick+=delegate {reset();};
        clear.Margin=new Thickness(10,0,0,0);clear.MinWidth=150;Grid.SetColumn(clear,2);bar.Children.Add(clear);
        journalList=new ListBox {BorderThickness=new Thickness(0),Background=Brushes.Transparent,Height=520,Padding=new Thickness(0,4,0,4)};
        ScrollViewer.SetHorizontalScrollBarVisibility(journalList,ScrollBarVisibility.Disabled);
        // The wheel still scrolls; the classic Windows scroll bar would clash with the cards.
        ScrollViewer.SetVerticalScrollBarVisibility(journalList,ScrollBarVisibility.Hidden);
        VirtualizingStackPanel.SetIsVirtualizing(journalList,true);
        journalList.ItemContainerStyle=JournalItemStyle();
        var card=Ui.Card(journalList);card.Padding=new Thickness(4);
        renderers["journal"]=()=>{if(DateTime.UtcNow-journalChecked>TimeSpan.FromSeconds(2)) LoadJournal(false);};
        return Stack(bar,card);
    }
    static Style JournalItemStyle() {
        var style=new Style(typeof(ListBoxItem));
        var template=new ControlTemplate(typeof(ListBoxItem));
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));
        template.VisualTree=presenter;
        style.Setters.Add(new Setter(Control.TemplateProperty,template));
        style.Setters.Add(new Setter(UIElement.FocusableProperty,false));
        return style;
    }
    void LoadJournal(bool force) {
        journalChecked=DateTime.UtcNow;
        try {
            var info=new FileInfo(EventJournal.FilePath);
            long length=info.Exists?info.Length:0;
            if(!force && length==journalLength) return;
            journalLength=length;journalLines.Clear();
            if(info.Exists) using(var stream=new FileStream(info.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var reader=new StreamReader(stream,Encoding.UTF8)) {
                // Only the tail matters; long journals keep the window fast.
                if(stream.Length>400000) {stream.Seek(-400000,SeekOrigin.End);reader.ReadLine();}
                string line;while((line=reader.ReadLine())!=null) if(line.Trim().Length>0) journalLines.Add(line);
            }
        } catch(Exception e) {journalLines.Clear();journalLines.Add(L.T("Журнал не прочитан: ")+e.Message);}
        FillJournal();
    }
    void FillJournal() {
        string filter=journalSearch.Text.Trim();
        var items=new List<FrameworkElement>();
        for(int i=journalLines.Count-1;i>=0 && items.Count<800;i--) {
            string line=journalLines[i];
            if(filter.Length>0 && line.IndexOf(filter,StringComparison.OrdinalIgnoreCase)<0) continue;
            items.Add(JournalItem(line));
        }
        if(items.Count==0) items.Add(JournalItem(filter.Length>0?L.T("Ничего не найдено"):L.T("Журнал пуст")));
        journalList.ItemsSource=items;
    }
    static FrameworkElement JournalItem(string line) {
        // Lines look like "2026-10-29 12:40:05  text".
        string time="",text=line;
        if(line.Length>20 && line[4]=='-' && line[13]==':') {time=line.Substring(0,19);text=line.Substring(19).Trim();}
        var grid=new Grid {Margin=new Thickness(12,6,12,6)};
        grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(150)});grid.ColumnDefinitions.Add(new ColumnDefinition());
        var t=Ui.Text(time,12.5,"Faint");grid.Children.Add(t);
        var body=Ui.Text(text,13,"Text");body.TextWrapping=TextWrapping.Wrap;body.TextTrimming=TextTrimming.None;
        string lower=text.ToLowerInvariant();
        if(lower.Contains("не через pixie")||lower.Contains("не удалось")||lower.Contains("внимание")||lower.Contains("outside pixie")||lower.Contains("could not")||lower.Contains("warning")) body.SetResourceReference(TextBlock.ForegroundProperty,"Text");
        if(lower.Contains("открыт")&&!lower.Contains("закрыт") || System.Text.RegularExpressions.Regex.IsMatch(lower,@"\bopen\b")&&!lower.Contains("closed")) {var dot=new Ellipse {Width=6,Height=6,Fill=Ui.B(Ui.Red),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)};grid.Children.Add(dot);}
        Grid.SetColumn(body,1);grid.Children.Add(body);
        return grid;
    }

    // ---------- home: today and recent events (read from the journal) ----------
    TextBlock todayOpen,todayOpens,todayForeign;Canvas dayBar;StackPanel recentList;
    readonly List<string> activityLines=new List<string>();long activityLength=-1,recentShown=-2;DateTime activityRead;
    FrameworkElement ActivityCard() {
        var grid=new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(28)});
        grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1.25,GridUnitType.Star)});
        // Left: today's numbers and a 24-hour strip, red where the lens was open.
        var left=new StackPanel();
        left.Children.Add(Ui.Text(L.T("Сегодня"),15,"Text",FontWeights.SemiBold));
        var numbers=new UniformGrid {Columns=3,Margin=new Thickness(0,12,0,0)};
        numbers.Children.Add(Figure(L.T("Объектив открыт"),out todayOpen));
        numbers.Children.Add(Figure(L.T("Открывали"),out todayOpens));
        numbers.Children.Add(Figure(L.T("Не через Pixie"),out todayForeign));
        left.Children.Add(numbers);
        dayBar=new Canvas {Height=10,Margin=new Thickness(0,18,0,0),ClipToBounds=true};
        dayBar.SizeChanged+=delegate {RenderActivity();};
        left.Children.Add(dayBar);
        var hours=new Grid {Margin=new Thickness(0,5,0,0)};
        string[] marks={"0:00","6:00","12:00","18:00","24:00"};
        for(int i=0;i<4;i++) hours.ColumnDefinitions.Add(new ColumnDefinition());
        for(int i=0;i<5;i++) {
            var mark=Ui.Text(marks[i],11,"Faint");
            Grid.SetColumn(mark,Math.Min(i,3));mark.HorizontalAlignment=i==4?HorizontalAlignment.Right:HorizontalAlignment.Left;
            if(i>0 && i<4) mark.Margin=new Thickness(-14,0,0,0);
            hours.Children.Add(mark);
        }
        left.Children.Add(hours);
        grid.Children.Add(left);
        // Right: the last journal lines and a way to the full journal.
        var right=new DockPanel();
        var head=new DockPanel {Margin=new Thickness(0,0,0,8)};
        var all=Ui.Soft(L.T("Весь журнал"),()=>Navigate("journal"));all.Padding=new Thickness(10,3,10,3);all.FontSize=12;
        DockPanel.SetDock(all,Dock.Right);head.Children.Add(all);
        head.Children.Add(Ui.Text(L.T("Последние события"),15,"Text",FontWeights.SemiBold));
        DockPanel.SetDock(head,Dock.Top);right.Children.Add(head);
        recentList=new StackPanel();right.Children.Add(recentList);
        Grid.SetColumn(right,2);grid.Children.Add(right);
        var card=Ui.Card(grid);card.Margin=new Thickness(0,18,0,0);card.Padding=new Thickness(20,16,20,16);
        return card;
    }
    static FrameworkElement Figure(string caption,out TextBlock value) {
        var stack=new StackPanel();
        value=Ui.Text("—",22,"Text",FontWeights.SemiBold);stack.Children.Add(value);
        var label=Ui.Text(caption,12,"Sub");label.Margin=new Thickness(0,2,0,0);stack.Children.Add(label);
        return stack;
    }
    void RenderActivity() {
        if(recentList==null) return;
        ReadActivity();
        var now=DateTime.Now;
        var day=DayActivity.Of(activityLines,now);
        todayOpen.Text=day.Open.TotalSeconds<1?L.T("0 мин"):MainWindow.Duration(day.Open);
        todayOpens.Text=day.Opens+" "+Times(day.Opens);
        todayForeign.Text=day.Foreign.ToString();
        if(day.Foreign>0) todayForeign.Foreground=Ui.B(Ui.Red); else todayForeign.SetResourceReference(TextBlock.ForegroundProperty,"Text");
        // The strip: track, red open spans, a thin mark for now.
        double w=dayBar.ActualWidth,dayLength=TimeSpan.FromDays(1).TotalSeconds;
        dayBar.Children.Clear();
        if(w>0) {
            var track=new Border {Width=w,Height=10,CornerRadius=new CornerRadius(5)};track.SetResourceReference(Border.BackgroundProperty,"Track");
            dayBar.Children.Add(track);
            foreach(var span in day.Spans) {
                double x=(span.Item1-now.Date).TotalSeconds/dayLength*w,x2=(span.Item2-now.Date).TotalSeconds/dayLength*w;
                var open=new Border {Width=Math.Max(3,x2-x),Height=10,CornerRadius=new CornerRadius(3),Background=Ui.B(Ui.Red)};
                Canvas.SetLeft(open,x);dayBar.Children.Add(open);
            }
            var mark=new Border {Width=2,Height=10};mark.SetResourceReference(Border.BackgroundProperty,"Sub");
            Canvas.SetLeft(mark,Math.Min(w-2,(now-now.Date).TotalSeconds/dayLength*w));dayBar.Children.Add(mark);
        }
        // Recent events: the last five lines, newest first, rebuilt only when the journal changed.
        if(recentShown==activityLength) return;
        recentShown=activityLength;
        recentList.Children.Clear();
        for(int i=activityLines.Count-1;i>=0 && recentList.Children.Count<5;i--) {
            string line=activityLines[i];
            DateTime at;
            if(!DayActivity.TryTime(line,out at)) continue;
            var row=new Grid {Margin=new Thickness(0,4,0,4)};
            row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(86)});row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(Ui.Text(at.Date==now.Date?at.ToString("HH:mm"):at.ToString("dd.MM HH:mm"),12.5,"Faint"));
            var text=Ui.Text(line.Substring(19).Trim(),13,"Text");text.ToolTip=text.Text;
            Grid.SetColumn(text,1);row.Children.Add(text);
            recentList.Children.Add(row);
        }
        if(recentList.Children.Count==0) recentList.Children.Add(Ui.Text(L.T("Событий пока нет"),13,"Sub"));
    }
    static string Times(int n) {
        int last=n%10,tens=n%100;
        if(L.English) return n==1?"time":"times";
        return last>=2&&last<=4&&(tens<12||tens>14)?"раза":"раз";
    }
    // The journal tail is reread only when the file grew, and at most every few seconds.
    void ReadActivity() {
        if((DateTime.UtcNow-activityRead).TotalSeconds<3 && activityLength>=0) return;
        activityRead=DateTime.UtcNow;
        try {
            var info=new FileInfo(EventJournal.FilePath);
            long length=info.Exists?info.Length:0;
            if(length==activityLength) return;
            activityLength=length;activityLines.Clear();
            if(info.Exists) using(var stream=new FileStream(info.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var reader=new StreamReader(stream,Encoding.UTF8)) {
                if(stream.Length>200000) {stream.Seek(-200000,SeekOrigin.End);reader.ReadLine();}
                string line;while((line=reader.ReadLine())!=null) if(line.Trim().Length>0) activityLines.Add(line);
            }
        } catch {}
    }

    // ---------- shared ----------
    async void Run(System.Threading.Tasks.Task task) {
        try {await task;} catch(Exception e) {EventJournal.Write(L.T("Ошибка интерфейса: ")+e.Message);}
        Render();
    }
}

// Today's lens activity from journal lines ("yyyy-MM-dd HH:mm:ss  text"): open time, openings and openings not
// made through Pixie Pro, plus the open spans for the day strip.
public sealed class DayActivity {
    public TimeSpan Open;public int Opens,Foreign;
    public readonly List<Tuple<DateTime,DateTime>> Spans=new List<Tuple<DateTime,DateTime>>();
    // 1: the lens is open, 0: closed (or Pixie Pro stopped watching), -1: not about the lens.
    public static int State(string text) {
        // The journal is written in the interface language; old lines may be in the other one.
        foreach(string prefix in new[]{"При запуске: ","At startup: "}) if(text.StartsWith(prefix)) text=text.Substring(prefix.Length);
        if(text.StartsWith("Камера открыта")||text.StartsWith("Camera open")) return 1;
        foreach(string closed in new[]{"Приватность включена","Объектив закрыт","Нет связи с камерой","Privacy on","Lens closed","No connection to the camera"})
            if(text.StartsWith(closed)) return 0;
        if(text=="Pixie Privacy Pro завершена"||text=="Pixie Privacy Pro exited") return 0;
        return -1;
    }
    public static bool TryTime(string line,out DateTime at) {
        at=DateTime.MinValue;
        return line.Length>=21 && line[4]=='-' &&
            DateTime.TryParseExact(line.Substring(0,19),"yyyy-MM-dd HH:mm:ss",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out at);
    }
    public static DayActivity Of(IList<string> lines,DateTime now) {
        var day=new DayActivity();
        DateTime start=now.Date;
        bool open=false;DateTime openSince=start;
        foreach(string line in lines) {
            DateTime at;
            if(!TryTime(line,out at)) continue;
            string text=line.Substring(19).Trim();
            int state=State(text);
            if(state<0) continue;
            if(at<start) {open=state==1;openSince=start;continue;}
            if(state==1 && !open) {
                open=true;openSince=at;day.Opens++;
                if(text.Contains("не через Pixie Pro")||text.Contains("outside Pixie Pro")) day.Foreign++;
            } else if(state==0 && open) {
                open=false;day.Add(openSince,at);
            }
        }
        if(open) day.Add(openSince,now);
        return day;
    }
    void Add(DateTime from,DateTime to) {
        if(to<=from) return;
        Spans.Add(Tuple.Create(from,to));Open+=to-from;
    }
}
