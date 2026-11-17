using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;

// Read-only download check against the published release; never runs it.
public static class LiveDownloadTest {
    public static int Main(string[] args){
        try{
            var asset=OnlineDownload.Latest(InstallerConfig.Owner,InstallerConfig.Repository);
            Console.WriteLine("Latest release metadata verified. Package size: "+asset.Size+" bytes.");
            if(args.Length>0){
                var request=typeof(OnlineDownload).GetMethod("Request",BindingFlags.Static|BindingFlags.NonPublic);
                using(var response=(HttpWebResponse)request.Invoke(null,new object[]{asset.Url,false}))
                using(var stream=response.GetResponseStream()){
                    long next=50L*1024*1024;
                    OnlineDownload.CopyVerified(stream,args[0],asset,(done,total)=>{
                        if(done>=next){Console.WriteLine("Downloaded "+(done/1048576)+" / "+(total/1048576)+" MiB.");next=done+50L*1024*1024;}
                    },CancellationToken.None);
                }
                Console.WriteLine("Downloaded package matches GitHub's checksum.");
            }
            return 0;
        }catch(Exception e){Console.Error.WriteLine(e.GetType().Name+": "+(e.InnerException??e).Message);return 1;}
    }
}
