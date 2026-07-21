# UniFi VLANs + LAN IN firewall rules — mirrors docs/05-unifi-network-isolation.md.
# Rule order matters and is expressed with ascending rule_index values.
# Keep this file in sync with that document.

# -----------------------------------------------------------------------------
# 1. Virtual networks (DHCP disabled — all LXCs use static IPs per docs/05)
# -----------------------------------------------------------------------------
resource "unifi_network" "vlan10" {
  name          = "Web-Ingress"
  purpose       = "corporate"
  subnet        = "10.10.10.1/24"
  vlan_id       = 10
  dhcp_enabled  = false
  network_group = "LAN"
}

resource "unifi_network" "vlan20" {
  name          = "Backend-Data"
  purpose       = "corporate"
  subnet        = "10.10.20.1/24"
  vlan_id       = 20
  dhcp_enabled  = false
  network_group = "LAN"
}

resource "unifi_network" "vlan30" {
  name          = "Management"
  purpose       = "corporate"
  subnet        = "10.10.30.1/24"
  vlan_id       = 30
  dhcp_enabled  = false
  network_group = "LAN"
}

# -----------------------------------------------------------------------------
# 2. Firewall rules (LAN IN, evaluated top-to-bottom)
# -----------------------------------------------------------------------------
locals {
  synology_nas = "10.10.10.90"
}

resource "unifi_firewall_rule" "web_to_postgres" {
  name           = "Allow Web -> PostgreSQL (5432)"
  action         = "accept"
  ruleset        = "LAN_IN"
  rule_index     = 2000
  protocol       = "tcp"
  src_network_id = unifi_network.vlan10.id
  dst_address    = "10.10.20.110"
  dst_port       = "5432"
  enabled        = true
}

resource "unifi_firewall_rule" "web_to_garnet" {
  name           = "Allow Web -> Garnet (6379)"
  action         = "accept"
  ruleset        = "LAN_IN"
  rule_index     = 2001
  protocol       = "tcp"
  src_network_id = unifi_network.vlan10.id
  dst_address    = "10.10.20.111"
  dst_port       = "6379"
  enabled        = true
}

resource "unifi_firewall_rule" "web_to_rabbitmq" {
  name           = "Allow Web -> RabbitMQ (5672)"
  action         = "accept"
  ruleset        = "LAN_IN"
  rule_index     = 2002
  protocol       = "tcp"
  src_network_id = unifi_network.vlan10.id
  dst_address    = "10.10.20.112"
  dst_port       = "5672"
  enabled        = true
}

resource "unifi_firewall_rule" "mgmt_to_nas" {
  name           = "Allow Management -> Synology NAS"
  action         = "accept"
  ruleset        = "LAN_IN"
  rule_index     = 2003
  protocol       = "all"
  src_network_id = unifi_network.vlan30.id
  dst_address    = local.synology_nas
  enabled        = true
}

resource "unifi_firewall_rule" "data_to_nas_nfs" {
  name           = "Allow Data -> Synology NAS (NFS)"
  action         = "accept"
  ruleset        = "LAN_IN"
  rule_index     = 2004
  protocol       = "tcp_udp"
  src_network_id = unifi_network.vlan20.id
  dst_address    = local.synology_nas
  dst_port       = "111,2049"
  enabled        = true
}

resource "unifi_firewall_rule" "drop_web_to_data" {
  name           = "Drop Web -> Data (all other)"
  action         = "drop"
  ruleset        = "LAN_IN"
  rule_index     = 2005
  protocol       = "all"
  src_network_id = unifi_network.vlan10.id
  dst_network_id = unifi_network.vlan20.id
  enabled        = true
}

resource "unifi_firewall_rule" "drop_web_to_mgmt" {
  name           = "Drop Web -> Management"
  action         = "drop"
  ruleset        = "LAN_IN"
  rule_index     = 2006
  protocol       = "all"
  src_network_id = unifi_network.vlan10.id
  dst_network_id = unifi_network.vlan30.id
  enabled        = true
}

resource "unifi_firewall_rule" "mgmt_to_any" {
  name           = "Allow Management -> Any"
  action         = "accept"
  ruleset        = "LAN_IN"
  rule_index     = 2007
  protocol       = "all"
  src_network_id = unifi_network.vlan30.id
  enabled        = true
}
