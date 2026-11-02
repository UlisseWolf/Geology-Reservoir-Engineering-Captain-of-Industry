<#
.SYNOPSIS
    One-time setup for the "Build mod package" GitHub workflow: uploads the game DLLs the
    mod compiles against to a release of a PRIVATE GitHub repository.

.DESCRIPTION
    The game's DLLs cannot go in a public repository. This script turns them into reference
    assemblies (metadata only: types and signatures, no method bodies) with JetBrains
    Refasmer, zips them as coi-refs.zip, and publishes the zip as the latest release of a
    private repository, from which the workflow downloads it.

    Requires: the .NET SDK (for the Refasmer tool) and the GitHub CLI (gh), logged in.
    Run it again after every game update that changes the modding API.

.EXAMPLE
    .\tools\New-CoiRefs.ps1 -GameVersion 0.8.8
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$GameVersion,

    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Captain of Industry',

    [string]$Repo = 'UlisseWolf/coi-reference-assemblies'
)

$ErrorActionPreference = 'Stop'

$managed = Join-Path $GameDir 'Captain of Industry_Data\Managed'
if (-not (Test-Path (Join-Path $managed 'Mafi.Core.dll'))) {
    throw "Mafi.Core.dll not found in '$managed'. Pass -GameDir with your game install folder."
}
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI (gh) not found: https://cli.github.com' }

if (-not (Get-Command refasmer -ErrorAction SilentlyContinue)) {
    dotnet tool install --global JetBrains.Refasmer.CliTool
    $env:PATH += ";$env:USERPROFILE\.dotnet\tools"
}

$work = Join-Path ([IO.Path]::GetTempPath()) "coi-refs-$GameVersion"
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
$out = Join-Path $work 'refs'
New-Item -ItemType Directory -Force -Path $out | Out-Null

# Every game/Unity assembly except the .NET framework ones, which the build gets from the
# .NET Framework targeting pack instead.
$dlls = Get-ChildItem $managed -Filter *.dll | Where-Object {
    $_.Name -notmatch '^(mscorlib|netstandard|System(\..*)?|Microsoft\..*)\.dll$'
}
refasmer --all --omit-non-api-members=false --continue --outputdir $out @($dlls.FullName)
if (-not (Test-Path (Join-Path $out 'Mafi.Core.dll'))) { throw 'Refasmer did not produce Mafi.Core.dll' }

$zip = Join-Path $work 'coi-refs.zip'
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -Force

gh repo view $Repo *> $null
if ($LASTEXITCODE -ne 0) {
    gh repo create $Repo --private --description 'Captain of Industry reference assemblies for mod CI builds (private, do not publish)'
}
$tag = "game-$GameVersion"
gh release view $tag --repo $Repo *> $null
if ($LASTEXITCODE -eq 0) {
    gh release upload $tag $zip --repo $Repo --clobber
} else {
    gh release create $tag $zip --repo $Repo --title "Captain of Industry $GameVersion" --notes 'Reference assemblies for CI builds.' --latest
}
Write-Host "Uploaded coi-refs.zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB) to $Repo release $tag"
