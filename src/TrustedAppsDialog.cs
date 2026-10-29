using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

// Edits the apps that may open the lens on their own in the "leave privacy" mode.
public sealed class TrustedAppsDialog : Form {
    readonly ListBox trusted=new ListBox(),readers=new ListBox();
    public List<string> Result {get; private set;}

    public TrustedAppsDialog(List<string> current,List<string> readingNow) {
        // Laid out in 96-DPI pixels; the app is per-monitor DPI aware, so WinForms scales it on load.
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        Text=L.T("Доверенные программы");ClientSize=new Size(700,400);
        FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(24,27,34);ForeColor=Color.White;Font=new Font("Segoe UI",10);
        var about=new Label {Text=L.T("Эти программы сами выводят камеру из приватности, когда открывают камеру Pixie. Остальные видят заставку. Пустой список — любая программа."),Bounds=new Rectangle(20,14,660,44)};
        var trustedLabel=new Label {Text=L.T("Доверенные:"),Bounds=new Rectangle(20,66,300,22)};
        var readersLabel=new Label {Text=L.T("Сейчас читают камеру Pixie:"),Bounds=new Rectangle(380,66,300,22)};
        trusted.SetBounds(20,92,300,220);readers.SetBounds(380,92,300,170);
        foreach(var box in new[]{trusted,readers}) {box.BackColor=Color.FromArgb(36,40,50);box.ForeColor=Color.White;box.BorderStyle=BorderStyle.FixedSingle;}
        foreach(string app in current) trusted.Items.Add(app);
        foreach(string app in readingNow) readers.Items.Add(app);
        if(readingNow.Count==0) {readers.Items.Add(L.T("(никто — откройте камеру Pixie в программе)"));readers.Enabled=false;}
        var add=MakeButton(L.T("← Добавить"),380,272,300);
        var browse=MakeButton(L.T("Добавить программу (.exe)…"),20,322,300);
        var remove=MakeButton(L.T("Удалить"),380,322,140);
        var save=MakeButton(L.T("Сохранить"),440,356,120);var cancel=MakeButton(L.T("Отмена"),570,356,110);
        add.Enabled=readingNow.Count>0;
        add.Click+=delegate {if(readers.SelectedItem!=null) Add((string)readers.SelectedItem); else foreach(string app in readingNow) Add(app);};
        readers.DoubleClick+=delegate {if(readers.SelectedItem!=null && readers.Enabled) Add((string)readers.SelectedItem);};
        browse.Click+=delegate {
            using(var dialog=new OpenFileDialog {Title=L.T("Программа, которой можно открывать камеру"),Filter=L.T("Программы|*.exe")})
                if(dialog.ShowDialog(this)==DialogResult.OK) Add(Path.GetFileName(dialog.FileName));
        };
        remove.Click+=delegate {if(trusted.SelectedIndex>=0) trusted.Items.RemoveAt(trusted.SelectedIndex);};
        save.Click+=delegate {Result=new List<string>();foreach(string app in trusted.Items) Result.Add(app);DialogResult=DialogResult.OK;};
        cancel.Click+=delegate {DialogResult=DialogResult.Cancel;};
        AcceptButton=save;CancelButton=cancel;
        Controls.AddRange(new Control[]{about,trustedLabel,readersLabel,trusted,readers,add,browse,remove,save,cancel});
    }
    Button MakeButton(string text,int x,int y,int width) {
        return new Button {Text=text,Bounds=new Rectangle(x,y,width,32),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(48,56,70),ForeColor=Color.White};
    }
    void Add(string app) {
        foreach(string item in trusted.Items) if(string.Equals(item,app,StringComparison.OrdinalIgnoreCase)) return;
        trusted.Items.Add(app);
    }
}
