param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$ReportId,
    [Parameter(Mandatory = $true)][string]$UserName,
    [Parameter(Mandatory = $true)][int]$Users,
    [Parameter(Mandatory = $true)][int]$Requests
)

$ErrorActionPreference = 'Stop'
$MaxUsers = 200
$MaxRequests = 50
$Take = 50

if ($Users -lt 1 -or $Users -gt $MaxUsers) {
    throw "Users must be from 1 to $MaxUsers."
}
if ($Requests -lt 1 -or $Requests -gt $MaxRequests) {
    throw "Requests per user must be from 1 to $MaxRequests."
}
if ($ReportId -notmatch '^[0-9a-fA-F-]{36}$') {
    throw 'Report id must be a GUID.'
}

function Normalize-BaseUrl([string]$url) {
    $url = $url.Trim()
    if ($url -notmatch '^https?://') {
        $url = "https://$url"
    }
    $builder = [System.UriBuilder]$url
    $local = $builder.Host -match '^(localhost|127\.0\.0\.1)$'
    if (-not $local -and $builder.Scheme -eq 'http') {
        $builder.Scheme = 'https'
        $builder.Port = 443
    }
    if ($builder.Path -match '/ReportBuilder(/|$)') {
        $builder.Path = '/ReportBuilder'
    }
    else {
        $builder.Path = $builder.Path.TrimEnd('/')
    }
    $builder.Query = ''
    $builder.Fragment = ''
    $normalized = $builder.Uri.AbsoluteUri.TrimEnd('/')
    if ($normalized.EndsWith('/api')) {
        $normalized = $normalized.Substring(0, $normalized.Length - 4).TrimEnd('/')
    }
    return $normalized
}

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}
catch { }

$BaseUrl = Normalize-BaseUrl $BaseUrl
$secure = Read-Host 'Password' -AsSecureString
$password = [Net.NetworkCredential]::new('', $secure).Password
if ([string]::IsNullOrWhiteSpace($password)) {
    throw 'Password is required.'
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outDir = Join-Path $PSScriptRoot "load-test-results\$stamp"
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Write-Host ""
Write-Host "Running $Users users x $Requests requests against $BaseUrl"
Write-Host "Report $ReportId"
Write-Host ""

$worker = {
    param($BaseUrl, $ReportId, $UserName, $Password, $UserIndex, $RequestCount, $Take)

    function Fail-Rows([string]$Message) {
        $rows = @()
        for ($n = 1; $n -le $RequestCount; $n++) {
            $rows += [pscustomobject]@{
                User    = $UserIndex
                Request = $n
                Status  = 0
                Ms      = 0
                Rows    = $null
                Error   = $Message
            }
        }
        return $rows
    }

    function Invoke-JsonPost([string]$Url, [string]$Body, [string]$Token) {
        $handler = New-Object System.Net.Http.HttpClientHandler
        $handler.AllowAutoRedirect = $false
        $client = New-Object System.Net.Http.HttpClient $handler
        $client.Timeout = [TimeSpan]::FromSeconds(180)
        $current = $Url
        try {
            for ($hop = 0; $hop -lt 5; $hop++) {
                $content = New-Object System.Net.Http.StringContent($Body, [Text.Encoding]::UTF8, 'application/json')
                $request = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::Post, $current)
                $request.Content = $content
                if ($Token) {
                    [void]$request.Headers.TryAddWithoutValidation('Authorization', "Bearer $Token")
                }
                $response = $client.SendAsync($request).GetAwaiter().GetResult()
                $code = [int]$response.StatusCode
                if ($code -in 301, 302, 303, 307, 308 -and $response.Headers.Location) {
                    $location = $response.Headers.Location
                    if (-not $location.IsAbsoluteUri) {
                        $location = New-Object System.Uri ([Uri]$current, $location)
                    }
                    $current = $location.AbsoluteUri
                    $response.Dispose()
                    continue
                }
                $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                $response.Dispose()
                return [pscustomobject]@{ Status = $code; Body = $text; Url = $current }
            }
            return [pscustomobject]@{ Status = 0; Body = ''; Url = $current }
        }
        finally {
            $client.Dispose()
            $handler.Dispose()
        }
    }

    $loginBody = @{ userName = $UserName; password = $Password } | ConvertTo-Json
    $login = Invoke-JsonPost "$BaseUrl/api/auth/login" $loginBody $null
    if ($login.Status -lt 200 -or $login.Status -ge 300) {
        return Fail-Rows "Login failed: POST $($login.Url) returned $($login.Status)."
    }
    $session = $login.Body | ConvertFrom-Json
    $token = $session.accessToken
    if ([string]::IsNullOrWhiteSpace($token)) {
        return Fail-Rows 'Login did not return an access token.'
    }

    $runBody = @{ skip = 0; take = $Take } | ConvertTo-Json
    $rows = @()
    for ($n = 1; $n -le $RequestCount; $n++) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $response = Invoke-JsonPost "$BaseUrl/api/reports/$ReportId/run" $runBody $token
        $sw.Stop()
        $rowCount = $null
        $error = ''
        if ($response.Status -ge 200 -and $response.Status -lt 300 -and $response.Body) {
            $payload = $response.Body | ConvertFrom-Json
            $rowCount = $payload.totalCount
        }
        elseif ($response.Status -eq 0) {
            $error = "POST $($response.Url) was redirected too many times."
        }
        else {
            $error = "POST $($response.Url) returned $($response.Status)."
        }
        $rows += [pscustomobject]@{
            User    = $UserIndex
            Request = $n
            Status  = [int]$response.Status
            Ms      = [int]$sw.ElapsedMilliseconds
            Rows    = $rowCount
            Error   = $error
        }
    }
    return $rows
}

