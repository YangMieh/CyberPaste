# 產生 CyberPaste 的 exe 圖示 src\app.ico,畫法與 Program.cs 的 MakeIcon(藍色「兩張疊紙/複製」)完全一致,
# 只是輸出成多尺寸 .ico 供 /win32icon 內嵌。改圖示就改這裡重跑。
[System.Reflection.Assembly]::LoadWithPartialName("System.Drawing") | Out-Null

$outIco = Join-Path (Split-Path $PSScriptRoot -Parent) "src\app.ico"
$sizes  = 16,20,24,32,48,64,128,256
$frames = @()

foreach ($sz in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s    = $sz / 32.0
    $main = [System.Drawing.Color]::FromArgb(40, 120, 215)   # 啟用(藍)
    $back = [System.Drawing.Color]::FromArgb(150, 190, 240)

    $bb  = New-Object System.Drawing.SolidBrush($back)
    $fb  = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $pen = New-Object System.Drawing.Pen($main, [float](2.0 * $s))
    $lp  = New-Object System.Drawing.Pen($main, [float](1.5 * $s))

    # 後面那張紙
    $g.FillRectangle($bb,  [float](6*$s),  [float](4*$s),  [float](16*$s), [float](20*$s))
    $g.DrawRectangle($pen, [float](6*$s),  [float](4*$s),  [float](16*$s), [float](20*$s))
    # 前面那張紙
    $g.FillRectangle($fb,  [float](11*$s), [float](9*$s),  [float](16*$s), [float](20*$s))
    $g.DrawRectangle($pen, [float](11*$s), [float](9*$s),  [float](16*$s), [float](20*$s))
    # 三條文字線
    $g.DrawLine($lp, [float](14*$s), [float](15*$s), [float](24*$s), [float](15*$s))
    $g.DrawLine($lp, [float](14*$s), [float](19*$s), [float](24*$s), [float](19*$s))
    $g.DrawLine($lp, [float](14*$s), [float](23*$s), [float](21*$s), [float](23*$s))

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,($ms.ToArray())
    $bmp.Dispose()
}

# 寫出 ICO(每個尺寸用 PNG 幀,Win7+ 支援)
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
Write-Host "已產生 $outIco ($((Get-Item $outIco).Length) bytes, 尺寸 $($sizes -join ','))"
