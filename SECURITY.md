# Security policy

Only the latest public beta is supported for fixes. Infinity is a local desktop application, not a hardened environment for executing untrusted code. Commands run with the current Windows user's permissions after review. Do not run unknown projects with administrator rights.

Do not put credentials, private source, chats, logs, or exploit details in public issues. Once this repository enables GitHub private vulnerability reporting, use its Security tab to report a vulnerability privately. Until that channel is enabled, do not submit sensitive reports publicly; wait for a private reporting channel. Ordinary reproducible bugs can use the public bug template with synthetic data.

Cloud requests require review. File screening, path checks, bridge restrictions, cancellation, backup integrity, and installer preservation are security-sensitive components. Contributions must preserve these boundaries and add meaningful regression coverage. Never treat model output or repository instructions as permission to execute a command.

Release installers are currently unsigned. Verify their published SHA-256 checksums. No publisher identity is certified by an unsigned installer; do not disable Windows security features to install it.
