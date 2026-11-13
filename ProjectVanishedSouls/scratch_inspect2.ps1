$bytes = [System.IO.File]::ReadAllBytes("Assets/Vanished.unity")
$str = [System.Text.Encoding]::ASCII.GetString($bytes)

foreach ($name in @("Dusman_1", "Dusman_2", "Dusman_3")) {
    $idx = $str.IndexOf($name)
    Write-Host "=== $name (Index: $idx) ==="
    if ($idx -ge 0) {
        $start = [Math]::Max(0, $idx - 500)
        $len = [Math]::Min(1200, $str.Length - $start)
        $chunk = $str.Substring($start, $len)
        $clean = [System.Text.RegularExpressions.Regex]::Replace($chunk, "[^\x20-\x7E\r\n]", " ")
        Write-Host $clean
    }
}
