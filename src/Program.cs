using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using OpenFileDialog=System.Windows.Forms.OpenFileDialog;

public class MainWindow : Form {
    Label status=new Label(),microphone=new Label(),usage=new Label(),hint=new Label();
    Button toggle=new Button(),preview=new Button(),tracking=new Button();
    Label trackingStatus=new Label();
    PictureBox input=new PictureBox(),output=new PictureBox();
    ComboBox shapes=new ComboBox(); TrackBar size=new TrackBar();
    NotifyIcon tray=new NotifyIcon(),trackingTray=new NotifyIcon(); ContextMenuStrip menu=new ContextMenuStrip();
    ToolStripMenuItem autostart=new ToolStripMenuItem(L.T("Запускать вместе с Windows"));
    ToolStripMenuItem menuToggle=new ToolStripMenuItem(L.T("Переключить приватность"));
    ToolStripMenuItem menuTracking=new ToolStripMenuItem(L.T("Включить автослежение"));
    ToolStripMenuItem menuAutoPrivacy=new ToolStripMenuItem(L.T("Закрывать объектив, когда камеру Pixie никто не использует"));
    ToolStripMenuItem menuAutoOpen=new ToolStripMenuItem(L.T("Выходить из приватности, когда приложение открывает камеру Pixie"));
    ToolStripMenuItem menuMfCamera=new ToolStripMenuItem(L.T("Камера для приложений Windows 11 (Media Foundation)"));
    GroupBox settingsBox=new GroupBox();
    RadioButton openManual=new RadioButton(),openAuto=new RadioButton();
    CheckBox closeUnused=new CheckBox(),mfBox=new CheckBox();
    Label pictureLabel=new Label(); PictureBox pictureThumb=new PictureBox(); Button pictureChoose=new Button(),pictureReset=new Button();
    ToolStripMenuItem menuPicture=new ToolStripMenuItem(L.T("Картинка при приватности"));
    ToolStripMenuItem menuPictureLock=new ToolStripMenuItem(L.T("Замок")),menuPictureCustom=new ToolStripMenuItem(L.T("Своя картинка…"));
    CheckBox lockBox=new CheckBox(),soundBox=new CheckBox(),mirrorBox=new CheckBox();
    Label trustedLabel=new Label(); Button trustedEdit=new Button(),soundChoose=new Button(),soundPlay=new Button();
    GroupBox lookBox=new GroupBox();
    ToolStripMenuItem menuSoundCustom=new ToolStripMenuItem(L.T("Выбрать свой звук…"));
    ToolStripMenuItem menuLockPrivacy=new ToolStripMenuItem(L.T("Закрывать объектив при блокировке, сне и отключении экрана"));
    ToolStripMenuItem menuSound=new ToolStripMenuItem(L.T("Включён"));
    ToolStripMenuItem menuTrusted=new ToolStripMenuItem(L.T("Доверенные программы…"));
    // Settings stored in the camera itself; -1 until read over USB.
    GroupBox cameraBox=new GroupBox();
    Label timerLabel=new Label(),micLabel=new Label();
    ComboBox timerChoice=new ComboBox(),micChoice=new ComboBox();
    CheckBox gestureBox=new CheckBox(),hwMirrorBox=new CheckBox(),hwFlipBox=new CheckBox(),hotKeyBox=new CheckBox();
    ToolStripMenuItem menuTimer=new ToolStripMenuItem(L.T("Камера сама закрывается")),menuMic=new ToolStripMenuItem(L.T("Микрофон камеры"));
    ToolStripMenuItem menuGestures=new ToolStripMenuItem(L.T("Управление жестами")),menuHwMirror=new ToolStripMenuItem(L.T("Зеркально в самой камере")),menuHwFlip=new ToolStripMenuItem(L.T("Перевернуть изображение"));
    ToolStripMenuItem menuHotKey=new ToolStripMenuItem(L.T("Горячая клавиша"));
    ComboBox hotKeyModifiers=new ComboBox(),hotKeyKey=new ComboBox(),soundKind=new ComboBox();
    static readonly string[] SoundNames={L.T("Стандартный: гул и голос"),L.T("Стандартный: зловещий голос"),L.T("Свой звук")};
    static readonly int[] TimerSeconds={0,10,30,60,300,900};
    static readonly string[] MicNames={L.T("Отключён полностью"),L.T("Шумоподавление"),L.T("Живой звук"),L.T("Оригинал (без обработки)")};
    int privacyTimer=-1,audioMode=-1,gestures=-1,hwMirror=-1,hwFlip=-1;
    bool settingsLoading,settingsRead,hotKeyRegistered;
    DateTime nextCameraPoll=DateTime.MinValue,revealAt=DateTime.MinValue;
    const int HotKeyId=1;
    bool mfCameraActive,syncingSettings,layoutOnly;
    // Apps reading a Pixie camera right now (see PixieReaders); a new trusted one may open the lens in auto mode.
    List<string> pixieReaders=new List<string>();
    bool readersBusy,pendingTrustedArrival;
    DateTime? openSince;
    string trayTip;
    IntPtr displayNotification;
    static Guid ConsoleDisplayState=new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");
    // Capture runs while an app reads a Pixie camera or the user asked for the preview.
    bool manualPreview,consumerWasActive;
    DateTime lastConsumer=DateTime.MinValue,retryStartAfter=DateTime.MinValue;
    DateTime? idleLensSince;
    System.Windows.Forms.Timer poll=new System.Windows.Forms.Timer(),paint=new System.Windows.Forms.Timer();
    VideoPipeline video=new VideoPipeline();
    CropSettings crop;
    Icon closedIcon,openIcon,unknownIcon;
    Icon trackingOnIcon,trackingOffIcon,trackingWaitingIcon;
    bool trackingWanted,cameraChanging;
    EyeAlert alert;
    bool exiting,hidden,micBusy,firstReading=true,unknownReported;
    DateTime? unknownSince;
    List<string> cameraApps=new List<string>(),microphoneApps=new List<string>();
    readonly SemaphoreSlim cameraGate=new SemaphoreSlim(1,1);
    int mode=-1,lastKnown=-1,ticks;
    Point dragOrigin; float dragX,dragY; bool dragging;
    // ConsumerGrace: an app that stops reading for a moment (switching scenes, reopening) keeps the capture.
    // IdleLensClose: an open lens nobody uses (no Pixie reader, preview or camera app in Windows) gets privacy back.
    static readonly TimeSpan UnknownNotice=TimeSpan.FromSeconds(10),ConsumerGrace=TimeSpan.FromSeconds(5),StartRetry=TimeSpan.FromSeconds(10),IdleLensClose=TimeSpan.FromSeconds(15);

