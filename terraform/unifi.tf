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
#    "Internal" zone (no custom unifi_firewall_zone needed) — policies below
#    are scoped by IP/CIDR rather than network_id (see note further down).
#
#    Every policy requires an explicit zone_id on BOTH source and destination
#    — confirmed live: the controller rejects "zoneId must not be null" even
#    when network_id/ips is already present, and even when a destination
#    block is omitted entirely (mgmt_to_any). network_id/ips alone is never
#    enough. The `port` field also rejects a comma-separated list ("111,2049",
#    "80,443") — "policyendpoint: port must be a valid port or port range" —
#    so a rule needing multiple discrete ports is split into one policy per
#    port (step_ca_to_preview below).
#
#    matching_target must ALSO be set explicitly on every source/destination
#    block ("IP" when ips is set, "ANY" when neither is set) — confirmed live:
#    once zone_id was added everywhere above, every block that relied on the
#    provider's matching_target auto-derivation instead started failing with
#    "Empty firewall policy source/destination network ids".
#
#    network_id (matching_target = "NETWORK") is avoided ENTIRELY below —
#    confirmed live: every single policy using network_id fails with that
#    same "empty network ids" error no matter what, even with matching_target
#    set explicitly, even with a real, valid network_id value (verified via
#    `terraform state show` and the provider's own schema/OpenAPI spec — this
#    is a genuine bug in resnickio/unifi v0.10.2's handling of network_id, not
#    a config mistake on our side). Instead, every VLAN-based source/
#    destination below matches on that VLAN's subnet CIDR via `ips` — a code
#    path that works correctly — which is functionally identical for a
#    single-VLAN-per-subnet layout like this one.
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
  synology_nas      = "10.10.10.90"
  internal_zone_id  = data.unifi_firewall_zone.internal.id
  vlan110_cidr      = "10.10.110.0/24"
  vlan120_cidr      = "10.10.120.0/24"
  vlan130_cidr      = "10.10.130.0/24"
  vlan140_cidr      = "10.10.140.0/24"
}

data "unifi_firewall_zone" "internal" {
  name = "Internal"
}

resource "unifi_firewall_policy" "web_to_postgres" {
  name     = "Allow Web -> PostgreSQL (5432)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    zone_id         = local.internal_zone_id
    ips             = [local.vlan110_cidr]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = ["10.10.120.110"]
    port            = "5432"
    matching_target = "IP"
  }
  enabled = true
}

resource "unifi_firewall_policy" "web_to_garnet" {
  name     = "Allow Web -> Garnet (6379)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    zone_id         = local.internal_zone_id
    ips             = [local.vlan110_cidr]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = ["10.10.120.111"]
    port            = "6379"
    matching_target = "IP"
  }
  enabled = true
}

resource "unifi_firewall_policy" "web_to_rabbitmq" {
  name     = "Allow Web -> RabbitMQ (5672)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    zone_id         = local.internal_zone_id
    ips             = [local.vlan110_cidr]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = ["10.10.120.112"]
    port            = "5672"
    matching_target = "IP"
  }
  enabled = true
}

resource "unifi_firewall_policy" "web_to_infisical" {
  # Confirmed live: the Infisical Agent on both web LXCs timed out dialing
  # 10.10.130.116:8080 - drop_web_to_mgmt below blocks all of VLAN 110 ->
  # VLAN 130 by default, and no exception for Infisical existed yet because
  # nothing on that LXC was actually reachable until ADR 26 installed it.
  name     = "Allow Web -> Infisical (8080)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    zone_id         = local.internal_zone_id
    ips             = [local.vlan110_cidr]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = ["10.10.130.116"]
    port            = "8080"
    matching_target = "IP"
  }
  enabled = true
}

