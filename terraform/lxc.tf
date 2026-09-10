# Master Infrastructure Matrix — the single source of truth mirrors
# docs/04-lxc-provisioning.md. Keep IPs/VLANs/resources in sync with that
# document and the CLAUDE.md topology matrix when you change them.
locals {
  lxcs = {
    # VMID convention: hundreds digit = the Proxmox node hosting it (1xx = pve1,
    # never used here since pve1 hosts no Terraform-managed LXC; 3xx = pve3;
    # 4xx = pve4). Last two digits preserved from the pre-renumbering IDs so
    # they're still recognizable. See LAB-RUNBOOK.md's DevOps LXC section for
    # why pve1 itself uses VMID 100.

    # VLAN 110 — Web / Ingress tier
    # blazor-web-01/02 bind-mount the NAS media share from the Proxmox HOST
    # (mount_point below) rather than mounting NFS in-guest - confirmed live
    # that unprivileged LXCs cannot mount NFS at all, feature flag or not.
    # pve3/pve4 each mount 10.10.10.90:/volume1/homelab-media at
    # /mnt/homelab-media (host-level /etc/fstab, not Terraform-managed). A
    # raw "bind" mount_point is root@pam-only regardless of an API token's
    # role (confirmed live) - see providers.tf's root@pam password auth.
    blazor-web-01 = { vm_id = 401, node = var.proxmox_node_1, ip = "10.10.110.101/24", gateway = "10.10.110.1", vlan = 110, cores = 2, memory = 1024, disk = 8, tags = ["terraform", "vlan110", "web"], mount_point = { volume = "/mnt/homelab-media", path = "/mnt/synology/media" } }
    blazor-web-02 = { vm_id = 302, node = var.proxmox_node_2, ip = "10.10.110.102/24", gateway = "10.10.110.1", vlan = 110, cores = 2, memory = 1024, disk = 8, tags = ["terraform", "vlan110", "web"], mount_point = { volume = "/mnt/homelab-media", path = "/mnt/synology/media" } }
    cloudflared   = { vm_id = 405, node = var.proxmox_node_1, ip = "10.10.110.5/24", gateway = "10.10.110.1", vlan = 110, cores = 1, memory = 512, disk = 4, tags = ["terraform", "vlan110", "ingress"] }

    # VLAN 120 — Backend / Data tier (pve4 Primary)
    # postgresql bind-mounts the NAS postgres-data share the same way - pve4
    # mounts 10.10.10.90:/volume1/homelab-postgres-data at
    # /mnt/homelab-postgres-data (host-level, not Terraform-managed).
    postgresql = { vm_id = 410, node = var.proxmox_node_1, ip = "10.10.120.110/24", gateway = "10.10.120.1", vlan = 120, cores = 4, memory = 4096, disk = 40, tags = ["terraform", "vlan120", "data"], mount_point = { volume = "/mnt/homelab-postgres-data", path = "/mnt/synology/postgres-data" } }
    garnet     = { vm_id = 411, node = var.proxmox_node_1, ip = "10.10.120.111/24", gateway = "10.10.120.1", vlan = 120, cores = 2, memory = 2048, disk = 8, tags = ["terraform", "vlan120", "data"] }
    rabbitmq   = { vm_id = 412, node = var.proxmox_node_1, ip = "10.10.120.112/24", gateway = "10.10.120.1", vlan = 120, cores = 1, memory = 1024, disk = 8, tags = ["terraform", "vlan120", "data"] }

    # VLAN 130 — Management / Infrastructure tier (Infisical back-office portal on pve4 Primary)
    infisical      = { vm_id = 416, node = var.proxmox_node_1, ip = "10.10.130.116/24", gateway = "10.10.130.1", vlan = 130, cores = 2, memory = 1536, disk = 16, tags = ["terraform", "vlan130", "mgmt"] }
    uptime-kuma    = { vm_id = 317, node = var.proxmox_node_2, ip = "10.10.130.117/24", gateway = "10.10.130.1", vlan = 130, cores = 1, memory = 512, disk = 8, tags = ["terraform", "vlan130", "mgmt"] }
    observability  = { vm_id = 318, node = var.proxmox_node_2, ip = "10.10.130.118/24", gateway = "10.10.130.1", vlan = 130, cores = 2, memory = 2048, disk = 32, tags = ["terraform", "vlan130", "mgmt"] }
    technitium-dns = { vm_id = 319, node = var.proxmox_node_2, ip = "10.10.130.119/24", gateway = "10.10.130.1", vlan = 130, cores = 1, memory = 512, disk = 8, tags = ["terraform", "vlan130", "mgmt", "dns"] }
    step-ca        = { vm_id = 321, node = var.proxmox_node_2, ip = "10.10.130.121/24", gateway = "10.10.130.1", vlan = 130, cores = 1, memory = 512, disk = 8, tags = ["terraform", "vlan130", "mgmt", "pki"] }
    patchmon       = { vm_id = 322, node = var.proxmox_node_2, ip = "10.10.130.122/24", gateway = "10.10.130.1", vlan = 130, cores = 1, memory = 1024, disk = 8, tags = ["terraform", "vlan130", "mgmt"] }

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
  unprivileged  = true
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
