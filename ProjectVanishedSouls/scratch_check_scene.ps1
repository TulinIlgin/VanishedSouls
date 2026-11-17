$bytes = [System.IO.File]::ReadAllBytes("Assets/Vanished.unity")
$str = [System.Text.Encoding]::ASCII.GetString($bytes)

foreach ($word in @("FogOfWar", "FogPlane", "FogRevealer", "FogVisibility", "teamMembers", "fogPlaneMeshFilter")) {
    $idx = 0
    $count = 0
    while (($idx = $str.IndexOf($word, $idx)) -ge 0) {
        $count++
        $idx += $word.Length
    }
    Write-Host "$word count: $count"
}