resource "unifi_firewall_policy" "web_to_homepage" {
  # ADR 47: exposing Homepage via the Cloudflare Tunnel means the cloudflared
  # connector (VLAN 110) must reach Homepage (10.10.130.120:3000) directly -
  # drop_web_to_mgmt below blocks all of VLAN 110 -> VLAN 130 by default, and
  # no exception for Homepage existed yet since it was never meant to be
  # reachable from VLAN 110 before this. Same class of gap as
  # web_to_infisical/web_to_patchmon above (ADR 29/37).
  name     = "Allow Web -> Homepage (3000)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    zone_id         = local.internal_zone_id
    ips             = [local.vlan110_cidr]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = ["10.10.130.120"]
    port            = "3000"
    matching_target = "IP"
  }
  enabled = true
}

resource "unifi_firewall_policy" "web_to_patchmon" {
  # Confirmed live: a manual curl from blazor-web-01 (now blazor-web-04,
  # renamed docs/01 ADR 50; same node, same IP) to
  # 10.10.130.122:3000/api/v1/auto-enrollment/enroll hung and timed out
  # (exit 124) rather than failing fast - drop_web_to_mgmt below blocks all
  # of VLAN 110 -> VLAN 130 by default, and no exception for PatchMon
  # existed yet, same class of gap as web_to_infisical above (ADR 29).
  name     = "Allow Web -> PatchMon (3000)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    zone_id         = local.internal_zone_id
    ips             = [local.vlan110_cidr]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = ["10.10.130.122"]
    port            = "3000"
    matching_target = "IP"
  }
  enabled = true
}

# ADR 40: pve1-4_to_nas (all four) were removed entirely, not just pve1/2.
# Tested live: applied with pve3_to_nas/pve4_to_nas also absent, and
# postgresql's data volume and blazor-web-01/02's (now -04/-03, ADR 50)
# media share (both
# confirmed active/mounted post-apply) needed no explicit rule at all.
# pve1-4 and the NAS all sit on the same pre-existing 10.10.10.0/24 LAN
# segment — same-subnet traffic never crosses the gateway's routing/
# firewall boundary in the first place, so a zone policy for it was never
# going to do anything either way. (Also: the actual mount is CIFS now, not
# NFS — see the removed data_to_nas_nfs* policies above — so even the
# original port-111/2049 rules were already stale before this cleanup.)
# The "access denied by server" that originally motivated these four rules
# was almost certainly a NAS-side share-permission issue, not a firewall
# one.

resource "unifi_firewall_policy" "drop_web_to_data" {
  name     = "Drop Web -> Data (all other)"
  action   = "BLOCK"
  protocol = "all"
  source = {
    zone_id         = local.internal_zone_id
    ips             = [local.vlan110_cidr]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = [local.vlan120_cidr]
    matching_target = "IP"
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
    zone_id         = local.internal_zone_id
    ips             = [local.vlan110_cidr]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = [local.vlan130_cidr]
    matching_target = "IP"
  }
  enabled = true
  # Must be evaluated after the specific allows above, or they'd never match.
  depends_on = [
    unifi_firewall_policy.web_to_infisical,
    unifi_firewall_policy.web_to_patchmon,
    unifi_firewall_policy.web_to_homepage,
  ]
}

resource "unifi_firewall_policy" "mgmt_to_any" {
  name     = "Allow Management -> Any"
  action   = "ALLOW"
  protocol = "all"
  source = {
    zone_id         = local.internal_zone_id
    ips             = [local.vlan130_cidr]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    matching_target = "ANY"
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
    zone_id         = local.internal_zone_id
    ips             = [local.vlan140_cidr]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = ["10.10.130.119"]
    port            = "53"
    matching_target = "IP"
  }
  enabled = true
}

resource "unifi_firewall_policy" "preview_to_step_ca" {
  name     = "Allow Preview -> step-ca ACME (4443)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    zone_id         = local.internal_zone_id
    ips             = [local.vlan140_cidr]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = ["10.10.130.121"]
    port            = "4443"
    matching_target = "IP"
  }
  enabled = true
}

