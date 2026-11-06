"""Record the final API compilation and transaction checks sequentially, with bounded worker counts."""
from pathlib import Path
import subprocess
import sys

HERE=Path(__file__).resolve().parent
logs=[]
for script in ('prepare.py','streaming_api.py','upload_check.py'):
    result=subprocess.run([sys.executable,'-I',str(HERE/script)],capture_output=True,text=True)
    logs.append(script+'\n'+result.stdout+result.stderr)
    print(script+': '+str(result.returncode),flush=True)
    (HERE/'Review/verification.txt').write_bytes('\n'.join(logs).replace('\r\n','\n').replace('\n','\r\n').encode())
    result.check_returncode()
