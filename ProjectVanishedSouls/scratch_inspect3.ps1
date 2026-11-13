$bytes = [System.IO.File]::ReadAllBytes("Assets/Vanished.unity")
$str = [System.Text.Encoding]::ASCII.GetString($bytes)

# Dusman_1, Dusman_2, Dusman_3 pozisyonları ve bileşenleri
$matches = [System.Text.RegularExpressions.Regex]::Matches($str, "(Dusman_[123]|EnemyAI|visualModel|extraVisualParts)")
foreach ($m in $matches) {
    Write-Host "$($m.Value) at $($m.Index)"
}
