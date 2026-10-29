' cscript //nologo installer-languages.vbs <english.msi> <russian.msi> <transform.mst> <lcid,lcid,...>
' Saves the difference of the Russian build as a language transform and embeds it into the English MSI once per LCID.
' Windows Installer applies an embedded transform named after the user's UI language by itself; other languages stay English.
Option Explicit
Dim english, russian, transform, lcids, installer, reference, updated, database, view, record, summary, i
english = WScript.Arguments(0) : russian = WScript.Arguments(1) : transform = WScript.Arguments(2)
lcids = Split(WScript.Arguments(3), ",")
Set installer = CreateObject("WindowsInstaller.Installer")
Set reference = installer.OpenDatabase(english, 0)
Set updated = installer.OpenDatabase(russian, 0)
updated.GenerateTransform reference, transform
updated.CreateTransformSummaryInfo reference, transform, 0, 0
Set reference = Nothing : Set updated = Nothing
Set database = installer.OpenDatabase(english, 1)
For i = 0 To UBound(lcids)
    Set view = database.OpenView("INSERT INTO `_Storages` (`Name`,`Data`) VALUES (?,?)")
    Set record = installer.CreateRecord(2)
    record.StringData(1) = lcids(i)
    record.SetStream 2, transform
    view.Execute record
    view.Close
Next
Set summary = database.SummaryInformation(20)
summary.Property(7) = "x64;1033," & Join(lcids, ",")
summary.Persist
database.Commit