$pool = [runspacefactory]::CreateRunspacePool(1, $Users)
$pool.Open()
$pending = @()
try {
    for ($i = 1; $i -le $Users; $i++) {
        $ps = [powershell]::Create()
        $ps.RunspacePool = $pool
        [void]$ps.AddScript($worker).AddArgument($BaseUrl).AddArgument($ReportId).AddArgument($UserName).AddArgument($password).AddArgument($i).AddArgument($Requests).AddArgument($Take)
        $pending += [pscustomobject]@{ PowerShell = $ps; Handle = $ps.BeginInvoke() }
    }

    $results = @()
    foreach ($job in $pending) {
        $results += @($job.PowerShell.EndInvoke($job.Handle))
        if ($job.PowerShell.Streams.Error.Count -gt 0) {
            $results += [pscustomobject]@{
                User = 0; Request = 0; Status = 0; Ms = 0; Rows = $null
                Error = ($job.PowerShell.Streams.Error | ForEach-Object { $_.ToString() }) -join ' '
            }
        }
        $job.PowerShell.Dispose()
    }
}
finally {
    $pool.Close()
    $pool.Dispose()
    $password = $null
}

$results = @($results | Where-Object { $_ -and $_.PSObject.Properties['Status'] })
$ok = @($results | Where-Object { $_.Status -ge 200 -and $_.Status -lt 300 })
$fail = @($results | Where-Object { $_.Status -lt 200 -or $_.Status -ge 300 })
$times = @($ok | ForEach-Object { [int]$_.Ms } | Sort-Object)

function Percentile([int[]]$sorted, [double]$p) {
    if ($sorted.Count -eq 0) { return 0 }
    $index = [Math]::Ceiling($p * $sorted.Count) - 1
    if ($index -lt 0) { $index = 0 }
    if ($index -ge $sorted.Count) { $index = $sorted.Count - 1 }
    return $sorted[$index]
}

$summary = [pscustomobject]@{
    Started          = $stamp
    BaseUrl          = $BaseUrl
    ReportId         = $ReportId
    Users            = $Users
    RequestsPerUser  = $Requests
    TotalRequests    = $results.Count
    Succeeded        = $ok.Count
    Failed           = $fail.Count
    MinMs            = $(if ($times.Count) { $times[0] } else { 0 })
    AvgMs            = $(if ($times.Count) { [int](($times | Measure-Object -Average).Average) } else { 0 })
    P95Ms            = Percentile $times 0.95
    MaxMs            = $(if ($times.Count) { $times[-1] } else { 0 })
}

