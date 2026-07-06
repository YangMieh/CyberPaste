# 由 copy.png(使用者提供的漸層藍複製圖示,512x512 透明底)產生多尺寸 exe 圖示 src\app.ico。
# 換圖示就換掉 copy.png 再重跑本檔。
# 註:System.Drawing 的型別在腳本「解析階段」就要能找到,所以呼叫本檔前父層要先
#    [System.Reflection.Assembly]::LoadWithPartialName("System.Drawing")。
[System.Reflection.Assembly]::LoadWithPartialName("System.Drawing") | Out-Null

$root   = Split-Path $PSScriptRoot -Parent
$srcPng = Join-Path $root "copy.png"
$outIco = Join-Path $root "src\app.ico"
$sizes  = 16,20,24,32,48,64,128,256

$srcImg = [System.Drawing.Image]::FromFile($srcPng)
$frames = @()
foreach ($sz in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    # 高品質縮圖(來源已是正方形透明底,直接鋪滿)
    $g.DrawImage($srcImg, (New-Object System.Drawing.Rectangle(0, 0, $sz, $sz)))
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,($ms.ToArray())
    $bmp.Dispose()
}
$srcImg.Dispose()

# 寫出 ICO(每尺寸一個 PNG 幀,Win7+ 支援)
$fs = [System.IO.File]::Create($outIco)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $data = $frames[$i]
    $w = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$w); $bw.Write([byte]$w); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($d in $frames) { $bw.Write($d) }
$bw.Flush(); $bw.Close(); $fs.Close()
Write-Host "已產生 $outIco ($((Get-Item $outIco).Length) bytes, 來源 copy.png, 尺寸 $($sizes -join ','))"