    public MainWindow(bool startHidden) : this(startHidden,false) {}
    public MainWindow(bool startHidden,bool layoutOnly) {
        hidden=startHidden;this.layoutOnly=layoutOnly;
        // Test harnesses drive the window without a tray; keep their actions out of the user's journal.
        if(layoutOnly) EventJournal.Enabled=false;
        Text="Pixie Privacy Pro"; ClientSize=new Size(1020,1036); MinimumSize=MaximumSize=Size;
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        // This form only hosts the tray, the hot key and power messages; people see ModernWindow (WPF).
        // Its controls stay as the state the logic reads and writes; test harnesses still draw them.
        if(!layoutOnly) {ShowInTaskbar=false;FormBorderStyle=FormBorderStyle.None;Opacity=0;StartPosition=FormStartPosition.Manual;Location=new Point(-32000,-32000);}
        MaximizeBox=false; if(layoutOnly) StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(24,27,34);ForeColor=Color.White;Font=new Font("Segoe UI",10);
        status.SetBounds(24,18,630,45);microphone.SetBounds(24,65,900,30);
        status.Text=L.T("Проверка состояния камеры…");microphone.Text=L.T("Проверка защиты микрофона…");
        toggle.SetBounds(690,18,300,42);toggle.Text=L.T("Переключить приватность");
        preview.SetBounds(24,108,340,40);preview.Text=L.T("Открыть камеру и предпросмотр");
        shapes.SetBounds(390,114,130,30);shapes.DropDownStyle=ComboBoxStyle.DropDownList;shapes.Items.AddRange(new object[]{"1:1","4:3","16:9",CropSettings.Circle});
        shapes.ForeColor=Color.Black;shapes.BackColor=Color.White;
        size.SetBounds(540,105,260,45);size.Minimum=20;size.Maximum=100;size.Value=100;size.TickFrequency=20;
        var zoomLabel=new Label {Text=L.T("Размер рамки"),Location=new Point(810,119),AutoSize=true};
        input.SetBounds(24,175,640,360);input.SizeMode=PictureBoxSizeMode.Zoom;input.BackColor=Color.Black;
        output.SetBounds(690,175,300,169);output.SizeMode=PictureBoxSizeMode.Zoom;output.BackColor=Color.Black;
        var resultLabel=new Label {Text=L.T("Результат для приложений"),Location=new Point(690,355),AutoSize=true};
        mirrorBox.SetBounds(880,353,110,24);mirrorBox.Text=L.T("Зеркально");mirrorBox.ForeColor=Color.White;mirrorBox.FlatStyle=FlatStyle.Flat;
        tracking.SetBounds(690,395,300,42);
        trackingStatus.SetBounds(690,450,300,85);
        trackingWanted=Preferences.GetFlag(Preferences.Tracking,false);
        usage.SetBounds(24,545,970,30);usage.ForeColor=Color.LightGray;usage.Text=L.T("Использование камеры и микрофона: проверка…");
        settingsBox.SetBounds(24,580,970,172);settingsBox.Text=L.T("Когда приложение открывает камеру Pixie");settingsBox.ForeColor=Color.LightGray;
        openManual.SetBounds(16,26,940,24);openManual.Text=L.T("Показывать заставку (замок или свою картинку) — объектив открываю я сам значком в трее (безопаснее)");
        openAuto.SetBounds(16,52,940,24);openAuto.Text=L.T("Сразу выходить из приватности и передавать видео — для доверенных программ (удобнее)");
        trustedLabel.SetBounds(40,82,700,24);trustedLabel.ForeColor=Color.White;
        trustedEdit.SetBounds(756,77,200,30);trustedEdit.Text=L.T("Изменить список…");
        closeUnused.SetBounds(16,112,470,24);closeUnused.Text=L.T("Закрывать объектив, когда камеру никто не использует");
        mfBox.SetBounds(500,112,460,24);mfBox.Text=L.T("Камера для приложений Windows 11 (Media Foundation)");mfBox.Visible=MfCamera.Supported;
        lockBox.SetBounds(16,138,470,24);lockBox.Text=L.T("Закрывать объектив при блокировке, сне и отключении экрана");
        hotKeyBox.SetBounds(500,138,160,24);hotKeyBox.Text=L.T("Горячая клавиша:");
        hotKeyModifiers.SetBounds(664,135,110,26);hotKeyKey.SetBounds(800,135,110,26);
        var hotKeyPlus=new Label {Text="+",Bounds=new Rectangle(780,138,16,24),ForeColor=Color.White};
        foreach(int m in HotKeySetting.ModifierChoices) hotKeyModifiers.Items.Add(HotKeySetting.ModifierName(m));
        foreach(Keys k in HotKeySetting.KeyChoices) hotKeyKey.Items.Add(HotKeySetting.KeyName(k));
        // Settings kept in the camera: they work in every app and without Pro.
        cameraBox.SetBounds(24,758,970,96);cameraBox.Text=L.T("Камера и микрофон (настройки хранятся в самой камере)");cameraBox.ForeColor=Color.LightGray;
        timerLabel.SetBounds(16,30,330,24);timerLabel.Text=L.T("Камера сама закрывается без использования:");timerLabel.ForeColor=Color.White;
        timerChoice.SetBounds(350,26,140,30);
        micLabel.SetBounds(512,30,140,24);micLabel.Text=L.T("Микрофон камеры:");micLabel.ForeColor=Color.White;
        micChoice.SetBounds(656,26,300,30);micChoice.Items.AddRange(MicNames);
        foreach(var c in new[]{timerChoice,micChoice}) {c.DropDownStyle=ComboBoxStyle.DropDownList;c.ForeColor=Color.Black;c.BackColor=Color.White;}
        gestureBox.SetBounds(16,62,300,24);gestureBox.Text=L.T("Управление жестами");
        hwMirrorBox.SetBounds(350,62,300,24);hwMirrorBox.Text=L.T("Зеркально в самой камере");
        hwFlipBox.SetBounds(656,62,300,24);hwFlipBox.Text=L.T("Перевернуть изображение");
        cameraBox.Controls.AddRange(new Control[]{timerLabel,timerChoice,micLabel,micChoice,gestureBox,hwMirrorBox,hwFlipBox});
        var tips=new ToolTip();
        tips.SetToolTip(timerChoice,L.T("Если камеру не используют и никто не управляет ею по USB, она сама опускает объектив.\nРаботает и без Pixie Pro. Во время видеозвонка не срабатывает."));
        tips.SetToolTip(micChoice,L.T("«Отключён полностью» — микрофон камеры выключен в Windows, его не видит ни одна программа.\nОстальные варианты включают микрофон с выбранной обработкой звука."));
        tips.SetToolTip(hwMirrorBox,L.T("Камера отражает изображение сама — для всех программ, а не только для камеры Pixie."));
        tips.SetToolTip(gestureBox,L.T("Жесты рукой перед камерой (работают в режиме автослежения)."));
        // Appearance: what apps see in privacy and what the alert sounds like, in matching rows.
        lookBox.SetBounds(24,860,970,120);lookBox.Text=L.T("Оформление");lookBox.ForeColor=Color.LightGray;
        pictureLabel.SetBounds(16,36,220,24);pictureLabel.Text=L.T("Картинка при приватности:");pictureLabel.ForeColor=Color.White;
        pictureThumb.SetBounds(240,24,80,45);pictureThumb.SizeMode=PictureBoxSizeMode.Zoom;pictureThumb.BorderStyle=BorderStyle.FixedSingle;pictureThumb.BackColor=Color.Black;
        pictureChoose.SetBounds(340,30,220,32);pictureChoose.Text=L.T("Выбрать свою картинку…");
        pictureReset.SetBounds(572,30,220,32);pictureReset.Text=L.T("Вернуть замок");
        soundBox.SetBounds(16,82,220,24);soundBox.Text=L.T("Звук предупреждения");
        soundPlay.SetBounds(240,78,80,32);soundPlay.Text="▶";soundPlay.Font=new Font("Segoe UI Symbol",13);
        soundKind.SetBounds(340,81,220,30);soundKind.Items.AddRange(SoundNames);
        soundChoose.SetBounds(572,78,220,32);soundChoose.Text=L.T("Выбрать свой звук…");
        foreach(var c in new[]{hotKeyModifiers,hotKeyKey,soundKind}) {c.DropDownStyle=ComboBoxStyle.DropDownList;c.ForeColor=Color.Black;c.BackColor=Color.White;}
        foreach(var b in new ButtonBase[]{openManual,openAuto,closeUnused,mfBox,lockBox,soundBox,hotKeyBox,gestureBox,hwMirrorBox,hwFlipBox}) {b.ForeColor=Color.White;b.FlatStyle=FlatStyle.Flat;}
        settingsBox.Controls.AddRange(new Control[]{openManual,openAuto,trustedLabel,trustedEdit,closeUnused,mfBox,lockBox,hotKeyBox,hotKeyModifiers,hotKeyPlus,hotKeyKey});
        lookBox.Controls.AddRange(new Control[]{pictureLabel,pictureThumb,pictureChoose,pictureReset,soundBox,soundPlay,soundKind,soundChoose});
        new ToolTip().SetToolTip(hotKeyKey,L.T("Включает и выключает приватность из любой программы.\nЕсли сочетание занято другой программой, внизу окна появится подсказка."));
        new ToolTip().SetToolTip(soundPlay,L.T("Показать предупреждение и проиграть звук"));
        hint.SetBounds(24,988,970,44);hint.Text=L.T("Выберите «Pixie Privacy Pro Camera» в приложении: видео пойдёт само, пока приложение читает камеру.\nКнопка предпросмотра нужна только чтобы посмотреть картинку и настроить рамку мышью и ползунком.");
        foreach(var b in new[]{toggle,preview,tracking,pictureChoose,pictureReset,trustedEdit,soundChoose,soundPlay}) {b.FlatStyle=FlatStyle.Flat;b.BackColor=Color.FromArgb(48,56,70);}
        pictureChoose.ForeColor=pictureReset.ForeColor=trustedEdit.ForeColor=soundChoose.ForeColor=soundPlay.ForeColor=Color.White;
        soundChoose.Click+=delegate {ChooseSound();};
        soundKind.SelectedIndexChanged+=delegate {if(!syncingSettings && soundKind.SelectedIndex>=0) SetSoundKind(soundKind.SelectedIndex);};
        soundPlay.Click+=delegate {TestAlert();};
        Controls.AddRange(new Control[]{status,microphone,toggle,preview,tracking,trackingStatus,shapes,size,zoomLabel,input,output,resultLabel,mirrorBox,usage,settingsBox,cameraBox,lookBox,hint});
        RenderSettings();RenderCameraSettings();
        timerChoice.SelectedIndexChanged+=async delegate {if(!syncingSettings && timerChoice.SelectedIndex>=0) await SetPrivacyTimer(TimerChoiceSeconds(timerChoice.SelectedIndex));};
        micChoice.SelectedIndexChanged+=async delegate {if(!syncingSettings && micChoice.SelectedIndex>=0) await SetMicrophone(micChoice.SelectedIndex);};
        gestureBox.CheckedChanged+=async delegate {if(!syncingSettings) await SetGestures(gestureBox.Checked);};
        hwMirrorBox.CheckedChanged+=async delegate {if(!syncingSettings) await SetCameraFeature(1,hwMirrorBox.Checked);};
        hwFlipBox.CheckedChanged+=async delegate {if(!syncingSettings) await SetCameraFeature(2,hwFlipBox.Checked);};
        hotKeyBox.CheckedChanged+=delegate {if(!syncingSettings) SetHotKey(hotKeyBox.Checked);};
        hotKeyModifiers.SelectedIndexChanged+=delegate {if(!syncingSettings && hotKeyModifiers.SelectedIndex>=0) ChangeHotKey(Preferences.HotKeyModifiers,HotKeySetting.ModifierChoices[hotKeyModifiers.SelectedIndex]);};
        hotKeyKey.SelectedIndexChanged+=delegate {if(!syncingSettings && hotKeyKey.SelectedIndex>=0) ChangeHotKey(Preferences.HotKeyKey,(int)HotKeySetting.KeyChoices[hotKeyKey.SelectedIndex]);};
        openAuto.CheckedChanged+=delegate {
            if(syncingSettings)return;
            SetPreference(()=>Preferences.SetFlag(Preferences.AutoOpen,openAuto.Checked));
            EventJournal.Write(openAuto.Checked?L.T("Режим: выход из приватности, когда приложение открывает камеру"):L.T("Режим: заставка, объектив открывает пользователь"));
            RenderSettings();
        };
        trustedEdit.Click+=delegate {EditTrusted();};
        lockBox.CheckedChanged+=delegate {if(!syncingSettings) SetPreference(()=>Preferences.SetFlag(Preferences.LockPrivacy,lockBox.Checked));};
        soundBox.CheckedChanged+=delegate {if(!syncingSettings) {SetPreference(()=>Preferences.SetFlag(Preferences.AlertSound,soundBox.Checked));if(soundBox.Checked) AlertSound.Play();}};
        mirrorBox.CheckedChanged+=delegate {if(!syncingSettings) {crop.Mirror=mirrorBox.Checked;video.SetCrop(crop);SaveCrop();}};
        pictureChoose.Click+=delegate {ChoosePicture();};
        pictureReset.Click+=delegate {ResetPicture();};
        closeUnused.CheckedChanged+=delegate {if(!syncingSettings) SetPreference(()=>Preferences.SetFlag(Preferences.AutoPrivacy,closeUnused.Checked));};
        mfBox.CheckedChanged+=async delegate {if(!syncingSettings) await SetMfCamera(mfBox.Checked);};
        crop=Preferences.LoadCrop();
        // The program-side mirror is gone: the camera's own mirror is the only switch. Clear an old saved one.
        if(crop.Mirror) {crop.Mirror=false;SaveCrop();}
        video.SetCrop(crop);
        shapes.SelectedItem=crop.Shape;if(shapes.SelectedIndex<0) shapes.SelectedIndex=2;
        float initialRatio=crop.Ratio;
        syncingSettings=true;mirrorBox.Checked=crop.Mirror;syncingSettings=false;
        size.Value=Math.Max(20,Math.Min(100,(int)Math.Round(crop.W*1280/Math.Min(1280,720*initialRatio)*100)));
        shapes.SelectedIndexChanged+=delegate {crop.Shape=shapes.Text;FitCrop();};size.ValueChanged+=delegate {FitCrop();};
        input.Paint+=DrawCrop;input.MouseDown+=StartDrag;input.MouseMove+=MoveDrag;input.MouseUp+=delegate {dragging=false;SaveCrop();};
        closedIcon=TrayIcons.Privacy(0);openIcon=TrayIcons.Privacy(1);unknownIcon=TrayIcons.Privacy(2);
        trackingOnIcon=TrayIcons.Tracking(Color.DeepSkyBlue);
        trackingOffIcon=TrayIcons.Tracking(Color.Gray);
        trackingWaitingIcon=TrayIcons.Tracking(Color.Goldenrod);
        // Right click opens the control center; Shift + right click keeps the classic menu.
        tray.MouseUp+=delegate(object s,MouseEventArgs e) {if(e.Button==MouseButtons.Right) ShowTrayMenu(tray);};
        trackingTray.MouseUp+=delegate(object s,MouseEventArgs e) {if(e.Button==MouseButtons.Right) ShowTrayMenu(trackingTray);};
        menu.Closed+=delegate {tray.ContextMenuStrip=null;trackingTray.ContextMenuStrip=null;};
        tray.Icon=unknownIcon;tray.Text=L.T("Pixie Pro — проверка");
        // Frequent actions stay on top; every setting lives under "Настройки" so the menu stays narrow.
        var settingsMenu=new ToolStripMenuItem(L.T("Настройки"));
        menu.Items.Add(menuToggle);menu.Items.Add(menuTracking);menu.Items.Add(L.T("Открыть окно"),null,delegate {OpenWindow();});
        menu.Items.Add(settingsMenu);
        menu.Items.Add(L.T("Журнал событий"),null,delegate {OpenJournal();});
        menu.Items.Add(new ToolStripSeparator());menu.Items.Add(L.T("Выход"),null,async delegate {await ExitApp();});
        var menuSoundGroup=new ToolStripMenuItem(L.T("Звук предупреждения"));
        menuSoundGroup.DropDownItems.Add(menuSound);menuSoundGroup.DropDownItems.Add(new ToolStripSeparator());
        var menuSoundKinds=new List<ToolStripMenuItem>();
        for(int i=0;i<SoundNames.Length;i++) {int kind=i;var item=new ToolStripMenuItem(SoundNames[i],null,delegate {SetSoundKind(kind);});menuSoundKinds.Add(item);menuSoundGroup.DropDownItems.Add(item);}
        menuSoundGroup.DropDownItems.Add(menuSoundCustom);menuSoundGroup.DropDownItems.Add(new ToolStripSeparator());menuSoundGroup.DropDownItems.Add(L.T("Проверить предупреждение"),null,delegate {ShowAlert();});
        menuPicture.DropDownItems.Add(menuPictureLock);menuPicture.DropDownItems.Add(menuPictureCustom);
        var settings=settingsMenu.DropDownItems;
        settings.Add(menuAutoOpen);settings.Add(menuTrusted);settings.Add(new ToolStripSeparator());
        settings.Add(menuAutoPrivacy);settings.Add(menuLockPrivacy);settings.Add(menuHotKey);settings.Add(new ToolStripSeparator());
        var menuCamera=new ToolStripMenuItem(L.T("Камера и микрофон"));
        for(int i=0;i<TimerSeconds.Length;i++) {int seconds=TimerSeconds[i];menuTimer.DropDownItems.Add(TimerName(seconds),null,async delegate {await SetPrivacyTimer(seconds);});}
        for(int i=0;i<MicNames.Length;i++) {int choice=i;menuMic.DropDownItems.Add(MicNames[i],null,async delegate {await SetMicrophone(choice);});}
        menuCamera.DropDownItems.AddRange(new ToolStripItem[]{menuTimer,menuMic,new ToolStripSeparator(),menuGestures,menuHwMirror,menuHwFlip});
        settings.Add(menuCamera);
        menuGestures.Click+=async delegate {await SetGestures(gestures!=1);};
        menuHwMirror.Click+=async delegate {await SetCameraFeature(1,hwMirror!=1);};
        menuHwFlip.Click+=async delegate {await SetCameraFeature(2,hwFlip!=1);};
        menuHotKey.Click+=delegate {SetHotKey(!menuHotKey.Checked);};
        settings.Add(menuSoundGroup);settings.Add(menuPicture);settings.Add(new ToolStripSeparator());
        if(MfCamera.Supported) settings.Add(menuMfCamera);
        settings.Add(autostart);
        menuTrusted.Click+=delegate {OpenWindow();if(modern!=null) modern.Navigate("privacy");};
        menuLockPrivacy.Click+=delegate {SetPreference(()=>Preferences.SetFlag(Preferences.LockPrivacy,!menuLockPrivacy.Checked));RenderSettings();};
        menuSoundCustom.Click+=delegate {ChooseSound();};
        menuSound.Click+=delegate {SetPreference(()=>Preferences.SetFlag(Preferences.AlertSound,!menuSound.Checked));RenderSettings();};
        menuPictureLock.Click+=delegate {ResetPicture();};menuPictureCustom.Click+=delegate {ChoosePicture();};
        menu.Opening+=delegate {
            autostart.Checked=Preferences.Autostart;
            menuAutoOpen.Checked=Preferences.GetFlag(Preferences.AutoOpen,false);
            menuAutoPrivacy.Checked=Preferences.GetFlag(Preferences.AutoPrivacy,true);
            menuMfCamera.Checked=mfCameraActive;
            menuLockPrivacy.Checked=Preferences.GetFlag(Preferences.LockPrivacy,true);
            menuSound.Checked=Preferences.GetFlag(Preferences.AlertSound,false);
            int soundChoice=AlertSound.Kind;
            for(int i=0;i<menuSoundKinds.Count;i++) {menuSoundKinds[i].Checked=i==soundChoice;menuSoundKinds[i].Enabled=i!=AlertSound.Custom||AlertSound.CustomFile!=null;}
            bool custom=Placeholder.HasCustom;menuPictureLock.Checked=!custom;menuPictureCustom.Checked=custom;
            menuHotKey.Checked=Preferences.GetFlag(Preferences.HotKey,true);menuHotKey.Text=L.T("Горячая клавиша ")+HotKeySetting.Current;
            bool known=privacyTimer>=0;
            for(int i=0;i<TimerSeconds.Length;i++) {var item=(ToolStripMenuItem)menuTimer.DropDownItems[i];item.Checked=TimerSeconds[i]==privacyTimer;item.Enabled=known;}
            int mic=MicChoice();
            for(int i=0;i<MicNames.Length;i++) {var item=(ToolStripMenuItem)menuMic.DropDownItems[i];item.Checked=i==mic;item.Enabled=i==0||audioMode>0;}
            menuGestures.Checked=gestures==1;menuGestures.Enabled=gestures>=0;
            menuHwMirror.Checked=hwMirror==1;menuHwMirror.Enabled=hwMirror>=0;
            menuHwFlip.Checked=hwFlip==1;menuHwFlip.Enabled=hwFlip>=0;
        };
        // Menu items flip the same settings as the window controls; RenderSettings keeps both in sync.
        menuMfCamera.Click+=async delegate {await SetMfCamera(!mfCameraActive);};
        autostart.Click+=delegate {SetPreference(()=>Preferences.Autostart=!autostart.Checked);};
        menuAutoPrivacy.Click+=delegate {SetPreference(()=>Preferences.SetFlag(Preferences.AutoPrivacy,!menuAutoPrivacy.Checked));RenderSettings();};
        menuAutoOpen.Click+=delegate {
            if(menuAutoOpen.Checked) openManual.Checked=true; else openAuto.Checked=true;
        };
        tray.MouseClick+=async delegate(object s,MouseEventArgs e) {if(e.Button==MouseButtons.Left) await UpdateCamera(true);};
        menuToggle.Click+=async delegate {await UpdateCamera(true);};toggle.Click+=async delegate {await UpdateCamera(true);};
        preview.Click+=async delegate {await TogglePreview();};
        tracking.Click+=async delegate {await ToggleTracking();};
        menuTracking.Click+=async delegate {await ToggleTracking();};
        trackingTray.MouseClick+=async delegate(object s,MouseEventArgs e) {if(e.Button==MouseButtons.Left)await ToggleTracking();};
        poll.Interval=1000;poll.Tick+=async delegate {await UpdateCamera(false);await Supervise();UpdateUsage();if(++ticks%5==0) await CheckMicrophone();};
        paint.Interval=120;paint.Tick+=delegate {RevealVideo();RefreshFrames();};
        Shown+=async delegate {
            EventJournal.Write(L.T("Pixie Privacy Pro запущена"));await UpdateCamera(false);UpdateUsage();await CheckMicrophone();poll.Start();paint.Start();
            await LoadCameraSettings();
            if(!layoutOnly && MfCamera.Supported && Preferences.GetFlag(Preferences.MfCamera,true)) await StartMfCamera();
        };
        Resize+=delegate {if(WindowState==FormWindowState.Minimized) Hide();};
        RenderTracking();tray.Visible=!layoutOnly;trackingTray.Visible=!layoutOnly && Preferences.GetFlag(Preferences.TrackingTrayIcon,false);
        if(!layoutOnly) {SystemEvents.SessionSwitch+=OnSessionSwitch;SystemEvents.PowerModeChanged+=OnPowerModeChanged;}
    }
    void SetPreference(Action change) {
        try {change();} catch(Exception e) {hint.Text=L.T("Не удалось сохранить настройку: ")+e.Message;}
    }

