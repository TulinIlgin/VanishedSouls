$bytes = [System.IO.File]::ReadAllBytes("Assets/Vanished.unity")

# Let's inspect the exact serialized fields for EnemyAI components
# In Unity YAML/binary:
# PPtr<Transform> visualModel
$str = [System.Text.Encoding]::ASCII.GetString($bytes)
$regex = [System.Text.RegularExpressions.Regex]::Matches($str, "(visualModel|extraVisualParts)")
foreach ($r in $regex) {
    Write-Host "$($r.Value) at $($r.Index)"
    $chunk = $str.Substring($r.Index, [Math]::Min(200, $str.Length - $r.Index))
    $clean = [System.Text.RegularExpressions.Regex]::Replace($chunk, "[^\x20-\x7E\r\n]", " ")
    Write-Host $clean
}
