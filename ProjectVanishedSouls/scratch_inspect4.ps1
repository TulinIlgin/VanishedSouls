$bytes = [System.IO.File]::ReadAllBytes("Assets/Vanished.unity")
$str = [System.Text.Encoding]::ASCII.GetString($bytes)

# Dusman_1, Dusman_2, Dusman_3 pozisyonları
foreach ($name in @("Aria", "Dusman_1", "Dusman_2", "Dusman_3", "Lamp", "Light")) {
    $idx = 0
    while (($idx = $str.IndexOf($name, $idx)) -ge 0) {
        $posIdx = $str.IndexOf("m_LocalPosition", $idx)
        if ($posIdx -ge 0 -and $posIdx -lt $idx + 600) {
            $chunk = $str.Substring($posIdx, 200)
            $clean = [System.Text.RegularExpressions.Regex]::Replace($chunk, "[^\x20-\x7E\r\n]", " ")
            Write-Host "Object: $name at $idx"
            Write-Host $clean
            Write-Host "--------------------"
        }
        $idx += $name.Length
    }
}