    // State and actions for the WPF window and the tray control center; they read it on a timer and call these.
    public int Mode {get {return mode;}}
    public bool Busy {get {return cameraChanging;}}
    public bool TrackingWanted {get {return trackingWanted;}}
    public bool PreviewOn {get {return manualPreview;}}
    public bool CaptureRunning {get {return video.Running;}}
    public bool VideoLive {get {return video.Live;}}
    public bool ConsumerActive {get {return consumerWasActive;}}
    public List<string> Readers {get {return consumerWasActive?pixieReaders:new List<string>();}}
    public List<string> CameraUsers {get {return cameraApps;}}
    public List<string> MicrophoneUsers {get {return microphoneApps;}}
    public DateTime? OpenSince {get {return openSince;}}
    public string MicrophoneText {get {return microphone.Text;}}
    public bool MicrophoneWarning {get {return microphone.ForeColor==Color.OrangeRed;}}
    public string HintText {get {return hint.Text;}}
    public int CameraTimer {get {return privacyTimer;}}
    public int MicrophoneChoice {get {return MicChoice();}}
    public bool MicrophoneKnown {get {return audioMode>0;}}
    public int AudioProcessing {get {return audioMode;}}
    public int GesturesState {get {return gestures;}}
    public int HardwareMirror {get {return hwMirror;}}
    public int HardwareFlip {get {return hwFlip;}}
    public bool MfCameraOn {get {return mfCameraActive;}}
    public bool MfCameraWanted {get {return Preferences.GetFlag(Preferences.MfCamera,true);}}
    public bool MfCameraBusy {get {return mfPending>0;}}
    public string MfCameraError {get {return mfError;}}
    public bool HotKeyBusy {get {return Preferences.GetFlag(Preferences.HotKey,true) && !hotKeyRegistered && !layoutOnly;}}
    public CropSettings CurrentCrop {get {return crop;}}
    public bool CopyPreview(IntPtr target,int stride,ref long seen) {return video.CopyPreview(target,stride,ref seen);}
    public string LiveSize {get {var s=video.SourceSize;return s.IsEmpty?"":s.Width+"×"+s.Height;}}
    public int CropSize {get {return size.Value;}}
    public static int[] TimerChoices {get {return TimerSeconds;}}
    public static string[] MicrophoneNames {get {return MicNames;}}
    public static string[] SoundChoices {get {return SoundNames;}}
    public static string ModeTitle(int value) {return ModeName(value);}

