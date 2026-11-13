$bytes = [System.IO.File]::ReadAllBytes("Assets/Vanished.unity")
$str = [System.Text.Encoding]::ASCII.GetString($bytes)
$matches = [System.Text.RegularExpressions.Regex]::Matches($str, "[a-zA-Z0-9_\-\.]{4,}")
$results = @()
foreach ($m in $matches) {
    if ($m.Value -match "Enemy|Guard|Dusman|Mutant|Zombie") {
        $results += $m.Value
    }
}
$results | Select-Object -Unique | Out-File "scratch_output.txt"
