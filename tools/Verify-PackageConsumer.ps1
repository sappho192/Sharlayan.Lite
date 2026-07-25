[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PackageDirectory,

    [Parameter(Mandatory = $true)]
    [string] $ExpectedPackageVersion,

    [Parameter(Mandatory = $true)]
    [string] $ExpectedRepositoryCommit
)

$ErrorActionPreference = 'Stop'
$resolvedPackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$packages = @(Get-ChildItem -LiteralPath $resolvedPackageDirectory -File -Filter '*.nupkg')
if ($packages.Count -ne 1) {
    throw "Expected exactly one .nupkg in '$resolvedPackageDirectory', found $($packages.Count)."
}

$package = $packages[0]
$expectedPackageName = "Sharlayan.Lite.$ExpectedPackageVersion.nupkg"
if ($package.Name -cne $expectedPackageName) {
    throw "Expected package file '$expectedPackageName', found '$($package.Name)'."
}

$fixtureDirectory = Join-Path $PSScriptRoot 'Sharlayan.PackageContractConsumer'
$verificationRoot = Join-Path (
    Split-Path -Parent $resolvedPackageDirectory
) ("consumer-contract-$ExpectedPackageVersion-" + [Guid]::NewGuid().ToString('N'))
$consumerDirectory = Join-Path $verificationRoot 'consumer'
$packagesCache = Join-Path $verificationRoot 'packages'
$nugetConfig = Join-Path $verificationRoot 'NuGet.Config'
New-Item -ItemType Directory -Path $consumerDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $packagesCache -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $fixtureDirectory 'Sharlayan.PackageContractConsumer.csproj') -Destination $consumerDirectory
Copy-Item -LiteralPath (Join-Path $fixtureDirectory 'Program.cs') -Destination $consumerDirectory

$escapedLocalSource = [System.Security.SecurityElement]::Escape($resolvedPackageDirectory)
$config = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-package" value="$escapedLocalSource" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
"@
[System.IO.File]::WriteAllText($nugetConfig, $config, [System.Text.UTF8Encoding]::new($false))

$project = Join-Path $consumerDirectory 'Sharlayan.PackageContractConsumer.csproj'
$previousPackagesCache = $env:NUGET_PACKAGES
try {
    $env:NUGET_PACKAGES = $packagesCache
    dotnet restore $project `
        --configfile $nugetConfig `
        --no-cache `
        -p:SharlayanPackageVersion=$ExpectedPackageVersion
    if ($LASTEXITCODE -ne 0) {
        throw "Clean consumer restore failed with exit code $LASTEXITCODE."
    }

    dotnet build $project `
        --configuration Release `
        --no-restore `
        --nologo `
        -p:SharlayanPackageVersion=$ExpectedPackageVersion
    if ($LASTEXITCODE -ne 0) {
        throw "Clean consumer build failed with exit code $LASTEXITCODE."
    }

    dotnet run --project $project `
        --configuration Release `
        --no-build `
        --no-restore `
        -- $ExpectedPackageVersion $ExpectedRepositoryCommit
    if ($LASTEXITCODE -ne 0) {
        throw "Clean consumer contract execution failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:NUGET_PACKAGES = $previousPackagesCache
}

$cachedPackage = Join-Path $packagesCache (
    "sharlayan.lite\$ExpectedPackageVersion\sharlayan.lite.$ExpectedPackageVersion.nupkg"
)
if (-not (Test-Path -LiteralPath $cachedPackage)) {
    throw "Fresh package cache does not contain the restored Sharlayan.Lite package."
}

$inputSha256 = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
$cachedSha256 = (Get-FileHash -LiteralPath $cachedPackage -Algorithm SHA256).Hash.ToLowerInvariant()
if ($inputSha256 -cne $cachedSha256) {
    throw "Fresh package cache contains different Sharlayan.Lite package bytes."
}

Write-Output "Verified clean package consumer in '$verificationRoot'."
Write-Output "Consumer package SHA-256: $inputSha256"
