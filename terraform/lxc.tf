# Master Infrastructure Matrix — the single source of truth mirrors
# docs/04-lxc-provisioning.md. Keep IPs/VLANs/resources in sync with that
# document and the CLAUDE.md topology matrix when you change them.
#
# This PR exists to get a reviewed terraform-plan.yml run/artifact for the
# blazor-web-01/02 -> -04/-03 rename (docs/01 ADR 50) and the ADR 48 memory
# bump, which landed via a direct push to main (ADR 22's normal PR gate was
# bypassed this session) - so terraform-apply.yml has a real plan_run_id to
# apply against instead of a manual SSH-run plan.
locals {
  lxcs = {
    # VMID convention: hundreds digit = the Proxmox node hosting it (1xx = pve1,
    # never used here since pve1 hosts no Terraform-managed LXC; 3xx = pve3;
    # 4xx = pve4). Last two digits preserved from the pre-renumbering IDs so
    # they're still recognizable. See LAB-RUNBOOK.md's DevOps LXC section for
    # why pve1 itself uses VMID 100.

    # VLAN 110 — Web / Ingress tier
    # blazor-web-03/04 are privileged and bind-mount the NAS media share from
    # the Proxmox HOST (mount_point below). Two confirmed-live findings drove
    # this: (1) unprivileged LXCs cannot mount NFS in-guest at all, feature
    # flag or not; (2) a bind mount_point on an UNPRIVILEGED container gets
    # its permissions masked to 0000/nobody:nogroup inside the guest - a
    # kernel user-namespace safety behavior for mounts made outside that
    # namespace, not a permissions/ownership mistake on the NAS side.
    # Privileged containers have no separate user namespace, so neither
    # limitation applies. pve3/pve4 each mount
    # 10.10.10.90:/volume1/homelab-media at /mnt/homelab-media (host-level
    # /etc/fstab, not Terraform-managed).
    # memory/swap bumped from 1024/0 (2026-09-18) after the node on pve4
    # wedged solid under load (2-core/1GB, load average 16-26, unresponsive
    # to SSH and even `pct exec`) - see docs/01 ADR 48. 1GB of swap is
    # breathing room for GC/JIT spikes, not steady-state usage; root cause
    # (OOM vs. CPU contention) was never confirmed via logs - still worth
    # investigating rather than relying on swap alone.
    #
    # Renamed 2026-09-19 (blazor-web-01/02 -> -04/-03, docs/01 ADR 50): the
    # old names had 01 on pve4 and 02 on pve3 - backwards from the VMID
    # convention above (hundreds digit = node) and confusing to read at a
    # glance. -04 keeps VMID 401 (pve4) unchanged - a pure Terraform-key
    # rename via the `moved` block below, no container destroyed. -03 takes
    # a fresh VMID 301 (was 302) to match pve3 - this one genuinely replaces
    # the container (vm_id is immutable); safe because -04 stays live on the
    # other node throughout, same as any other rolling change to this pair.
    blazor-web-04 = { vm_id = 401, node = var.proxmox_node_1, ip = "10.10.110.101/24", gateway = "10.10.110.1", vlan = 110, cores = 2, memory = 2048, swap = 1024, disk = 8, tags = ["terraform", "vlan110", "web"], privileged = true, mount_point = { volume = "/mnt/homelab-media", path = "/mnt/synology/media" } }
    blazor-web-03 = { vm_id = 301, node = var.proxmox_node_2, ip = "10.10.110.102/24", gateway = "10.10.110.1", vlan = 110, cores = 2, memory = 2048, swap = 1024, disk = 8, tags = ["terraform", "vlan110", "web"], privileged = true, mount_point = { volume = "/mnt/homelab-media", path = "/mnt/synology/media" } }
    cloudflared   = { vm_id = 405, node = var.proxmox_node_1, ip = "10.10.110.5/24", gateway = "10.10.110.1", vlan = 110, cores = 1, memory = 512, disk = 4, tags = ["terraform", "vlan110", "ingress"] }

    # VLAN 120 — Backend / Data tier (pve4 Primary)
    # postgresql: privileged, same reasons as blazor-web-03/04 above. pve4
    # mounts 10.10.10.90:/volume1/homelab-postgres-data at
    # /mnt/homelab-postgres-data (host-level, not Terraform-managed).
    postgresql = { vm_id = 410, node = var.proxmox_node_1, ip = "10.10.120.110/24", gateway = "10.10.120.1", vlan = 120, cores = 4, memory = 4096, disk = 40, tags = ["terraform", "vlan120", "data"], privileged = true, mount_point = { volume = "/mnt/homelab-postgres-data", path = "/mnt/synology/postgres-data" } }
    garnet     = { vm_id = 411, node = var.proxmox_node_1, ip = "10.10.120.111/24", gateway = "10.10.120.1", vlan = 120, cores = 2, memory = 2048, disk = 8, tags = ["terraform", "vlan120", "data"] }
    rabbitmq   = { vm_id = 412, node = var.proxmox_node_1, ip = "10.10.120.112/24", gateway = "10.10.120.1", vlan = 120, cores = 1, memory = 1024, disk = 8, tags = ["terraform", "vlan120", "data"] }

    # VLAN 130 — Management / Infrastructure tier (Infisical back-office portal on pve4 Primary)
    infisical      = { vm_id = 416, node = var.proxmox_node_1, ip = "10.10.130.116/24", gateway = "10.10.130.1", vlan = 130, cores = 2, memory = 1536, disk = 16, tags = ["terraform", "vlan130", "mgmt"] }
    uptime-kuma    = { vm_id = 317, node = var.proxmox_node_2, ip = "10.10.130.117/24", gateway = "10.10.130.1", vlan = 130, cores = 1, memory = 512, disk = 8, tags = ["terraform", "vlan130", "mgmt"] }
    observability  = { vm_id = 318, node = var.proxmox_node_2, ip = "10.10.130.118/24", gateway = "10.10.130.1", vlan = 130, cores = 2, memory = 2048, disk = 32, tags = ["terraform", "vlan130", "mgmt"] }
    technitium-dns = { vm_id = 319, node = var.proxmox_node_2, ip = "10.10.130.119/24", gateway = "10.10.130.1", vlan = 130, cores = 1, memory = 512, disk = 8, tags = ["terraform", "vlan130", "mgmt", "dns"] }
    step-ca        = { vm_id = 321, node = var.proxmox_node_2, ip = "10.10.130.121/24", gateway = "10.10.130.1", vlan = 130, cores = 1, memory = 512, disk = 8, tags = ["terraform", "vlan130", "mgmt", "pki"] }
    patchmon       = { vm_id = 322, node = var.proxmox_node_2, ip = "10.10.130.122/24", gateway = "10.10.130.1", vlan = 130, cores = 1, memory = 1024, disk = 8, tags = ["terraform", "vlan130", "mgmt"] }
    # ADR 38: Homepage dashboard. cores/memory match gethomepage's own
    # community-scripts installer defaults (2 vCPU / 4GB recommended for the
    # pnpm/Next.js build step) trimmed to 2GB - this host only builds once
    # per version bump, not on every boot.
    homepage       = { vm_id = 323, node = var.proxmox_node_2, ip = "10.10.130.120/24", gateway = "10.10.130.1", vlan = 130, cores = 2, memory = 2048, disk = 8, tags = ["terraform", "vlan130", "mgmt"] }

    # VLAN 140 — Non-Prod Single Docker Host tier (pve4 Primary)
    pr-preview = { vm_id = 420, node = var.proxmox_node_1, ip = "10.10.140.120/24", gateway = "10.10.140.1", vlan = 140, cores = 2, memory = 4096, disk = 60, tags = ["terraform", "vlan140", "preview"] }
  }
}

