[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PackageDirectory,

    [string] $ExpectedPackageId = 'Sharlayan.Lite',

    [string] $ExpectedPackageVersion,

    [long] $MaximumBytes = 386101
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Reflection.Metadata

function Get-PackageMetadata {
    param($Archive)

    $nuspecEntries = @($Archive.Entries | Where-Object { $_.FullName -like '*.nuspec' })
    if ($nuspecEntries.Count -ne 1) {
        throw 'Package does not contain exactly one nuspec file.'
    }

    $reader = [System.IO.StreamReader]::new($nuspecEntries[0].Open())
    try {
        [xml] $nuspec = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }

    $idNode = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='id']")
    $versionNode = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='version']")
    if ($null -eq $idNode -or $null -eq $versionNode) {
        throw 'Package nuspec is missing its ID or version.'
    }

    $dependencyIds = @($nuspec.SelectNodes("//*[local-name()='dependency']") | ForEach-Object { $_.GetAttribute('id') })
    $packageTypes = @($nuspec.SelectNodes("//*[local-name()='packageType']") | ForEach-Object { $_.GetAttribute('name') })
    return [pscustomobject]@{
        Id = $idNode.InnerText
        Version = $versionNode.InnerText
        DependencyIds = $dependencyIds
        PackageTypes = $packageTypes
    }
}

$packages = @(Get-ChildItem -LiteralPath $PackageDirectory -File -Filter '*.nupkg')
$symbolPackages = @(Get-ChildItem -LiteralPath $PackageDirectory -File -Filter '*.snupkg')
if ($packages.Count -ne 1) {
    throw "Expected exactly one .nupkg in '$PackageDirectory', found $($packages.Count)."
}

if ($symbolPackages.Count -ne 1) {
    throw "Expected exactly one .snupkg in '$PackageDirectory', found $($symbolPackages.Count)."
}

$package = $packages[0]
$symbolPackage = $symbolPackages[0]
$totalSize = $package.Length + $symbolPackage.Length
if ($totalSize -gt $MaximumBytes) {
    throw "Package artifacts are $totalSize bytes; maximum is $MaximumBytes bytes."
}

$archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $metadata = Get-PackageMetadata $archive
    if ($metadata.Id -cne $ExpectedPackageId) {
        throw "Expected package ID '$ExpectedPackageId', found '$($metadata.Id)'."
    }

    if ($ExpectedPackageVersion -and $metadata.Version -cne $ExpectedPackageVersion) {
        throw "Expected package version '$ExpectedPackageVersion', found '$($metadata.Version)'."
    }

    $expectedPackageName = "$($metadata.Id).$($metadata.Version).nupkg"
    if ($package.Name -cne $expectedPackageName) {
        throw "Expected package file '$expectedPackageName', found '$($package.Name)'."
    }

    $entries = @($archive.Entries | ForEach-Object { $_.FullName })
    $requiredEntries = @(
        'lib/net462/Sharlayan.dll',
        'lib/net48/Sharlayan.dll',
        'lib/net6.0/Sharlayan.dll',
        'lib/net7.0/Sharlayan.dll',
        'lib/net8.0/Sharlayan.dll',
        'THIRD-PARTY-NOTICES.md',
        'Logo.png'
    )

    foreach ($requiredEntry in $requiredEntries) {
        if ($entries -notcontains $requiredEntry) {
            throw "Package is missing required entry '$requiredEntry'."
        }
    }

    $allowedAssemblies = @($requiredEntries | Where-Object { $_ -like '*.dll' })
    $unexpectedExecutables = @($entries | Where-Object { $_ -match '(?i)\.(dll|exe|so|dylib)$' -and $_ -notin $allowedAssemblies })
    if ($unexpectedExecutables.Count -gt 0) {
        throw "Package contains unexpected executable entries: $($unexpectedExecutables -join ', ')."
    }

    $forbiddenDependencies = @('FFXIVClientStructs', 'InteropGenerator', 'Lumina', 'System.Net.Http')
    foreach ($dependency in $forbiddenDependencies) {
        if ($metadata.DependencyIds -icontains $dependency) {
            throw "Package declares forbidden runtime dependency '$dependency'."
        }
    }
}
finally {
    $archive.Dispose()
}

$expectedSymbolName = "$($metadata.Id).$($metadata.Version).snupkg"
if ($symbolPackage.Name -cne $expectedSymbolName) {
    throw "Expected symbol package file '$expectedSymbolName', found '$($symbolPackage.Name)'."
}

$symbolArchive = [System.IO.Compression.ZipFile]::OpenRead($symbolPackage.FullName)
try {
    $symbolMetadata = Get-PackageMetadata $symbolArchive
    if ($symbolMetadata.Id -cne $metadata.Id -or $symbolMetadata.Version -cne $metadata.Version) {
        throw 'Symbol package ID or version does not match the main package.'
    }

    if ($symbolMetadata.PackageTypes -inotcontains 'SymbolsPackage') {
        throw "Symbol package does not declare the 'SymbolsPackage' package type."
    }

    $symbolEntries = @($symbolArchive.Entries | ForEach-Object { $_.FullName })
    $allowedSymbolExtensions = @('.pdb', '.nuspec', '.xml', '.psmdcp', '.rels', '.p7s')
    $invalidSymbolEntries = @(
        $symbolEntries | Where-Object {
            -not $_.EndsWith('/') -and [System.IO.Path]::GetExtension($_).ToLowerInvariant() -notin $allowedSymbolExtensions
        }
    )
    if ($invalidSymbolEntries.Count -gt 0) {
        throw "Symbol package contains unsupported entries: $($invalidSymbolEntries -join ', ')."
    }

    $requiredSymbols = @(
        'lib/net462/Sharlayan.pdb',
        'lib/net48/Sharlayan.pdb',
        'lib/net6.0/Sharlayan.pdb',
        'lib/net7.0/Sharlayan.pdb',
        'lib/net8.0/Sharlayan.pdb'
    )

    foreach ($requiredSymbol in $requiredSymbols) {
        if ($symbolEntries -notcontains $requiredSymbol) {
            throw "Symbol package is missing required entry '$requiredSymbol'."
        }

        $symbolStream = $symbolArchive.GetEntry($requiredSymbol).Open()
        $symbolBuffer = [System.IO.MemoryStream]::new()
        try {
            $symbolStream.CopyTo($symbolBuffer)
            $symbolBuffer.Position = 0
            $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream(
                $symbolBuffer,
                [System.Reflection.Metadata.MetadataStreamOptions]::LeaveOpen)
            try {
                $null = $provider.GetMetadataReader([System.Reflection.Metadata.MetadataReaderOptions]::Default, $null)
            }
            finally {
                $provider.Dispose()
            }
        }
        catch {
            throw "Symbol '$requiredSymbol' is not a valid Portable PDB: $($_.Exception.Message)"
        }
        finally {
            $symbolBuffer.Dispose()
            $symbolStream.Dispose()
        }
    }

    $unexpectedSymbolExecutables = @($symbolEntries | Where-Object { $_ -match '(?i)\.(dll|exe|so|dylib)$' })
    if ($unexpectedSymbolExecutables.Count -gt 0) {
        throw "Symbol package contains executable entries: $($unexpectedSymbolExecutables -join ', ')."
    }
}
finally {
    $symbolArchive.Dispose()
}

Write-Output "Verified $($package.Name) and $($symbolPackage.Name) ($totalSize bytes combined)."
