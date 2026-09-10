terraform {
  required_version = ">= 1.6"

  # State lives outside the git checkout on the devops LXC (ADR 22) — the
  # CI workflows check out a fresh copy of this repo on every run, and
  # actions/checkout's default `git clean` would otherwise wipe a
  # workspace-local terraform.tfstate on every single plan/apply.
  backend "local" {
    path = "/opt/terraform-state/terraform.tfstate"
  }

  required_providers {
    proxmox = {
      source  = "bpg/proxmox"
      version = "~> 0.70"
    }
    unifi = {
      source  = "resnickio/unifi"
      version = "~> 0.10"
    }
    local = {
      source  = "hashicorp/local"
      version = "~> 2.5"
    }
  }
}
