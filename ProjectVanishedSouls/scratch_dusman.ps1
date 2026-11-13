$bytes = [System.IO.File]::ReadAllBytes("Assets/Vanished.unity")
$str = [System.Text.Encoding]::ASCII.GetString($bytes)

# Let's search for all GameObject definitions in the scene file
# A GameObject has "m_Name: <name>" and "m_Component:"
# In binary/text, let's find occurrences of Dusman
$regex = [System.Text.RegularExpressions.Regex]::Matches($str, "Dusman_[0-9]+")
foreach ($m in $regex) {
    Write-Host "Match: $($m.Value) at index $($m.Index)"
}
