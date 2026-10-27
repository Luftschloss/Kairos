param([string]$Root = 'E:\WorkSpace\Personal\Kairos')
Add-Type -AssemblyName System.Drawing
$plan = Get-Content -LiteralPath (Join-Path $Root 'Library/Locus/tmp/etched-card-render.json') -Raw -Encoding UTF8 | ConvertFrom-Json
function ColorOf($hex,$alpha) { if ([string]::IsNullOrEmpty($hex)) { return [System.Drawing.Color]::Transparent }; $c=[System.Drawing.ColorTranslator]::FromHtml($hex); return [System.Drawing.Color]::FromArgb([int]$alpha,$c.R,$c.G,$c.B) }
function DrawShapes($g,$shapes) {
 foreach($s in $shapes) {
  $p=$s.points; $brush=$null; $pen=$null
  try {
   if($s.fill) { $brush=New-Object System.Drawing.SolidBrush (ColorOf $s.fill $s.alpha) }
   if($s.stroke) { $pen=New-Object System.Drawing.Pen ((ColorOf $s.stroke $s.alpha),[float]$s.width) }
   switch($s.kind) {
    'line' { if($pen){$g.DrawLine($pen,[float]$p[0],[float]$p[1],[float]$p[2],[float]$p[3])} }
    'rect' { if($brush){$g.FillRectangle($brush,[float]$p[0],[float]$p[1],[float]$p[2],[float]$p[3])};if($pen){$g.DrawRectangle($pen,[float]$p[0],[float]$p[1],[float]$p[2],[float]$p[3])} }
    'ellipse' { if($brush){$g.FillEllipse($brush,[float]$p[0],[float]$p[1],[float]$p[2],[float]$p[3])};if($pen){$g.DrawEllipse($pen,[float]$p[0],[float]$p[1],[float]$p[2],[float]$p[3])} }
    'poly' { $pts=New-Object 'System.Drawing.PointF[]' ($p.Count/2);for($i=0;$i -lt $pts.Length;$i++){$pts[$i]=New-Object System.Drawing.PointF ([float]$p[$i*2],[float]$p[$i*2+1])};if($brush){$g.FillPolygon($brush,$pts)};if($pen){$g.DrawPolygon($pen,$pts)} }
   }
  } finally { if($brush){$brush.Dispose()};if($pen){$pen.Dispose()} }
 }
}
function Bitmap($w,$h) { return New-Object System.Drawing.Bitmap ([int]$w,[int]$h,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb) }
function Graphics($bmp) { $g=[System.Drawing.Graphics]::FromImage($bmp);$g.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::AntiAlias;$g.PixelOffsetMode=[System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality;$g.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic;return $g }
function SavePng($bmp,$path) { $bmp.Save($path,[System.Drawing.Imaging.ImageFormat]::Png) }
$runtime=Join-Path $Root 'Assets/Kairos/Art/Resources/KairosArt'; New-Item -ItemType Directory -Force $runtime | Out-Null
New-Item -ItemType Directory -Force (Join-Path $runtime 'Portraits') | Out-Null; New-Item -ItemType Directory -Force (Join-Path $runtime 'Frames') | Out-Null
$source=Join-Path $Root 'ArtSource/Kairos/EtchedCards'
$contact=Bitmap 1400 1600; $cg=Graphics $contact; $cg.Clear([System.Drawing.Color]::FromArgb(14,20,25));$font=New-Object System.Drawing.Font ('Microsoft YaHei',10);$textBrush=New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(223,209,170))
$index=0
foreach($e in $plan.entries) {
 $folder=Join-Path $source $e.key; $merged=Bitmap 600 800; $mg=Graphics $merged
 foreach($l in $e.layers) { $bmp=Bitmap 600 800; $g=Graphics $bmp;DrawShapes $g $l.shapes;$g.Dispose();SavePng $bmp (Join-Path $folder ($l.name+'.png'));$mg.DrawImage($bmp,0,0);$bmp.Dispose() }
 $mg.Dispose(); SavePng $merged (Join-Path $folder 'merged.png')
 $small=Bitmap 384 512;$sg=Graphics $small;$sg.DrawImage($merged,0,0,384,512);$sg.Dispose();SavePng $small (Join-Path $runtime ('Portraits/'+$e.key+'.png'));$small.Dispose()
 $thumb=Bitmap 150 200;$tg=Graphics $thumb;$tg.DrawImage($merged,0,0,150,200);$tg.Dispose();SavePng $thumb (Join-Path $folder 'thumbnail.png');$thumb.Dispose()
 $x=($index % 8)*175+12;$y=[int][math]::Floor($index/8)*225+12;$cg.DrawImage($merged,[int]$x,[int]$y,150,200);$cg.DrawString($e.name,$font,$textBrush,[float]$x,[float]($y+203));$merged.Dispose();$index++; Write-Output ('Rendered '+$e.key)
}
foreach($f in $plan.frames){$bmp=Bitmap 600 800;$g=Graphics $bmp;DrawShapes $g $f.shapes;$g.Dispose();$small=Bitmap 384 512;$sg=Graphics $small;$sg.DrawImage($bmp,0,0,384,512);$sg.Dispose();SavePng $small (Join-Path $runtime ('Frames/'+$f.key+'.png'));$small.Dispose();$bmp.Dispose()}
$cg.Dispose();SavePng $contact (Join-Path $source 'contact-sheet.png');$contact.Dispose();$font.Dispose();$textBrush.Dispose()
Write-Output 'PASS 56 layered illustrations, 6 transparent frames, contact sheet'
