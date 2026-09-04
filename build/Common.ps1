# Common.ps1 -- shared helpers for the irodori-tts-for-yomiwakechan build scripts.
# ASCII only. CRLF. Must parse and run on Windows PowerShell 5.1 and PowerShell 7+.
# No ternary operator, no null-coalescing, no '&&' inside this file.
#
# Dot-source it:   . (Join-Path $PSScriptRoot 'Common.ps1')
#
# Every caller is expected to have already set:
#   Set-StrictMode -Version Latest
#   $ErrorActionPreference = 'Stop'

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# TLS 1.2 is not the default on Windows PowerShell 5.1 on older builds.
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.ServicePointManager]::SecurityProtocol
} catch {
    # Ignore: PowerShell 7 on modern Windows already negotiates TLS 1.2/1.3.
}

$script:YwkLogFile = $null

function Get-YwkRepoRoot {
    <#
      .SYNOPSIS
        Absolute path of the repository root (the parent of build/).
    #>
    [CmdletBinding()]
    param()
    $buildDir = Split-Path -Parent $PSCommandPath
    if ([string]::IsNullOrEmpty($buildDir)) {
        $buildDir = $PSScriptRoot
    }
    return (Resolve-Path -LiteralPath (Join-Path $buildDir '..')).Path
}

function New-YwkDirectory {
    <#
      .SYNOPSIS
        Create a directory (and parents) if it is missing. Returns the absolute path.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        $null = New-Item -ItemType Directory -Path $Path -Force
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Start-YwkLog {
    <#
      .SYNOPSIS
        Open a transcript-like log file. All Write-YwkLog output is appended there too.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)
    $dir = Split-Path -Parent $Path
    if (-not [string]::IsNullOrEmpty($dir)) {
        $null = New-YwkDirectory -Path $dir
    }
    $script:YwkLogFile = $Path
    $stamp = (Get-Date).ToString('yyyy-MM-ddTHH:mm:sszzz')
    Set-Content -LiteralPath $Path -Value ("# log opened " + $stamp) -Encoding UTF8
    return $Path
}

function Write-YwkLog {
    <#
      .SYNOPSIS
        Write one timestamped line to the console and (if open) to the log file.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)][AllowEmptyString()][string]$Message,
        [ValidateSet('INFO', 'WARN', 'FAIL', 'STEP')][string]$Level = 'INFO'
    )
    $stamp = (Get-Date).ToString('HH:mm:ss')
    $line = '[' + $stamp + '] ' + $Level + ' ' + $Message
    if ($Level -eq 'FAIL') {
        Write-Host $line -ForegroundColor Red
    } elseif ($Level -eq 'WARN') {
        Write-Host $line -ForegroundColor Yellow
    } elseif ($Level -eq 'STEP') {
        Write-Host $line -ForegroundColor Cyan
    } else {
        Write-Host $line
    }
    if ($null -ne $script:YwkLogFile) {
        Add-Content -LiteralPath $script:YwkLogFile -Value $line -Encoding UTF8
    }
}

function Get-YwkSha256 {
    <#
      .SYNOPSIS
        Lower-case sha256 hex of a file.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)
    # Get-FileHash lives in Microsoft.PowerShell.Utility, which is not always
    # auto-loadable (a stripped PSModulePath, a constrained host). Everything
    # here rests on this hash, so fall back to the .NET provider rather than
    # fail with "the term Get-FileHash is not recognized".
    if ($null -ne (Get-Command 'Get-FileHash' -ErrorAction SilentlyContinue)) {
        $h = Get-FileHash -LiteralPath $Path -Algorithm SHA256
        return $h.Hash.ToLowerInvariant()
    }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
        try {
            $bytes = $sha.ComputeHash($fs)
        } finally {
            $fs.Dispose()
        }
    } finally {
        $sha.Dispose()
    }
    return (($bytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Get-YwkFileSize {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)
    return [int64](Get-Item -LiteralPath $Path).Length
}

function ConvertTo-YwkPosixPath {
    <#
      .SYNOPSIS
        Turn a Windows path into the forward-slash form that python312._pth wants.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)
    return ($Path -replace '\\', '/')
}

