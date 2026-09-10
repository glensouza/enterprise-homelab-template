# UniFi VLANs + zone-based firewall policies — mirrors docs/05-unifi-network-isolation.md.
# Keep this file in sync with that document.

# -----------------------------------------------------------------------------
# 1. Virtual networks (DHCP disabled — all LXCs use static IPs per docs/05)
# -----------------------------------------------------------------------------
# dhcp_boot_enabled/dhcp_gateway_enabled/dhcp_guarding_enabled/dhcp_ntp_enabled/
# dhcp_relay_enabled/dhcp_time_offset_enabled are set explicitly (matching what
# the controller actually returns) because leaving them unset triggers
# "Provider produced inconsistent result after apply" - the provider treats
# them as Optional+Computed but returns a concrete `false` instead of null,
# which Terraform's consistency check rejects when the plan left them null.
resource "unifi_network" "vlan110" {
  name                     = "Web-Ingress"
  purpose                  = "corporate"
  subnet                   = "10.10.110.1/24"
  vlan_id                  = 110
  dhcp_enabled             = false
  network_group            = "LAN"
  dhcp_boot_enabled        = false
  dhcp_gateway_enabled     = false
  dhcp_guarding_enabled    = false
  dhcp_ntp_enabled         = false
  dhcp_relay_enabled       = false
  dhcp_time_offset_enabled = false
}

resource "unifi_network" "vlan120" {
  name                     = "Backend-Data"
  purpose                  = "corporate"
  subnet                   = "10.10.120.1/24"
  vlan_id                  = 120
  dhcp_enabled             = false
  network_group            = "LAN"
  dhcp_boot_enabled        = false
  dhcp_gateway_enabled     = false
  dhcp_guarding_enabled    = false
  dhcp_ntp_enabled         = false
  dhcp_relay_enabled       = false
  dhcp_time_offset_enabled = false
}

resource "unifi_network" "vlan130" {
  name                     = "Management"
  purpose                  = "corporate"
  subnet                   = "10.10.130.1/24"
  vlan_id                  = 130
  dhcp_enabled             = false
  network_group            = "LAN"
  dhcp_boot_enabled        = false
  dhcp_gateway_enabled     = false
  dhcp_guarding_enabled    = false
  dhcp_ntp_enabled         = false
  dhcp_relay_enabled       = false
  dhcp_time_offset_enabled = false
}

resource "unifi_network" "vlan140" {
  name                     = "NonProd-Preview"
  purpose                  = "corporate"
  subnet                   = "10.10.140.1/24"
  vlan_id                  = 140
  dhcp_enabled             = false
  network_group            = "LAN"
  dhcp_boot_enabled        = false
  dhcp_gateway_enabled     = false
  dhcp_guarding_enabled    = false
  dhcp_ntp_enabled         = false
  dhcp_relay_enabled       = false
  dhcp_time_offset_enabled = false
}

# -----------------------------------------------------------------------------
# 2. Firewall policies (zone-based firewall, UniFi Network 9.0+/your controller
#    is 10.6.101 — the legacy unifi_firewall_rule/LAN_IN/rule_index model is
#    rejected outright by the controller's API on this version, hence the
#    resnickio/unifi provider and unifi_firewall_policy resource instead of
#    paultyng/unifi's unifi_firewall_rule. All four VLANs stay in the default
#    "Internal" zone (no custom unifi_firewall_zone needed) — policies are
#    scoped by network_id/ip, same granularity as the old rules.
#
#    IMPORTANT — evaluation order is no longer a settable `rule_index`; it's
#    a controller-assigned, read-only `index`. The `depends_on` below on each
#    catch-all BLOCK policy forces Terraform to create the specific ALLOW
#    exceptions first, on the assumption the controller appends new policies
#    in creation order (newest = evaluated last within a zone pair). This is
#    based on provider docs, not yet confirmed against this controller —
#    after applying, verify actual order in the UniFi GUI (Settings ->
#    Firewall & Security -> the Internal zone's policy list) before trusting
#    it in production.
# -----------------------------------------------------------------------------
locals {
  synology_nas = "10.10.10.90"
}

# The controller can infer a policy's zone when at least one side references a
# network_id, but when BOTH source and destination are bare IPs (no network_id
# anywhere in the rule), it has no way to infer the zone and rejects the policy
# with "zoneId must not be null" — confirmed live. Those rules need this
# explicit lookup of the built-in "Internal" zone every one of our VLANs
# belongs to (we never created custom zones).
data "unifi_firewall_zone" "internal" {
  name = "Internal"
}

