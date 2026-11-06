"""Run sequential, reproducible managed checks under a bounded four-processor worker pool."""
from pathlib import Path
import os
import subprocess
import sys

root = Path(__file__).resolve().parents[3]
phase = sys.argv[1] if len(sys.argv) > 1 else 'after'
args = sys.argv[2:] or ['1200', '1']
out = root / 'Temp/landmark-realism' / phase
env = dict(os.environ, DOTNET_TieredCompilation='0', DOTNET_PROCESSOR_COUNT='4', DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_GENERATE_ASPNET_CERTIFICATE='false')
dotnet = 'C:/Users/ali10/Unity/Editors/6000.6.7f1/Editor/Data/DotNetSdk/dotnet.exe'
result = subprocess.run([dotnet, str(out / 'bin/Release/net8.0/harness.dll'), *args], cwd=root, env=env)
sys.exit(result.returncode)
