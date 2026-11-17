"""Exercise release sequencing without contacting GitHub or uploading files."""
import tempfile
import unittest
from pathlib import Path
import publish_release as p

class FakeGitHub(p.GitHub):
    def __init__(self):
        super().__init__('never-log-this-test-token');self.events=[];self.draft=True;self.assets=[]
    def request(self,method,path,body=None,host=p.API,content_type='application/json',length=None):
        if '/releases/tags/' in path:return None
        if '/releases/' in path and method=='GET':return {'id':7,'assets':self.assets,'draft':self.draft}
        raise AssertionError((method,path))
    def json(self,method,path,data):
        self.events.append((method,data.copy()))
        if method=='POST':return {'id':7,'draft':True}
        if method=='PATCH':self.draft=False;return {'id':7,'draft':False,'html_url':'https://github.com/test/release'}
        raise AssertionError(method)
    def upload(self,release_id,name,path,expected):
        assert self.draft, 'Assets must upload before publication'
        assert p.digest(path)==expected
        self.events.append(('upload',name))

class Tests(unittest.TestCase):
    def test_draft_then_both_assets_then_publish(self):
        with tempfile.TemporaryDirectory() as t:
            package=Path(t)/'package';package.write_bytes(b'package')
            setup=Path(t)/'setup';setup.write_bytes(b'setup')
            api=FakeGitHub();link=api.release(package,setup,'2026.11.17-custom.1')
            self.assertEqual(link,'https://github.com/test/release')
            self.assertEqual([x[0] for x in api.events],['POST','upload','upload','PATCH'])
            self.assertTrue(api.events[0][1]['draft'])
    def test_private_repo_is_not_modified(self):
        class Private(p.GitHub):
            def request(self,*args,**kwargs):return {'private':True,'owner':{'login':p.OWNER}}
        with self.assertRaises(RuntimeError):Private('token').ensure_repo()
    def test_existing_different_asset_is_not_replaced(self):
        class Existing(p.GitHub):
            def request(self,*args,**kwargs):return {'assets':[{'name':'x','digest':'sha256:'+'0'*64}]}
        with tempfile.TemporaryDirectory() as t:
            f=Path(t)/'x';f.write_bytes(b'x')
            with self.assertRaises(RuntimeError):Existing('token').upload(1,'x',f,p.digest(f))

if __name__=='__main__':unittest.main()
