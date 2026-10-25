# Security Policy

Rostr handles Roblox login cookies, so security reports are taken seriously.

## Reporting a vulnerability

Please **do not open a public issue** for security problems.
Use GitHub's private reporting instead: **Security → Report a vulnerability** on this repository.

Include what you found, how to reproduce it and which version you used. You will get a reply as soon as possible.

## Scope examples

- Anything that could leak or misuse a saved cookie
- The self-update mechanism (download, checksum verification, script execution)
- The `roblox-player:` protocol handler

## If you ever pasted your cookie somewhere public

Log out of that session on roblox.com (or change your password) so the cookie is invalidated.