resource "unifi_firewall_policy" "web_to_postgres" {
  name     = "Allow Web -> PostgreSQL (5432)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    network_id = unifi_network.vlan110.id
  }
  destination = {
    ips  = ["10.10.120.110"]
    port = "5432"
  }
  enabled = true
}

resource "unifi_firewall_policy" "web_to_garnet" {
  name     = "Allow Web -> Garnet (6379)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    network_id = unifi_network.vlan110.id
  }
  destination = {
    ips  = ["10.10.120.111"]
    port = "6379"
  }
  enabled = true
}

resource "unifi_firewall_policy" "web_to_rabbitmq" {
  name     = "Allow Web -> RabbitMQ (5672)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    network_id = unifi_network.vlan110.id
  }
  destination = {
    ips  = ["10.10.120.112"]
    port = "5672"
  }
  enabled = true
}

resource "unifi_firewall_policy" "mgmt_to_nas" {
  name     = "Allow Management -> Synology NAS"
  action   = "ALLOW"
  protocol = "all"
  source = {
    network_id = unifi_network.vlan130.id
  }
  destination = {
    ips = [local.synology_nas]
  }
  enabled = true
}

# All four Proxmox cluster members (pve1, pve2, pve3, pve4) are pre-existing hardware on the
# existing 10.10.10.0/24 LAN, not on VLAN 130 — the policy above never covers their
# vzdump/shared-storage traffic to the NAS. Proxmox mounts cluster-wide storage on every node
# regardless of which two (pve3/pve4) actually host LXCs, so each host gets its own explicit
# allow — confirmed live: the NFS storage add failed with "access denied by server" until pve1
# and pve2 were both allowed through on the NAS side too.
resource "unifi_firewall_policy" "pve1_to_nas" {
  name     = "Allow Proxmox pve1 -> Synology NAS"
  action   = "ALLOW"
  protocol = "all"
  source = {
    zone_id = data.unifi_firewall_zone.internal.id
    ips     = ["10.10.10.101"]
  }
  destination = {
    zone_id = data.unifi_firewall_zone.internal.id
    ips     = [local.synology_nas]
  }
  enabled = true
}

resource "unifi_firewall_policy" "pve2_to_nas" {
  name     = "Allow Proxmox pve2 -> Synology NAS"
  action   = "ALLOW"
  protocol = "all"
  source = {
    zone_id = data.unifi_firewall_zone.internal.id
    ips     = ["10.10.10.102"]
  }
  destination = {
    zone_id = data.unifi_firewall_zone.internal.id
    ips     = [local.synology_nas]
  }
  enabled = true
}

resource "unifi_firewall_policy" "pve3_to_nas" {
  name     = "Allow Proxmox pve3 -> Synology NAS"
  action   = "ALLOW"
  protocol = "all"
  source = {
    zone_id = data.unifi_firewall_zone.internal.id
    ips     = ["10.10.10.103"]
  }
  destination = {
    zone_id = data.unifi_firewall_zone.internal.id
    ips     = [local.synology_nas]
  }
  enabled = true
}

resource "unifi_firewall_policy" "pve4_to_nas" {
  name     = "Allow Proxmox pve4 -> Synology NAS"
  action   = "ALLOW"
  protocol = "all"
  source = {
    zone_id = data.unifi_firewall_zone.internal.id
    ips     = ["10.10.10.104"]
  }
  destination = {
    zone_id = data.unifi_firewall_zone.internal.id
    ips     = [local.synology_nas]
  }
  enabled = true
}

resource "unifi_firewall_policy" "data_to_nas_nfs" {
  name     = "Allow Data -> Synology NAS (NFS)"
  action   = "ALLOW"
  protocol = "tcp_udp"
  source = {
    network_id = unifi_network.vlan120.id
  }
  destination = {
    ips  = [local.synology_nas]
    port = "111,2049"
  }
  enabled = true
}

resource "unifi_firewall_policy" "drop_web_to_data" {
  name     = "Drop Web -> Data (all other)"
  action   = "BLOCK"
  protocol = "all"
  source = {
    network_id = unifi_network.vlan110.id
  }
  destination = {
    network_id = unifi_network.vlan120.id
  }
  enabled = true
  # Must be evaluated after the specific allows above, or they'd never match.
  depends_on = [
    unifi_firewall_policy.web_to_postgres,
    unifi_firewall_policy.web_to_garnet,
    unifi_firewall_policy.web_to_rabbitmq,
  ]
}

