output "lxc_ips" {
  description = "Static IPv4 address of every provisioned LXC"
  value       = { for name, cfg in local.lxcs : name => split("/", cfg.ip)[0] }
}

# Render an Ansible inventory so `terraform apply` flows straight into
# `ansible-playbook site.yml` (docs/08). Written into ../ansible/inventory/.
resource "local_file" "ansible_inventory" {
  filename = "${path.module}/../ansible/inventory/terraform-hosts.yml"
  content = templatefile("${path.module}/templates/inventory.tftpl", {
    web_hosts = [
      for name, cfg in local.lxcs : { name = name, ip = split("/", cfg.ip)[0] }
      if contains(cfg.tags, "web")
    ]
    postgres_host = {
      name = "postgresql"
      ip   = split("/", local.lxcs["postgresql"].ip)[0]
    }
    preview_host = {
      name = "pr-preview"
      ip   = split("/", local.lxcs["pr-preview"].ip)[0]
    }
    dns_host = {
      name = "technitium-dns"
      ip   = split("/", local.lxcs["technitium-dns"].ip)[0]
    }
    pki_host = {
      name = "step-ca"
      ip   = split("/", local.lxcs["step-ca"].ip)[0]
    }
    # Every LXC without a dedicated Ansible group — included in `all` so
    # Cockpit, internal DNS records, and step-ca certs cover the fleet (ADR 21).
    infra_hosts = [
      for name in ["cloudflared", "garnet", "rabbitmq", "infisical", "uptime-kuma", "observability"] :
      { name = name, ip = split("/", local.lxcs[name].ip)[0] }
    ]
  })
}
