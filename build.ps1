param([switch]$RunTests, [switch]$TraceStartup, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkDir 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# 编译器不存在。' }
$outputDir = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $projectRoot 'outputs\DesktopTodo'
} elseif ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
}
$workDir = Join-Path $projectRoot 'work'
New-Item -ItemType Directory -Force -Path $outputDir,$workDir | Out-Null
$refs = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkDir $_) }
$iconPath = Join-Path $workDir 'todo.ico'
Add-Type -AssemblyName System.Drawing
$assetDir = Join-Path $outputDir 'assets'
New-Item -ItemType Directory -Force -Path $assetDir | Out-Null
$iconImages = foreach ($iconSize in @(16, 24, 32, 48, 64, 256)) {
    $bitmap = New-Object System.Drawing.Bitmap $iconSize,$iconSize
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70,107,80))
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White),2.4
    $shape = New-Object System.Drawing.Drawing2D.GraphicsPath
    $memory = New-Object System.IO.MemoryStream
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform(($iconSize / 32.0), ($iconSize / 32.0))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $shape.AddArc(4,2,12,12,180,90)
        $shape.AddArc(16,2,12,12,270,90)
        $shape.AddArc(16,18,12,12,0,90)
        $shape.AddArc(4,18,12,12,90,90)
        $shape.CloseFigure()
        $graphics.FillPath($brush,$shape)
        $graphics.DrawLine($pen,10,9,18,9)
        $points = [System.Drawing.PointF[]]@([System.Drawing.PointF]::new(10,16),[System.Drawing.PointF]::new(14,20),[System.Drawing.PointF]::new(22,11))
        $graphics.DrawLines($pen,$points)
        $bitmap.Save($memory,[System.Drawing.Imaging.ImageFormat]::Png)
        if ($iconSize -eq 256) { $bitmap.Save((Join-Path $assetDir 'logo.png'),[System.Drawing.Imaging.ImageFormat]::Png) }
        [pscustomobject]@{ Size=$iconSize; Bytes=$memory.ToArray() }
    } finally {
        $memory.Dispose(); $shape.Dispose(); $pen.Dispose(); $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    }
}
$stream = [System.IO.File]::Create($iconPath)
$writer = New-Object System.IO.BinaryWriter $stream
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$iconImages.Count)
    $iconOffset = 6 + 16 * $iconImages.Count
    foreach ($entry in $iconImages) {
        $dimension = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$entry.Bytes.Length); $writer.Write([uint32]$iconOffset)
        $iconOffset += $entry.Bytes.Length
    }
    foreach ($entry in $iconImages) { $writer.Write([byte[]]$entry.Bytes) }
} finally { $writer.Dispose(); $stream.Dispose() }
Copy-Item -LiteralPath $iconPath -Destination (Join-Path $assetDir 'DesktopTodo.ico') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\assets\logo.svg') -Destination $assetDir -Force
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
$arguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001','/main:DesktopTodo.Program',('/out:'+(Join-Path $outputDir 'DesktopTodo.exe')),('/win32manifest:'+(Join-Path $projectRoot 'src\app.manifest')),('/win32icon:'+$iconPath)) + $refs + $sources
if ($TraceStartup) { $arguments += '/define:TRACE_STARTUP' }
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw '程序编译失败。' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\DesktopTodo.exe.config') -Destination $outputDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $outputDir '使用说明.md') -Force
Write-Output ('已构建：' + (Join-Path $outputDir 'DesktopTodo.exe'))
if ($RunTests) {
    $testSources = @('src\Models.cs','src\Storage.cs','src\Deadline.cs','tests\StorageTests.cs') | ForEach-Object { Join-Path $projectRoot $_ }
    $testExe = Join-Path $workDir 'StorageTests.exe'
    & $compiler /nologo /target:exe /codepage:65001 ('/out:'+$testExe) @refs @testSources
    if ($LASTEXITCODE -ne 0) { throw '测试编译失败。' }
    Push-Location $projectRoot
    try {
        & $testExe
        if ($LASTEXITCODE -ne 0) { throw '数据与倒计时测试失败。' }
    } finally { Pop-Location }
    $uiChecks = @(
        @{ Name='DialogTemplateTests'; Sources=@('src\Models.cs','src\Dialogs.cs','tests\DialogTemplateTests.cs'); Arguments=@() },
        @{ Name='MenuTemplateTests'; Sources=@('src\MenuStyles.cs','tests\MenuTemplateTests.cs'); Arguments=@((Join-Path $workDir 'menu-preview.png')) }
    )
    foreach ($check in $uiChecks) {
        $checkExe = Join-Path $workDir ($check.Name + '.exe')
        $checkSources = $check.Sources | ForEach-Object { Join-Path $projectRoot $_ }
        & $compiler /nologo /target:exe /codepage:65001 ('/out:'+$checkExe) @refs @checkSources
        if ($LASTEXITCODE -ne 0) { throw ($check.Name + ' 编译失败。') }
        $checkArguments = $check.Arguments
        & $checkExe @checkArguments
        if ($LASTEXITCODE -ne 0) { throw ($check.Name + ' 检查失败。') }
    }
}
