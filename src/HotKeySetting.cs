using System;
using System.Collections.Generic;
using System.Windows.Forms;

// The privacy hotkey: a modifier pair (RegisterHotKey flags ALT 1, CONTROL 2, SHIFT 4, WIN 8) plus a key the user picks.
public static class HotKeySetting {
    public const int Alt=1,Control=2,Shift=4,Win=8;
    public static readonly int[] ModifierChoices={Win|Alt,Control|Alt,Control|Shift,Win|Shift,Control|Win};
    public static readonly Keys[] KeyChoices=BuildKeys();
    public const int DefaultModifiers=Win|Alt;
    // Win+Alt+Space: one hand, left side of the keyboard.
    public const Keys DefaultKey=Keys.Space;

    static Keys[] BuildKeys() {
        var keys=new List<Keys>{Keys.Space};
        for(Keys k=Keys.A;k<=Keys.Z;k++) keys.Add(k);
        for(Keys k=Keys.D0;k<=Keys.D9;k++) keys.Add(k);
        for(Keys k=Keys.F1;k<=Keys.F12;k++) keys.Add(k);
        return keys.ToArray();
    }
    public static string ModifierName(int modifiers) {
        var parts=new List<string>();
        if((modifiers&Control)!=0) parts.Add("Ctrl");
        if((modifiers&Win)!=0) parts.Add("Win");
        if((modifiers&Alt)!=0) parts.Add("Alt");
        if((modifiers&Shift)!=0) parts.Add("Shift");
        return string.Join("+",parts.ToArray());
    }
    public static string KeyName(Keys key) {
        if(key==Keys.Space) return L.T("Пробел");
        return key>=Keys.D0&&key<=Keys.D9 ? ((int)(key-Keys.D0)).ToString() : key.ToString();
    }
    public static string Name(int modifiers,Keys key) {return ModifierName(modifiers)+"+"+KeyName(key);}

    // Stored values outside the offered lists fall back to the default, so the hotkey is never unusable.
    public static int Modifiers {get {
        int value=Preferences.GetInt(Preferences.HotKeyModifiers,DefaultModifiers);
        return Array.IndexOf(ModifierChoices,value)>=0 ? value : DefaultModifiers;
    }}
    public static Keys Key {get {
        var value=(Keys)Preferences.GetInt(Preferences.HotKeyKey,(int)DefaultKey);
        return Array.IndexOf(KeyChoices,value)>=0 ? value : DefaultKey;
    }}
    public static string Current {get {return Name(Modifiers,Key);}}
}
