# Master Infrastructure Matrix — the single source of truth mirrors
# docs/04-lxc-provisioning.md. Keep IPs/VLANs/resources in sync with that
# document and the CLAUDE.md topology matrix when you change them.
locals {
  lxcs = {
    # VLAN 10 — Web / Ingress tier
    blazor-web-01 = { vm_id = 101, node = var.proxmox_node_1, ip = "10.10.10.101/24", gateway = "10.10.10.1", vlan = 10, cores = 2, memory = 1024, disk = 8, tags = ["terraform", "vlan10", "web"] }
    blazor-web-02 = { vm_id = 102, node = var.proxmox_node_2, ip = "10.10.10.102/24", gateway = "10.10.10.1", vlan = 10, cores = 2, memory = 1024, disk = 8, tags = ["terraform", "vlan10", "web"] }
    cloudflared   = { vm_id = 105, node = var.proxmox_node_1, ip = "10.10.10.5/24", gateway = "10.10.10.1", vlan = 10, cores = 1, memory = 512, disk = 4, tags = ["terraform", "vlan10", "ingress"] }

    # VLAN 20 — Backend / Data tier
    postgresql = { vm_id = 110, node = var.proxmox_node_1, ip = "10.10.20.110/24", gateway = "10.10.20.1", vlan = 20, cores = 4, memory = 4096, disk = 40, tags = ["terraform", "vlan20", "data"] }
    garnet     = { vm_id = 111, node = var.proxmox_node_1, ip = "10.10.20.111/24", gateway = "10.10.20.1", vlan = 20, cores = 2, memory = 2048, disk = 8, tags = ["terraform", "vlan20", "data"] }
    rabbitmq   = { vm_id = 112, node = var.proxmox_node_1, ip = "10.10.20.112/24", gateway = "10.10.20.1", vlan = 20, cores = 2, memory = 1024, disk = 8, tags = ["terraform", "vlan20", "data"] }

    # VLAN 30 — Management / Infrastructure tier
    infisical      = { vm_id = 116, node = var.proxmox_node_2, ip = "10.10.30.116/24", gateway = "10.10.30.1", vlan = 30, cores = 2, memory = 2048, disk = 16, tags = ["terraform", "vlan30", "mgmt"] }
    uptime-kuma    = { vm_id = 117, node = var.proxmox_node_2, ip = "10.10.30.117/24", gateway = "10.10.30.1", vlan = 30, cores = 1, memory = 1024, disk = 8, tags = ["terraform", "vlan30", "mgmt"] }
    observability  = { vm_id = 118, node = var.proxmox_node_2, ip = "10.10.30.118/24", gateway = "10.10.30.1", vlan = 30, cores = 2, memory = 2048, disk = 32, tags = ["terraform", "vlan30", "mgmt"] }
    technitium-dns = { vm_id = 119, node = var.proxmox_node_2, ip = "10.10.30.119/24", gateway = "10.10.30.1", vlan = 30, cores = 1, memory = 1024, disk = 8, tags = ["terraform", "vlan30", "mgmt", "dns"] }
    step-ca        = { vm_id = 121, node = var.proxmox_node_2, ip = "10.10.30.121/24", gateway = "10.10.30.1", vlan = 30, cores = 1, memory = 512, disk = 8, tags = ["terraform", "vlan30", "mgmt", "pki"] }

    # VLAN 40 — Non-Prod / Preview tier (ADR 19): single server hosting one
    # Docker compose stack per open PR. Nesting is enabled globally above.
    pr-preview = { vm_id = 120, node = var.proxmox_node_2, ip = "10.10.40.120/24", gateway = "10.10.40.1", vlan = 40, cores = 4, memory = 8192, disk = 60, tags = ["terraform", "vlan40", "preview"] }
  }
}

resource "proxmox_virtual_environment_container" "lxc" {
  for_each = local.lxcs

  node_name     = each.value.node
  vm_id         = each.value.vm_id
  description   = "Managed by Terraform (terraform/lxc.tf) — do not edit in the GUI."
  tags          = each.value.tags
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

  features {
    nesting = true
  }

  lifecycle {
    # Root SSH keys are rotated out-of-band; service stop/start belongs to ops.
    ignore_changes = [initialization[0].user_account, started]
  }
}
