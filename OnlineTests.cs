using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

public static class OnlineTests {
    static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static string Json(string repo,string asset,string hash,long size){
        return "{\"assets\":[{\"name\":\""+asset+"\",\"browser_download_url\":\"https://github.com/user/"+repo+"/releases/download/v1/"+asset+"\",\"digest\":\"sha256:"+hash+"\",\"size\":"+size+",\"state\":\"uploaded\"}]}";
    }
    public static int Main(string[] args){
        try{
            string dir=args[0];Directory.CreateDirectory(dir);byte[] bytes=Encoding.UTF8.GetBytes("verified release payload");string hash=Hash(bytes),asset=OnlineDownload.AssetName;
            string json=Json("repo",asset,hash,bytes.Length);ReleaseAsset release=OnlineDownload.ParseLatest(json,"user","repo");
            Assert(release.Size==bytes.Length&&release.Sha256==hash,"Release metadata was not parsed");
            bool rejected=false;try{OnlineDownload.ParseLatest(json,"other","repo");}catch(InvalidDataException){rejected=true;}Assert(rejected,"Wrong repository was accepted");
            rejected=false;try{OnlineDownload.ParseLatest(json.Replace("https://github.com/","http://github.com/"),"user","repo");}catch(InvalidDataException){rejected=true;}Assert(rejected,"Insecure download URL was accepted");
            rejected=false;try{OnlineDownload.ParseLatest(json.Replace("sha256:"+hash,"sha256:invalid"),"user","repo");}catch(InvalidDataException){rejected=true;}Assert(rejected,"Invalid digest was accepted");
            rejected=false;try{OnlineDownload.ParseLatest(json.Replace("\"size\":"+bytes.Length,"\"size\":0"),"user","repo");}catch(InvalidDataException){rejected=true;}Assert(rejected,"Empty package was accepted");
            string target=Path.Combine(dir,"package.exe");File.WriteAllText(target,"existing file");long progress=0;
            using(var data=new MemoryStream(bytes))OnlineDownload.CopyVerified(data,target,release,(done,total)=>progress=done,CancellationToken.None);
            Assert(OnlineDownload.ValidCached(target,release)&&progress==bytes.Length,"Verified download was not committed");
            Assert(Directory.GetFiles(dir,"previous-download-*.exe").Length==1,"Existing file was not preserved");
            byte[] changed=Encoding.UTF8.GetBytes("unverified release stuff");rejected=false;
            try{using(var data=new MemoryStream(changed))OnlineDownload.CopyVerified(data,target,release,(done,total)=>{},CancellationToken.None);}catch(InvalidDataException){rejected=true;}
            Assert(rejected&&OnlineDownload.ValidCached(target,release),"Corrupt download replaced the verified package");
            var cancel=new CancellationTokenSource();cancel.Cancel();rejected=false;
            try{using(var data=new MemoryStream(bytes))OnlineDownload.CopyVerified(data,target,release,(done,total)=>{},cancel.Token);}catch(OperationCanceledException){rejected=true;}
            Assert(rejected&&OnlineDownload.ValidCached(target,release),"Cancellation damaged the verified package");
            Assert(Directory.GetFiles(dir,"*.part").Length==0,"Partial downloads were not cleaned up");
            Console.WriteLine("Online installer passed: release identity, HTTPS, digest, size, cache, verified download, preserved files, corruption and cancellation.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
