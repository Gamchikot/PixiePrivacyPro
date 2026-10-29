# Builds, tests and packs dist\PixiePrivacyPro-Setup-<version>.msi (English, with Russian embedded) and its SHA-256.
# Tools: WiX Toolset 3.14 (set WIX to its folder) and what build.ps1 needs.
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    & .\build.ps1 -Test
    $version=(Get-Content .\version.txt -Raw).Trim()
    $wix=$env:WIX
    if(-not $wix -or -not (Test-Path "$wix\candle.exe")){throw 'Set WIX to the WiX Toolset 3.14 binaries folder: https://github.com/wixtoolset/wix3/releases'}
    # Relative File sources in installer\Installer.wxs are looked up in these folders.
    $bind='-b','bin','-b','third_party','-b','.'
    New-Item -ItemType Directory -Force .\dist | Out-Null
    # One installer for every language: the English and Russian builds share the ProductCode, and installer-languages.vbs
    # embeds the Russian difference into the English MSI as a language transform (see there).
    $productCode=[guid]::NewGuid().ToString().ToUpper()
    $built=@{}
    foreach($lang in @(@{Code='en';Culture='en-us'},@{Code='ru';Culture='ru-ru'})) {
        $out=".\dist\Installer-$($lang.Code).msi"
        & "$wix\candle.exe" -nologo -arch x64 "-dVersion=$version" "-dLang=$($lang.Code)" "-dProductCode=$productCode" -ext "$wix\WixUtilExtension.dll" installer\Installer.wxs -out ".\dist\Installer-$($lang.Code).wixobj"
        if($LASTEXITCODE -ne 0){throw 'Installer compile failed'}
        & "$wix\light.exe" -nologo $bind -ext "$wix\WixUIExtension.dll" -ext "$wix\WixUtilExtension.dll" "-cultures:$($lang.Culture)" ".\dist\Installer-$($lang.Code).wixobj" -out $out
        if($LASTEXITCODE -ne 0){throw 'Installer linking failed'}
        $built[$lang.Code]=(Resolve-Path $out).Path
    }
    $msi=".\dist\PixiePrivacyPro-Setup-$version.msi"
    Copy-Item $built['en'] $msi -Force
    $msiPath=(Resolve-Path $msi).Path
    $mst=Join-Path (Resolve-Path .\dist).Path 'ru.mst'
    # Russian for Russian, Belarusian, Kazakh, Kyrgyz, Tajik, Uzbek (Latin, Cyrillic), Armenian, Azerbaijani (Latin, Cyrillic).
    $russian=1049,1059,1087,1088,1064,1091,2115,1067,1068,2092
    & cscript.exe //nologo .\installer\installer-languages.vbs $msiPath $built['ru'] $mst ($russian -join ',')
    if($LASTEXITCODE -ne 0){throw 'Embedding the Russian transform failed'}
    Remove-Item $built['en'],$built['ru'],$mst,.\dist\Installer-*.wixpdb,.\dist\Installer-*.wixobj -ErrorAction SilentlyContinue
    $msis=@($msi)
    Get-FileHash $msis | Select-Object Hash,Path | ConvertTo-Json | Set-Content ".\dist\SHA256-$version.json"
} finally {Pop-Location}
