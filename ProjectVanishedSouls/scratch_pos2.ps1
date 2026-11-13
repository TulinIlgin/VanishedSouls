$bytes = [System.IO.File]::ReadAllBytes("Assets/Vanished.unity")

foreach ($item in @(@{Name="Dusman_1"; Offset=272075}, @{Name="Dusman_2"; Offset=149375}, @{Name="Dusman_3"; Offset=215054})) {
    $start = [Math]::Max(0, $item.Offset - 2000)
    $len = 4000
    $sub = [System.Text.Encoding]::ASCII.GetString($bytes, $start, $len)
    $clean = [System.Text.RegularExpressions.Regex]::Replace($sub, "[^\x20-\x7E\r\n]", " ")
    
    # Let's search for floats or position text
    Write-Host "=== $($item.Name) ==="
    $lines = $clean -split "\r?\n"
    foreach ($l in $lines) {
        if ($l -match "Position|Rotation|Enemy|visual|Mesh|Model") {
            Write-Host $l
        }
    }
}
