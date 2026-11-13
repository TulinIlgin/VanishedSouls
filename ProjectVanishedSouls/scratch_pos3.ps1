$bytes = [System.IO.File]::ReadAllBytes("Assets/Vanished.unity")

# Find "Dusman_1", "Dusman_2", "Dusman_3", "Aria"
# Unity binary files contain serialized strings and floats
# Let's search for "Dusman_1" and find its GameObject, Transform, and m_LocalPosition
$str = [System.Text.Encoding]::ASCII.GetString($bytes)

function FindPosition($name) {
    $idx = $str.IndexOf($name)
    if ($idx -lt 0) { return }
    # Search backwards for "m_LocalPosition"
    $posIdx = $str.LastIndexOf("m_LocalPosition", $idx)
    if ($posIdx -ge 0 -and ($idx - $posIdx) -lt 3000) {
        $chunk = $str.Substring($posIdx, [Math]::Min(300, $str.Length - $posIdx))
        $clean = [System.Text.RegularExpressions.Regex]::Replace($chunk, "[^\x20-\x7E\r\n]", " ")
        Write-Host "Position near $name (dist $($idx - $posIdx)):"
        Write-Host $clean
    }
}

FindPosition "Aria"
FindPosition "Dusman_1"
FindPosition "Dusman_2"
FindPosition "Dusman_3"
