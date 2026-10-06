"""Task-local environment; no GUI, normal profile, shared NuGet cache or system SQLite."""
import os, pathlib, subprocess, sys
root = pathlib.Path(__file__).resolve().parents[2]
env = os.environ.copy()
for key, sub in {"DOTNET_CLI_HOME":"cli", "NUGET_PACKAGES":"nuget", "NUGET_HTTP_CACHE_PATH":"nuget-http", "TMPDIR":"tmp", "TMP":"tmp", "TEMP":"tmp", "DOTNET_BUNDLE_EXTRACT_BASE_DIR":"native-extract"}.items():
    p = root / "work" / sub
    p.mkdir(parents=True, exist_ok=True)
    env[key] = str(p)
env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1", DOTNET_NOLOGO="1")
sdk = root / "work/toolchain/dotnet/dotnet"
exe = str(sdk) if sdk.exists() else "dotnet"
cmd = [exe, *sys.argv[1:]]
(root / "work/evidence").mkdir(parents=True, exist_ok=True)
import json, datetime
stamp=datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
log_path=root/"work/evidence"/(stamp+"-dotnet.log")
with (root / "work/evidence/commands.jsonl").open("a") as f:
    f.write(json.dumps({"utc":datetime.datetime.now(datetime.timezone.utc).isoformat(), "cwd":str(root), "argv":cmd, "task_environment":{k:env[k] for k in ["DOTNET_CLI_HOME","NUGET_PACKAGES","NUGET_HTTP_CACHE_PATH","TMPDIR"]}})+"\n")
with log_path.open("w") as log:
    p=subprocess.Popen(cmd,cwd=root,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
    for line in p.stdout:
        print(line,end="",flush=True);log.write(line);log.flush()
    code=p.wait()
with (root/"work/evidence/commands.jsonl").open("a") as f:
    f.write(json.dumps({"completedUtc":datetime.datetime.now(datetime.timezone.utc).isoformat(),"exitCode":code,"log":str(log_path)})+"\n")
sys.exit(code)
