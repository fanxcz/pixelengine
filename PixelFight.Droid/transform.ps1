$ErrorActionPreference = 'Stop'
mkdir C:\Lfall\PixelFight.Droid\Game -Force | Out-Null

function Convert-File($src, $dst, $linGrad) {
  $t = Get-Content $src -Raw -Encoding UTF8
  if ($linGrad) {
    $t = $t -creplace 'new LinearGradientBrush\(new Rectangle\(0, 0, W, 172\),\r?\n            Color\.FromArgb\(126, 168, 214\), Color\.FromArgb\(226, 232, 222\), LinearGradientMode\.Vertical\)', 'new LinGrad(Color.FromArgb(126, 168, 214), Color.FromArgb(226, 232, 222), true)'
    $t = $t -creplace 'new LinearGradientBrush\(new Rectangle\(0, 168, W, 90\),\r?\n            Color\.FromArgb\(168, 172, 140\), Color\.FromArgb\(128, 136, 112\), LinearGradientMode\.Vertical\)', 'new LinGrad(Color.FromArgb(168, 172, 140), Color.FromArgb(128, 136, 112), true)'
    $t = $t -creplace 'new LinearGradientBrush\(new Rectangle\(0, gy, W, gh\),\r?\n            Color\.FromArgb\(126, 168, 96\), Color\.FromArgb\(82, 116, 64\), LinearGradientMode\.Vertical\)', 'new LinGrad(Color.FromArgb(126, 168, 96), Color.FromArgb(82, 116, 64), true)'
  }
  $t = $t -creplace 'using System\.Drawing\.Text;\r?\n', ''
  $t = $t -creplace 'using System\.Drawing\.Drawing2D;\r?\n', ''
  $t = $t -creplace 'using System\.Drawing;\r?\n', ''
  $t = $t -creplace 'using System\.Windows\.Forms;\r?\n', 'using PFMaui;'
  $t = $t -creplace '\bPointF\b', 'PtF'
  $t = $t -creplace '\bPoint\b', 'Pt'
  $t = $t -creplace '\bRectangle\b', 'GRect'
  $t = $t -creplace '\bGraphics\b', 'Gfx'
  $t = $t -creplace '\bBrush\b', 'Br'
  Set-Content $dst $t -Encoding UTF8 -NoNewline
  Write-Output "wrote $dst"
}

Convert-File C:\Lfall\PixelFight\Game\Game.cs C:\Lfall\PixelFight.Droid\Game\Game.cs $true
Convert-File C:\Lfall\PixelFight\Engine\Ui.cs C:\Lfall\PixelFight.Droid\Game\Ui.cs $false
Convert-File C:\Lfall\PixelFight\Game\Countries.cs C:\Lfall\PixelFight.Droid\Game\Countries.cs $false
Copy-Item C:\Lfall\PixelFight\Engine\Save.cs C:\Lfall\PixelFight.Droid\Game\Save.cs -Force
Write-Output 'save copied'