function Read-YwkJsonFile {
    <#
      .SYNOPSIS
        Read a UTF-8 JSON file into an object. Fails loudly on malformed JSON.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw ('json file not found: ' + $Path)
    }
    $raw = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    return ($raw | ConvertFrom-Json)
}

function Write-YwkJsonFile {
    <#
      .SYNOPSIS
        Write an object as UTF-8 (no BOM) JSON with LF newlines.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Value,
        [int]$Depth = 12
    )
    $dir = Split-Path -Parent $Path
    if (-not [string]::IsNullOrEmpty($dir)) {
        $null = New-YwkDirectory -Path $dir
    }
    $json = ($Value | ConvertTo-Json -Depth $Depth)
    $json = $json -replace "`r`n", "`n"
    if (-not $json.EndsWith("`n")) {
        $json = $json + "`n"
    }
    $enc = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $json, $enc)
    return $Path
}

function Write-YwkTextFile {
    <#
      .SYNOPSIS
        Write text as UTF-8 without BOM, normalising to the requested newline.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Text,
        [ValidateSet('LF', 'CRLF')][string]$Newline = 'LF'
    )
    $dir = Split-Path -Parent $Path
    if (-not [string]::IsNullOrEmpty($dir)) {
        $null = New-YwkDirectory -Path $dir
    }
    $t = $Text -replace "`r`n", "`n"
    if ($Newline -eq 'CRLF') {
        $t = $t -replace "`n", "`r`n"
    }
    $enc = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $t, $enc)
    return $Path
}

function ConvertTo-YwkCommandLine {
    <#
      .SYNOPSIS
        Join arguments into one Windows command line, quoting per the CRT rules.
        Needed because .NET Framework (Windows PowerShell 5.1) has no ArgumentList.
    #>
    [CmdletBinding()]
    param([string[]]$Arguments = @())
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($a in $Arguments) {
        $s = [string]$a
        if ($s -match '^[^ \t\n\v"]+$' -and $s.Length -gt 0) {
            $parts.Add($s)
            continue
        }
        $sb = New-Object System.Text.StringBuilder
        $null = $sb.Append('"')
        $i = 0
        while ($i -lt $s.Length) {
            $slashes = 0
            while ($i -lt $s.Length -and $s[$i] -eq '\') {
                $slashes = $slashes + 1
                $i = $i + 1
            }
            if ($i -eq $s.Length) {
                $null = $sb.Append('\' * ($slashes * 2))
            } elseif ($s[$i] -eq '"') {
                $null = $sb.Append('\' * ($slashes * 2 + 1))
                $null = $sb.Append('"')
                $i = $i + 1
            } else {
                $null = $sb.Append('\' * $slashes)
                $null = $sb.Append([string]$s[$i])
                $i = $i + 1
            }
        }
        $null = $sb.Append('"')
        $parts.Add($sb.ToString())
    }
    return ($parts -join ' ')
}

function Invoke-YwkNative {
    <#
      .SYNOPSIS
        Run an external program, capture stdout+stderr, return a hashtable.
      .OUTPUTS
        @{ ExitCode = <int>; StdOut = <string>; StdErr = <string> }
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory = $null,
        [hashtable]$Environment = $null,
        [int]$TimeoutSeconds = 0
    )
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $hasArgList = $null -ne ($psi | Get-Member -Name 'ArgumentList' -ErrorAction SilentlyContinue)
    if ($hasArgList) {
        foreach ($a in $Arguments) {
            $null = $psi.ArgumentList.Add([string]$a)
        }
    } else {
        $psi.Arguments = ConvertTo-YwkCommandLine -Arguments $Arguments
    }
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    if (-not [string]::IsNullOrEmpty($WorkingDirectory)) {
        $psi.WorkingDirectory = $WorkingDirectory
    }
    if ($null -ne $Environment) {
        foreach ($k in $Environment.Keys) {
            $psi.Environment[[string]$k] = [string]$Environment[$k]
        }
    }
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $psi
    $null = $p.Start()
    $outTask = $p.StandardOutput.ReadToEndAsync()
    $errTask = $p.StandardError.ReadToEndAsync()
    if ($TimeoutSeconds -gt 0) {
        $finished = $p.WaitForExit($TimeoutSeconds * 1000)
        if (-not $finished) {
            try { $p.Kill() } catch { }
            throw ('process timed out after ' + $TimeoutSeconds + ' s: ' + $FilePath)
        }
    } else {
        $p.WaitForExit()
    }
    $stdout = $outTask.GetAwaiter().GetResult()
    $stderr = $errTask.GetAwaiter().GetResult()
    return @{ ExitCode = $p.ExitCode; StdOut = $stdout; StdErr = $stderr }
}

function Get-YwkWebExceptionStatus {
    <#
      .SYNOPSIS
        Dig the HTTP status code out of an ErrorRecord, however deep the WebException is wrapped.
        PowerShell 7 wraps .NET method failures in a MethodInvocationException, so a plain
        "-is [System.Net.WebException]" test on $_.Exception misses every 4xx.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)]$ErrorRecord)
    $ex = $ErrorRecord.Exception
    $depth = 0
    while ($null -ne $ex -and $depth -lt 6) {
        if ($ex -is [System.Net.WebException]) {
            if ($null -ne $ex.Response) {
                return [int]([System.Net.HttpWebResponse]$ex.Response).StatusCode
            }
            return $null
        }
        $ex = $ex.InnerException
        $depth = $depth + 1
    }
    return $null
}

