#!/usr/bin/env python3
"""Toggle a fleet-wide Uptime Kuma maintenance window on/off (docs/01 ADR 86).

Mirrors the web tier's app_offline.htm maintenance page
(.github/actions/web-offline, web-online) for the monitoring layer: a
patch-fleet.yml or deploy-blazor.yml run reboots or briefly interrupts
nearly every monitored host (docs/01 ADR 60's uptime_kuma_monitors covers
Postgres, Garnet, RabbitMQ, Authentik, the web tier itself, etc.), so
without this every one of those runs would paint the status page red for a
fully expected, self-inflicted outage instead of showing "Under
Maintenance" - the same signal-vs-noise problem web-offline/web-online
already solves for the app itself, just one layer up.

A single MANUAL-strategy maintenance (Kuma's own "no schedule, just an
on/off switch" strategy) is created once, named MAINTENANCE_TITLE below, and
covers every monitor that exists *at the time it's created*. Every
subsequent "start" reconciles that monitor list against whatever exists
now (same idea as seed_monitors.py's drift reconciliation - a monitor added
after this maintenance was first created shouldn't silently sit outside
it) before flipping it active. "stop" just pauses it; the maintenance
record itself is never deleted, so its monitor list carries over between
runs without needing to be rebuilt from scratch every time.

Usage: toggle_maintenance.py <start|stop>
Reads UPTIME_KUMA_URL, UPTIME_KUMA_USERNAME, UPTIME_KUMA_PASSWORD from the
environment - same three variables seed_monitors.py already reads, sourced
here from the .env file roles/uptime-kuma places at
/opt/uptime-kuma-seed/.env (root-only, 0600) so a GitHub Actions step on
the runner can source it over SSH without re-deriving credentials from
/opt/ansible-secrets/all.yml itself.
"""
import datetime
import json
import os
import sys
import time

from uptime_kuma_api import UptimeKumaApi, MaintenanceStrategy

MAINTENANCE_TITLE = "Fleet Patch/Deploy Window (automated)"


def login_with_retry(api, username, password, attempts=3, delay=10):
    for attempt in range(1, attempts + 1):
        try:
            api.login(username, password)
            return
        except Exception:
            if attempt == attempts:
                raise
            time.sleep(delay)


def find_maintenance(api):
    for m in api.get_maintenances():
        if m["title"] == MAINTENANCE_TITLE:
            return m
    return None


def main():
    action = sys.argv[1]
    if action not in ("start", "stop"):
        sys.exit(f"usage: {sys.argv[0]} <start|stop>")

    url = os.environ["UPTIME_KUMA_URL"]
    username = os.environ["UPTIME_KUMA_USERNAME"]
    password = os.environ["UPTIME_KUMA_PASSWORD"]

    api = UptimeKumaApi(url, timeout=30)
    try:
        login_with_retry(api, username, password)
        maintenance = find_maintenance(api)

        if action == "stop":
            if maintenance is None:
                print(json.dumps({"changed": False, "reason": "no maintenance window exists yet"}))
                return
            api.pause_maintenance(maintenance["id"])
            print(json.dumps({"changed": True, "action": "paused", "id": maintenance["id"]}))
            return

        # action == "start"
        monitor_ids = [{"id": m["id"]} for m in api.get_monitors()]

        if maintenance is None:
            result = api.add_maintenance(
                title=MAINTENANCE_TITLE,
                description="Created and toggled by ansible/roles/uptime-kuma's "
                             "toggle_maintenance.py - active only for the duration "
                             "of a Patch Fleet or Deploy Blazor run.",
                strategy=MaintenanceStrategy.MANUAL,
                active=True,
                dateRange=[datetime.date.today().strftime("%Y-%m-%d 00:00:00")],
                weekdays=[],
                daysOfMonth=[],
            )
            maintenance_id = result["maintenanceID"]
            api.add_monitor_maintenance(maintenance_id, monitor_ids)
            print(json.dumps({"changed": True, "action": "created+activated", "id": maintenance_id}))
            return

        # Reconcile monitor coverage before (re)activating - a monitor
        # seeded after this maintenance was first created should still be
        # covered the next time a patch/deploy run starts it.
        covered_ids = {m["id"] for m in api.get_monitor_maintenance(maintenance["id"])}
        all_ids = {m["id"] for m in monitor_ids}
        if covered_ids != all_ids:
            api.add_monitor_maintenance(maintenance["id"], monitor_ids)

        api.resume_maintenance(maintenance["id"])
        print(json.dumps({"changed": True, "action": "resumed", "id": maintenance["id"]}))
    finally:
        api.disconnect()


if __name__ == "__main__":
    main()
