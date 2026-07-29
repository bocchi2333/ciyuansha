param(
    [int]$Port = 24567
)

$ErrorActionPreference = "Stop"

$addresses = Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object {
        $_.IPAddress -ne "127.0.0.1" `
            -and $_.PrefixOrigin -ne "WellKnown" `
            -and $_.IPAddress -notlike "169.254.*"
    } |
    Sort-Object InterfaceAlias, IPAddress

Write-Host "CiyuanSha LAN playtest info"
Write-Host "Port: $Port"
Write-Host ""
Write-Host "Possible LAN IP addresses:"
foreach ($address in $addresses) {
    Write-Host "- $($address.IPAddress)  [$($address.InterfaceAlias)]"
}

Write-Host ""
Write-Host "Give your friend one of these addresses plus the port if needed."
Write-Host "Example: 192.168.1.23"
Write-Host "Example with port: 192.168.1.23:$Port"
Write-Host ""
Write-Host "If your friend cannot join:"
Write-Host "1. Confirm both computers are on the same Wi-Fi/LAN/hotspot."
Write-Host "2. Confirm Windows network profile is Private on the host."
Write-Host "3. Run PrepareLanFirewall.ps1 as Administrator on the host."
