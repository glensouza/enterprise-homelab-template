terraform {
  required_version = ">= 1.6"

  required_providers {
    proxmox = {
      source  = "bpg/proxmox"
      version = "~> 0.70"
    }
    unifi = {
      source  = "paultyng/unifi"
      version = "~> 0.41"
    }
    local = {
      source  = "hashicorp/local"
      version = "~> 2.5"
    }
  }
}
