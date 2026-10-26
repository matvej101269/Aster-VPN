param([switch]$SystemRoute)
$ErrorActionPreference = 'Stop'
$config = Get-Content 'C:/Users/mat10/AppData/Local/AsterVPN/runtime.json' -Raw | ConvertFrom-Json
$probe = $config.inbounds | Where-Object tag -eq 'probe'
$handler = [Net.Http.HttpClientHandler]::new()
$handler.Proxy = [Net.WebProxy]::new("socks5://127.0.0.1:$($probe.listen_port)")
$handler.Proxy.Credentials = [Net.NetworkCredential]::new($probe.users[0].username, $probe.users[0].password)
$handler.UseProxy = !$SystemRoute
$handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(15)
try {
    foreach ($target in @('https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration','https://teams.microsoft.com/','https://chatgpt.com/','https://www.gstatic.com/generate_204')) {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        try {
            $response = $client.GetAsync($target, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
            '{0}: HTTP {1}; {2} ms' -f ([uri]$target).Host, [int]$response.StatusCode, $watch.ElapsedMilliseconds
            $response.Dispose()
        } catch { '{0}: FAILED {1}; {2} ms' -f ([uri]$target).Host, $_.Exception.GetBaseException().GetType().Name, $watch.ElapsedMilliseconds }
    }
} finally { $client.Dispose() }