function Invoke-YwkHttpGetString {
    <#
      .SYNOPSIS
        HTTP GET returning the body as a string. Follows redirects. Retries.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [int]$MaxAttempts = 4,
        [int]$TimeoutSeconds = 120,
        [string]$Accept = 'application/json, text/html;q=0.9, */*;q=0.8'
    )
    $attempt = 0
    $lastError = $null
    while ($attempt -lt $MaxAttempts) {
        $attempt = $attempt + 1
        try {
            $req = [System.Net.HttpWebRequest]::Create($Url)
            $req.Method = 'GET'
            $req.UserAgent = 'irodori-tts-ywk-build/1'
            $req.Accept = $Accept
            $req.Timeout = $TimeoutSeconds * 1000
            $req.ReadWriteTimeout = $TimeoutSeconds * 1000
            $req.AllowAutoRedirect = $true
            $resp = $req.GetResponse()
            try {
                $sr = New-Object System.IO.StreamReader($resp.GetResponseStream(), [System.Text.Encoding]::UTF8)
                try {
                    return $sr.ReadToEnd()
                } finally {
                    $sr.Dispose()
                }
            } finally {
                $resp.Close()
            }
        } catch {
            $lastError = $_
            $status = Get-YwkWebExceptionStatus -ErrorRecord $_
            if ($null -ne $status -and $status -ge 400 -and $status -lt 500 -and $status -ne 429) {
                throw ('HTTP ' + $status + ' for ' + $Url)
            }
            Start-Sleep -Seconds ([Math]::Min(20, 2 * $attempt))
        }
    }
    throw ('GET failed after ' + $MaxAttempts + ' attempts: ' + $Url + ' -- ' + $lastError)
}

function Resolve-YwkRedirect {
    <#
      .SYNOPSIS
        Follow redirects for a URL without downloading the body; return the final URL.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [int]$TimeoutSeconds = 60
    )
    $req = [System.Net.HttpWebRequest]::Create($Url)
    $req.Method = 'HEAD'
    $req.UserAgent = 'irodori-tts-ywk-build/1'
    $req.Timeout = $TimeoutSeconds * 1000
    $req.AllowAutoRedirect = $true
    $resp = $req.GetResponse()
    try {
        return $resp.ResponseUri.AbsoluteUri
    } finally {
        $resp.Close()
    }
}

