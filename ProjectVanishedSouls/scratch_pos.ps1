$bytes = [System.IO.File]::ReadAllBytes("Assets/Vanished.unity")

function ExtractTransforms($bytes) {
    # Let's search for "m_Name: Dusman" or Transform records in Unity binary/text
    $str = [System.Text.Encoding]::UTF8.GetString($bytes)
    # Search for occurrences of Dusman
    $names = @("Dusman_1", "Dusman_2", "Dusman_3", "Aria")
    foreach ($n in $names) {
        $idx = 0
        while (($idx = $str.IndexOf($n, $idx)) -ge 0) {
            Write-Host "Found $n at byte $idx"
            $idx += $n.Length
        }
    }
}
ExtractTransforms $bytes
