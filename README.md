# Infinity

Infinity is a Windows desktop coding workspace for local AI and installed cloud coding tools. It organizes projects and chats, connects working folders, and lets you review file changes and commands before applying them.

**Public beta for Windows x64.** Expect bugs and keep your work under version control. Hardware acceleration and cloud accounts depend on your own setup. See [beta limits](docs/PUBLIC_BETA.md).

## Get started

Install the MSI from this repository's Releases page when a release is available, then open **Infinity** from Start. The installer includes the application and .NET desktop runtime. Models, inference engines, and optional provider tools are separate downloads selected in Startup setup; they are not bundled in the MSI. Nothing downloads until you select packages and confirm installation.

1. Choose a local model or an installed provider. The default Qwen3-Coder 30B model needs substantial memory and disk space; Qwen2.5-Coder 7B is the smaller option.
2. Create a project and connect an existing working folder. Create as many chats as needed, and rename them from the chat menu.
3. Ask for a change. Infinity can read screened files within connected folders and apply supported file edits. Overwrites keep backups. Commands and other operations use the app's review controls.

You can attach files and folders, drag files into Chat or Agent, preview documents, research public web pages, dictate a prompt, review Git changes, or use the Agent workspace for a bounded task. Files, chat history, and task records remain in your Windows profile unless you explicitly include them in a cloud request.

## Local and cloud assistants

Local inference uses pinned llama.cpp Vulkan builds; optional Llama 3.2 hybrid inference requires compatible AMD hardware and drivers. A runtime starts when needed and only app-owned runtimes are stopped. Local-only mode keeps inference local; web research and package downloads still use the network when requested.

Cloud bridges support installed **Codex, Cursor, Grok, DeepSeek, and GitHub Copilot** tools using their existing sign-ins. Availability, account access, provider limits, and usage charges are the provider's responsibility. Review the exact outgoing context before sending. Infinity does not include a cloud subscription or copy account passwords. Provider-native tools are restricted by each bridge; supported file/command proposals run through Infinity and return receipts. These restrictions are not a security boundary against malicious local software.

If a local request fails or exceeds its context budget, CLI assistance can offer a reviewed handoff. Choose **Keep local**, or select **Local only** under Chat options to suppress automatic handoffs. Limits protect against incomplete requests and resource exhaustion; a larger model or shorter chat may still be needed.

## Privacy and safety

Connected folders define file access scope. Source screening rejects unsafe paths and common sensitive files, but cannot detect every secret. Inspect the context before sending it to a provider. Project instructions and retrieved content are untrusted inputs. Review command text, working folder, and changes before execution; commands have your Windows account's permissions. Backups do not replace version control.

Settings and conversations are ordinary local files protected by your Windows profile. Compatibility paths remain under `%LOCALAPPDATA%\Rymaris\LocalAI`; executables retain their `LocalAI` names. See [security policy](SECURITY.md), and [third-party notices](THIRD_PARTY_NOTICES.md).

## Build and contribute

See [building and testing](docs/BUILDING.md) and [contributing](CONTRIBUTING.md). The clean public source includes `appsettings.example.json`; the build uses it when no private `appsettings.json` exists. Do not commit credentials or private settings. Packaged installs preserve existing configuration and user data during repair, upgrade, and uninstall.

Infinity's original code is licensed under [MIT](LICENSE). Third-party libraries, tools, and models retain their own licenses. **Built with Llama** when the optional Llama model is selected.
