"""Read/extract an MSI without installing it, and verify every payload byte."""
import argparse
import ctypes as C
from ctypes import wintypes as W
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile


class Msi:
    def __init__(self,path):
        self.api=C.WinDLL("msi")
        for name,types in {"MsiOpenDatabaseW":[W.LPCWSTR,W.LPCWSTR,C.POINTER(W.UINT)],"MsiDatabaseOpenViewW":[W.UINT,W.LPCWSTR,C.POINTER(W.UINT)],"MsiViewExecute":[W.UINT,W.UINT],"MsiViewFetch":[W.UINT,C.POINTER(W.UINT)],"MsiRecordGetStringW":[W.UINT,W.UINT,W.LPWSTR,C.POINTER(W.DWORD)],"MsiRecordReadStream":[W.UINT,W.UINT,C.c_void_p,C.POINTER(W.DWORD)],"MsiCloseHandle":[W.UINT],"MsiOpenPackageExW":[W.LPCWSTR,W.DWORD,C.POINTER(W.UINT)],"MsiSetPropertyW":[W.UINT,W.LPCWSTR,W.LPCWSTR],"MsiGetPropertyW":[W.UINT,W.LPCWSTR,W.LPWSTR,C.POINTER(W.DWORD)],"MsiDoActionW":[W.UINT,W.LPCWSTR],"MsiEvaluateConditionW":[W.UINT,W.LPCWSTR],"MsiSetInternalUI":[W.UINT,C.c_void_p]}.items():
            fn=getattr(self.api,name);fn.argtypes=types;fn.restype=W.UINT
        handle=W.UINT();self.check(self.api.MsiOpenDatabaseW(str(path),None,C.byref(handle)));self.handle=handle.value
    @staticmethod
    def check(code):
        if code:raise OSError("Windows Installer read failed: "+str(code))
    def text(self,record,index):
        size=W.DWORD();code=self.api.MsiRecordGetStringW(record,index,None,C.byref(size))
        if code not in (0,234):self.check(code)
        buffer=C.create_unicode_buffer(size.value+1);size=W.DWORD(len(buffer));self.check(self.api.MsiRecordGetStringW(record,index,buffer,C.byref(size)));return buffer.value
    def records(self,sql):
        view=W.UINT();self.check(self.api.MsiDatabaseOpenViewW(self.handle,sql,C.byref(view)))
        try:
            self.check(self.api.MsiViewExecute(view.value,0))
            while True:
                record=W.UINT();code=self.api.MsiViewFetch(view.value,C.byref(record))
                if code==259:break
                self.check(code)
                try:yield record.value
                finally:self.api.MsiCloseHandle(record.value)
        finally:self.api.MsiCloseHandle(view.value)
    def rows(self,sql,fields):return [[self.text(record,i) for i in range(1,fields+1)] for record in self.records(sql)]
    def stream(self,name,path):
        sql="SELECT `Data` FROM `_Streams` WHERE `Name`='"+name.replace("'","''")+"'"
        with path.open("wb") as output:
            found=False
            for record in self.records(sql):
                if found:raise ValueError("Duplicate embedded stream")
                found=True
                while True:
                    buffer=C.create_string_buffer(65536);size=W.DWORD(len(buffer));self.check(self.api.MsiRecordReadStream(record,1,buffer,C.byref(size)))
                    if not size.value:break
                    output.write(buffer.raw[:size.value])
            if not found:raise ValueError("Missing embedded stream")
    def close(self):self.api.MsiCloseHandle(self.handle)
    def probe_configuration_search(self,msi,root):
        """Run only AppSearch against an owned fixture, never ExecuteAction."""
        prior=self.api.MsiSetInternalUI(2,None);results=[]
        try:
            # AppSearch caches signature results within one installation. Use
            # independent sessions for the two independent installation cases.
            for present in (True,False):
                profile=root/("profile-existing" if present else "profile-missing");config=profile/"Programs/Infinity/app/appsettings.json";config.parent.mkdir(parents=True)
                if present:config.write_text('{"SyntheticPreservedSetting":true}')
                session=W.UINT();self.check(self.api.MsiOpenPackageExW(str(msi),1,C.byref(session)))
                try:
                    self.check(self.api.MsiSetPropertyW(session.value,"LocalAppDataFolder",str(profile)+os.sep));self.check(self.api.MsiDoActionW(session.value,"AppSearch"))
                    results.append(self.api.MsiEvaluateConditionW(session.value,"NOT EXISTING_CONFIGURATION")== (0 if present else 1))
                finally:self.api.MsiCloseHandle(session.value)
            return tuple(results)
        finally:
            self.api.MsiSetInternalUI(prior,None)


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument("--build",type=Path,required=True);p.add_argument("--output",type=Path,required=True);args=p.parse_args()
    root=args.output.resolve();root.relative_to(Path(tempfile.gettempdir()).resolve())
    if root.exists():raise ValueError("Use a fresh owned extraction folder")
    root.mkdir(parents=True);build=json.loads(args.build.read_text());msi=Path(build["msi"]);assert hashlib.sha256(msi.read_bytes()).hexdigest()==build["sha256"]
    database=Msi(msi);checks={}
    try:
        properties=dict(database.rows("SELECT `Property`,`Value` FROM `Property`",2));checks["productVersionMatchesBuild"]=properties["ProductVersion"]==build["version"];checks["sourceRevisionMatchesVerifiedApplication"]=properties["INFINITY_SOURCE_REVISION"]==build["applicationSourceRevision"];checks["packageIsPerUser"]=properties.get("ALLUSERS","")==""
        components={r[0]:r for r in database.rows("SELECT `Component`,`Directory_`,`Attributes`,`Condition`,`KeyPath` FROM `Component`",5)};files=database.rows("SELECT `File`,`Component_`,`FileName` FROM `File`",3);directories={r[0]:(r[1],r[2].split(":")[0].split("|")[-1]) for r in database.rows("SELECT `Directory`,`Directory_Parent`,`DefaultDir` FROM `Directory`",3)}
        def folder(key):
            if key=="INSTALLFOLDER":return Path()
            parent,name=directories[key];return folder(parent)/name
        mapping={}
        for identifier,component,name in files:
            if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_.]*",identifier):raise ValueError("Unsafe cabinet member id")
            relative=folder(components[component][1])/name.split("|")[-1]
            if relative.is_absolute() or ".." in relative.parts:raise ValueError("Unsafe MSI destination")
            mapping[identifier]=relative
        for cabinet, in database.rows("SELECT `Cabinet` FROM `Media`",1):
            if not cabinet.startswith("#"):raise ValueError("Installer must be self contained")
            name=cabinet[1:]
            if Path(name).name!=name:raise ValueError("Unsafe cabinet name")
            cab=root/name;database.stream(name,cab);dest=root/"files";dest.mkdir(exist_ok=True)
            subprocess.run(["expand.exe","-F:*",str(cab),str(dest)],stdout=subprocess.DEVNULL,check=True)
        payload=Path(build["payload"]);mismatches=[]
        for identifier,relative in mapping.items():
            extracted=root/"files"/identifier;original=payload/relative
            if hashlib.sha256(extracted.read_bytes()).digest()!=hashlib.sha256(original.read_bytes()).digest():mismatches.append(str(relative))
        checks["allEmbeddedPayloadBytesMatch"]=not mismatches
        checks["completeFileInventory"]=set(mapping.values())=={f.relative_to(payload) for f in payload.rglob("*") if f.is_file()}
        settings=components["PersistentSettings"];checks["configurationIsPermanentAndNeverOverwritten"]=int(settings[2])&16!=0 and int(settings[2])&128!=0;checks["preexistingSettingsSkipDefaultInstallation"]=settings[3]=="NOT EXISTING_CONFIGURATION"
        registry=database.rows("SELECT `Root` FROM `Registry`",1);checks["registryWritesAreCurrentUserOnly"]=all(v=="1" for v, in registry)
        shortcuts={r[0]:r[1:] for r in database.rows("SELECT `Shortcut`,`Target`,`Arguments` FROM `Shortcut`",3)}
        checks["normalShortcutRunsFirstLaunchDependencySetup"]=shortcuts["InfinityShortcut"]==["[INSTALLFOLDER]LocalAI.Desktop.exe",""]
        config=json.loads((root/"files/DefaultSettings").read_text());checks["configurationContainsPublicDefaultsOnly"]=set(config)<= {"Models","LoadTimeoutSeconds","GenerationTimeoutSeconds"} and all(set(v)<={"Id","Label","Engine","Endpoint","ContextLimit"} for v in config["Models"])
        preserved,missing=database.probe_configuration_search(msi,root);checks["realAppSearchProtectsPreexistingSettings"]=preserved;checks["realAppSearchAllowsMissingDefaults"]=missing
        result={"passed":all(checks.values()),"checks":checks,"payloadFilesVerified":len(mapping),"msiSha256":build["sha256"],"scope":"Read-only Windows Installer database inspection and exact cabinet extraction; no host MSI install performed."};(root/"result.json").write_text(json.dumps(result,indent=2));print(json.dumps(result));assert result["passed"]
    finally:database.close()


if __name__=="__main__":main()
