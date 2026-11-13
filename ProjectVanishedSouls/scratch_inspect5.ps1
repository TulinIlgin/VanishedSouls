$bytes = [System.IO.File]::ReadAllBytes("Assets/Vanished.unity")
$str = [System.Text.Encoding]::ASCII.GetString($bytes)

foreach ($name in @("Aria", "Dusman_1", "Dusman_2", "Dusman_3")) {
    $idx = $str.IndexOf($name)
    if ($idx -ge 0) {
        $start = [Math]::Max(0, $idx - 800)
        $chunk = $str.Substring($start, 1200)
        $clean = [System.Text.RegularExpressions.Regex]::Replace($chunk, "[^\x20-\x7E\r\n]", " ")
        $lines = $clean -split "`r`n"
        Write-Host "=== $name ==="
        foreach ($l in $lines) {
            if ($l -match "m_LocalPosition|m_Name") {
                Write-Host $l
            }
        }
    }
}
