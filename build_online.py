"""Build the small bootstrapper; the large package is a GitHub release asset."""
import argparse
import shutil
import subprocess
from pathlib import Path

root=Path(__file__).resolve().parent
workspace=root.parent
parser=argparse.ArgumentParser()
parser.add_argument('--output',type=Path,default=(workspace/'dist' if (workspace/'custom-updater').exists() else root/'dist')/'DashieRR-Custom-Online-Setup.exe')
output=parser.parse_args().output.resolve()
compiler=Path('C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe')
icon=root/'icon.ico'
if not icon.exists():icon=workspace/'emote-import/installer-analysis/app/icon.ico'
output.parent.mkdir(parents=True,exist_ok=True)
prefix=['rtk','proxy'] if shutil.which('rtk') else []
subprocess.run(prefix+[str(compiler),'/nologo','/target:winexe','/platform:x64','/optimize+',
                '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll',
                '/win32icon:'+str(icon),'/out:'+str(output),str(root/'OnlineInstaller.cs'),str(root/'InstallerConfig.cs')],check=True)
print('Built',output,'size',output.stat().st_size,'bytes',flush=True)