# Split into one policy per port — the API rejects a comma-separated port list.
resource "unifi_firewall_policy" "step_ca_to_preview_http" {
  # ACME HTTP-01 validation: the CA must reach the preview host.
  name     = "Allow step-ca -> Preview (80)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    zone_id         = local.internal_zone_id
    ips             = ["10.10.130.121"]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = [local.vlan140_cidr]
    port            = "80"
    matching_target = "IP"
  }
  enabled = true
}

resource "unifi_firewall_policy" "step_ca_to_preview" {
  # ACME TLS-ALPN-01 validation: the CA must reach the preview host.
  name     = "Allow step-ca -> Preview (443)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    zone_id         = local.internal_zone_id
    ips             = ["10.10.130.121"]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = [local.vlan140_cidr]
    port            = "443"
    matching_target = "IP"
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
    zone_id         = local.internal_zone_id
    ips             = ["10.10.140.120"]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = ["10.10.120.110"]
    port            = "5432"
    matching_target = "IP"
  }
  enabled = true
}

resource "unifi_firewall_policy" "preview_to_garnet" {
  name     = "Allow Preview -> Garnet (6379, RedisInsight)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    zone_id         = local.internal_zone_id
    ips             = ["10.10.140.120"]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = ["10.10.120.111"]
    port            = "6379"
    matching_target = "IP"
  }
  enabled = true
}

# ADR 41: pr-preview now pushes pgAdmin's login to Infisical (preview-host
# role), the first thing on VLAN140 that's ever needed to reach Infisical -
# confirmed live this hung/failed exactly like the PatchMon gap (ADR 40)
# until this was added. Same class of gap, same fix.
resource "unifi_firewall_policy" "preview_to_infisical" {
  name     = "Allow Preview -> Infisical (8080)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    zone_id         = local.internal_zone_id
    ips             = ["10.10.140.120"]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = ["10.10.130.116"]
    port            = "8080"
    matching_target = "IP"
  }
  enabled = true
}

# Every host in the fleet needs to reach PatchMon to enroll and report
# package-update status (ADR 34) - VLAN110 already has this (web_to_patchmon,
# ADR 37) and VLAN120 falls through to the same-zone default-allow (no drop
# rule exists for data -> mgmt), but VLAN140 is fully dropped by
# drop_preview_to_mgmt below with no exception carved out yet. Confirmed
# live: this exact gap is what made the fleet-wide PatchMon enrollment play
# hang on pr-preview and stall the whole CI run (ADR 39's investigation).
resource "unifi_firewall_policy" "preview_to_patchmon" {
  name     = "Allow Preview -> PatchMon (3000)"
  action   = "ALLOW"
  protocol = "tcp"
  source = {
    zone_id         = local.internal_zone_id
    ips             = ["10.10.140.120"]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = ["10.10.130.122"]
    port            = "3000"
    matching_target = "IP"
  }
  enabled = true
}

resource "unifi_firewall_policy" "drop_preview_to_web" {
  name     = "Drop Preview -> Web"
  action   = "BLOCK"
  protocol = "all"
  source = {
    zone_id         = local.internal_zone_id
    ips             = [local.vlan140_cidr]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = [local.vlan110_cidr]
    matching_target = "IP"
  }
  enabled = true
}

resource "unifi_firewall_policy" "drop_preview_to_data" {
  name     = "Drop Preview -> Data (all other)"
  action   = "BLOCK"
  protocol = "all"
  source = {
    zone_id         = local.internal_zone_id
    ips             = [local.vlan140_cidr]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = [local.vlan120_cidr]
    matching_target = "IP"
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
    zone_id         = local.internal_zone_id
    ips             = [local.vlan140_cidr]
    matching_target = "IP"
  }
  destination = {
    zone_id         = local.internal_zone_id
    ips             = [local.vlan130_cidr]
    matching_target = "IP"
  }
  enabled = true
  depends_on = [
    unifi_firewall_policy.preview_to_dns,
    unifi_firewall_policy.preview_to_step_ca,
    unifi_firewall_policy.preview_to_patchmon,
    unifi_firewall_policy.preview_to_infisical,
  ]
}
