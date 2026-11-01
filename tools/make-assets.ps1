# 生成应用图标 / 磁贴图 / 启动图（白色挂锁，透明底；启动图带深色底与文字）。
# 用法： powershell -ExecutionPolicy Bypass -File tools\make-assets.ps1
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Screenlocker\Assets')
)

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path -LiteralPath $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
}

function New-LockBitmap {
    param(
        [int]$Width,
        [int]$Height,
        [switch]$Background,
        [string]$Caption
    )

    $bmp = New-Object System.Drawing.Bitmap -ArgumentList @($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    if ($Background) {
        $g.Clear([System.Drawing.ColorTranslator]::FromHtml('#1F1F1F'))
    }
    else {
        $g.Clear([System.Drawing.Color]::Transparent)
    }

    $white = [System.Drawing.Brushes]::White
    $size = [double][Math]::Min($Width, $Height)
    $cx = $Width / 2.0
    $cy = if ($Caption) { $Height * 0.38 } else { $Height / 2.0 }

    $bodyW = $size * 0.44
    $bodyH = $size * 0.32
    $bx = $cx - $bodyW / 2.0
    $by = $cy - $bodyH / 2.0 + $size * 0.10

    # 锁环
    $shackleW = $bodyW * 0.62
    $shackleH = $bodyW * 0.62
    $penWidth = [Math]::Max(1.5, $size * 0.09)
    $pen = New-Object System.Drawing.Pen -ArgumentList @([System.Drawing.Color]::White, [float]$penWidth)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($pen, [float]($cx - $shackleW / 2.0), [float]($by - $shackleH * 0.95), [float]$shackleW, [float]$shackleH, 180.0, 180.0)

    # 锁体（圆角矩形）
    $r = [Math]::Max(1.0, $size * 0.05)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc([float]$bx, [float]$by, [float]$r, [float]$r, 180.0, 90.0)
    $path.AddArc([float]($bx + $bodyW - $r), [float]$by, [float]$r, [float]$r, 270.0, 90.0)
    $path.AddArc([float]($bx + $bodyW - $r), [float]($by + $bodyH - $r), [float]$r, [float]$r, 0.0, 90.0)
    $path.AddArc([float]$bx, [float]($by + $bodyH - $r), [float]$r, [float]$r, 90.0, 90.0)
    $path.CloseFigure()
    $g.FillPath($white, $path)

    if ($Caption) {
        $font = New-Object System.Drawing.Font -ArgumentList @('Microsoft YaHei UI', [float]($size * 0.13), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
        $format = New-Object System.Drawing.StringFormat
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $rect = New-Object System.Drawing.RectangleF -ArgumentList @(0.0, [float]($Height * 0.66), [float]$Width, [float]($Height * 0.26))
        $g.DrawString($Caption, $font, $white, $rect, $format)
        $font.Dispose()
        $format.Dispose()
    }

    $g.Dispose()
    $pen.Dispose()
    $path.Dispose()
    return $bmp
}

function Save-Asset {
    param(
        [System.Drawing.Bitmap]$Bitmap,
        [string]$Name
    )

    $target = Join-Path $OutDir $Name
    $Bitmap.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)
    $Bitmap.Dispose()
    Write-Host ("  生成 {0}" -f $Name)
}

Write-Host ("输出目录：{0}" -f $OutDir)

# 锁屏徽章（必须 24x24 单色，140/180 是高分屏版本）
Save-Asset (New-LockBitmap -Width 24 -Height 24) 'LockBadge.png'
Save-Asset (New-LockBitmap -Width 33 -Height 33) 'LockBadge.scale-140.png'
Save-Asset (New-LockBitmap -Width 43 -Height 43) 'LockBadge.scale-180.png'

# 小图标
Save-Asset (New-LockBitmap -Width 44 -Height 44) 'Square44x44Logo.png'
Save-Asset (New-LockBitmap -Width 88 -Height 88) 'Square44x44Logo.scale-200.png'
Save-Asset (New-LockBitmap -Width 176 -Height 176) 'Square44x44Logo.scale-400.png'
Save-Asset (New-LockBitmap -Width 24 -Height 24) 'Square44x44Logo.targetsize-24_altform-unplated.png'

# 中磁贴 / 宽磁贴
Save-Asset (New-LockBitmap -Width 150 -Height 150) 'Square150x150Logo.png'
Save-Asset (New-LockBitmap -Width 300 -Height 300) 'Square150x150Logo.scale-200.png'
Save-Asset (New-LockBitmap -Width 600 -Height 600) 'Square150x150Logo.scale-400.png'
Save-Asset (New-LockBitmap -Width 310 -Height 150) 'Wide310x150Logo.png'
Save-Asset (New-LockBitmap -Width 620 -Height 300) 'Wide310x150Logo.scale-200.png'

# 商店图标
Save-Asset (New-LockBitmap -Width 50 -Height 50) 'StoreLogo.png'

# 启动图
Save-Asset (New-LockBitmap -Width 620 -Height 300 -Background -Caption '锁屏文字') 'SplashScreen.png'
Save-Asset (New-LockBitmap -Width 1240 -Height 600 -Background -Caption '锁屏文字') 'SplashScreen.scale-200.png'

Write-Host '资源生成完成。'
