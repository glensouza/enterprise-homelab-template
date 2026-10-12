# Read-only Proxmox API identity for prometheus-pve-exporter (docs/01 ADR 115).
#
# Terraform itself authenticates as root@pam with a password (providers.tf, ADR 17),
# so there was no API token anywhere. The exporter must NOT use that credential: this
# creates a dedicated pve@realm user with the built-in PVEAuditor role (read-only) on
# "/" and a token for it. privileges_separation = false makes the token inherit the
# user's role (with true it would need its own ACL and would see nothing).
#
# Lifecycle: these are ordinary managed resources, so `terraform destroy` deletes the
# token along with the LXCs it served; the next apply mints a new one and rewrites the
# secret file. The secret is only returned at creation, so it is kept in Terraform
# state (already holding the root@pam password) and written, 0600, to the same
# /opt/ansible-secrets directory the Infisical bootstrap uses; converge.sh loads it.

resource "proxmox_virtual_environment_user" "pve_exporter" {
  user_id = "pve-exporter@pve"
  comment = "Read-only monitoring identity for prometheus-pve-exporter (ADR 115)"
  enabled = true
}

resource "proxmox_virtual_environment_acl" "pve_exporter_auditor" {
  path      = "/"
  propagate = true
  role_id   = "PVEAuditor"
  user_id   = proxmox_virtual_environment_user.pve_exporter.user_id
}

resource "proxmox_virtual_environment_user_token" "pve_exporter" {
  user_id               = proxmox_virtual_environment_user.pve_exporter.user_id
  token_name            = "exporter"
  comment               = "prometheus-pve-exporter on the observability LXC (ADR 115)"
  privileges_separation = false

  depends_on = [proxmox_virtual_environment_acl.pve_exporter_auditor]
}

# `value` is "<user>@<realm>!<token_name>=<secret>"; only the part after "=" is secret.
resource "local_sensitive_file" "pve_exporter_secrets" {
  filename        = "/opt/ansible-secrets/pve-exporter.yml"
  file_permission = "0600"
  content = yamlencode({
    pve_exporter_user         = proxmox_virtual_environment_user.pve_exporter.user_id
    pve_exporter_token_name   = proxmox_virtual_environment_user_token.pve_exporter.token_name
    pve_exporter_token_secret = split("=", proxmox_virtual_environment_user_token.pve_exporter.value)[1]
  })
}
