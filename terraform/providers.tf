provider "proxmox" {
  endpoint  = var.proxmox_api_url
  api_token = var.proxmox_api_token
  insecure  = var.proxmox_tls_insecure

  # Used by the provider for file uploads (e.g. snippets). An SSH key for the
  # Proxmox host must be loaded in your agent (or use ssh { password }).
  ssh {
    agent    = true
    username = var.proxmox_ssh_user
  }
}

provider "unifi" {
  username       = var.unifi_username
  password       = var.unifi_password
  api_url        = var.unifi_api_url
  site           = var.unifi_site
  allow_insecure = true
}
