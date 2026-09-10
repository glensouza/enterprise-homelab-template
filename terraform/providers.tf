provider "proxmox" {
  endpoint = var.proxmox_api_url
  insecure = var.proxmox_tls_insecure

  # root@pam password (ticket auth), not an API token - see variables.tf's
  # proxmox_password description for why. The provider's own docs confirm
  # auth is all-or-nothing (api_token, if set, takes precedence over
  # username/password), so this is deliberately the only credential here.
  username = "root@pam"
  password = var.proxmox_password

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
