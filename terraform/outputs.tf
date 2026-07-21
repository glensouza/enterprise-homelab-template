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
  })
}
