# Enterprise Proxmox + .NET 10 Blazor Home Lab Template

A production-grade, highly available, decentralized home lab architecture template for hosting **.NET 10 Blazor** web applications on **Proxmox VE 9.1**. 

This repository utilizes a consolidated AI-Native approach via a single `CLAUDE.md` file, teaching Claude Code your exact architecture, network layout, safety guardrails, and required developer workflow (auto-updating tests and docs).

## Repository Structure

```text
enterprise-homelab-claude-v5/
├── README.md
├── CLAUDE.md                          
├── LICENSE                            
├── docker-compose.local.yml           
├── docs/
│   ├── architecture.drawio            
│   ├── 01-architecture-decisions.md   
│   ├── 02-proxmox-and-backups.md
│   ├── 03-kemp-load-balancer.md
│   ├── 04-lxc-provisioning.md
│   ├── 05-unifi-network-isolation.md  
│   ├── 06-testing-strategy.md         
│   ├── 07-observability.md            
│   └── 08-infrastructure-as-code.md   # Terraform & Ansible Automation Strategy
├── src/
│   ├── RoadrunnerAuction/             
│   │   ├── RoadrunnerAuction.csproj
│   │   ├── Program.cs                 
│   │   ├── appsettings.json           
│   │   ├── appsettings.Development.json 
│   │   └── ... (Blazor Application Code)
│   └── systemd/
│       └── blazor-app.service
├── tests/
│   └── RoadrunnerAuction.Tests/       
└── .github/
    └── workflows/
        └── deploy-blazor.yml          
```

---
### Source Material & Attribution
Architectural patterns within this template are derived from best practices documented by the Microsoft .NET Foundation (Blazor/EF Core), Grafana Labs (Loki/Observability), MassTransit (Distributed Systems), and the Proxmox VE Community.
