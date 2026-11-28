param([string]$Executable)

$ErrorActionPreference = 'Stop'
if (-not $Executable) { $Executable = Join-Path $PSScriptRoot '../dist/WheekKeys.exe' }
if (-not (Test-Path -LiteralPath $Executable)) { throw 'Run build.ps1 before testing pixel skins.' }
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkDir 'csc.exe'))) {
    $frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'
}
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('wheek-pixel-smoke-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $scratch
$testExe = Join-Path $scratch 'PixelSkinSmokeTest.exe'
$penTestExe = Join-Path $scratch 'PenInputSmokeTest.exe'
try {
    $arguments = @('/nologo', '/codepage:65001', "/out:$testExe", '/reference:System.Core.dll',
        '/reference:System.IO.Compression.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Xaml.dll')
    foreach ($assembly in 'WindowsBase', 'PresentationCore', 'PresentationFramework') {
        $arguments += '/reference:' + (Join-Path $frameworkDir "WPF/$assembly.dll")
    }
    & (Join-Path $frameworkDir 'csc.exe') @arguments (Join-Path $PSScriptRoot 'PixelSkinSmokeTest.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Pixel skin test compilation failed.' }
    & $testExe $Executable
    if ($LASTEXITCODE -ne 0) { throw 'Pixel skin smoke test failed.' }
    $penArguments = @('/nologo', '/codepage:65001', "/out:$penTestExe")
    foreach ($assembly in 'WindowsBase', 'PresentationCore', 'PresentationFramework') {
        $penArguments += '/reference:' + (Join-Path $frameworkDir "WPF/$assembly.dll")
    }
    & (Join-Path $frameworkDir 'csc.exe') @penArguments (Join-Path $PSScriptRoot '../src/PenInput.cs') (Join-Path $PSScriptRoot '../src/PetReactions.cs') (Join-Path $PSScriptRoot 'PenInputSmokeTest.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Pen and reaction test compilation failed.' }
    & $penTestExe
    if ($LASTEXITCODE -ne 0) { throw 'Pen and reaction smoke test failed.' }
}
finally {
    if (Test-Path -LiteralPath $penTestExe) { Remove-Item -LiteralPath $penTestExe }
    if (Test-Path -LiteralPath $testExe) { Remove-Item -LiteralPath $testExe }
    if (@(Get-ChildItem -LiteralPath $scratch -Force).Count -eq 0) { Remove-Item -LiteralPath $scratch }
}
