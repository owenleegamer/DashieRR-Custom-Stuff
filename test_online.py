"""Run download tests in a fresh synthetic folder."""
from pathlib import Path
import subprocess
import tempfile
import shutil

root=Path(__file__).resolve().parent
workspace=root.parent
compiler=Path('C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe')
package=(workspace/'dist' if (workspace/'custom-updater').exists() else root/'dist')/'DashieRR-Custom-Online-Setup.exe'
test_root=root/'test-runs';test_root.mkdir(exist_ok=True)
fixture=Path(tempfile.mkdtemp(prefix='online-',dir=test_root))
test_exe=fixture/'OnlineTests.exe'
reference=fixture/package.name
shutil.copy2(package,reference)
prefix=['rtk','proxy'] if shutil.which('rtk') else []
subprocess.run(prefix+[str(compiler),'/nologo','/target:exe','/platform:x64',
                '/reference:'+str(reference),'/out:'+str(test_exe),str(root/'OnlineTests.cs')],check=True)
subprocess.run(prefix+[str(test_exe),str(fixture)],check=True)