    public Task TogglePrivacy() {return UpdateCamera(true);}
    public Task SwitchTracking() {return ToggleTracking();}
    public Task SwitchPreview() {return TogglePreview();}
    public Task ChooseCameraTimer(int seconds) {return SetPrivacyTimer(seconds);}
    public Task ChooseMicrophone(int choice) {return SetMicrophone(choice);}
    public Task ChooseGestures(bool on) {return SetGestures(on);}
    public Task ChooseHardwareMirror(bool on) {return SetCameraFeature(1,on);}
    public Task ChooseHardwareFlip(bool on) {return SetCameraFeature(2,on);}
    public Task ChooseMfCamera(bool on) {return SetMfCamera(on);}
    public Task Quit() {return ExitApp();}
    // The language is read once at startup, so a new choice restarts the app; it reopens on the Appearance page.
    public async Task ChangeLanguage(int choice) {
        if(choice==Preferences.GetInt(Preferences.Language,0)) return;
        SetPreference(()=>Preferences.SetInt(Preferences.Language,choice));
        Process.Start(Application.ExecutablePath,"--restart "+Process.GetCurrentProcess().Id+" --page look");
        await ExitApp();
    }
    public void SetAutoOpen(bool auto) {if(auto) openAuto.Checked=true; else openManual.Checked=true;}
    public void SetFlag(string name,bool value) {SetPreference(()=>Preferences.SetFlag(name,value));RenderSettings();}
    public void SetAutostart(bool value) {SetPreference(()=>Preferences.Autostart=value);}
    public void SetHotKeyOn(bool on) {SetHotKey(on);}
    public void SetHotKeyCombo(int modifiers,Keys key) {
        SetPreference(()=>{Preferences.SetInt(Preferences.HotKeyModifiers,modifiers);Preferences.SetInt(Preferences.HotKeyKey,(int)key);});
        ChangeHotKey(Preferences.HotKeyKey,(int)key);
    }
    // While the window records a new combination the old one must not toggle privacy.
    public void PauseHotKey(bool pause) {
        if(pause) {if(hotKeyRegistered && IsHandleCreated) {UnregisterHotKey(Handle,HotKeyId);hotKeyRegistered=false;}}
        else RegisterHotKeys();
    }
    public void ChooseSoundKind(int kind) {SetSoundKind(kind);}
    public void PickSound() {ChooseSound();}
    public void PickPicture() {ChoosePicture();}
    public void ClearPicture() {ResetPicture();}
    // Trusted apps are edited right on the Privacy page.
    public void SetTrustedApps(List<string> apps) {
        SetPreference(()=>Preferences.SetTrustedApps(apps));
        EventJournal.Write(L.T("Доверенные программы: ")+(apps.Count==0?L.T("любая программа"):CapabilityUsage.Describe(apps)));
        RenderSettings();
    }
    public void AddTrusted(string name) {
        var apps=Preferences.TrustedApps();
        if(PixieReaders.Contains(apps,name)) return;
        apps.Add(name);SetTrustedApps(apps);
    }
    public void RemoveTrusted(string name) {
        var apps=Preferences.TrustedApps();
        apps.RemoveAll(a=>string.Equals(a,name,StringComparison.OrdinalIgnoreCase));
        SetTrustedApps(apps);
    }
    public void AddTrustedFromFile() {
        using(var dialog=new OpenFileDialog {Title=L.T("Программа, которой можно открывать камеру"),Filter=L.T("Программы|*.exe")})
            if(dialog.ShowDialog(Visible?this:null)==DialogResult.OK) AddTrusted(Path.GetFileName(dialog.FileName));
    }
    // The test button follows the picture and sound switches; pressed again it stops the test.
    public void TestAlert() {if(AlertRunning) StopAlert(); else ShowAlert();}
    public bool AlertRunning {get {return (alert!=null && !alert.IsDisposed) || DateTime.UtcNow<soundUntil;}}
    public bool AlertEnabled {get {return Preferences.GetFlag(Preferences.AlertPicture,true) || Preferences.GetFlag(Preferences.AlertSound,false);}}
    public void ShowJournalFile() {OpenJournal();}
    public void SetCropShape(string shape) {shapes.SelectedItem=shape;}
    public void SetCropSize(int percent) {size.Value=Math.Max(size.Minimum,Math.Min(size.Maximum,percent));}
    public void SetMirror(bool value) {mirrorBox.Checked=value;}
    public void MoveCrop(float x,float y,bool save) {crop.Move(x,y);video.SetCrop(crop);if(save) SaveCrop();}
    public void SetTrackingTrayIcon(bool show) {
        SetPreference(()=>Preferences.SetFlag(Preferences.TrackingTrayIcon,show));
        trackingTray.Visible=show && !layoutOnly;
    }
    public void ThemeChanged() {if(modern!=null) modern.ApplyTheme();}
    ModernWindow modern;ControlCenter center;
    public ModernWindow Window {get {return modern;}}
    void ShowTrayMenu(NotifyIcon icon) {
        if((ModifierKeys&Keys.Shift)!=0) {
            icon.ContextMenuStrip=menu;
            typeof(NotifyIcon).GetMethod("ShowContextMenu",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(icon,null);
            return;
        }
        ShowControlCenter();
    }
    public void ShowControlCenter() {
        if(center!=null && center.IsVisible) {center.Close();return;}
        center=new ControlCenter(this);
        center.ShowNearTray();
    }
    void RenderSettings() {
        syncingSettings=true;
        try {
            bool auto=Preferences.GetFlag(Preferences.AutoOpen,false);
            openAuto.Checked=auto;openManual.Checked=!auto;
            closeUnused.Checked=Preferences.GetFlag(Preferences.AutoPrivacy,true);
            mfBox.Checked=Preferences.GetFlag(Preferences.MfCamera,true);
            lockBox.Checked=Preferences.GetFlag(Preferences.LockPrivacy,true);
            soundBox.Checked=Preferences.GetFlag(Preferences.AlertSound,false);
            soundKind.SelectedIndex=AlertSound.Kind;
            var trusted=Preferences.TrustedApps();
            trustedLabel.Text=L.T("Доверенные: ")+(trusted.Count==0?L.T("любая программа"):CapabilityUsage.Describe(trusted));
            // A disabled Label draws dark grey, unreadable on the dark theme; dim it by colour instead.
            trustedLabel.ForeColor=auto?Color.White:Color.FromArgb(150,156,168);
            trustedEdit.Enabled=auto;
            pictureThumb.Image=Placeholder.Privacy();pictureReset.Enabled=Placeholder.HasCustom;
            bool hotKeyOn=Preferences.GetFlag(Preferences.HotKey,true);
            hotKeyBox.Checked=hotKeyOn;hotKeyModifiers.Enabled=hotKeyKey.Enabled=hotKeyOn;
            hotKeyModifiers.SelectedIndex=Array.IndexOf(HotKeySetting.ModifierChoices,HotKeySetting.Modifiers);
            hotKeyKey.SelectedIndex=Array.IndexOf(HotKeySetting.KeyChoices,HotKeySetting.Key);
            menuToggle.ShortcutKeyDisplayString=hotKeyOn?HotKeySetting.Current:null;
        } finally {syncingSettings=false;}
    }
    public static string TimerName(int seconds) {
        if(seconds<=0) return L.T("никогда");
        return seconds<60||seconds%60!=0 ? L.T("через ")+seconds+L.T(" с") : L.T("через ")+seconds/60+L.T(" мин");
    }
    int TimerChoiceSeconds(int index) {return index<TimerSeconds.Length?TimerSeconds[index]:privacyTimer;}
    // 0 = microphone disabled in Windows; 1-3 = allowed with that processing.
    int MicChoice() {return !MicrophoneGuard.Allowed?0:audioMode>=1&&audioMode<=3?audioMode:-1;}
    void RenderCameraSettings() {
        syncingSettings=true;
        try {
            timerChoice.Items.Clear();
            foreach(int s in TimerSeconds) timerChoice.Items.Add(TimerName(s));
            int index=Array.IndexOf(TimerSeconds,privacyTimer);
            if(privacyTimer>0 && index<0) {timerChoice.Items.Add(TimerName(privacyTimer));index=TimerSeconds.Length;}
            timerChoice.SelectedIndex=index;timerChoice.Enabled=privacyTimer>=0;
            micChoice.SelectedIndex=MicChoice();
            gestureBox.Checked=gestures==1;gestureBox.Enabled=gestures>=0;
            hwMirrorBox.Checked=hwMirror==1;hwMirrorBox.Enabled=hwMirror>=0;
            hwFlipBox.Checked=hwFlip==1;hwFlipBox.Enabled=hwFlip>=0;
        } finally {syncingSettings=false;}
    }
    // Runs on a worker thread inside a Pixy lock; a setting the camera does not answer stays unknown.
    void ReadCameraSettings(Pixy camera) {
        try {privacyTimer=camera.PrivacyTimer();} catch {}
        try {audioMode=camera.AudioMode();} catch {}
        try {gestures=camera.Gestures()?1:0;} catch {}
        try {hwMirror=camera.Feature(1)?1:0;} catch {}
        try {hwFlip=camera.Feature(2)?1:0;} catch {}
    }
    async Task LoadCameraSettings() {
        if(settingsLoading||exiting||layoutOnly) return;
        settingsLoading=true;
        await cameraGate.WaitAsync();
        try {
            if(exiting) return;
            bool chosen=Preferences.GetFlag(Preferences.CameraTimerChosen,false);
            await Task.Run(()=>{using(var camera=new Pixy()) {
                ReadCameraSettings(camera);
                settingsRead=true;
                // The camera ships with the timer off; turn the hardware safety net on once, then respect the user's choice.
                if(!chosen && privacyTimer==0) privacyTimer=camera.SetPrivacyTimer(60);
            }});
            if(!chosen && privacyTimer>0) {
                SetPreference(()=>Preferences.SetFlag(Preferences.CameraTimerChosen,true));
                EventJournal.Write(L.T("Камера сама закрывается ")+TimerName(privacyTimer)+L.T(" без использования"));
            }
        } catch(Exception e) {hint.Text=L.T("Не удалось прочитать настройки камеры: ")+e.Message;}
        finally {cameraGate.Release();settingsLoading=false;}
        if(!exiting) RenderCameraSettings();
    }
    async Task<bool> ChangeCamera(string journal,Action<Pixy> change) {
        if(exiting) return false;
        await cameraGate.WaitAsync();
        bool done=false;
        try {
            if(exiting) return false;
            await Task.Run(()=>{using(var camera=new Pixy()) {change(camera);ReadCameraSettings(camera);}});
            EventJournal.Write(journal);done=true;
        } catch(Exception e) {
            hint.Text=L.T("Не удалось изменить настройку камеры: ")+e.Message;
            tray.ShowBalloonTip(4000,"Pixie Privacy Pro",L.T("Не удалось изменить настройку камеры: ")+e.Message,ToolTipIcon.Warning);
        } finally {cameraGate.Release();}
        if(!exiting) RenderCameraSettings();
        return done;
    }
    async Task SetPrivacyTimer(int seconds) {
        if(seconds==privacyTimer) return;
        if(await ChangeCamera(L.T("Камера сама закрывается: ")+TimerName(seconds),camera=>camera.SetPrivacyTimer(seconds)))
            SetPreference(()=>Preferences.SetFlag(Preferences.CameraTimerChosen,true));
        nextCameraPoll=DateTime.MinValue;
    }
    async Task SetGestures(bool on) {await ChangeCamera(L.T("Управление жестами ")+(on?L.T("включено"):L.T("выключено")),camera=>camera.SetGestures(on));}
    async Task SetCameraFeature(int id,bool on) {
        await ChangeCamera((id==1?L.T("Зеркально в самой камере"):L.T("Переворот изображения"))+(on?L.T(": включено"):L.T(": выключено")),camera=>camera.SetFeature(id,on));
    }
    // "Disabled" switches the endpoint off in Windows through the service; a processing mode allows it again.
    async Task SetMicrophone(int choice) {
        if(choice==MicChoice()) return;
        try {
            if(choice==0) {
                MicrophoneGuard.SetAllowed(false);
                EventJournal.Write(L.T("Микрофон камеры: отключён полностью"));
            } else {
                if(!await ChangeCamera(L.T("Обработка звука микрофона: ")+MicNames[choice],camera=>camera.SetAudioMode(choice))) return;
                MicrophoneGuard.SetAllowed(true);
                EventJournal.Write(L.T("Микрофон камеры включён: ")+MicNames[choice]);
            }
        } catch(Exception e) {
            hint.Text=L.T("Не удалось изменить микрофон: ")+e.Message;
            tray.ShowBalloonTip(4000,"Pixie Privacy Pro",L.T("Не удалось изменить микрофон: ")+e.Message,ToolTipIcon.Warning);
        } finally {if(!exiting) RenderCameraSettings();}
        // The service applies the choice within three seconds.
        microphone.Text=L.T("Микрофон PIXY: применяется…");
        await Task.Delay(3500);
        await CheckMicrophone();
    }
    void SetHotKey(bool on) {
        SetPreference(()=>Preferences.SetFlag(Preferences.HotKey,on));
        RegisterHotKeys();
        RenderSettings();
    }
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr window,int id,int modifiers,int key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window,int id);
    void RegisterHotKeys() {
        if(layoutOnly||!IsHandleCreated) return;
        if(hotKeyRegistered) {UnregisterHotKey(Handle,HotKeyId);hotKeyRegistered=false;}
        if(!Preferences.GetFlag(Preferences.HotKey,true)) return;
        // 0x4000: MOD_NOREPEAT, holding the keys toggles once.
        hotKeyRegistered=RegisterHotKey(Handle,HotKeyId,HotKeySetting.Modifiers|0x4000,(int)HotKeySetting.Key);
        if(!hotKeyRegistered) {
            hint.Text=L.T("Сочетание ")+HotKeySetting.Current+L.T(" уже занято Windows или другой программой. Выберите другое в «Горячая клавиша».");
            tray.ShowBalloonTip(5000,"Pixie Privacy Pro",L.T("Сочетание ")+HotKeySetting.Current+L.T(" занято — выберите другое в окне программы."),ToolTipIcon.Warning);
        }
    }
    void ChangeHotKey(string name,int value) {
        SetPreference(()=>Preferences.SetInt(name,value));
        RegisterHotKeys();
        if(hotKeyRegistered) {hint.Text=L.T("Приватность включается и выключается сочетанием ")+HotKeySetting.Current+".";EventJournal.Write(L.T("Горячая клавиша: ")+HotKeySetting.Current);}
        RenderSettings();
    }
    // After leaving privacy apps get the video only once the lens has risen and the picture has settled.
    void RevealVideo() {
        if(revealAt==DateTime.MinValue || DateTime.UtcNow<revealAt) return;
        revealAt=DateTime.MinValue;
        if(mode!=2) video.SetAllowed(true);
    }
    bool VideoAllowed {get {return mode!=2 && revealAt==DateTime.MinValue;}}
    void EditTrusted() {
        using(var dialog=new TrustedAppsDialog(Preferences.TrustedApps(),pixieReaders)) {
            if(dialog.ShowDialog(Visible?this:null)!=DialogResult.OK) return;
            SetPreference(()=>Preferences.SetTrustedApps(dialog.Result));
            EventJournal.Write(L.T("Доверенные программы: ")+(dialog.Result.Count==0?L.T("любая программа"):CapabilityUsage.Describe(dialog.Result)));
        }
        RenderSettings();
    }
    void ChooseSound() {
        using(var dialog=new OpenFileDialog {Title=L.T("Звук для предупреждения «Я вижу тебя»"),Filter=L.T("Звук|*.mp3;*.wav;*.wma")}) {
            if(dialog.ShowDialog(Visible?this:null)!=DialogResult.OK) return;
            try {AlertSound.SetCustom(dialog.FileName);EventJournal.Write(L.T("Звук предупреждения: ")+Path.GetFileName(dialog.FileName));}
            catch(Exception e) {hint.Text=L.T("Не удалось взять звук: ")+e.Message;tray.ShowBalloonTip(4000,"Pixie Privacy Pro",L.T("Не удалось взять звук: ")+e.Message,ToolTipIcon.Warning);return;}
        }
        // Choosing a sound means the user wants to hear it.
        SetPreference(()=>{Preferences.SetFlag(Preferences.AlertSound,true);Preferences.SetInt(Preferences.AlertSoundKind,AlertSound.Custom);});
        RenderSettings();AlertSound.Play();
    }
    // "Own sound" without a chosen file asks for one first.
    void SetSoundKind(int kind) {
        if(kind==AlertSound.Custom && AlertSound.CustomFile==null) {ChooseSound();RenderSettings();return;}
        SetPreference(()=>Preferences.SetInt(Preferences.AlertSoundKind,kind));
        EventJournal.Write(L.T("Звук предупреждения: ")+SoundNames[kind]);
        RenderSettings();AlertSound.Play();
    }
    void ChoosePicture() {
        using(var dialog=new OpenFileDialog {Title=L.T("Картинка, которую приложения видят при приватности"),Filter=L.T("Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Все файлы|*.*")}) {
            if(dialog.ShowDialog(Visible?this:null)!=DialogResult.OK) return;
            try {Placeholder.SetCustom(dialog.FileName);EventJournal.Write(L.T("Картинка при приватности: ")+Path.GetFileName(dialog.FileName));}
            catch(Exception e) {hint.Text=L.T("Не удалось открыть картинку: ")+e.Message;tray.ShowBalloonTip(4000,"Pixie Privacy Pro",L.T("Не удалось открыть картинку: ")+e.Message,ToolTipIcon.Warning);return;}
        }
        PictureChanged();
    }
    void ResetPicture() {
        try {Placeholder.ClearCustom();EventJournal.Write(L.T("Картинка при приватности: замок"));}
        catch(Exception e) {hint.Text=L.T("Не удалось вернуть замок: ")+e.Message;return;}
        PictureChanged();
    }
    void PictureChanged() {
        video.SetIdleFrame(Placeholder.Privacy());
        RenderSettings();
    }
    // Starting the MF camera takes seconds; clicks during it are queued and the latest choice wins.
    readonly SemaphoreSlim mfGate=new SemaphoreSlim(1,1);
    int mfPending;string mfError;
    async Task SetMfCamera(bool enable) {
        SetPreference(()=>Preferences.SetFlag(Preferences.MfCamera,enable));
        mfPending++;
        await mfGate.WaitAsync();
        try {
            if(Preferences.GetFlag(Preferences.MfCamera,true)) await StartMfCamera(); else await StopMfCamera();
        } finally {mfGate.Release();mfPending--;}
        RenderSettings();
    }
    void SaveTracking(bool value) {
        if(trackingWanted==value)return;
        trackingWanted=value;
        try {Preferences.SetFlag(Preferences.Tracking,value);}
        catch(Exception e){hint.Text=L.T("Не удалось сохранить автослежение: ")+e.Message;}
    }
    int LeavePrivacy(Pixy camera) {
        int result=camera.Set(0);
        return trackingWanted?camera.Set(1):result;
    }
    void RenderTracking() {
        string action=trackingWanted?L.T("Выключить автослежение"):L.T("Включить автослежение");
        tracking.Text=menuTracking.Text=action;
        tracking.Enabled=menuTracking.Enabled=!cameraChanging && (mode==0||mode==1||mode==3);
        menuTracking.Checked=mode==1;
        string text=mode<0?L.T("Автослежение: состояние неизвестно"):
            mode==2?L.T("Автослежение: только при открытом объективе"):
            mode==3?(trackingWanted?L.T("Выбрано слежение · ожидание видео"):L.T("Выбран обычный режим · ожидание видео")):
            mode==1?L.T("Автослежение включено"):L.T("Автослежение выключено");
        trackingStatus.Text=text;
        trackingTray.Text=text;
        Icon icon=mode==1?trackingOnIcon:mode==3||mode<0?trackingWaitingIcon:trackingOffIcon;
        if(trackingTray.Icon!=icon)trackingTray.Icon=icon;
    }
    async Task ToggleTracking() {
        if(exiting)return;
        await cameraGate.WaitAsync();
        if(exiting){cameraGate.Release();return;}
        cameraChanging=true;RenderTracking();
        try {
            int current=await Task.Run(()=>{using(var camera=new Pixy())return camera.Query();});
            if(exiting)return;
            ApplyMode(current,false);
            if(current==2)return; // Tracking works only with the lens open; never open it through the tracking button.
            bool wanted=!trackingWanted;
            int result=await Task.Run(()=>{using(var camera=new Pixy()) {
                // Recheck after the first query in case another app enabled privacy.
                if(camera.Query()==2)return 2;
                return camera.Set(wanted?1:0);
            }});
            if(exiting)return;
            if(result!=2)SaveTracking(wanted);
            ApplyMode(result,true);
        } catch(Exception e) {
            if(!exiting){MarkUnknown();hint.Text=e.Message;trackingTray.ShowBalloonTip(4000,L.T("Автослежение"),e.Message,ToolTipIcon.Warning);}
        } finally {cameraChanging=false;cameraGate.Release();if(!exiting)RenderTracking();}
    }
    void FitCrop() {var image=input.Image;crop.Fit(image==null?1280:image.Width,image==null?720:image.Height,size.Value/100f);video.SetCrop(crop);input.Invalidate();SaveCrop();}
    void SaveCrop() {try {Preferences.SaveCrop(crop);} catch(Exception e) {hint.Text=L.T("Не удалось сохранить рамку: ")+e.Message;}}
    RectangleF ImageBounds() {
        if(input.Image==null) return RectangleF.Empty;
        float s=Math.Min(input.Width/(float)input.Image.Width,input.Height/(float)input.Image.Height);
        return new RectangleF((input.Width-input.Image.Width*s)/2,(input.Height-input.Image.Height*s)/2,input.Image.Width*s,input.Image.Height*s);
    }
    void DrawCrop(object sender,PaintEventArgs e) {
        RectangleF b=ImageBounds();if(b.IsEmpty) return;
        RectangleF r=new RectangleF(b.X+crop.X*b.Width,b.Y+crop.Y*b.Height,crop.W*b.Width,crop.H*b.Height);
        using(var shape=new System.Drawing.Drawing2D.GraphicsPath()) {
            if(crop.IsCircle) shape.AddEllipse(r); else shape.AddRectangle(r);
            using(var shade=new SolidBrush(Color.FromArgb(130,0,0,0))) using(var region=new Region(b)) {region.Exclude(shape);e.Graphics.FillRegion(shade,region);}
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var pen=new Pen(Color.LimeGreen,2)) e.Graphics.DrawPath(pen,shape);
        }
    }
    void StartDrag(object sender,MouseEventArgs e) {if(e.Button!=MouseButtons.Left||input.Image==null) return;dragging=true;dragOrigin=e.Location;dragX=crop.X;dragY=crop.Y;input.Capture=true;}
    void MoveDrag(object sender,MouseEventArgs e) {if(!dragging)return;var b=ImageBounds();if(b.IsEmpty)return;crop.Move(dragX+(e.X-dragOrigin.X)/b.Width,dragY+(e.Y-dragOrigin.Y)/b.Height);video.SetCrop(crop);input.Invalidate();}
    void RefreshFrames() {
        if(exiting)return;
        bool shown=modern!=null && modern.PreviewShown;
        video.PreviewVisible=shown;
        if(shown) {
            // Apps get only the privacy picture; the window also tells "starting" (yellow) and "unknown" (grey) apart.
            // Live frames are copied by the window itself on every render; here only the fallback picture is chosen.
            modern.ShowLive(video.Running && video.Live?null:Placeholder.Status(mode));
        }
        try {video.PulseClosed();} catch(Exception e) {hint.Text=L.T("Виртуальная камера: ")+e.Message;}
        if(video.Error!=null) hint.Text=L.T("Видео с PIXY: ")+video.Error;
    }
    // Called every second and after privacy changes: starts/stops the capture for apps reading a Pixie camera.
    public async Task Supervise() {
        if(exiting||cameraChanging)return;
        bool consumer=video.ConsumerActive;
        bool arrived=consumer && !consumerWasActive;
        if(consumer!=consumerWasActive) EventJournal.Write(consumer?L.T("Приложение открыло камеру Pixie"):L.T("Приложения больше не читают камеру Pixie"));
        consumerWasActive=consumer;
        if(consumer) lastConsumer=DateTime.UtcNow;
        bool recent=DateTime.UtcNow-lastConsumer<ConsumerGrace;
        if(consumer && (arrived || ticks%3==0)) RefreshReaders();
        if(!recent) pixieReaders=new List<string>();
        bool autoOpen=Preferences.GetFlag(Preferences.AutoOpen,false);
        // With a trusted list the lens opens when a trusted reader is identified, which may lag the first frame request.
        bool opener=Preferences.TrustedApps().Count==0 ? arrived : pendingTrustedArrival;
        pendingTrustedArrival=false;
        switch(CaptureRules.Decide(manualPreview,recent,opener,mode,autoOpen,video.Running)) {
            case CaptureAction.OpenLens: await OpenLensForApp(); break;
            case CaptureAction.Start: if(DateTime.UtcNow>=retryStartAfter) await StartCapture(); break;
            case CaptureAction.Stop: await StopCapture(!manualPreview && !recent && mode!=2 && Preferences.GetFlag(Preferences.AutoPrivacy,true)); break;
        }
        if(!CaptureRules.IdleLens(Preferences.GetFlag(Preferences.AutoPrivacy,true),mode,video.Running,manualPreview,recent,cameraApps.Count)) idleLensSince=null;
        else if(idleLensSince==null) idleLensSince=DateTime.UtcNow;
        else if(DateTime.UtcNow-idleLensSince.Value>=IdleLensClose) {idleLensSince=null;await CloseIdleLens();}
        RenderPreviewButton();
    }
    async void RefreshReaders() {
        if(readersBusy||exiting||layoutOnly) return;
        readersBusy=true;
        try {
            bool mf=video.MfConsumerActive;var apps=cameraApps;
            var directShow=await Task.Run(()=>PixieReaders.DirectShow());
            if(exiting) return;
            var now=PixieReaders.Merge(directShow,mf,apps);
            var added=new List<string>();
            foreach(string app in now) if(!PixieReaders.Contains(pixieReaders,app)) added.Add(app);
            pixieReaders=now;
            if(added.Count==0) return;
            EventJournal.Write(L.T("Камеру Pixie читает: ")+CapabilityUsage.Describe(added));
            var trusted=Preferences.TrustedApps();
            if(trusted.Count==0 || !Preferences.GetFlag(Preferences.AutoOpen,false)) return;
            if(PixieReaders.AnyTrusted(trusted,added)) {pendingTrustedArrival=true;await Supervise();}
            else if(mode==2) {
                EventJournal.Write(L.T("Не в списке доверенных, объектив не открыт: ")+CapabilityUsage.Describe(added));
                tray.ShowBalloonTip(4000,"Pixie Privacy Pro",CapabilityUsage.Describe(added)+L.T(" не в списке доверенных — показывается заставка."),ToolTipIcon.Info);
            }
        } catch(Exception e) {hint.Text=L.T("Не удалось определить, кто читает камеру: ")+e.Message;}
        finally {readersBusy=false;}
    }
    async Task CloseIdleLens() {
        await cameraGate.WaitAsync();
        if(exiting){cameraGate.Release();return;}
        cameraChanging=true;RenderTracking();
        try {
            int closed=await Task.Run(()=>{using(var camera=new Pixy())return camera.Set(2);});
            ApplyMode(closed,true);
            EventJournal.Write(L.T("Объектив закрыт: камеру никто не использует"));
        } catch(Exception e) {hint.Text=e.Message;}
        finally {cameraChanging=false;cameraGate.Release();if(!exiting)RenderTracking();}
    }
    void RenderPreviewButton() {
        preview.Text=manualPreview?L.T("Остановить предпросмотр"):L.T("Открыть камеру и предпросмотр");
    }
    async Task OpenLensForApp() {
        await cameraGate.WaitAsync();
        if(exiting){cameraGate.Release();return;}
        cameraChanging=true;RenderTracking();
        try {
            int result=await Task.Run(()=>{using(var camera=new Pixy()) {int m=camera.Query();return m==2?LeavePrivacy(camera):m;}});
            ApplyMode(result,true);
            EventJournal.Write(L.T("Выход из приватности: приложение открыло камеру Pixie"));
            tray.ShowBalloonTip(4000,"Pixie Privacy Pro",L.T("Приложение открыло камеру Pixie — объектив открыт."),ToolTipIcon.Info);
        } catch(Exception e) {MarkUnknown();hint.Text=e.Message;}
        finally {cameraChanging=false;cameraGate.Release();if(!exiting)RenderTracking();}
        await Supervise();
    }
    async Task StartCapture() {
        await cameraGate.WaitAsync();
        if(exiting){cameraGate.Release();return;}
        cameraChanging=true;
        try {
            video.SetIdleFrame(Placeholder.Privacy());
            video.SetAllowed(VideoAllowed);video.SetCrop(crop);
            await Task.Run(()=>video.Start());
            nextCameraPoll=DateTime.MinValue;
            EventJournal.Write(L.T("Видео с PIXY передаётся в камеру Pixie"));
        } catch(Exception e) {
            retryStartAfter=DateTime.UtcNow+StartRetry;
            hint.Text=L.T("Не удалось запустить видео с PIXY: ")+e.Message;
            EventJournal.Write(L.T("Не удалось запустить видео с PIXY: ")+e.Message);
        } finally {cameraChanging=false;cameraGate.Release();}
    }
    async Task StopCapture(bool closeLens) {
        await cameraGate.WaitAsync();
        if(exiting){cameraGate.Release();return;}
        cameraChanging=true;RenderTracking();
        try {
            await Task.Run(()=>video.Stop());
            EventJournal.Write(L.T("Видео с PIXY остановлено"));
            if(closeLens) {
                int closed=await Task.Run(()=>{using(var camera=new Pixy())return camera.Set(2);});
                ApplyMode(closed,true);
                EventJournal.Write(L.T("Объектив закрыт: камеру Pixie никто не использует"));
            }
        } catch(Exception e) {hint.Text=e.Message;}
        finally {cameraChanging=false;cameraGate.Release();if(!exiting)RenderTracking();}
    }
    static void ReplaceImage(PictureBox box,Bitmap image) {var old=box.Image;box.Image=image;if(old!=null)old.Dispose();}
    async Task CheckMicrophone() {
        if(micBusy||exiting)return;micBusy=true;
        try {
            string text=await Task.Run(()=> {string state=MicrophoneGuard.Inspect(false);return state+" · "+GuardServiceState();});
            if(!exiting) {
                bool warning=text.Contains(L.T("ВНИМАНИЕ"))||text.Contains(L.T("остановлена"))||text.Contains(L.T("не установлена"));
                // The first result only replaces "checking…"; later changes go to the journal and warnings to a balloon.
                bool checkedBefore=microphone.Text!=L.T("Проверка защиты микрофона…");
                if(text!=microphone.Text && checkedBefore) EventJournal.Write(L.T("Защита микрофона: ")+text);
                if(warning && microphone.ForeColor!=Color.OrangeRed && checkedBefore) tray.ShowBalloonTip(5000,L.T("Защита микрофона"),text,ToolTipIcon.Warning);
                microphone.Text=text;microphone.ForeColor=warning?Color.OrangeRed:Color.LightGray;
            }
        } catch(Exception e) {if(!exiting) {microphone.Text=L.T("Защита микрофона не подтверждена: ")+e.Message;microphone.ForeColor=Color.OrangeRed;}}
        finally {micBusy=false;}
    }
    static string GuardServiceState() {
        try {
            using(var service=new ServiceController(MicrophoneGuard.ServiceName))
                return service.Status==ServiceControllerStatus.Running ? L.T("защита работает") : L.T("служба защиты остановлена");
        } catch(InvalidOperationException) {return L.T("служба защиты не установлена");}
    }
    void UpdateUsage() {
        if(exiting)return;
        try {
            var camera=CapabilityUsage.Active(CapabilityUsage.Camera,Application.ExecutablePath);
            var mic=CapabilityUsage.Active(CapabilityUsage.Microphone,Application.ExecutablePath);
            JournalChanges(L.T("камеру"),cameraApps,camera);
            JournalChanges(L.T("микрофон"),microphoneApps,mic);
            cameraApps=camera;microphoneApps=mic;
            usage.Text=(consumerWasActive?L.T("Камеру Pixie читает: ")+(pixieReaders.Count==0?L.T("приложение"):CapabilityUsage.Describe(pixieReaders))+" · ":"")+L.T("Сейчас используют камеру: ")+CapabilityUsage.Describe(camera)+L.T(" · микрофон: ")+CapabilityUsage.Describe(mic);
            usage.ForeColor=consumerWasActive||camera.Count>0||mic.Count>0?Color.Gold:Color.LightGray;
        } catch(Exception e) {usage.Text=L.T("Использование камеры и микрофона не определено: ")+e.Message;}
        UpdateTrayTip();
    }
    // Tooltip: state, how long the lens has been open, and who reads it.
    void UpdateTrayTip() {
        string tip;
        bool readers=consumerWasActive && pixieReaders.Count>0;
        if(mode==2) tip=L.T("Pixie Pro — приватность включена")+(readers?L.T("\nЗаставку видит: ")+CapabilityUsage.Describe(pixieReaders):"");
        else if(mode<0) tip=L.T("Pixie Pro — состояние неизвестно");
        else {
            tip=L.T("Pixie Pro — камера открыта")+(openSince==null?"":" "+Duration(DateTime.UtcNow-openSince.Value));
            if(readers) tip+=L.T("\nЧитает: ")+CapabilityUsage.Describe(pixieReaders);
            else if(cameraApps.Count>0) tip+=L.T("\nИспользует: ")+CapabilityUsage.Describe(cameraApps);
        }
        SetTrayText(tip);
    }
    public static string Duration(TimeSpan time) {
        if(time.TotalMinutes<1) return L.T("меньше минуты");
        if(time.TotalHours<1) return (int)time.TotalMinutes+L.T(" мин");
        return (int)time.TotalHours+L.T(" ч ")+time.Minutes+L.T(" мин");
    }
    // NotifyIcon.Text refuses more than 63 characters on .NET Framework, while Windows shows up to 127.
    void SetTrayText(string text) {
        if(text.Length>127) text=text.Substring(0,126)+"…";
        if(text==trayTip) return;
        trayTip=text;
        try {
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(NotifyIcon).GetField("text",flags).SetValue(tray,text);
            if(tray.Visible) typeof(NotifyIcon).GetMethod("UpdateIcon",flags).Invoke(tray,new object[]{true});
        } catch {tray.Text=text.Length>63?text.Substring(0,62)+"…":text;}
    }
    void JournalChanges(string device,List<string> before,List<string> now) {
        foreach(string app in now) if(!before.Contains(app)) EventJournal.Write(app+L.T(" начал использовать ")+device);
        foreach(string app in before) if(!now.Contains(app)) EventJournal.Write(app+L.T(" перестал использовать ")+device);
    }
    // MFCreateVirtualCamera objects are free-threaded; keep them off the UI (STA) thread.
    async Task StartMfCamera() {
        if(mfCameraActive)return;
        string error=await Task.Run(()=>MfCamera.Create());
        mfCameraActive=error==null;mfError=error;
        EventJournal.Write(error==null?L.T("Виртуальная камера «")+MfCamera.Name+L.T("» включена"):L.T("Виртуальная камера Media Foundation не включена: ")+error);
        if(error!=null) hint.Text=L.T("Камера Media Foundation: ")+error;
    }
    async Task StopMfCamera() {
        if(!mfCameraActive)return;
        await Task.Run(()=>MfCamera.Remove());
        mfCameraActive=false;
        EventJournal.Write(L.T("Виртуальная камера «")+MfCamera.Name+L.T("» выключена"));
    }
    void OpenJournal() {
        try {
            if(!File.Exists(EventJournal.FilePath)) EventJournal.Write(L.T("Журнал создан"));
            Process.Start(new ProcessStartInfo(EventJournal.FilePath) {UseShellExecute=true});
        } catch(Exception e) {hint.Text=L.T("Не удалось открыть журнал: ")+e.Message;}
    }
    // The preview button only asks for the capture; apps reading a Pixie camera keep it running on their own.
    async Task TogglePreview() {
        if(exiting)return;
        if(manualPreview) {manualPreview=false;RenderPreviewButton();await Supervise();return;}
        manualPreview=true;RenderPreviewButton();
        await cameraGate.WaitAsync();
        if(exiting){cameraGate.Release();return;}
        preview.Enabled=false;cameraChanging=true;RenderTracking();
        try {
            // The user explicitly asked to see the camera, so the button leaves privacy.
            int current=await Task.Run(()=> {using(var camera=new Pixy()) {int m=camera.Query();return m==2?LeavePrivacy(camera):m;}});
            ApplyMode(current,true);
        } catch(Exception e) {MarkUnknown();hint.Text=e.Message;}
        finally {cameraChanging=false;cameraGate.Release();if(!exiting){preview.Enabled=true;RenderTracking();}}
        retryStartAfter=DateTime.MinValue;
        await Supervise();
    }
    async Task UpdateCamera(bool change) {
        if(exiting)return;
        if(!change && DateTime.UtcNow<nextCameraPoll && !video.Running && !manualPreview)return;
        if(change) await cameraGate.WaitAsync();
        else if(!cameraGate.Wait(0)) return;
        if(exiting){cameraGate.Release();return;}
        // Background reads must not reset hover/focus by disabling the controls.
        if(change){cameraChanging=true;toggle.Enabled=false;menuToggle.Enabled=false;RenderTracking();}
        try {
            int before=-1;
            int result=await Task.Run(()=> {
                using(var camera=new Pixy()) {
                    int current=camera.Query();
                    before=current;
                    if(!change)return current;
                    if(current!=2) {
                        video.SetIdleFrame(Placeholder.Privacy());
                        video.SetAllowed(false);video.Stop();return camera.Set(2);
                    }
                    return LeavePrivacy(camera);
                }
            });
            if(exiting)return;
            if(change && (before==0||before==1))SaveTracking(before==1);
            ApplyMode(result,change);
            if(result==2 && video.Running) await Task.Run(()=>video.Stop());
        } catch(Exception e) {
            if(exiting)return;
            MarkUnknown();
            if(change) {hint.Text=e.Message;tray.ShowBalloonTip(4000,"Pixie Privacy Pro",e.Message,ToolTipIcon.Warning);}
            else ReportLongUnknown(e.Message);
        } finally {if(change)cameraChanging=false;cameraGate.Release();if(change&&!exiting){toggle.Enabled=true;menuToggle.Enabled=true;RenderTracking();}}
        // Leaving privacy brings the video back at once for an app that still reads a Pixie camera.
        if(change && !exiting) {retryStartAfter=DateTime.MinValue;await Supervise();}
    }
    void MarkUnknown() {
        mode=-1;video.SetIdleFrame(Placeholder.Privacy());video.SetAllowed(false);tray.Icon=unknownIcon;UpdateTrayTip();status.Text=L.T("Состояние камеры не подтверждено");
        if(unknownSince==null)unknownSince=DateTime.UtcNow;
        RenderTracking();
    }
    // Without USB answers the privacy monitor is blind; say so once instead of only greying the icon.
    void ReportLongUnknown(string reason) {
        if(unknownReported || unknownSince==null || DateTime.UtcNow-unknownSince.Value<UnknownNotice)return;
        unknownReported=true;
        EventJournal.Write(L.T("Нет связи с камерой, контроль приватности не работает: ")+reason);
        tray.ShowBalloonTip(6000,L.T("Контроль приватности не работает"),L.T("Камера не отвечает по USB, переходы не отслеживаются. ")+reason,ToolTipIcon.Warning);
    }
    public void ApplyMode(int value,bool ownAction) {
        if(unknownReported) EventJournal.Write(L.T("Связь с камерой восстановлена"));
        // Settings unread at startup (camera missing or busy) are read once the camera answers.
        if((unknownSince!=null||!settingsRead) && !firstReading && !layoutOnly) BeginInvoke((MethodInvoker)async delegate {await LoadCameraSettings();});
        unknownSince=null;unknownReported=false;
        // PIXY powers up open; at startup an unused open lens is closed quietly instead of alerting.
        bool closeAtStartup=firstReading && !layoutOnly && CloseAtStartup(value,ownAction,Preferences.GetFlag(Preferences.AutoPrivacy,true),video.ConsumerActive,cameraApps.Count);
        if(ShouldAlert(lastKnown,value,ownAction)) ShowAlert();
        else if(firstReading && !closeAtStartup && ShouldAlertAtStartup(value,ownAction)) ShowAlert();
        if(closeAtStartup) BeginInvoke((MethodInvoker)async delegate {await ClosePrivacy(L.T("камера включилась вместе с компьютером"));});
        if(firstReading) EventJournal.Write(L.T("При запуске: ")+ModeName(value));
        else if(value==2 && lastKnown==3 && !ownAction && privacyTimer>0) EventJournal.Write(L.T("Объектив закрыт самой камерой: её не использовали ")+TimerName(privacyTimer).Replace(L.T("через "),""));
        else if(value!=lastKnown) EventJournal.Write(ModeName(value)+(ownAction?"":L.T(" (изменено не через Pixie Pro)")));
        if(lastKnown==2 && value>=0 && value!=2) revealAt=DateTime.UtcNow+CaptureRules.RevealDelay;
        if(value==2) revealAt=DateTime.MinValue;
        firstReading=false;
        mode=value;lastKnown=value;
        nextCameraPoll=DateTime.UtcNow.AddSeconds(CaptureRules.CameraPollSeconds(value,video.Running||manualPreview,privacyTimer));
        if(value==2) openSince=null; else if(openSince==null) openSince=DateTime.UtcNow;
        video.SetIdleFrame(Placeholder.Privacy());
        video.SetAllowed(VideoAllowed);
        if(value==0||value==1)SaveTracking(value==1);
        RenderTracking();
        var nextIcon=value==2?closedIcon:openIcon;
        if(tray.Icon!=nextIcon)tray.Icon=nextIcon;
        UpdateTrayTip();
        status.Text=value==2?L.T("Приватность включена · объектив закрыт"):value==3?L.T("Камера открыта · ожидание видеопотока"):L.T("Камера открыта · видеопоток активен");
        toggle.Text=menuToggle.Text=value==2?L.T("Выключить приватность"):L.T("Включить приватность");
    }
    static string ModeName(int value) {
        return value==2?L.T("Приватность включена"):value==3?L.T("Камера открыта, ожидание видео"):value==1?L.T("Камера открыта, автослежение"):L.T("Камера открыта, обычный режим");
    }
    public static bool ShouldAlert(int previous,int current,bool ownAction) {return !ownAction && previous==2 && current>=0 && current!=2;}
    public static bool ShouldAlertAtStartup(int current,bool ownAction) {return !ownAction && current>=0 && current!=2;}
    public static bool CloseAtStartup(int current,bool ownAction,bool autoPrivacy,bool consumer,int cameraApps) {
        return autoPrivacy && ShouldAlertAtStartup(current,ownAction) && !consumer && cameraApps==0;
    }
    DateTime soundUntil;
    void StopAlert() {
        if(alert!=null && !alert.IsDisposed) alert.Close();
        AlertSound.Stop();soundUntil=DateTime.MinValue;
    }
    // The picture and the sound are switched separately: both, either one, or no alert at all.
    public void ShowAlert() {
        StopAlert();
        bool sound=Preferences.GetFlag(Preferences.AlertSound,false);
        if(!Preferences.GetFlag(Preferences.AlertPicture,true)) {
            if(sound) {double seconds=AlertSound.Seconds();AlertSound.Play();soundUntil=DateTime.UtcNow.AddSeconds(seconds+.3);}
            return;
        }
        alert=sound?new EyeAlert(AlertSound.Play,AlertSound.Seconds()):new EyeAlert();alert.Show();
    }
    // Leaving the computer (lock, sleep, display off) closes the lens; coming back never reopens it.
    void OnSessionSwitch(object sender,SessionSwitchEventArgs e) {
        if(e.Reason==SessionSwitchReason.SessionLock||e.Reason==SessionSwitchReason.ConsoleDisconnect||e.Reason==SessionSwitchReason.RemoteDisconnect)
            BeginInvoke((MethodInvoker)async delegate {await PrivacyForAbsence(L.T("компьютер заблокирован"));});
    }
    // The system waits for this handler before sleeping, so close the lens synchronously.
    void OnPowerModeChanged(object sender,PowerModeChangedEventArgs e) {
        if(e.Mode!=PowerModes.Suspend) return;
        if(InvokeRequired) Invoke((MethodInvoker)delegate {ForcePrivacy(L.T("компьютер уходит в сон"));});
        else ForcePrivacy(L.T("компьютер уходит в сон"));
    }
    bool AbsencePrivacyWanted {get {return !exiting && !layoutOnly && Preferences.GetFlag(Preferences.LockPrivacy,true);}}
    async Task PrivacyForAbsence(string reason) {
        if(AbsencePrivacyWanted) await ClosePrivacy(reason);
    }
    async Task ClosePrivacy(string reason) {
        if(exiting||layoutOnly) return;
        manualPreview=false;RenderPreviewButton();
        await cameraGate.WaitAsync();
        if(exiting){cameraGate.Release();return;}
        cameraChanging=true;RenderTracking();
        try {
            int before=-1;
            int result=await Task.Run(()=>{
                video.SetIdleFrame(Placeholder.Privacy());video.SetAllowed(false);video.Stop();
                using(var camera=new Pixy()) {before=camera.Query();return before==2?2:camera.Set(2);}
            });
            ApplyMode(result,true);
            if(before!=2) EventJournal.Write(L.T("Объектив закрыт: ")+reason);
        } catch(Exception e) {EventJournal.Write(L.T("Не удалось включить приватность (")+reason+"): "+e.Message);}
        finally {cameraChanging=false;cameraGate.Release();if(!exiting)RenderTracking();}
    }
    void ForcePrivacy(string reason) {
        if(!AbsencePrivacyWanted) return;
        manualPreview=false;
        // Another camera operation may hold the gate across an await; sleep will not wait for it.
        bool gate=cameraGate.Wait(0);
        try {
            video.SetAllowed(false);video.Stop();
            int before,result;
            using(var camera=new Pixy()) {before=camera.Query();result=before==2?2:camera.Set(2);}
            ApplyMode(result,true);
            if(before!=2) EventJournal.Write(L.T("Объектив закрыт: ")+reason);
        } catch(Exception e) {EventJournal.Write(L.T("Не удалось включить приватность (")+reason+"): "+e.Message);}
        finally {if(gate)cameraGate.Release();}
    }
    [DllImport("user32.dll")] static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient,ref Guid setting,int flags);
    [DllImport("user32.dll")] static extern bool UnregisterPowerSettingNotification(IntPtr handle);
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        if(!layoutOnly && displayNotification==IntPtr.Zero) displayNotification=RegisterPowerSettingNotification(Handle,ref ConsoleDisplayState,0);
        hotKeyRegistered=false;RegisterHotKeys();
    }
    public void OpenWindow() {
        if(layoutOnly) {Show();WindowState=FormWindowState.Normal;Activate();return;}
        if(modern==null) modern=new ModernWindow(this);
        modern.Open();
    }
    // With auto-privacy on, leaving Pro closes the lens it opened for apps or the preview.
    async Task ExitApp() {
        if(exiting)return;
        if(video.Running && Preferences.GetFlag(Preferences.AutoPrivacy,true)) {
            await cameraGate.WaitAsync();
            try {
                await Task.Run(()=>{video.Stop();using(var camera=new Pixy())camera.Set(2);});
                EventJournal.Write(L.T("Приватность включена при выходе"));
            } catch(Exception e) {EventJournal.Write(L.T("Не удалось включить приватность при выходе: ")+e.Message);}
            finally {cameraGate.Release();}
        }
        await StopMfCamera();
        EventJournal.Write(L.T("Pixie Privacy Pro завершена"));
        exiting=true;Close();
    }
    protected override void OnShown(EventArgs e) {
        bool open=!hidden;
        if(!layoutOnly || hidden) {hidden=false;Hide();}
        base.OnShown(e);
        if(open && !layoutOnly) {OpenWindow();if(Program.StartPage!=null && modern!=null) modern.Navigate(Program.StartPage);}
    }
    protected override void WndProc(ref Message m) {
        if(m.Msg==Program.ShowMessage)OpenWindow();
        if(m.Msg==Program.ExitMessage)BeginInvoke((MethodInvoker)async delegate {await ExitApp();});
        if(m.Msg==0x312 && (int)m.WParam==HotKeyId)BeginInvoke((MethodInvoker)async delegate {await UpdateCamera(true);});
        // WM_POWERBROADCAST / PBT_POWERSETTINGCHANGE: POWERBROADCAST_SETTING is a GUID, a length and the value (0 = display off).
        if(m.Msg==0x218 && (int)m.WParam==0x8013 && m.LParam!=IntPtr.Zero) {
            var setting=(Guid)Marshal.PtrToStructure(m.LParam,typeof(Guid));
            if(setting==ConsoleDisplayState && Marshal.ReadInt32(m.LParam,20)==0)
                BeginInvoke((MethodInvoker)async delegate {await PrivacyForAbsence(L.T("экран выключен"));});
        }
        base.WndProc(ref m);
    }
    protected override void OnFormClosing(FormClosingEventArgs e) {if(!exiting&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}base.OnFormClosing(e);}
    protected override void Dispose(bool disposing) {
        if(disposing){
            exiting=true;
            if(center!=null) center.Close();
            if(modern!=null) modern.Shutdown();
            if(!layoutOnly) {SystemEvents.SessionSwitch-=OnSessionSwitch;SystemEvents.PowerModeChanged-=OnPowerModeChanged;}
            if(displayNotification!=IntPtr.Zero) {UnregisterPowerSettingNotification(displayNotification);displayNotification=IntPtr.Zero;}
            if(hotKeyRegistered && IsHandleCreated) {UnregisterHotKey(Handle,HotKeyId);hotKeyRegistered=false;}
            poll.Dispose();paint.Dispose();video.Dispose();tray.Visible=false;tray.Dispose();trackingTray.Visible=false;trackingTray.Dispose();menu.Dispose();if(alert!=null)alert.Dispose();ReplaceImage(input,null);ReplaceImage(output,null);closedIcon.Dispose();openIcon.Dispose();unknownIcon.Dispose();trackingOnIcon.Dispose();trackingOffIcon.Dispose();trackingWaitingIcon.Dispose();}
        base.Dispose(disposing);
    }
}

