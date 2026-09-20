# Run after the Release build to regenerate the README's split-theme keyboard.
$ErrorActionPreference = 'Stop'

Push-Location $PSScriptRoot
try {
    $previewDirectory = Join-Path $PSScriptRoot '.runtime\keyboard-screenshot'
    New-Item -ItemType Directory -Force -Path $previewDirectory | Out-Null
    foreach ($theme in @('steam-soft', 'steam-flat')) {
        $previewPath = Join-Path $previewDirectory "$theme.png"
        dotnet run --project src/GoBoard.App -c Release --no-build -- --render $previewPath --layout us --state hover --theme $theme --controls
        if ($LASTEXITCODE -ne 0) { throw "Rendering $theme failed." }
    }

    Add-Type -AssemblyName System.Drawing
    $soft = $flat = $combined = $graphics = $clip = $divider = $null
    try {
        $soft = [System.Drawing.Bitmap]::FromFile((Join-Path $previewDirectory 'steam-soft.png'))
        $flat = [System.Drawing.Bitmap]::FromFile((Join-Path $previewDirectory 'steam-flat.png'))
        if ($soft.Size -ne $flat.Size) { throw 'Theme previews must have matching dimensions.' }
        $combined = [System.Drawing.Bitmap]::new($soft.Width, $soft.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($combined)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.DrawImageUnscaled($soft, 0, 0)

        # Both halves retain the renderer's exact geometry, meeting at the center.
        $topX = [single]($soft.Width * 0.56)
        $bottomX = [single]($soft.Width * 0.44)
        $clip = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $clip.AddPolygon([System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new($topX, 0),
            [System.Drawing.PointF]::new($soft.Width, 0),
            [System.Drawing.PointF]::new($soft.Width, $soft.Height),
            [System.Drawing.PointF]::new($bottomX, $soft.Height)
        ))
        $graphics.SetClip($clip)
        $graphics.DrawImageUnscaled($flat, 0, 0)
        $graphics.ResetClip()

        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $divider = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(210, 102, 192, 244), 3)
        $graphics.DrawLine($divider, $topX, 0, $bottomX, $soft.Height)
        $outputPath = Join-Path $PSScriptRoot 'docs\images\goboard-keyboard.png'
        $combined.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Output "Rendered split-theme keyboard to $outputPath"
    }
    finally {
        foreach ($resource in @($divider, $clip, $graphics, $combined, $flat, $soft)) {
            if ($null -ne $resource) { $resource.Dispose() }
        }
    }
}
finally {
    Pop-Location
}
