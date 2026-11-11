# Third-party licenses and notices

The root MIT license covers original Infinity code. It does not relicense dependencies, model weights, trademarks, or provider services.

## Redistributed components

Full license texts and notices are in `LocalAI.Desktop/Assets/Licenses` and installed under `Assets/Licenses`:

| Component | Version | Terms and notice |
| --- | --- | --- |
| .NET and Windows Desktop/WPF runtime | 10.0.x self-contained | MIT plus component notices: DotNet.txt, DotNet-notices.txt, Wpf.txt, Wpf-notices.txt |
| System.Speech | 10.0.0 | MIT; DotNet.txt and System.Speech-notices.txt |
| Microsoft.Extensions.AI.Abstractions | 10.2.0 | MIT; DotNet.txt |
| NAudio.Core, Wasapi, WinMM | 2.2.1 | MIT; NAudio.txt |
| PdfPig and its bundled components | 0.1.16 | Apache-2.0 plus component notices; PdfPig.txt |
| Whisper.net and native runtime packages | 1.9.1 | MIT; Whisper-net.txt and Whisper-cpp.txt |
| Microsoft WebView2 SDK | 1.0.3537.50 | Microsoft license; WebView2.txt and WebView2-notice.txt |

The patched Ryzen host and its build headers retain `native/THIRD_PARTY_NOTICES.txt`, also installed beside the helper. Pinned upstream source and Infinity's build/patch process are in `native/`.

WiX UI 7.0.0 uses the Microsoft Reciprocal License (MS-RL). Its license, corresponding source archive, and pin are under `installer/third-party` and distributed by the installer. This material is not covered by Infinity's MIT license.

## Separate downloads

Exact publisher links, revisions, sizes, hashes, and license links are in `LocalAI.Core/dependency-catalog.json`. Startup setup displays license links. These packages are not bundled in the MSI or source archive:

- llama.cpp: MIT. Ryzen AI runtime: publisher terms and included component notices.
- Qwen3-Coder and Qwen2.5-Coder distributions: Apache-2.0 as declared by their publisher model cards. Retain upstream licenses if redistributing weights.
- Llama 3.2: Meta Llama 3.2 Community License and Acceptable Use Policy. **Built with Llama** when selected. Consult the [upstream model](https://huggingface.co/meta-llama/Llama-3.2-3B-Instruct) and [AMD conversion](https://huggingface.co/amd/Llama-3.2-3B-Instruct-onnx-ryzenai-1.7-hybrid).
- MinGit: GPL-2.0 and bundled component licenses; corresponding source is available from its Git for Windows publisher. It is an optional separate tool.
- Codex, Cursor, Grok, DeepSeek, GitHub Copilot, VC++ Redistributable, WebView2 Runtime, and hosted services retain their respective terms, trademarks, account requirements, and licenses. No endorsement or affiliation is implied.
- Optional Whisper model weights retain the upstream MIT license; Whisper-model.txt accompanies the application.

Consult publisher terms before redistributing their packages. Pins and notices grant no additional rights.
