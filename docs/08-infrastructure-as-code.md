# Infrastructure as Code (IaC) Automation Strategy

As the lab evolves past manual Proxmox CLI scripts, you should orchestrate the environment using Terraform and Ansible. This achieves declarative, idempotent provisioning.

## 1. Terraform (Infrastructure Provisioning)
Instead of executing community bash scripts, use Terraform to configure your VLANs, Proxmox LXCs, and storage.

*   **Provider:** Use the `bpg/proxmox` provider for modern Proxmox VE 8/9 support. This provider offers full support for SDN configuration and API token management.
*   **UniFi Automation:** Combine with the `paultyng/unifi` provider to script the VLAN 10/20/30 subnets and Firewall routing directly into code.

## 2. Ansible (Configuration Management)
Once Terraform creates the LXC and assigns the static IP, Ansible steps in over SSH:
*   Installs the `.NET 10` runtime.
*   Configures and enables the `blazor-app.service` systemd unit.
*   Sets up the `/mnt/synology` NFS mount points using `/etc/fstab`.

By transitioning to this IaC pipeline, you can recreate the entire enterprise home lab from scratch by running `terraform apply` followed by `ansible-playbook setup.yml`.

---
### Source Material & Attribution
Strategy relies on official Terraform documentation and `bpg/terraform-provider-proxmox` registry specs.
