# Copie propre du code vers le dépôt PUBLIC louismph8-arch/DynamicIsland (7B, choix de l'utilisateur) : les fichiers suivis
# par git, SANS l'historique, sans CLAUDE.md (notes de travail personnelles), sans .github (le workflow publie depuis le dépôt
# privé) ; le dossier public/ devient la racine (README FR/EN, LICENSE, THIRD-PARTY-NOTICES, images).
# Par défaut : prépare la copie et montre ce qui change. -Commit : crée le commit. -Push : l'envoie (à faire seulement avec
# l'accord de l'utilisateur).
param(
    [string]$Target = (Join-Path $env:TEMP 'opaline-public'),
    [switch]$Commit,
    [switch]$Push
)
# Pas de 'Stop' : git et gh écrivent leur progression sur stderr (PowerShell 5 la prendrait pour une erreur).
$ErrorActionPreference = 'Continue'
$root = Split-Path $PSScriptRoot -Parent
$excluded = @('CLAUDE.md', '.github/*', 'public/*')

if (-not (Test-Path (Join-Path $Target '.git'))) {
    gh repo clone louismph8-arch/DynamicIsland $Target
    if ($LASTEXITCODE) { throw "Impossible de cloner le dépôt public" }
}
# Même auteur que les commits du dépôt privé (adresse « noreply » de GitHub).
git -C $Target config user.name (git -C $root config user.name)
git -C $Target config user.email (git -C $root config user.email)
git -C $Target pull --ff-only
if ($LASTEXITCODE) { throw "Le dépôt public a des changements locaux : regarde $Target" }

# On repart de zéro (sauf .git) : un fichier retiré du dépôt privé disparaît aussi du dépôt public.
Get-ChildItem $Target -Force | Where-Object { $_.Name -ne '.git' } | Remove-Item -Recurse -Force

$files = git -C $root ls-files
foreach ($file in $files) {
    if ($excluded | Where-Object { $file -like $_ }) { continue }
    $to = Join-Path $Target $file
    New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null
    Copy-Item (Join-Path $root $file) $to
}
foreach ($file in $files | Where-Object { $_ -like 'public/*' }) {
    $to = Join-Path $Target ($file.Substring('public/'.Length))
    New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null
    Copy-Item (Join-Path $root $file) $to
}

# Garde-fou : jamais de notes personnelles ni de secret dans la copie publique.
if (Test-Path (Join-Path $Target 'CLAUDE.md')) { throw "CLAUDE.md ne doit pas être publié" }
git -C $Target add -A
$leak = git -C $Target grep --cached -nIE 'sk-ant-api03-[A-Za-z0-9_-]{80,}|ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|BEGIN [A-Z ]*PRIVATE KEY'
if ($leak) { throw "Possible secret dans la copie publique :`n$leak" }

git -C $Target status --short | Select-Object -First 40
"Fichiers publiés : $((git -C $Target ls-files --cached).Count)"

if ($Commit) {
    $version = ([xml](Get-Content (Join-Path $root 'src/DynamicIsland/DynamicIsland.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    git -C $Target commit -m "Code source d'Opaline $version"
    if ($Push) { git -C $Target push }
}
