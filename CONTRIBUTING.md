# Contributing

Read the README and docs/BUILDING.md first. Open an issue describing a reproducible problem or proposed feature before making broad changes. Use synthetic fixtures and omit credentials, personal paths, private chats, downloaded models, and generated output.

Keep changes focused. Preserve existing LocalAI assembly names and user-data paths. Test both failure and cancellation paths for file operations, cloud handoffs, and commands. A passing build alone does not prove behavior. State exactly which checks you ran and any unverified hardware/provider scenarios.

Do not change model/runtime pins without verifying publisher hashes, licenses, compatibility, and migration behavior. Do not automatically approve AI-generated actions. No cloud-provider tests should run in CI or use contributors' accounts implicitly.

By contributing, you agree your original contribution is offered under the project's MIT license. Third-party material must retain its own license and attribution.
