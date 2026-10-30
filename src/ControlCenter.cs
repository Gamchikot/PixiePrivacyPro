using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

// Tray control center (right click): big tiles for privacy and tracking, round quick toggles and the camera timer,
// as in the iPhone Control Center. It slides up above the tray and closes when it loses focus.
public class ControlCenter : Window {
    readonly MainWindow app;
    readonly DispatcherTimer refresh=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(400)};
    static DateTime lastClosed;
    Border privacyTile,trackingTile,privacyIconBox,trackingIconBox;TextBlock privacyTitle,privacySub,trackingTitle,trackingSub;
    Border previewButton,micButton,soundButton,lockButton;
    Segments timer,openMode;TextBlock readerValue,micValue,hotKeyValue;
    static readonly int[] TimerChoices={0,30,60,300};
    bool closing;

    public ControlCenter(MainWindow app) {
        this.app=app;
        WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;
        ShowInTaskbar=false;Topmost=true;SizeToContent=SizeToContent.WidthAndHeight;
        FontFamily=new FontFamily("Segoe UI Variable Text, Segoe UI");
        UseLayoutRounding=true;
        Ui.ApplyTheme(Resources,Ui.Dark);
        System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(this);

        var stack=new StackPanel {Width=344};
        var big=new Grid();
        big.ColumnDefinitions.Add(new ColumnDefinition());big.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(10)});big.ColumnDefinitions.Add(new ColumnDefinition());
        privacyTile=BigTile("lock",out privacyIconBox,out privacyTitle,out privacySub,()=>Run(app.TogglePrivacy()));
        trackingTile=BigTile("target",out trackingIconBox,out trackingTitle,out trackingSub,()=>Run(app.SwitchTracking()));
        big.Children.Add(privacyTile);Grid.SetColumn(trackingTile,2);big.Children.Add(trackingTile);
        stack.Children.Add(big);
        var small=new UniformGrid {Rows=1,Margin=new Thickness(-4,10,-4,0)};
        small.Children.Add(previewButton=Round("video",L.T("Предпросмотр"),()=>{app.OpenWindow();if(app.Window!=null) app.Window.OpenPreview();Close();}));
        small.Children.Add(micButton=Round("mic",L.T("Микрофон"),()=>Run(app.ChooseMicrophone(app.MicrophoneChoice>0?0:(app.AudioProcessing>=1&&app.AudioProcessing<=3?app.AudioProcessing:1)))));
        small.Children.Add(soundButton=Round("volume",L.T("Звук"),()=>app.SetFlag(Preferences.AlertSound,!Preferences.GetFlag(Preferences.AlertSound,false))));
        small.Children.Add(lockButton=Round("lock",L.T("При блокировке"),()=>app.SetFlag(Preferences.LockPrivacy,!Preferences.GetFlag(Preferences.LockPrivacy,true))));
        stack.Children.Add(small);
        stack.Children.Add(Label(L.T("Камера сама закрывается")));
        timer=new Segments(new[]{L.T("Никогда"),L.T("30 с"),L.T("1 мин"),L.T("5 мин")},i=>Run(app.ChooseCameraTimer(TimerChoices[i])));
        stack.Children.Add(timer);
        stack.Children.Add(Label(L.T("Когда приложение открывает камеру")));
        openMode=new Segments(new[]{L.T("Заставка"),L.T("Открывать")},i=>{app.SetAutoOpen(i==1);Render();});
        stack.Children.Add(openMode);
        var info=new StackPanel();
        readerValue=InfoRow(info,L.T("Камеру читает"));micValue=InfoRow(info,L.T("Микрофон PIXY"));hotKeyValue=InfoRow(info,L.T("Горячая клавиша"));
        var infoBox=new Border {CornerRadius=new CornerRadius(14),Padding=new Thickness(14,10,14,10),Margin=new Thickness(0,12,0,0),Child=info};
        infoBox.SetResourceReference(Border.BackgroundProperty,"Tile");
        stack.Children.Add(infoBox);
        var foot=new UniformGrid {Rows=1,Margin=new Thickness(-4,10,-4,0)};
        foot.Children.Add(Foot("window",L.T("Окно"),()=>{Close();app.OpenWindow();}));
        foot.Children.Add(Foot("journal",L.T("Журнал"),()=>{Close();app.OpenWindow();if(app.Window!=null) app.Window.Navigate("journal");}));
        foot.Children.Add(Foot("power",L.T("Выход"),()=>{Close();Run(app.Quit());}));
        stack.Children.Add(foot);

        var panel=new Border {CornerRadius=new CornerRadius(22),Padding=new Thickness(14),BorderThickness=new Thickness(1),Child=stack,Margin=new Thickness(18),
            Effect=new DropShadowEffect {BlurRadius=28,ShadowDepth=6,Opacity=.4}};
        panel.SetResourceReference(Border.BackgroundProperty,"Flyout");panel.SetResourceReference(Border.BorderBrushProperty,"CardLine");
        Content=panel;

        refresh.Tick+=delegate {Render();};
        Deactivated+=delegate {if(!closing) Close();};
        KeyDown+=(s,e)=>{if(e.Key==Key.Escape) Close();};
        Closing+=delegate {closing=true;refresh.Stop();lastClosed=DateTime.UtcNow;};
        Render();
    }
    // A click on the tray icon while the panel is open first deactivates it; that click must not reopen it.
    public static bool JustClosed {get {return DateTime.UtcNow-lastClosed<TimeSpan.FromMilliseconds(350);}}

    public void ShowNearTray() {
        if(JustClosed) return;
        Opacity=0;
        Show();
        // Place the panel above the tray of the screen with the cursor, in device pixels converted to WPF units.
        var source=PresentationSource.FromVisual(this);
        double scale=source!=null?source.CompositionTarget.TransformToDevice.M11:1;
        var screen=System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        Left=screen.Right/scale-ActualWidth+6;
        Top=screen.Bottom/scale-ActualHeight+6;
        Activate();
        var move=new TranslateTransform(0,24);((Border)Content).RenderTransform=move;
        move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(24,0,TimeSpan.FromSeconds(.32)) {EasingFunction=new BackEase {Amplitude=.35,EasingMode=EasingMode.EaseOut}});
        BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromSeconds(.18)));
        refresh.Start();
    }

    Border BigTile(string icon,out Border iconBox,out TextBlock title,out TextBlock sub,Action click) {
        var stack=new StackPanel();
        iconBox=new Border {Width=40,Height=40,CornerRadius=new CornerRadius(20),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,18)};
        stack.Children.Add(iconBox);
        title=new TextBlock {FontSize=14.5,FontWeight=FontWeights.SemiBold};
        sub=new TextBlock {FontSize=12,Margin=new Thickness(0,2,0,0),TextTrimming=TextTrimming.CharacterEllipsis};
        stack.Children.Add(title);stack.Children.Add(sub);
        var tile=new Border {CornerRadius=new CornerRadius(18),Padding=new Thickness(14,14,14,14),Child=stack,Cursor=Cursors.Hand};
        Press(tile,click);
        return tile;
    }
    Border Round(string icon,string tip,Action click) {
        var box=new Border {Height=62,CornerRadius=new CornerRadius(16),Margin=new Thickness(4,0,4,0),Cursor=Cursors.Hand,ToolTip=tip,Tag=icon};
        Press(box,click);
        return box;
    }
    Button Foot(string icon,string text,Action click) {
        var b=Ui.Soft(icon,text,click);b.Margin=new Thickness(4,0,4,0);b.Padding=new Thickness(8,9,8,9);b.FontSize=12.5;
        return b;
    }
    static TextBlock Label(string text) {
        var t=Ui.Text(text,11.5,"Sub");t.Margin=new Thickness(4,14,0,6);
        return t;
    }
    static TextBlock InfoRow(Panel panel,string name) {
        var row=new Grid {Margin=new Thickness(0,3,0,3)};
        row.Children.Add(Ui.Text(name,12.5,"Sub"));
        var value=Ui.Text("",12.5,"Text",FontWeights.SemiBold);value.HorizontalAlignment=HorizontalAlignment.Right;value.MaxWidth=170;
        row.Children.Add(value);panel.Children.Add(row);
        return value;
    }
    // Tiles shrink a little while pressed and spring back, like iOS controls.
    static void Press(Border element,Action click) {
        var scale=new ScaleTransform(1,1);element.RenderTransform=scale;element.RenderTransformOrigin=new Point(.5,.5);
        bool down=false;
        element.MouseLeftButtonDown+=delegate {down=true;Animate(scale,.94,.1);};
        element.MouseLeave+=delegate {if(down){down=false;Animate(scale,1,.3);}};
        element.MouseLeftButtonUp+=delegate {if(!down) return;down=false;Animate(scale,1,.4);click();};
    }
    static void Animate(ScaleTransform t,double to,double seconds) {
        var a=new DoubleAnimation(to,TimeSpan.FromSeconds(seconds)) {EasingFunction=to<1?(IEasingFunction)new QuadraticEase():new BackEase {Amplitude=.6,EasingMode=EasingMode.EaseOut}};
        t.BeginAnimation(ScaleTransform.ScaleXProperty,a);t.BeginAnimation(ScaleTransform.ScaleYProperty,a);
    }

    void Render() {
        int mode=app.Mode;
        bool closed=mode==2;
        // The privacy tile always stays lit; only the symbol and its colour tell the state (as the orb on Home).
        Paint(privacyTile,privacyIconBox,privacyTitle,privacySub,true,"pixy"+PixyArt.State(mode),closed?Ui.Green:mode<0?Ui.Gray:Ui.Red,
            L.T("Приватность"),closed?L.T("Объектив закрыт"):mode<0?L.T("Нет связи"):L.T("Камера открыта"));
        bool tracking=app.TrackingWanted;
        Paint(trackingTile,trackingIconBox,trackingTitle,trackingSub,tracking,"target",Ui.Blue,L.T("Автослежение"),
            mode==1?L.T("Включено"):mode==2?L.T("При открытом объективе"):tracking?L.T("С видео"):L.T("Выключено"));
        trackingTile.Opacity=(mode==0||mode==1||mode==3)&&!app.Busy?1:.55;
        PaintRound(previewButton,app.PreviewOn,"video");
        int mic=app.MicrophoneChoice;
        PaintRound(micButton,mic>0,mic>0?"mic":"micoff");
        micButton.ToolTip=mic>0?L.T("Микрофон: ")+MainWindow.MicrophoneNames[mic]:L.T("Микрофон отключён полностью");
        bool sound=Preferences.GetFlag(Preferences.AlertSound,false);
        PaintRound(soundButton,sound,sound?"volume":"mute");
        PaintRound(lockButton,Preferences.GetFlag(Preferences.LockPrivacy,true),"lock");
        lockButton.ToolTip=L.T("Закрывать объектив при блокировке и сне");
        timer.Select(Array.IndexOf(TimerChoices,app.CameraTimer));timer.IsEnabled=app.CameraTimer>=0;
        openMode.Select(Preferences.GetFlag(Preferences.AutoOpen,false)?1:0);
        var readers=app.Readers;
        readerValue.Text=readers.Count>0?CapabilityUsage.Describe(readers):app.ConsumerActive?L.T("приложение"):L.T("никто");
        readerValue.Foreground=Ui.B(readers.Count>0||app.ConsumerActive?Ui.Red:Ui.Green);
        micValue.Text=mic==0?L.T("отключён"):mic>0?MainWindow.MicrophoneNames[mic].ToLowerInvariant():L.T("проверка…");
        micValue.Foreground=Ui.B(mic==0?Ui.Green:Ui.Orange);
        hotKeyValue.Text=Preferences.GetFlag(Preferences.HotKey,true)?HotKeySetting.Current:L.T("выключена");
    }
    void Paint(Border tile,Border iconBox,TextBlock title,TextBlock sub,bool on,string icon,Color color,string name,string state) {
        if(on) {tile.SetResourceReference(Border.BackgroundProperty,"TileOn");title.Foreground=Ui.B("#111318");sub.Foreground=Ui.B("#5D6470");}
        else {tile.SetResourceReference(Border.BackgroundProperty,"Tile");title.SetResourceReference(TextBlock.ForegroundProperty,"Text");sub.SetResourceReference(TextBlock.ForegroundProperty,"Sub");}
        iconBox.Background=Ui.B(on?color:Ui.Alpha(Ui.Gray,90));
        if((string)iconBox.Tag!=icon) {iconBox.Tag=icon;iconBox.Child=icon.StartsWith("pixy")?PixyArt.Draw(int.Parse(icon.Substring(4)),32):Ui.Icon(icon,20,Brushes.White,2.2);}
        title.Text=name;sub.Text=state;
    }
    void PaintRound(Border box,bool on,string icon) {
        if(on) box.Background=Ui.B(Ui.Blue); else box.SetResourceReference(Border.BackgroundProperty,"Tile");
        string key=icon+(on?"+":"-");
        if((string)box.Tag!=key) {box.Tag=key;box.Child=on?Ui.Icon(icon,22,Brushes.White,2):Ui.Icon(icon,22,"Text");}
    }
    async void Run(System.Threading.Tasks.Task task) {
        try {await task;} catch(Exception e) {EventJournal.Write(L.T("Ошибка пункта управления: ")+e.Message);}
        if(IsVisible) Render();
    }
}
