# Public beta scope

Candidate version: **1.26.1113-beta.2** (MSI product version 1.26.1113).

Infinity targets Windows 11 x64. The application is self-contained; building requires .NET 10 SDK. Inference and optional tools require additional downloads, compatible drivers, and their own license/account terms. Selecting all model/tool packages uses tens of GiB; Startup setup displays exact sizes before approval. Leave additional space for staging and model loading. Memory needs depend on model, context length, and GPU offload; the 30B default is not suitable for every PC. Choose the smaller 7B option if necessary. Hybrid NPU inference is specific to compatible AMD hardware.

## Known limits

- This is a beta, with an unsigned installer. Code signing and broad device certification are not complete.
- Models can produce incorrect code or claim capabilities inaccurately. Use actual file/command receipts and test results to assess work.
- Context, output, timeout, and bridge limits remain. Local-only mode avoids handoff dialogs; it does not increase model capacity.
- Source attachments and unsent drafts are not guaranteed across restart. Chat history and saved projects persist locally as documented in the app.
- Cloud providers require compatible installed tools and sign-in. A clean installation does not include authenticated cloud accounts. Provider updates may change CLI behavior.
- Startup setup downloads tools/models separately; it cannot supply GPU/NPU drivers or finish third-party account sign-in for you.
- Voice recognition accuracy depends on device/model/language. Microphone permissions are managed by Windows.
- Offline regression checks exercise synthetic fixtures and UI handlers. They do not certify every physical input, GPU/NPU, provider account, or Windows configuration.

The release preparation report distributed beside the candidate records verification of its exact revision and installer hashes. Historical tests are not evidence for later revisions. Hosted GitHub checks can only be confirmed after publication.
