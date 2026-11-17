"""Publish a verified custom package and small installer to GitHub Releases.

Authentication uses Git Credential Manager; credentials never appear in URLs,
command arguments, release files or logs. Existing releases/assets are never
deleted or replaced. A failed upload leaves a draft that can be resumed.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
import time

ROOT=Path(__file__).resolve().parent
WORKSPACE=ROOT.parent
OWNER='owenleegamer'
REPOSITORY='DashieRR-Custom-Stuff'
API='https://api.github.com'
UPLOAD='https://uploads.github.com'
ASSET_PACKAGE='DashieRR-Custom-Package.exe'
ASSET_SETUP='DashieRR-Custom-Setup.exe'
SOURCE_FILES=['.gitignore','README.md','OnlineInstaller.cs','InstallerConfig.cs','build_online.py',
              'publish_release.py','test_online.py','OnlineTests.cs','test_publish.py','icon.ico']

def digest(path):
    h=hashlib.sha256()
    with Path(path).open('rb') as f:
        for chunk in iter(lambda:f.read(1024*1024),b''):h.update(chunk)
    return h.hexdigest()

def credential():
    env=os.environ.copy();env['GCM_INTERACTIVE']='Never';env['GIT_TERMINAL_PROMPT']='0'
    cmd=(['rtk','proxy'] if shutil.which('rtk') else [])+['git','credential','fill']
    result=subprocess.run(cmd,input=f'protocol=https\nhost=github.com\nusername={OWNER}\n\n',text=True,
                          stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=env)
    if result.returncode:raise RuntimeError('GitHub authorization is unavailable. Sign in with Git Credential Manager first.')
    fields=dict(line.split('=',1) for line in result.stdout.splitlines() if '=' in line)
    if fields.get('username','').lower()!=OWNER.lower() or not fields.get('password'):
        raise RuntimeError('The saved GitHub authorization is for a different account.')
    return fields['password']

class GitHub:
    def __init__(self,token):self.token=token
    def request(self,method,path,body=None,host=API,content_type='application/json',length=None):
        if host not in (API,UPLOAD) or not path.startswith('/'):raise ValueError('Unexpected GitHub API destination')
        headers={'Authorization':'Bearer '+self.token,'User-Agent':'DashieRR-Custom-Release',
                 'Accept':'application/vnd.github+json','X-GitHub-Api-Version':'2022-11-28'}
        if body is not None:
            headers['Content-Type']=content_type
            if length is not None:headers['Content-Length']=str(length)
        request=urllib.request.Request(host+path,data=body,method=method,headers=headers)
        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self,req,fp,code,msg,headers,newurl):return None
        try:
            with urllib.request.build_opener(NoRedirect).open(request,timeout=600) as response:
                raw=response.read(4*1024*1024+1)
                if len(raw)>4*1024*1024:raise RuntimeError('GitHub response is too large')
                return json.loads(raw.decode('utf-8')) if raw else None
        except urllib.error.HTTPError as e:
            if e.code==404 and method=='GET':return None
            raise RuntimeError(f'GitHub API {method} failed with HTTP {e.code}.') from None
    def json(self,method,path,data):
        raw=json.dumps(data,separators=(',',':')).encode('utf-8')
        return self.request(method,path,raw,length=len(raw))
    def verify_account(self):
        user=self.request('GET','/user')
        if not user or user.get('login','').lower()!=OWNER.lower():raise RuntimeError('GitHub account does not match the chosen repository owner.')
        print('Authorized GitHub account:',user['login'],flush=True)
    def ensure_repo(self):
        path=f'/repos/{OWNER}/{REPOSITORY}'
        repo=self.request('GET',path)
        if repo:
            if repo.get('private') or repo.get('owner',{}).get('login','').lower()!=OWNER.lower():
                raise RuntimeError('The named repository exists but is not the requested public repository.')
            print('Using existing public repository',repo['html_url'],flush=True);return repo
        repo=self.json('POST','/user/repos',{'name':REPOSITORY,'private':False,'auto_init':False,
                     'description':'Unofficial DashieRR custom emote installer and update releases.'})
        if not repo or repo.get('private') or repo.get('owner',{}).get('login','').lower()!=OWNER.lower():raise RuntimeError('GitHub returned an unexpected repository.')
        print('Created public repository',repo['html_url'],flush=True);return repo
    def upload(self,release_id,name,path,expected):
        release_path=f'/repos/{OWNER}/{REPOSITORY}/releases/{release_id}'
        release=self.request('GET',release_path);assets=[a for a in release.get('assets',[]) if a.get('name')==name]
        if assets:
            if len(assets)==1 and (assets[0].get('digest') or '').lower()=='sha256:'+expected:
                print('Verified existing release asset',name,flush=True);return
            raise RuntimeError('A different asset with this name already exists; it was not changed: '+name)
        class Reader:
            def __init__(self,f,total):self.f=f;self.total=total;self.readbytes=0;self.next=25*1024*1024
            def read(self,size=-1):
                block=self.f.read(size);self.readbytes+=len(block)
                if self.readbytes>=self.next:
                    print('Uploading',name,round(self.readbytes/1048576),'/',round(self.total/1048576),'MiB',flush=True);self.next=self.readbytes+25*1024*1024
                return block
        size=Path(path).stat().st_size;encoded=urllib.parse.quote(name,safe='')
        with Path(path).open('rb') as file:
            result=self.request('POST',f'/repos/{OWNER}/{REPOSITORY}/releases/{release_id}/assets?name={encoded}',Reader(file,size),
                                host=UPLOAD,content_type='application/octet-stream',length=size)
        if result and result.get('name')==name and result.get('size')==size and not result.get('digest'):
            for _ in range(12):
                time.sleep(2)
                latest=self.request('GET',release_path)
                found=[a for a in latest.get('assets',[]) if a.get('name')==name]
                if len(found)==1 and found[0].get('digest'):result=found[0];break
        if not result or result.get('name')!=name or result.get('size')!=size or (result.get('digest') or '').lower()!='sha256:'+expected:
            raise RuntimeError('GitHub did not confirm the expected checksum for '+name+'. The release remains a draft.')
        print('Uploaded and verified',name,flush=True)
    def release(self,package,setup,version):
        package_sha=digest(package);setup_sha=digest(setup)
        tag='v'+version+'-'+package_sha[:8];base=f'/repos/{OWNER}/{REPOSITORY}'
        release=self.request('GET',base+'/releases/tags/'+urllib.parse.quote(tag,safe=''))
        if not release:
            release=self.json('POST',base+'/releases',{'tag_name':tag,'name':'DashieRR Custom '+version,
                'body':'Download **DashieRR-Custom-Setup.exe** for the small installer. It downloads and verifies the full custom package. A matching DashieRR game installation is required.','draft':True,'prerelease':False})
            print('Created draft release',tag,flush=True)
        if not release or not isinstance(release.get('id'),int):raise RuntimeError('GitHub did not return a release ID.')
        release_id=release['id']
        self.upload(release_id,ASSET_PACKAGE,package,package_sha)
        self.upload(release_id,ASSET_SETUP,setup,setup_sha)
        if release.get('draft'):
            release=self.json('PATCH',base+'/releases/'+str(release_id),{'draft':False})
        print('Published release',release['html_url'],flush=True)
        return release['html_url']

def sync_source():
    prefix=['rtk','proxy'] if shutil.which('rtk') else []
    def git(*args,check=True):
        return subprocess.run(prefix+['git',*args],cwd=ROOT,check=check,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
    icon=ROOT/'icon.ico'
    if not icon.exists():
        original=WORKSPACE/'emote-import/installer-analysis/app/icon.ico'
        if not original.exists():raise RuntimeError('Installer icon is missing.')
        shutil.copy2(original,icon)
    for name in SOURCE_FILES:
        if not (ROOT/name).is_file():raise RuntimeError('Missing repository source: '+name)
    if not (ROOT/'.git').exists():git('init','--initial-branch=main')
    expected=f'https://github.com/{OWNER}/{REPOSITORY}.git'
    remote=git('remote','get-url','origin',check=False)
    if remote.returncode:git('remote','add','origin',expected)
    elif remote.stdout.strip().lower()!=expected.lower():raise RuntimeError('The local repository points to a different remote; nothing was pushed.')
    before=git('diff','--cached','--name-only').stdout.splitlines()
    if any(name not in SOURCE_FILES for name in before):raise RuntimeError('Other staged files are present; they were not committed.')
    git('add','--',*SOURCE_FILES)
    if git('diff','--cached','--quiet',check=False).returncode:
        git('-c',f'user.name={OWNER}','-c',f'user.email={OWNER}@users.noreply.github.com',
            'commit','-m','Update online installer and release tools')
    result=git('push','-u','origin','HEAD:refs/heads/main',check=False)
    if result.returncode:raise RuntimeError('Could not push installer source. The remote may have new changes; no force push was attempted.')
    print('Published installer source to the repository.',flush=True)

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--check',action='store_true',help='Check authorization without changing GitHub')
    parser.add_argument('--init',action='store_true',help='Create the requested public repository if needed')
    parser.add_argument('--sync',action='store_true',help='Publish the allowlisted installer source')
    parser.add_argument('--publish',action='store_true',help='Publish the current custom package')
    parser.add_argument('--package',type=Path,default=WORKSPACE/'dist/DashieRR-Custom-Setup.exe')
    parser.add_argument('--version',help='Release version; defaults to the local custom package manifest')
    args=parser.parse_args()
    if not (args.check or args.init or args.sync or args.publish):parser.error('Choose --check, --init, --sync or --publish')
    github=GitHub(credential());github.verify_account()
    if args.init:github.ensure_repo()
    if args.sync or args.publish:sync_source()
    if args.publish:
        if not args.init and not github.request('GET',f'/repos/{OWNER}/{REPOSITORY}'):
            raise RuntimeError('The public repository does not exist yet.')
        setup=(WORKSPACE/'dist' if (WORKSPACE/'custom-updater').exists() else ROOT/'dist')/'DashieRR-Custom-Online-Setup.exe'
        subprocess.run((["rtk","proxy"] if shutil.which('rtk') else [])+[sys.executable,str(ROOT/'build_online.py'),'--output',str(setup)],check=True)
        version=args.version
        if not version:
            manifest=json.loads((WORKSPACE/'custom-updater/package-manifest.json').read_text(encoding='utf-8'));version=manifest['version']
        if not all(c.isalnum() or c in '.-_' for c in version) or not version:raise ValueError('Invalid release version')
        github.release(args.package,setup,version)

if __name__=='__main__':
    try:main()
    except (OSError,RuntimeError,ValueError,urllib.error.URLError) as e:
        print('Release was not published:',str(e),file=sys.stderr);sys.exit(1)
