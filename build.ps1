param(
    [switch]$Test,
    [switch]$Package
)
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Build this Windows desktop application on Windows with .NET Framework installed.'
}

# Native test processes use this directory for their fixture-containment checks.
# Always restore the caller's location, including when compilation or a test fails.
Push-Location -LiteralPath $PSScriptRoot
try {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler)) {
        $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    }
    if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework C# compiler was not found.' }
    $output = Join-Path $PSScriptRoot 'dist'
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    $app = Join-Path $output 'EverQuest-Beta-UI-Copier.exe'
    if (Test-Path -LiteralPath $app) {
        try {
            $outputHandle = [IO.File]::Open($app, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            $outputHandle.Dispose()
        }
        catch {
            throw "Cannot replace $app. Close any running copies of the UI copier and make sure the output folder is writable. $($_.Exception.Message)"
        }
    }
    $sourceFiles = @('Program.cs', 'LauncherResolver.cs', 'TransferCore.cs', 'MapDownloader.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
    & $compiler /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror+ "/out:$app" "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll @sourceFiles
    if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed. See the compiler diagnostics above.' }
    Write-Output "Built $app"
    if ($Test) {
        $testApp = Join-Path $output 'TransferCore.Tests.exe'
        & $compiler /nologo /target:exe /platform:anycpu /optimize+ /warnaserror+ "/out:$testApp" /reference:System.dll /reference:System.Core.dll /reference:System.IO.Compression.dll (Join-Path $PSScriptRoot 'TransferCore.cs') (Join-Path $PSScriptRoot 'MapDownloader.cs') (Join-Path $PSScriptRoot 'TransferCore.Tests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
        & $testApp (Join-Path $PSScriptRoot 'test-work')
        if ($LASTEXITCODE -ne 0) { throw 'Transfer tests failed.' }
        $mapTestApp = Join-Path $output 'MapDownloader.Tests.exe'
        & $compiler /nologo /target:exe /platform:anycpu /optimize+ /warnaserror+ "/out:$mapTestApp" /reference:System.dll /reference:System.Core.dll /reference:System.IO.Compression.dll (Join-Path $PSScriptRoot 'MapDownloader.cs') (Join-Path $PSScriptRoot 'MapDownloader.Tests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Map test compilation failed.' }
        & $mapTestApp
        if ($LASTEXITCODE -ne 0) { throw 'Map download tests failed.' }
        $launcherTestApp = Join-Path $output 'LauncherResolver.Tests.exe'
        & $compiler /nologo /target:exe /platform:anycpu /optimize+ /warnaserror+ "/out:$launcherTestApp" /reference:System.dll /reference:System.Core.dll (Join-Path $PSScriptRoot 'LauncherResolver.cs') (Join-Path $PSScriptRoot 'LauncherResolver.Tests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Launcher test compilation failed.' }
        & $launcherTestApp (Join-Path $PSScriptRoot 'test-work')
        if ($LASTEXITCODE -ne 0) { throw 'Launcher tests failed.' }
        $formTestApp = Join-Path $output 'WinForms.Tests.exe'
        & $compiler /nologo /target:exe /main:EverQuestBetaUiCopier.WinFormsTests /platform:anycpu /optimize+ /warnaserror+ "/out:$formTestApp" "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll @sourceFiles (Join-Path $PSScriptRoot 'WinForms.Tests.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Form test compilation failed.' }
        & $formTestApp (Join-Path $PSScriptRoot 'test-work')
        if ($LASTEXITCODE -ne 0) { throw 'Form tests failed.' }
    }
    if ($Package) {
        $archive = Join-Path $output 'EverQuest-Beta-UI-Copier.zip'
        Compress-Archive -LiteralPath $app -DestinationPath $archive -Force
        Write-Output "Packaged $archive"
    }
}
finally {
    Pop-Location
}
