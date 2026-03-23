$u = New-Object System.Net.Sockets.UdpClient
$b = [System.Text.Encoding]::UTF8.GetBytes('{"type":"shutdown"}')
$u.Send($b, $b.Length, "127.0.0.1", 19871) | Out-Null
$u.Close()
Write-Host "Apricadabra Core stopped."
