<#
.SYNOPSIS
    Publishes a local WebGL build to the gh-pages branch.

.DESCRIPTION
    GitHub Pages serves whatever sits at the root of gh-pages, so this replaces that branch's
    contents with Build/WebGL and pushes. History on gh-pages is not interesting - it is build
    output, not source - so each publish is a single commit on a detached tree rather than a
    growing pile of binary diffs.

    Build first:
      Unity.exe -batchmode -quit -projectPath . -buildTarget WebGL `
                -executeMethod ColosseumDuel.EditorTools.ProjectBootstrap.BuildWebGL

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/publish-webgl.ps1
#>
param(
    [string]$BuildDir = "Build/WebGL",
    [string]$Branch = "gh-pages",
    [string]$Remote = "origin"
)

$ErrorActionPreference = "Stop"

$repoRoot = (git rev-parse --show-toplevel)
if (-not $repoRoot) { Write-Error "Not inside a git repository." }
Set-Location $repoRoot

$buildPath = Join-Path $repoRoot $BuildDir
if (-not (Test-Path -LiteralPath (Join-Path $buildPath "index.html"))) {
    Write-Error "No index.html in $buildPath - build the game first (see the help in this file)."
}

# A worktree keeps the checked-out source tree untouched: no stashing, no risk of committing
# build output onto main by accident.
$worktree = Join-Path ([System.IO.Path]::GetTempPath()) ("colosseum-pages-" + [guid]::NewGuid().ToString("N").Substring(0, 8))

$branchExistsRemotely = (git ls-remote --heads $Remote $Branch)
if ($branchExistsRemotely) {
    git fetch $Remote "${Branch}:refs/remotes/$Remote/$Branch" 2>&1 | Out-Null
    git worktree add --no-checkout -B $Branch $worktree "refs/remotes/$Remote/$Branch" | Out-Null
} else {
    git worktree add --no-checkout --detach $worktree | Out-Null
    Push-Location $worktree
    git checkout --orphan $Branch | Out-Null
    Pop-Location
}

try {
    Push-Location $worktree
    # Clear whatever the previous publish left, keeping .git itself.
    git rm -rqf . 2>&1 | Out-Null
    Get-ChildItem -Force | Where-Object { $_.Name -ne ".git" } | Remove-Item -Recurse -Force

    Copy-Item -Path (Join-Path $buildPath "*") -Destination $worktree -Recurse -Force

    # Without this, Pages runs the build through Jekyll, which drops files it does not like.
    New-Item -ItemType File -Path (Join-Path $worktree ".nojekyll") -Force | Out-Null

    git add -A
    if (-not (git status --porcelain)) {
        Write-Host "Nothing changed since the last publish."
        return
    }

    $sourceCommit = (git -C $repoRoot rev-parse --short HEAD)
    git commit -q -m "Publish WebGL build from $sourceCommit"
    git push -q $Remote $Branch
    Write-Host "Published $Branch from source commit $sourceCommit."
}
finally {
    Pop-Location -ErrorAction SilentlyContinue
    git worktree remove --force $worktree 2>&1 | Out-Null
}
