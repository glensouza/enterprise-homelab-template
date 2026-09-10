provider "proxmox" {
  endpoint  = var.proxmox_api_url
  api_token = var.proxmox_api_token
  insecure  = var.proxmox_tls_insecure

  # Used by the provider for file uploads (e.g. snippets). An explicit key
  # file (not ssh-agent) since this runs unattended from the devops LXC's
  # systemd-managed GitHub Actions runner service (ADR 22) — no interactive
  # session to hold an agent socket open. Same key pair Ansible uses against
  # the LXC guests; the Proxmox hosts (pve1/pve3/pve4) additionally need this
  # public key in root's authorized_keys (LAB-RUNBOOK.md's DevOps LXC step).
  ssh {
    private_key = file(pathexpand("~/.ssh/brewhouse_ansible"))
    username    = var.proxmox_ssh_user
  }
}

provider "unifi" {
  username = var.unifi_username
  password = var.unifi_password
  base_url = var.unifi_api_url
  site     = var.unifi_site
  insecure = true
}
