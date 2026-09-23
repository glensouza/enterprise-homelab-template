output "lxc_ips" {
  description = "Static IPv4 address of every provisioned LXC"
  value       = { for name, cfg in local.lxcs : name => split("/", cfg.ip)[0] }
}

# Render an Ansible inventory so `terraform apply` flows straight into
# `ansible-playbook site.yml` (docs/08). Written into ../ansible/inventory/.
resource "local_file" "ansible_inventory" {
  filename = "${path.module}/../ansible/inventory/terraform-hosts.yml"
  content = templatefile("${path.module}/templates/inventory.tftpl", {
    # docs/01 ADR 98: iterates sort(keys(...)) rather than local.lxcs
    # directly - a `for` producing a LIST (not an object/map, via `=>`)
    # over a map's own iteration doesn't have a stable order guarantee, so
    # this file's rendered host ordering (and therefore its content hash,
    # what local_file's `id` is keyed on) could shuffle between separate
    # plan/apply runs with zero real config changes, showing up as a
    # phantom diff every time. Sorting the keys first makes the list order
    # - and this file's content - fully deterministic.
    web_hosts = [
      for name in sort(keys(local.lxcs)) : { name = name, ip = split("/", local.lxcs[name].ip)[0] }
      if contains(local.lxcs[name].tags, "web")
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
    observability_host = {
      name = "observability"
      ip   = split("/", local.lxcs["observability"].ip)[0]
    }
    uptime_kuma_host = {
      name = "uptime-kuma"
      ip   = split("/", local.lxcs["uptime-kuma"].ip)[0]
    }
    homepage_host = {
      name = "homepage"
      ip   = split("/", local.lxcs["homepage"].ip)[0]
    }
    # Every LXC without a dedicated Ansible group — included in `all` so
    # Cockpit, internal DNS records, and step-ca certs cover the fleet (ADR 21).
    infra_hosts = [
      for name in ["cloudflared", "garnet", "rabbitmq", "infisical", "patchmon"] :
      { name = name, ip = split("/", local.lxcs[name].ip)[0] }
    ]
  })
}
