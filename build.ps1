# Builds the app, the microphone service and the Media Foundation camera into bin\.
# -Tests also builds the test programs into tests\bin; -Test builds them and runs the offline tests.
# Tools: the .NET Framework 4.8 compiler that ships with Windows and llvm-mingw (set LLVM_MINGW to its folder).
param([switch]$Tests,[switch]$Test)
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    $version=(Get-Content .\version.txt -Raw).Trim()
    $bin=Join-Path $PSScriptRoot 'bin'
    New-Item -ItemType Directory -Force $bin | Out-Null
    # version.txt is the single source for the assemblies, the MSI and the package names.
    Set-Content (Join-Path $bin 'Version.cs') "[assembly:System.Reflection.AssemblyVersion(`"$version.0`")]`r`n[assembly:System.Reflection.AssemblyFileVersion(`"$version.0`")]`r`n[assembly:System.Reflection.AssemblyInformationalVersion(`"$version`")]" -Encoding utf8
    $compiler='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $wpf='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF'
    $mingw=$env:LLVM_MINGW
    if(-not $mingw -or -not (Test-Path "$mingw\bin")){throw 'Set LLVM_MINGW to the llvm-mingw (ucrt, x86_64) folder: https://github.com/mstorsjo/llvm-mingw/releases'}
    # The runtime files sit next to the exe: third-party libraries, the window icon and the bundled alert sound.
    Copy-Item third_party\AForge.dll,third_party\AForge.Video.dll,third_party\AForge.Video.DirectShow.dll,third_party\hidapi.dll,third_party\UnityCaptureFilter32.dll,third_party\UnityCaptureFilter64.dll,assets\PixiePrivacyPro.ico,assets\dark-voice.mp3 $bin -Force
    # The window and the tray control center are WPF (.NET Framework 4.8); the eye picture is embedded as eye.png.
    $references='/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Management.dll','/reference:System.ServiceProcess.dll',"/reference:$wpf\System.Speech.dll","/reference:$wpf\PresentationCore.dll","/reference:$wpf\PresentationFramework.dll","/reference:$wpf\WindowsBase.dll","/reference:$wpf\WindowsFormsIntegration.dll",'/reference:System.Xaml.dll',"/reference:$bin\AForge.dll","/reference:$bin\AForge.Video.dll","/reference:$bin\AForge.Video.DirectShow.dll",'/resource:assets\eye.png,eye.png'
    $appSources=@("$bin\Version.cs")+('Lang','Pixy','Program','Video','Overlay','MicrophoneGuard','CapabilityUsage','EventJournal','Preferences','TrayIcons','MfCamera','Placeholder','Readers','AlertSound','TrustedAppsDialog','HotKeySetting','Ui','ModernWindow','ControlCenter' | ForEach-Object {"src\$_.cs"})
    # Media Foundation virtual camera source (native). llvm-mingw links the C++ runtime statically.
    & "$mingw\bin\x86_64-w64-mingw32-clang++.exe" -std=c++17 -O2 -Wall -Wno-unknown-pragmas -Wno-delete-non-abstract-non-virtual-dtor -shared -static -o "$bin\PixieVirtualCamera.dll" native\PixieVCam.cpp native\PixieVCam.def -lmfplat -lmfuuid -lole32 -luuid -ladvapi32 -lshell32
    if($LASTEXITCODE -ne 0){throw 'Virtual camera DLL compilation failed'}
    & $compiler /nologo /codepage:65001 /target:winexe /platform:x64 /unsafe /main:Program /win32icon:assets\PixiePrivacyPro.ico "/out:$bin\PixiePrivacyPro.exe" $references $appSources
    if($LASTEXITCODE -ne 0){throw 'App compilation failed'}
    & $compiler /nologo /codepage:65001 /target:exe /platform:x64 /main:GuardService "/out:$bin\PixieMicGuard.exe" /reference:System.Management.dll /reference:System.ServiceProcess.dll "$bin\Version.cs" src\Lang.cs src\MicrophoneGuard.cs src\Pixy.cs
    if($LASTEXITCODE -ne 0){throw 'Service compilation failed'}
    if($Tests -or $Test) {
        $testBin=Join-Path $PSScriptRoot 'tests\bin'
        New-Item -ItemType Directory -Force $testBin | Out-Null
        $shared=$appSources | Where-Object {$_ -notlike '*Version.cs'}
        # Hardware tests (Tests, TrackingCheck, TrackingUiCheck, CameraCli, PixyProbe) need a connected PIXY and are run by hand.
        # MfCameraCheck needs the installed MSI (registered PixieVirtualCamera.dll) and Windows 11.
        # UiShots renders the WPF window, control center, alert and tray icons to PNG without a camera.
        foreach($name in 'UnitTests','Tests','TrackingCheck','TrackingUiCheck','LayoutCheck','CameraCli','MfCameraCheck','PixyProbe','UiShots') {
            & $compiler /nologo /codepage:65001 /target:exe /platform:x64 /unsafe /main:$name "/out:$testBin\$name.exe" $references "tests\$name.cs" $shared
            if($LASTEXITCODE -ne 0){throw "$name compilation failed"}
        }
        Copy-Item "$bin\hidapi.dll","$bin\AForge.dll","$bin\AForge.Video.dll","$bin\AForge.Video.DirectShow.dll","$bin\PixieVirtualCamera.dll","$bin\PixiePrivacyPro.ico" $testBin -Force
    }
    if($Test) {
        & "$PSScriptRoot\tests\bin\UnitTests.exe"
        if($LASTEXITCODE -ne 0){throw 'Offline tests failed'}
    }
} finally {Pop-Location}
