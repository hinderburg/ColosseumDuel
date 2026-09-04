<#
.SYNOPSIS
    Serves a Unity WebGL build over plain HTTP for local testing.

.DESCRIPTION
    A Unity WebGL build cannot be opened from file:// - the loader fetches the .data and .wasm
    files, and browsers refuse cross-origin requests on the file scheme. This is the smallest
    thing that makes the build openable locally, with no Node or Python needed.

    Serves the build uncompressed, which matches how GitHub Pages will serve it (the project
    builds with Compression Format = Disabled for exactly that reason), so what you see here is
    what a visitor gets.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/serve-webgl.ps1
    powershell -ExecutionPolicy Bypass -File Tools/serve-webgl.ps1 -Port 8123 -Root Build/WebGL
#>
param(
    [int]$Port = 8080,
    [string]$Root = "Build/WebGL"
)

$ErrorActionPreference = "Stop"

$rootPath = Resolve-Path -LiteralPath $Root
if (-not (Test-Path -LiteralPath (Join-Path $rootPath "index.html"))) {
    Write-Error "No index.html in $rootPath - build first (Tools > Colosseum > Build WebGL)."
}

# Unity serves these with specific types; the loader checks them and the browser needs
# application/wasm to stream-compile the module.
$mimeTypes = @{
    ".html" = "text/html; charset=utf-8"
    ".js"   = "application/javascript"
    ".wasm" = "application/wasm"
    ".data" = "application/octet-stream"
    ".json" = "application/json"
    ".css"  = "text/css"
    ".png"  = "image/png"
    ".jpg"  = "image/jpeg"
    ".svg"  = "image/svg+xml"
    ".ico"  = "image/x-icon"
}

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()

Write-Host "Serving $rootPath at http://localhost:$Port/  (Ctrl+C to stop)"

try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        $request = $context.Request
        $response = $context.Response

        $relative = [System.Uri]::UnescapeDataString($request.Url.AbsolutePath).TrimStart('/')
        if ([string]::IsNullOrWhiteSpace($relative)) { $relative = "index.html" }

        $target = Join-Path $rootPath $relative

        # Refuse anything that resolves outside the build directory.
        $full = [System.IO.Path]::GetFullPath($target)
        if (-not $full.StartsWith([string]$rootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            $response.StatusCode = 403
            $response.Close()
            continue
        }

        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
            $response.StatusCode = 404
            $response.Close()
            Write-Host "404 $relative"
            continue
        }

        $extension = [System.IO.Path]::GetExtension($full).ToLowerInvariant()
        $response.ContentType = if ($mimeTypes.ContainsKey($extension)) { $mimeTypes[$extension] } else { "application/octet-stream" }

        $bytes = [System.IO.File]::ReadAllBytes($full)
        $response.ContentLength64 = $bytes.Length
        $response.OutputStream.Write($bytes, 0, $bytes.Length)
        $response.Close()

        Write-Host ("200 {0} ({1:N0} bytes)" -f $relative, $bytes.Length)
    }
}
finally {
    $listener.Stop()
    $listener.Close()
}
