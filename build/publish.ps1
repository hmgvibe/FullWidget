# Builds the release zips into dist\:
#   FullWidget-<version>-win-x64.zip        small; needs the .NET 9 Desktop Runtime
#   FullWidget-<version>-win-x64-full.zip   larger; runs without installing anything
# Usage: powershell -ExecutionPolicy Bypass -File build\publish.ps1 [-Step all|build|pack]
#   build  only builds the two folders (dist\<name>\FullWidget), e.g. to sign the files in them
#   pack   only zips folders that were built before
#   all    both (the default)
param([ValidateSet('all', 'build', 'pack')] [string]$Step = 'all')
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$proj = Join-Path $root 'src\FullWidget\FullWidget.csproj'
$dist = Join-Path $root 'dist'
[xml]$csproj = Get-Content $proj
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$builds = @(@{ Name = "FullWidget-$version-win-x64"; SelfContained = $false }, @{ Name = "FullWidget-$version-win-x64-full"; SelfContained = $true })

if ($Step -ne 'pack') {
  # only this version's folders and zips: other versions (and old DeskWidgets zips) stay
  foreach ($b in $builds) { Remove-Item (Join-Path $dist $b.Name), (Join-Path $dist "$($b.Name).zip") -Recurse -Force -ErrorAction SilentlyContinue }
  New-Item -ItemType Directory $dist -Force | Out-Null
  foreach ($b in $builds) {
    $out = Join-Path $dist "$($b.Name)\FullWidget"
    Write-Host "== build $($b.Name)"
    & dotnet publish $proj -c Release -r win-x64 --self-contained $b.SelfContained -o $out -p:DebugType=none -p:GenerateDocumentationFile=false --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "publish failed" }
    # IntelliSense files and the WPF control of the WebView2 package aren't used at run time.
    Get-ChildItem $out -Filter *.xml | Remove-Item
    Remove-Item (Join-Path $out 'Microsoft.Web.WebView2.Wpf.dll') -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE') $out
  }
}

if ($Step -ne 'build') {
  foreach ($b in $builds) {
    $out = Join-Path $dist "$($b.Name)\FullWidget"
    if (-not (Test-Path (Join-Path $out 'FullWidget.exe'))) { throw "$out is missing: run with -Step build first" }
    $zip = Join-Path $dist "$($b.Name).zip"
    Remove-Item $zip -ErrorAction SilentlyContinue
    Compress-Archive -Path $out -DestinationPath $zip -CompressionLevel Optimal
    $mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host "   $zip ($mb MB)"
  }
}
