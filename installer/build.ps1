# Construit artifacts\Opaline-Setup.exe : publication Release autonome (win-x64, .NET inclus, rien à
# installer chez les potes) puis compilation de l'installeur Inno Setup.
#   powershell -ExecutionPolicy Bypass -File installer\build.ps1
param([switch]$SkipPublish)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot
$project = Join-Path $root 'src\DynamicIsland\DynamicIsland.csproj'
$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$publish = Join-Path $root 'artifacts\publish'

if (-not $SkipPublish) {
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $publish
    if ($LASTEXITCODE) { throw "dotnet publish a échoué" }
}

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 introuvable : winget install JRSoftware.InnoSetup" }

& $iscc "/DAppVersion=$version" "/DPublishDir=$publish" (Join-Path $PSScriptRoot 'DynamicIsland.iss')
if ($LASTEXITCODE) { throw "Inno Setup a échoué" }

$setup = Get-Item (Join-Path $root 'artifacts\Opaline-Setup.exe')
"Installeur prêt : $($setup.FullName) ($([math]::Round($setup.Length / 1MB, 1)) Mo, version $version)"

# 7B : empreinte SHA-256 publiée avec chaque version (fichier .sha256 + paragraphe à ajouter aux notes de la Release).
$hash = (Get-FileHash $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  Opaline-Setup.exe" | Set-Content (Join-Path $root 'artifacts\Opaline-Setup.exe.sha256') -Encoding ascii
@"

### 🔒 Vérifier le fichier · Verify the file
SHA-256 de / of **Opaline-Setup.exe** : ``$hash``
- PowerShell : ``Get-FileHash .\Opaline-Setup.exe`` doit afficher la même empreinte / must show the same fingerprint.
- Antivirus : [VirusTotal](https://www.virustotal.com/gui/file/$hash) (page vide = personne ne l'a encore analysé : dépose le fichier sur virustotal.com / empty page = not scanned yet: upload the file on virustotal.com).
"@ | Set-Content (Join-Path $root 'artifacts\verifier.md') -Encoding utf8
"SHA-256 : $hash"
