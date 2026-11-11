# Patched hybrid host

Infinity bundles a narrowly patched Ryzen AI Server 1.7.0 host. The original server emits the previous token again when generation stops without extending the token sequence. Infinity only decodes a token when sequence length grows. The Responses stream carries an explicit output-limit reason and uses the JSON library to escape all control characters. It also exposes a versioned backend identity endpoint and restricts publisher DLL resolution to a verified runtime directory. Model/publisher runtime files are never overwritten.

Inputs are public, immutable and SHA-256 pinned in inputs.json. The build is offline and requires Visual Studio 2022 C++ tools and CMake. Copyright/license notices are included in THIRD_PARTY_NOTICES.txt.

From the canonical repository:

```
python native/prepare_inputs.py --directory C:\tmp\Infinity-NativeBuildInputs-1.7.0 --download --runtime-directory <verified-publisher-runtime-directory>
python native/build_backend.py --inputs C:\tmp\Infinity-NativeBuildInputs-1.7.0 --output C:\tmp\Infinity-NativeOutput --revision <git-commit>
dotnet publish LocalAI.Desktop -c Release -r win-x64 --self-contained true -p:InfinityNativeBackendDirectory=C:\tmp\Infinity-NativeOutput
```

Omit --download to verify an already populated input cache without network requests. Unknown/mismatching inputs fail closed. --runtime-directory reuses only the exact pinned GenAI DLL for import-library generation; no DLL is copied into the delivered helper folder. The application verifies that publisher ABI before starting the helper. Without --runtime-directory, explicit download obtains the pinned publisher archive and extracts only the matching build input.

For release evidence use scripts/verify_release.py with --native-inputs and --node. Its default HEAD mode builds the native host from the exported commit and packages all three Native assets. INDEX is a development candidate only. Preserve original failed test evidence, and rebuild after committing before signing or delivery.