function Invoke-YwkDownload {
    <#
      .SYNOPSIS
        Download a URL to a file with atomic landing, sha256 verification, resume and cache reuse.
      .DESCRIPTION
        - If Destination already exists and its sha256 matches ExpectedSha256, nothing is fetched.
        - The body lands in "<Destination>.part" and is renamed only after the hash matches.
        - A partial ".part" from an earlier run is resumed with an HTTP Range request --
          unless it is already at or past ExpectedSize, the previous attempt added no
          bytes, or the server answered 416, in which case it is discarded and the fetch
          restarts from zero (a full-length .part otherwise 416s for ever).
        - When ExpectedSha256 is empty the file is fetched and its sha256 is returned (no check).
      .OUTPUTS
        @{ Sha256 = <string>; Size = <int64>; FromCache = <bool>; Path = <string> }
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$Destination,
        [AllowEmptyString()][string]$ExpectedSha256 = '',
        [int64]$ExpectedSize = 0,
        [int]$MaxAttempts = 5,
        [int]$TimeoutSeconds = 900
    )
    $dir = Split-Path -Parent $Destination
    if (-not [string]::IsNullOrEmpty($dir)) {
        $null = New-YwkDirectory -Path $dir
    }
    $want = ''
    if (-not [string]::IsNullOrEmpty($ExpectedSha256)) {
        $want = $ExpectedSha256.ToLowerInvariant()
    }

    if (Test-Path -LiteralPath $Destination) {
        $have = Get-YwkSha256 -Path $Destination
        if ([string]::IsNullOrEmpty($want) -or $have -eq $want) {
            return @{ Sha256 = $have; Size = (Get-YwkFileSize -Path $Destination); FromCache = $true; Path = $Destination }
        }
        Write-YwkLog -Level 'WARN' -Message ('cached file hash mismatch, refetching: ' + $Destination)
        Remove-Item -LiteralPath $Destination -Force
    }

    $part = $Destination + '.part'
    $attempt = 0
    $lastError = $null
    $lastOffset = [int64](-1)
    while ($attempt -lt $MaxAttempts) {
        $attempt = $attempt + 1
        $offset = [int64]0
        if (Test-Path -LiteralPath $part) {
            $offset = (Get-Item -LiteralPath $part).Length
        }
        # A .part that is already the full length asks for "Range: bytes=<len>-",
        # which every server answers with 416 -- for ever, since nothing here
        # shortens it. The same dead end is reachable from the size-mismatch
        # throw below. So: if the leftover is at or past the expected length, or
        # the previous attempt added not one byte, start from zero instead.
        if ($offset -gt 0) {
            $restart = ''
            if ($ExpectedSize -gt 0 -and $offset -ge $ExpectedSize) {
                $restart = 'the leftover .part is already ' + $offset + ' B (expected ' + $ExpectedSize + ' B)'
            } elseif ($offset -eq $lastOffset) {
                $restart = 'the previous attempt added no bytes'
            }
            if ($restart -ne '') {
                Write-YwkLog -Level 'WARN' -Message ('discarding ' + $part + ': ' + $restart)
                Remove-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue
                $offset = [int64]0
            }
        }
        $lastOffset = $offset
        try {
            $req = [System.Net.HttpWebRequest]::Create($Url)
            $req.Method = 'GET'
            $req.UserAgent = 'irodori-tts-ywk-build/1'
            $req.Timeout = 120000
            $req.ReadWriteTimeout = $TimeoutSeconds * 1000
            $req.AllowAutoRedirect = $true
            if ($offset -gt 0) {
                $req.AddRange([int64]$offset)
            }
            $resp = [System.Net.HttpWebResponse]$req.GetResponse()
            try {
                $mode = [System.IO.FileMode]::Create
                if ($offset -gt 0 -and [int]$resp.StatusCode -eq 206) {
                    $mode = [System.IO.FileMode]::Append
                } elseif ($offset -gt 0) {
                    Write-YwkLog -Level 'WARN' -Message ('server ignored Range, restarting: ' + $Url)
                }
                $fs = New-Object System.IO.FileStream($part, $mode, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
                try {
                    $rs = $resp.GetResponseStream()
                    $buf = New-Object byte[] 1048576
                    while ($true) {
                        $n = $rs.Read($buf, 0, $buf.Length)
                        if ($n -le 0) { break }
                        $fs.Write($buf, 0, $n)
                    }
                    $fs.Flush($true)
                } finally {
                    $fs.Dispose()
                }
            } finally {
                $resp.Close()
            }

            $size = (Get-Item -LiteralPath $part).Length
            if ($ExpectedSize -gt 0 -and $size -ne $ExpectedSize) {
                # Delete it, exactly as the sha256 branch does: a .part of the
                # wrong length is what makes the next attempt ask for a range the
                # server refuses with 416.
                Remove-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue
                throw ('size mismatch: got ' + $size + ' want ' + $ExpectedSize + ' for ' + $Url)
            }
            $got = Get-YwkSha256 -Path $part
            if (-not [string]::IsNullOrEmpty($want) -and $got -ne $want) {
                Remove-Item -LiteralPath $part -Force
                throw ('sha256 mismatch: got ' + $got + ' want ' + $want + ' for ' + $Url)
            }
            Move-Item -LiteralPath $part -Destination $Destination -Force
            return @{ Sha256 = $got; Size = $size; FromCache = $false; Path = $Destination }
        } catch {
            $lastError = $_
            Write-YwkLog -Level 'WARN' -Message ('download attempt ' + $attempt + ' failed: ' + $_.Exception.Message)
            # 416 Range Not Satisfiable is never fixed by retrying the same
            # range: drop the .part so the next attempt starts from zero.
            if ($_.Exception.Message -match '416') {
                if (Test-Path -LiteralPath $part) {
                    Write-YwkLog -Level 'WARN' -Message ('416 for a resumed range; discarding ' + $part)
                    Remove-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue
                }
            }
            if ($attempt -lt $MaxAttempts) {
                Start-Sleep -Seconds ([Math]::Min(30, 3 * $attempt))
            }
        }
    }
    throw ('download failed after ' + $MaxAttempts + ' attempts: ' + $Url + ' -- ' + $lastError)
}

