# Security policy

## Reporting a vulnerability

Please do not publish operational keys, packet captures, receiver identifiers,
customer information, or exploitable details in a public issue.

Until a dedicated security contact is configured, open a GitHub issue that
contains only a short, non-sensitive request for private coordination.

## Supported version

Only the latest version on the default branch is maintained.

## Secrets

The application expects the preset key in `SURGARD_PRESET_KEY`. Production
addresses and secrets must be supplied outside source control.
