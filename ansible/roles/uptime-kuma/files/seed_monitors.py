#!/usr/bin/env python3
"""Idempotently create Uptime Kuma monitors from a JSON file (docs/01 ADR 60).

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

Usage: seed_monitors.py <monitors.json>
Reads UPTIME_KUMA_URL, UPTIME_KUMA_USERNAME, UPTIME_KUMA_PASSWORD from the
environment.
"""
import json
import os
import sys

from uptime_kuma_api import UptimeKumaApi, MonitorType
from uptime_kuma_api.api import _convert_monitor_input, _check_arguments_monitor
from uptime_kuma_api.event import Event


def main():
    with open(sys.argv[1]) as f:
        desired = json.load(f)

    url = os.environ["UPTIME_KUMA_URL"]
    username = os.environ["UPTIME_KUMA_USERNAME"]
    password = os.environ["UPTIME_KUMA_PASSWORD"]

    api = UptimeKumaApi(url, timeout=15)
    try:
        api.login(username, password)
        existing_names = {m["name"] for m in api.get_monitors()}

        added = []
        for spec in desired:
            name = spec["name"]
            if name in existing_names:
                continue

            kwargs = dict(spec)
            kwargs["type"] = MonitorType[kwargs["type"]]
            data = api._build_monitor_data(**kwargs)
            data["conditions"] = []
            _convert_monitor_input(data)
            _check_arguments_monitor(data)

            with api.wait_for_event(Event.MONITOR_LIST):
                api._call("add", data)
            added.append(name)

        print(json.dumps({"added": added}))
    finally:
        api.disconnect()


if __name__ == "__main__":
    main()