$csvPath = Join-Path $outDir 'requests.csv'
$summaryPath = Join-Path $outDir 'summary.txt'
$htmlPath = Join-Path $outDir 'report.html'
$results | Export-Csv -Path $csvPath -NoTypeInformation -Encoding UTF8

@(
    "Report Builder load test"
    "Started: $($summary.Started)"
    "API: $($summary.BaseUrl)"
    "Report: $($summary.ReportId)"
    "Users: $($summary.Users)"
    "Requests per user: $($summary.RequestsPerUser)"
    "Total requests: $($summary.TotalRequests)"
    "Succeeded: $($summary.Succeeded)"
    "Failed: $($summary.Failed)"
    "Min ms: $($summary.MinMs)"
    "Average ms: $($summary.AvgMs)"
    "P95 ms: $($summary.P95Ms)"
    "Max ms: $($summary.MaxMs)"
) | Set-Content -Path $summaryPath -Encoding UTF8

function HtmlEncode([string]$value) {
    if ($null -eq $value) { return '' }
    return ($value -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;' -replace '"', '&quot;')
}

$rowHtml = foreach ($row in $results) {
    $cls = if ($row.Status -ge 200 -and $row.Status -lt 300) { 'ok' } else { 'bad' }
    "<tr class=`"$cls`"><td>$($row.User)</td><td>$($row.Request)</td><td>$($row.Status)</td><td>$($row.Ms)</td><td>$($row.Rows)</td><td>$(HtmlEncode $row.Error)</td></tr>"
}

@"
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <title>Load test $stamp</title>
  <style>
    body { font-family: Segoe UI, sans-serif; margin: 32px; color: #142329; }
    h1 { font-size: 22px; margin-bottom: 4px; }
    .meta { color: #4b6067; margin-bottom: 20px; }
    .cards { display: flex; gap: 12px; flex-wrap: wrap; margin-bottom: 24px; }
    .card { border: 1px solid #d5e0e2; border-radius: 12px; padding: 12px 16px; min-width: 120px; }
    .card b { display: block; font-size: 22px; }
    table { border-collapse: collapse; width: 100%; font-size: 13px; }
    th, td { border-bottom: 1px solid #e4ecec; text-align: left; padding: 6px 8px; }
    th { background: #f4f7f7; }
    tr.bad td { background: #fff4f2; }
  </style>
</head>
<body>
  <h1>Load test results</h1>
  <p class="meta">$(HtmlEncode $BaseUrl) · report $(HtmlEncode $ReportId) · $Users users × $Requests requests</p>
  <div class="cards">
    <div class="card"><b>$($summary.TotalRequests)</b>requests</div>
    <div class="card"><b>$($summary.Succeeded)</b>succeeded</div>
    <div class="card"><b>$($summary.Failed)</b>failed</div>
    <div class="card"><b>$($summary.AvgMs) ms</b>average</div>
    <div class="card"><b>$($summary.P95Ms) ms</b>p95</div>
    <div class="card"><b>$($summary.MaxMs) ms</b>max</div>
  </div>
  <table>
    <thead><tr><th>User</th><th>Request</th><th>Status</th><th>ms</th><th>Rows</th><th>Error</th></tr></thead>
    <tbody>
      $($rowHtml -join "`n")
    </tbody>
  </table>
</body>
</html>
"@ | Set-Content -Path $htmlPath -Encoding UTF8

Write-Host "Succeeded $($summary.Succeeded) / $($summary.TotalRequests)"
Write-Host "Average $($summary.AvgMs) ms   P95 $($summary.P95Ms) ms   Max $($summary.MaxMs) ms"
Write-Host "Report: $htmlPath"
Start-Process $htmlPath
