# Changelog

## 1.26.1113-beta.2 — hosted CI cleanup correction

- Handle read-only Git fixture files during bounded CI cleanup, without following junction targets or changing read-only hardlinks outside the owned directory.
- Add six cleanup regression checks. The reviewed MSI/application binary is unchanged from beta 1.

## 1.26.1113-beta.1 — public beta candidate

- Windows desktop projects with multiple named chats, connected folders, file previews, and reviewed coding actions.
- Local Vulkan inference and optional AMD hybrid inference with pinned dependency setup.
- Codex, Cursor, Grok, DeepSeek, and GitHub Copilot bridges with explicit outgoing-context review.
- Chat and Agent file drag and drop, command review, source control, voice input, and bounded tasks.
- Native folder browsing shared across working-folder fields; cancellation leaves the current location unchanged.
- Managed Git is discovered for source control and isolated workspaces even when direct chat launch skips startup setup.
- Per-user installer, repair/uninstall preservation, installation verification, and isolated regression suites.

This candidate is prepared locally. Publication, hosted CI, code signing, and broader hardware certification are separate release steps. See docs/PUBLIC_BETA.md.
