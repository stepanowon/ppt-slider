<#
.SYNOPSIS
  Builds an MSIX package: self-contained publish -> layout with Package\AppxManifest.xml and Assets -> makeappx pack
  -> sign with a self-made certificate for local test installs.

.PARAMETER Store
  Skip signing. The Store signs the package itself; use this for the upload to Partner Center
  (after putting the Partner Center Identity into Package\AppxManifest.xml).

.EXAMPLE
  .\build-msix.ps1            # bin\msix\PptSlider_<version>_x64.msix + PptSlider-test.cer
  .\build-msix.ps1 -Store     # unsigned package for Store submission
#>
param([switch]$Store)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$work = Join-Path $root 'obj\msix'
$out = Join-Path $root 'bin\msix'

# dotnet with an SDK: the one on PATH may be a runtime-only install, so also try the per-user install
$dotnet = @((Get-Command dotnet -ErrorAction SilentlyContinue).Source, (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe')) |
    Where-Object { $_ -and (Test-Path $_) -and (& $_ --list-sdks 2>$null) } | Select-Object -First 1
if (-not $dotnet) { throw '.NET SDK(dotnet)를 찾을 수 없습니다.' }

# makeappx / signtool: from an installed Windows SDK, else from the Microsoft.Windows.SDK.BuildTools NuGet package
function Find-Tool([string]$name) {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $hit = Get-ChildItem $kits -Recurse -Filter $name -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
    if ($hit) { return $hit.FullName }
    $ver = '10.0.26100.1742'
    $pkg = Join-Path $root "obj\tools\sdk-buildtools.$ver"
    if (-not (Test-Path $pkg)) {
        Write-Host "Windows SDK가 없어 Microsoft.Windows.SDK.BuildTools $ver 을(를) 내려받습니다..."
        $zip = "$pkg.zip"
        New-Item -ItemType Directory -Force (Split-Path $zip) | Out-Null
        Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/$ver/microsoft.windows.sdk.buildtools.$ver.nupkg" -OutFile $zip
        Expand-Archive $zip $pkg
        Remove-Item $zip
    }
    $hit = Get-ChildItem (Join-Path $pkg 'bin') -Recurse -Filter $name | Where-Object { $_.Directory.Name -eq 'x64' } | Select-Object -First 1
    if (-not $hit) { throw "$name 을(를) 찾을 수 없습니다." }
    $hit.FullName
}

# 1. self-contained publish
$version = ([xml](Get-Content (Join-Path $root 'src\PptSlider.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$pkgVersion = (($version -split '\.') + @('0', '0', '0', '0'))[0..3] -join '.'
$layout = Join-Path $work 'layout'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
& $dotnet publish (Join-Path $root 'src\PptSlider.csproj') -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=false -o $layout -nologo
if ($LASTEXITCODE) { throw "dotnet publish 실패 ($LASTEXITCODE)" }
Get-ChildItem $layout -Filter *.pdb | Remove-Item

# 2. manifest (version stamped) and logos
[xml]$manifest = Get-Content (Join-Path $root 'Package\AppxManifest.xml') -Encoding UTF8
$manifest.Package.Identity.Version = $pkgVersion
$manifest.Save((Join-Path $layout 'AppxManifest.xml'))
Copy-Item -Recurse (Join-Path $root 'Package\Assets') (Join-Path $layout 'Assets')

# 3. pack
New-Item -ItemType Directory -Force $out | Out-Null
$msix = Join-Path $out "PptSlider_${pkgVersion}_x64.msix"
$log = & (Find-Tool 'makeappx.exe') pack /d $layout /p $msix /o
if ($LASTEXITCODE) { $log | Write-Host; throw "makeappx 실패 ($LASTEXITCODE)" }

if ($Store) { Write-Host "Store 제출용(서명 안 함): $msix"; return }

# 4. self-signed certificate whose subject equals the manifest Publisher (required for signing)
$publisher = $manifest.Package.Identity.Publisher
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $publisher -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(1) } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature -FriendlyName 'PPT Slider 테스트 서명' `
        -CertStoreLocation Cert:\CurrentUser\My -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
}
$cer = Join-Path $out 'PptSlider-test.cer'
Export-Certificate -Cert $cert -FilePath $cer | Out-Null
& (Find-Tool 'signtool.exe') sign /fd SHA256 /sha1 $cert.Thumbprint $msix
if ($LASTEXITCODE) { throw "signtool 실패 ($LASTEXITCODE)" }

Write-Host ''
Write-Host "패키지: $msix"
Write-Host "인증서: $cer"
Write-Host '테스트 설치: 관리자 PowerShell에서 한 번만 인증서를 신뢰한 뒤 패키지를 설치합니다.'
Write-Host "  Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
Write-Host "  Add-AppxPackage '$msix'"
