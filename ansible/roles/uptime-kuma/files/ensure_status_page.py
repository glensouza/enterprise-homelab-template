#!/usr/bin/env python3
"""Idempotently create/update the BrewHouse status page (docs/01 ADR 89).

The one branding gap Uptime Kuma still had after ADR 86 (maintenance mode):
its public status page didn't exist at all yet (`GET /api/status-page/brewhouse`
404s on a fresh instance - confirmed live before this landed), let alone
carry the BrewHouse title/icon every other branded surface in this repo
uses (Homepage, ADR 38; Authentik, ADR 88).

Creates (if missing) or updates (if present) a single status page at slug
`brewhouse`, title "BrewHouse Homelab", icon `/brewhouse-icon.jpg` (this
role also drops that file into Kuma's own `dist/` directory, so it's
served same-origin - no dependency on Homepage or Authentik being up, same
reasoning ADR 88 used for Authentik's own brand assets). Every current
monitor is grouped under one "Fleet" section, reconciled on every run the
same way seed_monitors.py (ADR 60/70) reconciles monitor drift - a monitor
added after this status page was first created still ends up listed.

Does NOT use the library's own save_status_page()/get_status_page() - a
second real uptime-kuma-api bug found live, same class as ADR 60's
add_monitor() one: get_status_page() merges a socket response with a plain
REST GET of api/status-page/<slug> and reads that REST response's
"incident" key, but Kuma 2.5.3's actual response uses "incidents" (plural,
a list) - confirmed live, a bare KeyError: 'incident' every time, on a
brand-new AND an already-existing status page alike, since save_status_page()
calls get_status_page() internally too. Worked around the same way ADR 60
did: build the save payload directly via the library's own lower-level
_build_status_page_data() + _call('saveStatusPage', ...), using
_call('getStatusPage', slug) (the socket leg only, unaffected by this bug)
just to learn the page's numeric id.

Usage: ensure_status_page.py
Reads UPTIME_KUMA_URL, UPTIME_KUMA_USERNAME, UPTIME_KUMA_PASSWORD from the
environment, same as seed_monitors.py/toggle_maintenance.py.
"""
import json
import os
import time

from uptime_kuma_api import UptimeKumaApi

SLUG = "brewhouse"
TITLE = "BrewHouse Homelab"
DESCRIPTION = "Live status for every service in the BrewHouse homelab fleet."


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
    url = os.environ["UPTIME_KUMA_URL"]
    username = os.environ["UPTIME_KUMA_USERNAME"]
    password = os.environ["UPTIME_KUMA_PASSWORD"]

    api = UptimeKumaApi(url, timeout=30)
    try:
        login_with_retry(api, username, password)

        try:
            page = api._call("getStatusPage", SLUG)
            created = False
        except Exception:
            api.add_status_page(SLUG, TITLE)
            page = api._call("getStatusPage", SLUG)
            created = True

        page_id = page["config"]["id"]
        monitor_list = [{"id": m["id"]} for m in api.get_monitors()]
        page_slug, config, icon, public_group_list = api._build_status_page_data(
            slug=SLUG,
            id=page_id,
            title=TITLE,
            description=DESCRIPTION,
            icon="/brewhouse-icon.jpg",
            publicGroupList=[
                {
                    "name": "Fleet",
                    "weight": 1,
                    "monitorList": monitor_list,
                }
            ],
        )
        # _build_status_page_data() (2023-era library, ADR 60/70's same
        # vintage) never learned about Kuma 2.x's analytics fields - leaving
        # them out entirely makes the server see `undefined`, which fails
        # its own `config.analyticsType !== null` check (confirmed live,
        # "Invalid analytics type" from server/socket-handlers/status-page-
        # socket-handler.js) even though `null` itself is explicitly
        # allowed there. Setting them to null explicitly is what "no
        # analytics configured" actually means server-side.
        config.update({"analyticsType": None, "analyticsId": None, "analyticsScriptUrl": None})
        api._call("saveStatusPage", (page_slug, config, icon, public_group_list))
        print(json.dumps({"changed": True, "action": "created" if created else "updated", "slug": SLUG}))
    finally:
        api.disconnect()


if __name__ == "__main__":
    main()
