#!/bin/bash
# Wrapper for `ansible-playbook site.yml` that always supplies the
# "supplied by you" secrets from their absolute, checkout-independent
# location (docs/01 ADR 36) - run this instead of raw ansible-playbook,
# from the ansible/ directory, exactly like `ansible-playbook site.yml`
# itself: e.g. `ansible-playbook-site.sh --limit dns`.
exec ansible-playbook site.yml \
  -e @/opt/ansible-secrets/all.yml \
  -e @/opt/ansible-secrets/dns.yml \
  -e @/opt/ansible-secrets/web.yml \
  "$@"