function Expand-YwkZip {
    <#
      .SYNOPSIS
        Extract a zip into a directory. Refuses entries that escape the destination.
      .OUTPUTS
        Number of files written.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string]$Destination,
        [string]$Prefix = '',
        [switch]$StripPrefix
    )
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    $destFull = (New-YwkDirectory -Path $Destination)
    $destRoot = $destFull.TrimEnd('\') + '\'
    $count = 0
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName
            if ($name.EndsWith('/')) { continue }
            if (-not [string]::IsNullOrEmpty($Prefix)) {
                if (-not $name.StartsWith($Prefix)) { continue }
                if ($StripPrefix) {
                    $name = $name.Substring($Prefix.Length)
                }
            }
            $name = $name.TrimStart('/')
            if ([string]::IsNullOrEmpty($name)) { continue }
            $target = [System.IO.Path]::GetFullPath((Join-Path $destFull ($name -replace '/', '\')))
            if (-not $target.StartsWith($destRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw ('zip entry escapes destination: ' + $entry.FullName)
            }
            $tdir = Split-Path -Parent $target
            if (-not (Test-Path -LiteralPath $tdir)) {
                $null = New-Item -ItemType Directory -Path $tdir -Force
            }
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            $count = $count + 1
        }
    } finally {
        $zip.Dispose()
    }
    return $count
}

function Get-YwkZipTopLevelDirectory {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$ZipPath)
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($entry in $zip.Entries) {
            $n = $entry.FullName
            $i = $n.IndexOf('/')
            if ($i -gt 0) {
                return $n.Substring(0, $i + 1)
            }
        }
    } finally {
        $zip.Dispose()
    }
    throw ('no top level directory in ' + $ZipPath)
}

function Move-YwkDirectoryContents {
    <#
      .SYNOPSIS
        Merge the contents of one directory into another (files overwrite).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )
    if (-not (Test-Path -LiteralPath $Source)) { return 0 }
    $null = New-YwkDirectory -Path $Destination
    $srcFull = (Resolve-Path -LiteralPath $Source).Path
    $moved = 0
    $items = Get-ChildItem -LiteralPath $srcFull -Recurse -File -Force
    foreach ($f in $items) {
        $rel = $f.FullName.Substring($srcFull.Length).TrimStart('\')
        $target = Join-Path $Destination $rel
        $tdir = Split-Path -Parent $target
        if (-not (Test-Path -LiteralPath $tdir)) {
            $null = New-Item -ItemType Directory -Path $tdir -Force
        }
        Move-Item -LiteralPath $f.FullName -Destination $target -Force
        $moved = $moved + 1
    }
    Remove-Item -LiteralPath $srcFull -Recurse -Force
    return $moved
}

