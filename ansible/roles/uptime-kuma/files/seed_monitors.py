#!/usr/bin/env python3
"""Idempotently create/update Uptime Kuma monitors from a JSON file (docs/01 ADR 60/70).

uptime-kuma-api's own add_monitor() cannot be used as-is against Kuma 2.x:
its `_build_monitor_data()` predates Kuma's `conditions` column (the
conditional-alerting feature), so it never sends that field - the server
then does `monitor.conditions = JSON.stringify(monitor.conditions)` on
`undefined`, and Kuma stores/attempts a NULL into a NOT NULL column, so the
INSERT is rejected outright (confirmed live against the real Kuma 2.5.3
instance). Building the payload via the library's own _build_monitor_data()
and adding `conditions: []` before calling its lower-level add path directly
works and reuses everything else the library already gets right (auth,
socket event correlation) instead of reimplementing a full Socket.IO client.

Also reconciles existing monitors (matched by name) whose url/hostname/port
differ from the desired spec, via edit_monitor() - added after a real
incident (docs/01 ADR 70): this script originally only ever added missing
monitors, so two monitors' specs drifted from what was actually live
(Authentik's stale :9443 port, survived a whole port-move ADR unnoticed;
Kemp's monitor pointed at the WUI address instead of the real VIP) with no
mechanism to ever catch up short of deleting and re-adding by hand.
edit_monitor() doesn't have add_monitor()'s conditions bug - it fetches the
live monitor's own current data first (conditions included) and merges
in just the changed keys, unlike _build_monitor_data() building from scratch.

Usage: seed_monitors.py <monitors.json>
Reads UPTIME_KUMA_URL, UPTIME_KUMA_USERNAME, UPTIME_KUMA_PASSWORD from the
environment.

Confirmed live: the socket.io login handshake can time out transiently
(observed once against the real fleet during a fleet-wide converge, with
plenty of other Ansible tasks running concurrently) even though the exact
same call succeeds a moment later against the exact same instance - retried
here rather than left to intermittently fail the whole play.
"""
import json
import os
import sys
import time

from uptime_kuma_api import UptimeKumaApi, MonitorType
from uptime_kuma_api.api import _convert_monitor_input, _check_arguments_monitor
from uptime_kuma_api.event import Event


def login_with_retry(api, username, password, attempts=3, delay=10):
    for attempt in range(1, attempts + 1):
        try:
            api.login(username, password)
            return
        except Exception:
            if attempt == attempts:
                raise
            time.sleep(delay)


def main():
    with open(sys.argv[1]) as f:
        desired = json.load(f)

    url = os.environ["UPTIME_KUMA_URL"]
    username = os.environ["UPTIME_KUMA_USERNAME"]
    password = os.environ["UPTIME_KUMA_PASSWORD"]

    # Fields worth reconciling on an existing monitor - deliberately not
    # every key add_monitor() accepts (e.g. interval), so a human tweaking
    # a check interval by hand in the UI isn't silently stomped every run.
    reconcile_fields = ("url", "hostname", "port")

    api = UptimeKumaApi(url, timeout=30)
    try:
        login_with_retry(api, username, password)
        existing = {m["name"]: m for m in api.get_monitors()}

        added = []
        updated = []
        for spec in desired:
            name = spec["name"]
            current = existing.get(name)

            if current is None:
                kwargs = dict(spec)
                kwargs["type"] = MonitorType[kwargs["type"]]
                data = api._build_monitor_data(**kwargs)
                data["conditions"] = []
                _convert_monitor_input(data)
                _check_arguments_monitor(data)

                with api.wait_for_event(Event.MONITOR_LIST):
                    api._call("add", data)
                added.append(name)
                continue

            changed = {
                field: spec[field]
                for field in reconcile_fields
                if field in spec and spec[field] != current.get(field)
            }
            if changed:
                api.edit_monitor(current["id"], **changed)
                updated.append({"name": name, "changed": changed})

        print(json.dumps({"added": added, "updated": updated}))
    finally:
        api.disconnect()


if __name__ == "__main__":
    main()