resource "unifi_firewall_policy" "drop_web_to_mgmt" {
  name     = "Drop Web -> Management"
  action   = "BLOCK"
  protocol = "all"
  source = {
    network_id = unifi_network.vlan110.id
  }
  destination = {
    network_id = unifi_network.vlan130.id
  }
  enabled = true
}

resource "unifi_firewall_policy" "mgmt_to_any" {
  name     = "Allow Management -> Any"
  action   = "ALLOW"
  protocol = "all"
  source = {
    network_id = unifi_network.vlan130.id
  }
  enabled = true
}

# --- VLAN 140 (Non-Prod / Preview) isolation (ADR 19) -------------------------
# The preview tier may only resolve DNS against Technitium and reach the
# step-ca ACME endpoint — it is fully isolated from the production tiers.

resource "unifi_firewall_policy" "preview_to_dns" {
  name     = "Allow Preview -> Technitium DNS (53)"
  action   = "ALLOW"
  protocol = "tcp_udp"
  source = {
    network_id = unifi_network.vlan140.id
  }
  destination = {
    ips  = ["10.10.130.119"]
    port = "53"
  }
  enabled = true
}

resource "unifi_firewall_policy" "preview_to_step_ca" {
  name     = "Allow Preview -> step-ca ACME (4443)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    network_id = unifi_network.vlan140.id
  }
  destination = {
    ips  = ["10.10.130.121"]
    port = "4443"
  }
  enabled = true
}

resource "unifi_firewall_policy" "step_ca_to_preview" {
  # ACME HTTP-01/TLS-ALPN-01 validation: the CA must reach the preview host.
  name     = "Allow step-ca -> Preview (80,443)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    ips = ["10.10.130.121"]
  }
  destination = {
    network_id = unifi_network.vlan140.id
    port       = "80,443"
  }
  enabled = true
}

# Targeted admin-tool access (ADR 21): pgAdmin and RedisInsight run on the
# preview host and must reach the production database/cache. Everything else
# from VLAN 140 to the production tiers remains dropped below.
resource "unifi_firewall_policy" "preview_to_postgres" {
  name     = "Allow Preview -> PostgreSQL (5432, pgAdmin)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    zone_id = data.unifi_firewall_zone.internal.id
    ips     = ["10.10.140.120"]
  }
  destination = {
    zone_id = data.unifi_firewall_zone.internal.id
    ips     = ["10.10.120.110"]
    port    = "5432"
  }
  enabled = true
}

resource "unifi_firewall_policy" "preview_to_garnet" {
  name     = "Allow Preview -> Garnet (6379, RedisInsight)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    zone_id = data.unifi_firewall_zone.internal.id
    ips     = ["10.10.140.120"]
  }
  destination = {
    zone_id = data.unifi_firewall_zone.internal.id
    ips     = ["10.10.120.111"]
    port    = "6379"
  }
  enabled = true
}

resource "unifi_firewall_policy" "drop_preview_to_web" {
  name     = "Drop Preview -> Web"
  action   = "BLOCK"
  protocol = "all"
  source = {
    network_id = unifi_network.vlan140.id
  }
  destination = {
    network_id = unifi_network.vlan110.id
  }
  enabled = true
}

resource "unifi_firewall_policy" "drop_preview_to_data" {
  name     = "Drop Preview -> Data (all other)"
  action   = "BLOCK"
  protocol = "all"
  source = {
    network_id = unifi_network.vlan140.id
  }
  destination = {
    network_id = unifi_network.vlan120.id
  }
  enabled = true
  depends_on = [
    unifi_firewall_policy.preview_to_postgres,
    unifi_firewall_policy.preview_to_garnet,
  ]
}

resource "unifi_firewall_policy" "drop_preview_to_mgmt" {
  name     = "Drop Preview -> Management (all other)"
  action   = "BLOCK"
  protocol = "all"
  source = {
    network_id = unifi_network.vlan140.id
  }
  destination = {
    network_id = unifi_network.vlan130.id
  }
  enabled = true
  depends_on = [
    unifi_firewall_policy.preview_to_dns,
    unifi_firewall_policy.preview_to_step_ca,
  ]
}
