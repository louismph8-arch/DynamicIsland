# Compile les pixel shaders WPF (src\DynamicIsland\Themes\Shaders\*.fx -> *.ps, embarqués dans l'app).
# À relancer après chaque modification d'un .fx. Demande fxc.exe (SDK Windows 10/11).
# Cible ps_2_0 par défaut ; un .fx dont la 1re ligne contient « ps_3_0 » est compilé en ps_3_0.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$shaders = Join-Path $root 'src\DynamicIsland\Themes\Shaders'

$fxc = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\fxc.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $fxc) { throw "fxc.exe introuvable : installer le SDK Windows (Windows Kits 10)." }

foreach ($source in Get-ChildItem $shaders -Filter *.fx) {
    $output = [IO.Path]::ChangeExtension($source.FullName, '.ps')
    # ps_2_0 : le rendu logiciel de WPF (RenderTargetBitmap, --snapshot) ne sait pas exécuter ps_3_0.
    $target = if ((Get-Content $source.FullName -TotalCount 1) -match 'ps_3_0') { 'ps_3_0' } else { 'ps_2_0' }
    & $fxc.FullName /nologo /T $target /E main /O3 /Fo $output $source.FullName
    if ($LASTEXITCODE) { throw "Échec de compilation : $($source.Name)" }
    Write-Host "OK : $($source.Name) -> $([IO.Path]::GetFileName($output)) ($target)"
}
