$ErrorActionPreference = 'Stop'
$bin = 'C:\Lfall\Publish\PlayerSC'
Remove-Item $bin -Recurse -Force -ErrorAction SilentlyContinue
Write-Output 'publishing player (self-contained, single file)...'
dotnet publish C:\Lfall\PixelFight.Player\PixelFight.Player.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o C:\Lfall\Publish\PlayerSC --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'player publish failed' }
$games = @(
  @('DemoGame', 'demo.lic', 'Demo platformer on PixelEngine.'),
  @('Ages', 'strategy.lic', 'Turn-based strategy: 30 provinces, wars, AI.'),
  @('WorldAges', 'world.lic', 'Whole world from GitHub: pick a country, conquer Earth.'),
  @('Merchant', 'merchant.lic', 'Merchant: trade goods, make 3000 in 24 months.'),
  @('Tower', 'tower.lic', 'Tower defense: 15 waves, BOW and CANNON towers.'),
  @('Knights', 'knights.lic', 'Knights like Battle Cats: destroy the camp!')
)
foreach ($g in $games) {
  $name = $g[0]
  $dir = "C:\Lfall\Publish\Games\$name"
  Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
  mkdir $dir -Force | Out-Null
  Get-ChildItem $bin -File | Where-Object { $_.Extension -notin @('.png', '.pdb') } | Copy-Item -Destination $dir -Force
  Copy-Item "C:\Lfall\Lic\$($g[1])" "$dir\game.lic" -Force
  if ($name -ne 'WorldAges') { Remove-Item "$dir\world.lic" -ErrorAction SilentlyContinue }
  Remove-Item "$dir\*.pdb" -ErrorAction SilentlyContinue
  Remove-Item "$dir\PixelFight.exe" -ErrorAction SilentlyContinue
  Remove-Item "$dir\PixelFight.runtimeconfig.json" -ErrorAction SilentlyContinue
  Remove-Item "$dir\PixelFight.deps.json" -ErrorAction SilentlyContinue
  Copy-Item "$dir\PixelPlayer.exe" "$dir\$name.exe" -Force
  foreach ($ext in @('.runtimeconfig.json', '.deps.json')) {
    if (Test-Path "$dir\PixelPlayer$ext") { Copy-Item "$dir\PixelPlayer$ext" "$dir\$name$ext" -Force }
  }
  Remove-Item "$dir\PixelPlayer.exe" -Force
  foreach ($ext in @('.runtimeconfig.json', '.deps.json', '.pdb')) {
    Remove-Item "$dir\PixelPlayer$ext" -ErrorAction SilentlyContinue
  }
  $txt = "$name - a game on PixelEngine.`r`n$($g[2])`r`nDouble-click $name.exe. Needs .NET 8. Esc - exit."
  [System.IO.File]::WriteAllText("$dir\README.txt", $txt, [System.Text.Encoding]::UTF8)
  Write-Output "packed $name"
}
