<#
.SYNOPSIS
    Gera o ícone do LessonsPlayer: o contorno de um pergaminho (a aula) com um play no meio (o player), em branco,
    sobre um quadrado arredondado azul. O fundo garante contraste na barra de tarefas clara e na escura.
    Desenho escolhido pelo usuário em 2026-11-07, entre pergaminho com "CC", com linhas de texto e com play.

.DESCRIPTION
    Cada tamanho é desenhado direto na sua resolução (e não reduzido do maior), para os traços ficarem nítidos.
    Abaixo de 48 px, as pontas dos rolos ficam lisas: os anéis do papel enrolado ficariam borrados.
    O .ico guarda cada tamanho como PNG, o que o Windows aceita desde o Vista.

.PARAMETER Output
    O .ico gerado (no projeto: src\LessonsPlayer.App\Assets\LessonsPlayer.ico).

.PARAMETER Preview
    Uma folha PNG com todos os tamanhos sobre fundo claro e escuro, para conferir o desenho.
#>
param(
    [string]$Output,
    [string]$Preview
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Sizes = 16, 20, 24, 32, 40, 48, 64, 256
$Accent = [System.Drawing.Color]::FromArgb(0x25, 0x63, 0xEB)
$White = [System.Drawing.Color]::White

function New-RoundedRectangle([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

# Desenha num quadro de 256 unidades, escalado para o tamanho pedido.
function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0
    $g.ScaleTransform($s, $s)

    $small = $size -lt 32
    # Traço com no mínimo ~1,3 px na tela.
    $stroke = [Math]::Max(12.0, 1.3 / $s)
    $pen = New-Object System.Drawing.Pen($White, $stroke)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $background = New-RoundedRectangle 0 0 256 256 56
    $g.FillPath((New-Object System.Drawing.SolidBrush($Accent)), $background)

    # Rolos (cápsulas) em cima e embaixo, um pouco mais largos que o corpo.
    $rollH = if ($small) { 44 } else { 38 }
    $top = New-RoundedRectangle 44 36 168 $rollH ($rollH / 2)
    $bottom = New-RoundedRectangle 44 (220 - $rollH) 168 $rollH ($rollH / 2)
    $g.DrawPath($pen, $top)
    $g.DrawPath($pen, $bottom)

    # Corpo: as laterais do papel entre os rolos.
    $bodyLeft = 64; $bodyRight = 192
    $g.DrawLine($pen, $bodyLeft, 36 + $rollH, $bodyLeft, 220 - $rollH)
    $g.DrawLine($pen, $bodyRight, 36 + $rollH, $bodyRight, 220 - $rollH)

    if ($size -ge 48) {
        # O papel enrolado na ponta esquerda do rolo de cima e na direita do de baixo: um anel de traço mais fino.
        $ring = New-Object System.Drawing.Pen($White, ($stroke * 0.6))
        $curl = $rollH * 0.5
        $g.DrawEllipse($ring, 44 + $rollH / 2 - $curl / 2, 36 + $rollH / 2 - $curl / 2, $curl, $curl)
        $g.DrawEllipse($ring, 212 - $rollH / 2 - $curl / 2, 220 - $rollH / 2 - $curl / 2, $curl, $curl)
        $ring.Dispose()
    }

    # Play no centro do papel, com os cantos arredondados pelo traço.
    $middle = 128
    $points = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(108, ($middle - 30))),
        (New-Object System.Drawing.PointF(108, ($middle + 30))),
        (New-Object System.Drawing.PointF(160, $middle)))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush($White)), $points)
    $g.DrawPolygon($pen, $points)

    $g.Dispose()
    return $bmp
}

function Get-PngBytes($bitmap) {
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$stream.ToArray()
}

function Write-Icon([string]$path) {
    $images = foreach ($size in $Sizes) {
        $bmp = New-IconBitmap $size
        [pscustomobject]@{ Size = $size; Png = (Get-PngBytes $bmp) }
        $bmp.Dispose()
    }

    $writer = New-Object System.IO.BinaryWriter([System.IO.File]::Create($path))
    try {
        # ICONDIR + uma ICONDIRENTRY (16 bytes) por tamanho, seguidas dos PNGs.
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
        $offset = 6 + 16 * $images.Count
        foreach ($image in $images) {
            $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$image.Png.Length); $writer.Write([uint32]$offset)
            $offset += $image.Png.Length
        }

        foreach ($image in $images) { $writer.Write($image.Png) }
    }
    finally {
        $writer.Dispose()
    }
}

function Write-Preview([string]$path) {
    $sheet = New-Object System.Drawing.Bitmap(($Sizes.Count * 70 + 220), 300)
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $label = New-Object System.Drawing.Font('Segoe UI', 14)
    $y = 10
    foreach ($dark in $false, $true) {
        $band = if ($dark) { [System.Drawing.Color]::FromArgb(0x20, 0x20, 0x20) } else { [System.Drawing.Color]::White }
        $g.FillRectangle((New-Object System.Drawing.SolidBrush($band)), 0, $y, $sheet.Width, 136)
        $textBrush = if ($dark) { [System.Drawing.Brushes]::White } else { [System.Drawing.Brushes]::Black }
        $g.DrawString($(if ($dark) { 'escuro' } else { 'claro' }), $label, $textBrush, 8, $y + 8)
        $x = 100
        foreach ($size in ($Sizes | Where-Object { $_ -le 64 })) {
            $bmp = New-IconBitmap $size
            $g.DrawImageUnscaled($bmp, $x, $y + 68 - [int]($size / 2))
            $bmp.Dispose()
            $x += [Math]::Max(40, $size + 20)
        }

        $big = New-IconBitmap 128
        $g.DrawImage($big, $x + 10, $y + 4, 128, 128)
        $big.Dispose()
        $y += 140
    }

    $sheet.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $sheet.Dispose()
}

if ($Preview) { Write-Preview $Preview; "prévia: $Preview" }
if ($Output) { Write-Icon $Output; "ícone: $Output (tamanhos $($Sizes -join ', '))" }
