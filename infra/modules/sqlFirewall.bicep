// One SQL firewall rule per outbound IPv4 address of the Web App.
// A separate module because the address list is only known once the Web App exists.

@description('Name of the existing Azure SQL logical server.')
param sqlServerName string

@description('Comma-separated outbound addresses of the Web App (possibleOutboundIpAddresses).')
param outboundIpAddresses string

// Azure SQL firewall rules are IPv4 only; skip any IPv6 entries.
var ipv4Addresses = filter(split(outboundIpAddresses, ','), ip => !contains(ip, ':') && !empty(ip))

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' existing = {
  name: sqlServerName
}

resource rules 'Microsoft.Sql/servers/firewallRules@2023-08-01' = [
  for (ip, i) in ipv4Addresses: {
    parent: sqlServer
    name: 'webapp-outbound-${padLeft(i, 2, '0')}'
    properties: {
      startIpAddress: ip
      endIpAddress: ip
    }
  }
]

output ruleCount int = length(ipv4Addresses)
