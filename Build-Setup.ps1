$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$payload = Join-Path $root 'Universal-BAS-Setup-Payload.exe'
$output = Join-Path $root 'Universal-BAS-Setup-x64.exe'
$source = Join-Path $root 'setup-bootstrapper\BASSetup.cs'
$manifest = Join-Path $root 'setup-bootstrapper\BASSetup.manifest'

if (-not (Test-Path -LiteralPath $iscc)) {
    throw "Inno Setup 6 compiler not found at $iscc"
}
if (-not (Test-Path -LiteralPath $csc)) {
    throw ".NET Framework C# compiler not found at $csc"
}

& $iscc (Join-Path $root 'UniversalBAS.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE" }

& $csc /nologo /target:winexe /platform:anycpu /optimize+ "/win32manifest:$manifest" "/resource:$payload,BASSetup.Payload.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$output" $source
if ($LASTEXITCODE -ne 0) { throw "BAS Setup bootstrapper compilation failed with exit code $LASTEXITCODE" }

Write-Host "Created $output"
