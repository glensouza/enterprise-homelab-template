# -----------------------------------------------------------------------------
# Proxmox connection
# -----------------------------------------------------------------------------
variable "proxmox_api_url" {
  description = "Proxmox VE API endpoint — point at the cluster master (pve1), e.g. https://10.10.10.101:8006/. The Proxmox API is cluster-aware, so this doesn't need to match proxmox_node_1/proxmox_node_2."
  type        = string
}

variable "proxmox_password" {
  description = "root@pam password. Not an API token: Proxmox hardcodes several container operations (privileged containers, any features flag but nesting, bind-type mount points) to root@pam-only regardless of an API token's assigned role - confirmed live creating/updating blazor-web-01/02 and postgresql, which bind-mount NFS shares from the host (ADR 17). The provider's own docs confirm auth is all-or-nothing (api_token takes precedence over username/password if both are set), so this applies to every Terraform-managed Proxmox resource, not just those three."
  type        = string
  sensitive   = true
}

variable "proxmox_tls_insecure" {
  description = "Skip TLS verification for the Proxmox API (self-signed lab certs)"
  type        = bool
  default     = true
}

variable "proxmox_ssh_user" {
  description = "SSH user on the Proxmox hosts (used by the provider for uploads)"
  type        = string
  default     = "root"
}

# -----------------------------------------------------------------------------
# UniFi connection
# -----------------------------------------------------------------------------
variable "unifi_username" {
  description = "UniFi local admin username (UDM-Pro)"
  type        = string
}

variable "unifi_password" {
  description = "UniFi local admin password"
  type        = string
  sensitive   = true
}

variable "unifi_api_url" {
  description = "UniFi controller URL (UDM-Pro gateway)"
  type        = string
  default     = "https://10.10.10.1"
}

variable "unifi_site" {
  description = "UniFi site name"
  type        = string
  default     = "default"
}

# -----------------------------------------------------------------------------
# LXC shared settings
# -----------------------------------------------------------------------------
variable "proxmox_node_1" {
  description = "Name of Proxmox node 1 (Primary: pve4 — 8 vCPU / 16 GB RAM — 10.10.10.104). Distinct from the cluster master pve1, which never hosts an LXC."
  type        = string
  default     = "pve4"
}

variable "proxmox_node_2" {
  description = "Name of Proxmox node 2 (Secondary: pve3 — 4 vCPU / 8 GB RAM — 10.10.10.103). Distinct from the cluster master pve1, which never hosts an LXC."
  type        = string
  default     = "pve3"
}

variable "lxc_datastore" {
  description = "Proxmox datastore for LXC root disks (local NVMe/SSD per ADR 02). Confirmed as plain 'local' (not 'local-lvm') on pve1 — verify pve3/pve4 match via `pvesm status` before the first real terraform apply, and override in tfvars/repo variables if either differs."
  type        = string
  default     = "local"
}

variable "debian_template_id" {
  description = "Container template for all LXCs, e.g. 'local:vztmpl/debian-12-standard_12.7-1_amd64.tar.zst' (upload once per node storage)"
  type        = string
}

variable "ssh_public_key" {
  description = "Public key installed as root's authorized_keys in every LXC (Ansible connects with the matching private key)"
  type        = string
}
