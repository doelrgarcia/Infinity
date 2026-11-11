"""Build Infinity's narrowly patched Ryzen 1.7 backend from verified offline inputs.

Requires CMake and Visual Studio 2022 C++ tools. Nothing is downloaded or installed.
The original publisher runtime/model directories are never modified.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import zipfile


def replace_once(text, old, new):
    if text.count(old) != 1:
        raise ValueError("Pinned upstream patch anchor changed: " + old[:80])
    return text.replace(old, new)


def build(inputs, output, revision):
    here = Path(__file__).resolve().parent
    pins = json.loads((here / "inputs.json").read_text(encoding="utf-8-sig"))
    for pin in pins:
        if hashlib.sha256((inputs / pin["name"]).read_bytes()).hexdigest() != pin["sha256"]:
            raise ValueError("Native input hash mismatch: " + pin["name"])
    work = Path(tempfile.mkdtemp(prefix="Infinity-native-"))
    with zipfile.ZipFile(inputs / "source.zip") as archive:
        for entry in archive.infolist():
            target = (work / entry.filename).resolve()
            if not target.is_relative_to(work.resolve()) or (entry.external_attr >> 16) & 0xF000 == 0xA000 or entry.file_size > 100_000_000:
                raise ValueError("Unsafe native source archive")
        archive.extractall(work)
    source = work / "ryzenai-server-1.7.0"
    oga = work / "oga"
    (oga / "LLM/include").mkdir(parents=True)
    (oga / "LLM/lib").mkdir()
    for name in ("ort_genai.h", "ort_genai_c.h"):
        shutil.copy2(inputs / name, oga / "LLM/include" / name)
    for name, relative in (("httplib.h", "external/cpp-httplib"), ("json.hpp", "external/json")):
        target = source / relative
        target.mkdir(parents=True, exist_ok=True)
        shutil.copy2(inputs / name, target / name)
    vswhere = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft Visual Studio/Installer/vswhere.exe"
    installation = Path(subprocess.check_output([str(vswhere), "-latest", "-version", "[17.0,18.0)", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath"], text=True).strip())
    versions = sorted((installation / "VC/Tools/MSVC").iterdir(), reverse=True)
    tools = versions[0] / "bin/Hostx64/x64"
    exports = subprocess.check_output([str(tools / "dumpbin.exe"), "/exports", str(inputs / "onnxruntime-genai.dll")], text=True)
    names = re.findall(r"^\s+\d+\s+[0-9A-F]+\s+[0-9A-F]+\s+(Oga[A-Za-z0-9_]+)", exports, re.M)
    if len(names) < 40 or len(names) != len(set(names)):
        raise ValueError("Incomplete/ambiguous GenAI import API")
    definition = oga / "LLM/lib/onnxruntime-genai.def"
    definition.write_text("LIBRARY onnxruntime-genai.dll\nEXPORTS\n" + "\n".join(names), encoding="ascii")
    library = oga / "LLM/lib/onnxruntime-genai.lib"
    subprocess.run([str(tools / "lib.exe"), "/nologo", "/machine:x64", "/def:" + str(definition), "/out:" + str(library)], check=True)
    engine = source / "src/inference_engine.cpp"
    text = engine.read_text(encoding="utf-8-sig")
    start = text.index("void InferenceEngine::streamComplete(")
    end = text.index("\nint InferenceEngine::countTokens(", start)
    section = text[start:end]
    section = replace_once(section, "StreamCallback callback)", "StreamCallback callback, bool* output_limit_reached)")
    section = replace_once(section, "        if (client_disconnected) {", "        if (output_limit_reached) *output_limit_reached = generator->GetSequenceCount(0) >= total_max_length;\n        if (client_disconnected) {")
    header = source / "include/ryzenai/inference_engine.h"
    declaration = header.read_text(encoding="utf-8-sig")
    declaration = replace_once(declaration, "StreamCallback callback);", "StreamCallback callback, bool* output_limit_reached = nullptr);")
    header.write_text(declaration, encoding="utf-8")
    section = replace_once(section, "        size_t token_count = 0;", "        size_t token_count = 0;\n        size_t last_sequence_length = input_ids.size();")
    section = replace_once(section, "            int32_t new_token = all_tokens[num_tokens - 1];", "            // OGA may finish without appending EOS. Do not emit the prior token again.\n            if (num_tokens <= last_sequence_length) continue;\n            last_sequence_length = num_tokens;\n            int32_t new_token = all_tokens[num_tokens - 1];")
    engine.write_text(text[:start] + section + text[end:], encoding="utf-8")
    main = source / "src/main.cpp"
    text = "#define WIN32_LEAN_AND_MEAN\n#include <windows.h>\n" + main.read_text(encoding="utf-8-sig")
    initialization = r'''
    // Resolve only this application, the verified backend and Windows system DLLs.
    // GenAI is delay-loaded, so this runs before its first import is resolved.
    wchar_t backend_directory[32768];
    DWORD length = GetEnvironmentVariableW(L"INFINITY_OGA_DIRECTORY", backend_directory, 32768);
    if (length == 0 || length >= 32768 ||
        !SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_USER_DIRS | LOAD_LIBRARY_SEARCH_SYSTEM32) ||
        !AddDllDirectory(backend_directory)) {
        std::cerr << "Infinity native dependency directory is unavailable." << std::endl;
        return 2;
    }
'''
    text = replace_once(text, "int main(int argc, char* argv[]) {", "int main(int argc, char* argv[]) {\n" + initialization)
    main.write_text(text, encoding="utf-8")
    server = source / "src/server.cpp"
    text = server.read_text(encoding="utf-8-sig")
    marker = r'''    http_server_->Get("/infinity/backend", [](const httplib::Request&, httplib::Response& response) {
        response.set_content("{\"patch\":\"sequence-growth-v2\"}", "application/json");
    });
'''
    text = replace_once(text, "void RyzenAIServer::setupRoutes() {", "void RyzenAIServer::setupRoutes() {\n" + marker)
    response_start=text.index("void RyzenAIServer::handleResponses(")
    response_prefix=text[:response_start]
    text=text[response_start:]
    text = replace_once(text, "                        std::string full_response;", "                        std::string full_response;\n                        bool output_limit_reached = false;")
    text = replace_once(text, "                        );\n                        \n                        std::cout << \"[Server] Token generation completed, sending final events\"", "                        , &output_limit_reached);\n                        \n                        std::cout << \"[Server] Token generation completed, sending final events\"")
    old_finish = '                            "\\\"tool_choice\\\":\\\"auto\\\",\\\"tools\\\":[]}}\\n\\n";'
    new_finish = '                            "\\\"tool_choice\\\":\\\"auto\\\",\\\"tools\\\":[],\\\"finish_reason\\\":\\\"" + std::string(output_limit_reached ? "length" : "stop") + "\\\"}}\\n\\n";'
    text = replace_once(text, old_finish, new_finish)
    # The upstream hand-written escaping misses tabs and other JSON control bytes.
    first=text.index("                                // Escape special characters for JSON")
    last=text.index("                                // Accumulate unescaped token for final response", first)
    text=text[:first]+"                                const auto token_json = json(token).dump();\n                                const auto escaped_token = token_json.substr(1, token_json.size() - 2);\n\n"+text[last:]
    first=text.index("                        // Escape full_response for JSON")
    last=text.index("                        // Send response.completed event", first)
    text=text[:first]+"                        const auto full_json = json(full_response).dump();\n                        const auto escaped_full_response = full_json.substr(1, full_json.size() - 2);\n\n"+text[last:]
    server.write_text(response_prefix+text, encoding="utf-8")
    cmake = source / "CMakeLists.txt"
    text = cmake.read_text(encoding="utf-8-sig")
    text = replace_once(text, "add_executable(ryzenai-server ${SOURCES})", "add_executable(ryzenai-server ${SOURCES})\nset_target_properties(ryzenai-server PROPERTIES OUTPUT_NAME Infinity.RyzenHost)\ntarget_link_options(ryzenai-server PRIVATE /DELAYLOAD:onnxruntime-genai.dll)\ntarget_link_libraries(ryzenai-server PRIVATE delayimp)")
    cmake.write_text(text, encoding="utf-8")
    output.mkdir(parents=True, exist_ok=True)
    compiler_temp=work / "compiler-temp"
    compiler_temp.mkdir()
    environment=os.environ.copy()
    environment.update(TEMP=str(compiler_temp), TMP=str(compiler_temp))
    commands = [["cmake", "-S", str(source), "-B", str(work / "build"), "-G", "Visual Studio 17 2022", "-A", "x64", "-DOGA_ROOT=" + str(oga), "-DOGA_INCLUDE=" + str(oga / "LLM/include"), "-DOGA_LIB=" + str(library)], ["cmake", "--build", str(work / "build"), "--config", "Release", "--parallel", "6"]]
    with (output / "build.log").open("wb") as log:
        for command in commands:
            subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, env=environment, check=True)
    binary = output / "Infinity.RyzenHost.exe"
    shutil.copy2(work / "build/bin/Release/Infinity.RyzenHost.exe", binary)
    shutil.copy2(here / "THIRD_PARTY_NOTICES.txt", output)
    provenance = {"sourceRevision": revision, "upstream": "lemonade-sdk/ryzenai-server v1.7.0", "patch": "sequence-growth-v2", "inputs": pins, "sha256": hashlib.sha256(binary.read_bytes()).hexdigest(), "work": str(work)}
    (output / "backend-build.json").write_text(json.dumps(provenance, indent=2), encoding="utf-8")
    return binary


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inputs", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--revision", required=True)
    args = parser.parse_args()
    print(build(args.inputs.resolve(), args.output.resolve(), args.revision))