static class Program {
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int RegisterWindowMessage(string text);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
    public static readonly int ShowMessage=RegisterWindowMessage("PixiePrivacyPro.ShowWindow.v1");
    public static readonly int ExitMessage=RegisterWindowMessage("PixiePrivacyPro.Exit.v1");
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    public static string StartPage;
    [STAThread] static void Main(string[] args) {
        if(Array.IndexOf(args,"--cleanup")>=0) {Cleanup();return;}
        // After a language change the old instance is still closing: wait for it before taking the single-instance mutex.
        int restart=Array.IndexOf(args,"--restart");
        if(restart>=0 && restart+1<args.Length) {
            int pid;
            if(int.TryParse(args[restart+1],out pid)) try {using(var old=Process.GetProcessById(pid)) old.WaitForExit(15000);} catch(ArgumentException) {}
        }
        int page=Array.IndexOf(args,"--page");
        if(page>=0 && page+1<args.Length) StartPage=args[page+1];
        // Per-monitor DPI v2: the WPF window stays sharp at 125–200 %; tray icons are drawn for the real size.
        try {SetProcessDpiAwarenessContext((IntPtr)(-4));} catch(EntryPointNotFoundException) {}
        bool first;using(var mutex=new Mutex(true,"Local\\PixiePrivacyPro",out first)) {
            if(!first){PostMessage((IntPtr)0xffff,ShowMessage,IntPtr.Zero,IntPtr.Zero);return;}
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWindow(Array.IndexOf(args,"--tray")>=0));
        }
    }
    // Run by the uninstaller as the current user: drop autostart and close the running tray app.
    static void Cleanup() {
        try {Preferences.RemoveAutostart();} catch {}
        PostMessage((IntPtr)0xffff,ExitMessage,IntPtr.Zero,IntPtr.Zero);
        int self=Process.GetCurrentProcess().Id;
        foreach(var process in Process.GetProcessesByName("PixiePrivacyPro")) using(process) {
            if(process.Id==self) continue;
            try {if(!process.WaitForExit(8000)) process.Kill();} catch {}
        }
    }
}