function Get-YwkUvPath {
    <#
      .SYNOPSIS
        Locate uv.exe. Fails when it is missing or is not the pinned build.
    #>
    [CmdletBinding()]
    param([string]$RequiredVersion = '')
    $cmd = Get-Command 'uv' -ErrorAction SilentlyContinue
    if ($null -eq $cmd) {
        throw 'uv was not found on PATH. Install uv 0.12.7 (decisions.md 25) and retry.'
    }
    $uv = $cmd.Source
    $r = Invoke-YwkNative -FilePath $uv -Arguments @('--version')
    if ($r.ExitCode -ne 0) {
        throw ('uv --version failed with exit code ' + $r.ExitCode)
    }
    $ver = ($r.StdOut).Trim()
    if (-not [string]::IsNullOrEmpty($RequiredVersion)) {
        if ($ver -notmatch ([regex]::Escape($RequiredVersion))) {
            throw ('uv version mismatch: expected ' + $RequiredVersion + ', got "' + $ver + '"')
        }
    }
    return @{ Path = $uv; Version = $ver }
}

function Get-YwkGitPath {
    [CmdletBinding()]
    param()
    $cmd = Get-Command 'git' -ErrorAction SilentlyContinue
    if ($null -eq $cmd) {
        throw 'git was not found on PATH.'
    }
    return $cmd.Source
}

function New-YwkChildEnvironment {
    <#
      .SYNOPSIS
        A hashtable of the current environment plus the safety flags every python run needs.
      .DESCRIPTION
        PYTHONDONTWRITEBYTECODE=1 is mandatory: the upstream submodules must never receive
        a __pycache__ directory (decisions.md 22 / design doc section 8).
    #>
    [CmdletBinding()]
    param([hashtable]$Extra = $null)
    $env2 = @{}
    foreach ($e in [System.Environment]::GetEnvironmentVariables().GetEnumerator()) {
        $env2[[string]$e.Key] = [string]$e.Value
    }
    $env2['PYTHONDONTWRITEBYTECODE'] = '1'
    $env2['PYTHONUTF8'] = '1'
    $env2['PYTHONUNBUFFERED'] = '1'
    if ($null -ne $Extra) {
        foreach ($k in $Extra.Keys) {
            $env2[[string]$k] = [string]$Extra[$k]
        }
    }
    return $env2
}

function Test-YwkUpstreamClean {
    <#
      .SYNOPSIS
        Assert that both upstream submodules are byte-identical to their pinned commits.
      .OUTPUTS
        @{ Ok = <bool>; Report = <string[]> }
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$RepoRoot)
    $git = Get-YwkGitPath
    $report = New-Object System.Collections.Generic.List[string]
    $ok = $true
    foreach ($sub in @('upstream/Irodori-TTS', 'upstream/Irodori-TTS-Server')) {
        $path = Join-Path $RepoRoot ($sub -replace '/', '\')
        if (-not (Test-Path -LiteralPath $path)) {
            $report.Add($sub + ': MISSING')
            $ok = $false
            continue
        }
        $r = Invoke-YwkNative -FilePath $git -Arguments @('-C', $path, 'status', '--porcelain', '--ignored')
        if ($r.ExitCode -ne 0) {
            $report.Add($sub + ': git status failed (' + $r.ExitCode + ') ' + $r.StdErr.Trim())
            $ok = $false
            continue
        }
        $dirty = ($r.StdOut).Trim()
        if ([string]::IsNullOrEmpty($dirty)) {
            $report.Add($sub + ': clean')
        } else {
            $report.Add($sub + ': DIRTY')
            foreach ($l in ($dirty -split "`n")) {
                $report.Add('    ' + $l.Trim())
            }
            $ok = $false
        }
        $pyc = Get-ChildItem -LiteralPath $path -Recurse -Directory -Filter '__pycache__' -Force -ErrorAction SilentlyContinue
        if ($null -ne $pyc) {
            foreach ($d in $pyc) {
                $report.Add('    __pycache__: ' + $d.FullName)
                $ok = $false
            }
        }
    }
    return @{ Ok = $ok; Report = $report.ToArray() }
}

function Get-YwkTimestamp {
    [CmdletBinding()]
    param()
    return (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
}
