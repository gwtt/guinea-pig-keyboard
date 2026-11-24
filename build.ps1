$ErrorActionPreference = 'Stop'
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path (Join-Path $frameworkDir 'csc.exe'))) {
    $frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $frameworkDir 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Building WheekKeys requires the Windows .NET Framework 4.x compiler.'
}

Push-Location $PSScriptRoot
try {
    $null = New-Item -ItemType Directory -Path 'dist' -Force
    $arguments = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/codepage:65001',
        '/out:dist/WheekKeys.exe', '/win32icon:assets/wheek.ico', '/reference:System.Drawing.dll',
        '/reference:System.Windows.Forms.dll', '/reference:System.Xaml.dll')
    foreach ($assembly in 'WindowsBase', 'PresentationCore', 'PresentationFramework') {
        $arguments += '/reference:' + (Join-Path $frameworkDir "WPF\$assembly.dll")
    }
    $arguments += '/resource:assets/avatar.png,avatar.png'
    $arguments += '/resource:assets/pixel/doze.txt,doze.txt'
    $arguments += '/resource:assets/keyboard-layout.txt,keyboard-layout.txt', '/resource:assets/keyboard.png,keyboard.png', '/resource:assets/mouse.png,mouse.png'
    $arguments += '/resource:assets/guinea-pig.png,guinea-pig.png', '/resource:assets/paw.png,paw.png'
    foreach ($asset in Get-ChildItem -LiteralPath 'assets/skins' -Filter '*.png' -File) {
        $arguments += "/resource:assets/skins/$($asset.Name),$($asset.Name)"
    }
    foreach ($asset in Get-ChildItem -LiteralPath 'assets/pixel' -Filter '*.png' -File) {
        if ($asset.Name.StartsWith('mac-')) { continue }
        $arguments += "/resource:assets/pixel/$($asset.Name),pixel-$($asset.Name)"
    }
    $sources = (Get-ChildItem -LiteralPath 'src' -Filter '*.cs').FullName
    & $compiler @arguments @sources
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed. Close WheekKeys before rebuilding.' }
    Get-Item -LiteralPath 'dist/WheekKeys.exe' | Select-Object FullName, Length
}
finally {
    Pop-Location
}
