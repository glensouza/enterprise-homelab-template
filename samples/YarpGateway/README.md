# YARP gateway sample

A reverse proxy in one project, used by the S1-09 video and never deployed to the homelab. Ansible, Terraform, and the GitHub workflows do not reference it.

```bash
dotnet run --project samples/YarpGateway
```

`/api/**` is load-balanced across `http://localhost:5000` and `http://localhost:5001`. Point those at two copies of BrewHouse if you want the on-camera picture. Stopping this process is the "then I deleted it" beat. Kemp stays the edge.