resource "proxmox_virtual_environment_container" "lxc" {
  for_each = local.lxcs

  node_name   = each.value.node
  vm_id       = each.value.vm_id
  description = "Managed by Terraform (terraform/lxc.tf) — do not edit in the GUI."
  tags        = each.value.tags
  # Unprivileged by default (least privilege). A handful of LXCs opt into
  # privileged = true above - see the notes on blazor-web-03/04/postgresql.
  unprivileged  = !try(each.value.privileged, false)
  started       = true
  start_on_boot = true

  initialization {
    hostname = each.key

    ip_config {
      ipv4 {
        address = each.value.ip
        gateway = each.value.gateway
      }
    }

    user_account {
      keys = [trimspace(var.ssh_public_key)]
    }
  }

  network_interface {
    name    = "eth0"
    bridge  = "vmbr0"
    vlan_id = each.value.vlan
  }

  cpu {
    cores = each.value.cores
  }

  memory {
    dedicated = each.value.memory
    swap      = try(each.value.swap, null)
  }

  disk {
    datastore_id = var.lxc_datastore
    size         = each.value.disk
  }

  operating_system {
    template_file_id = var.debian_template_id
    type             = "debian"
  }

  dynamic "mount_point" {
    for_each = try([each.value.mount_point], [])
    content {
      volume = mount_point.value.volume
      path   = mount_point.value.path
    }
  }

  features {
    nesting = true
    # No `mount = ["nfs"]` here: confirmed live that in-guest NFS mounting
    # inside unprivileged LXCs doesn't work at all, feature flag or not
    # (kernel/namespace limitation, not a config gap) - NFS access for
    # web/postgres is solved via the host-mount + mount_point bind-mount
    # above instead. Every non-nesting features attribute (and privileged
    # containers, and bind-type mount points) requires root@pam - hence
    # providers.tf's password auth instead of an API token.
  }

  lifecycle {
    # Root SSH keys are rotated out-of-band; service stop/start belongs to ops.
    ignore_changes = [initialization[0].user_account, started]
  }
}

# docs/01 ADR 50: for_each key renames need an explicit `moved` block each,
# or Terraform sees an unrelated destroy (old key) + create (new key) pair
# instead of one tracked address rename. blazor-web-01 -> blazor-web-04
# keeps the same VMID (401), so `moved` makes this a true no-op rename -
# nothing about the container itself changes. blazor-web-02 -> blazor-web-03
# ALSO gets a `moved` block even though its VMID changes too (302 -> 301,
# vm_id forces replacement) - `moved` doesn't prevent that replacement, it
# just keeps the plan showing one lineage-tracked "replace" at the renamed
# address instead of two disconnected, unordered create/destroy entries.
moved {
  from = proxmox_virtual_environment_container.lxc["blazor-web-01"]
  to   = proxmox_virtual_environment_container.lxc["blazor-web-04"]
}

moved {
  from = proxmox_virtual_environment_container.lxc["blazor-web-02"]
  to   = proxmox_virtual_environment_container.lxc["blazor-web-03"]
}
