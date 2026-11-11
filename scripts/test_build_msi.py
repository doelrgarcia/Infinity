"""Packaging guard tests only; never install an MSI on the host."""
from pathlib import Path
import json
import tempfile
import unittest
import xml.etree.ElementTree as ET
from build_msi import validate_report, package_path, author_files, digest, NS


class PackageGuards(unittest.TestCase):
    def setUp(self):
        self.root=Path(tempfile.mkdtemp(prefix="Infinity-msi-guard-"))
        self.app=self.root/"app"
        self.app.mkdir()
        contract=self.root/"scripts/verification_contract.json"
        contract.parent.mkdir()
        contract.write_text('{"fixture":["passed"]}')
        rows=[]
        for name in ["LocalAI.Desktop.exe","LocalAI.Core.dll","Native/Infinity.RyzenHost.exe"]:
            p=self.app/name;p.parent.mkdir(exist_ok=True);p.write_bytes(b"synthetic non-executing payload")
            rows.append({"path":name.replace("/","\\"),"sha256":digest(p)})
        self.report={"passed":True,"sourceRevision":"a"*40,"assertions":1,"work":str(self.root),"checks":[{"name":"fixture","exitCode":0,"assertions":1}],"sourceFiles":[{"path":"scripts/verification_contract.json","sha256":digest(contract)}],"packageFiles":rows}
        # Fixtures deliberately remain under the caller's owned TEMP for its
        # final checked leaf-by-leaf cleanup; no unrelated folder is removed.

    def test_verified_windows_paths_are_accepted(self):
        self.assertEqual(validate_report(self.report)[1],self.app)

    def test_partial_or_mutated_verification_is_rejected(self):
        for field,value in [("passed",False),("assertions",0),("sourceRevision","INDEX"),("checks",[])]:
            with self.subTest(field=field),self.assertRaises(ValueError):validate_report({**self.report,field:value})
        (self.root/"scripts/verification_contract.json").write_text('{}')
        with self.assertRaises(ValueError):validate_report(self.report)

    def test_modified_payload_is_rejected(self):
        (self.app/"LocalAI.Core.dll").write_bytes(b"changed")
        with self.assertRaises(ValueError):validate_report(self.report)

    def test_duplicate_separator_and_case_aliases_are_rejected(self):
        row=self.report["packageFiles"][-1]
        self.report["packageFiles"].append({**row,"path":row["path"].replace("\\","/").upper()})
        with self.assertRaises(ValueError):validate_report(self.report)

    def test_private_configuration_and_traversal_are_rejected(self):
        for name in ["appsettings.json","APPSETTINGS.JSON","../outside.txt",str(self.root/"outside.txt"),r"\\invalid.example\share\file"]:
            with self.subTest(name=name),self.assertRaises(ValueError):package_path(self.app,name)

    def test_generated_components_have_stable_ids_and_user_registry_keys(self):
        out=self.root/"Files.wxs";author_files(self.app,out);a=ET.parse(out)
        components=a.findall(f".//{{{NS}}}Component")
        self.assertEqual(len(components),3)
        self.assertTrue(all(c.find(f"{{{NS}}}RegistryValue").get("Root")=="HKCU" for c in components))
        before=[c.get("Guid") for c in components];author_files(self.app,out)
        self.assertEqual(before,[c.get("Guid") for c in ET.parse(out).findall(f".//{{{NS}}}Component")])

    def test_default_configuration_has_no_host_paths_or_credentials(self):
        path=Path(__file__).resolve().parents[1]/"installer/default-settings.json"
        config=json.loads(path.read_text())
        self.assertLessEqual(set(config),{"Models","LoadTimeoutSeconds","GenerationTimeoutSeconds"})
        self.assertEqual(len({p["Id"] for p in config["Models"]}),len(config["Models"]))
        for profile in config["Models"]:
            self.assertLessEqual(set(profile),{"Id","Label","Engine","Endpoint","ContextLimit"})


if __name__=="__main__":unittest.main()
