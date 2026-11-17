using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Public repository is the trust root for updates. GitHub's asset digest is
// checked before the downloaded package is ever executed.
public sealed class ReleaseAsset {
    public Uri Url; public string Sha256; public long Size;
}

public static class OnlineDownload {
    public const string AssetName="DashieRR-Custom-Package.exe";
    public const long MaximumPackageSize=2L*1024*1024*1024;
    static bool Eq(string a,string b){return String.Equals(a,b,StringComparison.OrdinalIgnoreCase);}
    static bool Hex(string value){if(value==null||value.Length!=64)return false;foreach(char c in value)if(!Uri.IsHexDigit(c))return false;return true;}
    public static ReleaseAsset ParseLatest(string json,string owner,string repo){
        if(String.IsNullOrEmpty(json)||json.Length>2000000)throw new InvalidDataException("Invalid GitHub release information.");
        var root=new JavaScriptSerializer{MaxJsonLength=2000000,RecursionLimit=32}.DeserializeObject(json) as Dictionary<string,object>;
        object value;if(root==null||!root.TryGetValue("assets",out value))throw new InvalidDataException("The latest release has no download list.");
        object[] assets=value as object[];if(assets==null||assets.Length>1000)throw new InvalidDataException("Invalid GitHub release assets.");
        ReleaseAsset result=null;
        foreach(object item in assets){
            var obj=item as Dictionary<string,object>;object name;
            if(obj==null||!obj.TryGetValue("name",out name)||!Eq(name as string,AssetName))continue;
            if(result!=null)throw new InvalidDataException("The release contains duplicate packages.");
            object url,digest,size,state;
            if(!obj.TryGetValue("browser_download_url",out url)||!obj.TryGetValue("digest",out digest)||!obj.TryGetValue("size",out size)||!obj.TryGetValue("state",out state)||!Eq(state as string,"uploaded"))throw new InvalidDataException("The release package is incomplete.");
            string hash=digest as string;Uri uri;long length;
            if(hash==null||!hash.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase)||!Hex(hash.Substring(7)))throw new InvalidDataException("GitHub has not provided a valid package checksum yet.");
            if(!Int64.TryParse(Convert.ToString(size),out length)||length<=0||length>MaximumPackageSize)throw new InvalidDataException("Unexpected package size.");
            if(!Uri.TryCreate(url as string,UriKind.Absolute,out uri)||uri.Scheme!=Uri.UriSchemeHttps||!Eq(uri.Host,"github.com")||uri.Port!=443||!String.IsNullOrEmpty(uri.UserInfo)||
               !uri.AbsolutePath.StartsWith("/"+owner+"/"+repo+"/releases/download/",StringComparison.OrdinalIgnoreCase)||
               !uri.AbsolutePath.EndsWith("/"+AssetName,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The release points to an unexpected download location.");
            result=new ReleaseAsset{Url=uri,Sha256=hash.Substring(7).ToLowerInvariant(),Size=length};
        }
        if(result==null)throw new InvalidDataException("The latest release does not contain the custom package yet.");
        return result;
    }
    static HttpWebResponse Request(Uri url,bool api){
        ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
        Uri current=url;
        for(int i=0;i<6;i++){
            bool host=api?Eq(current.Host,"api.github.com"):Eq(current.Host,"github.com")||current.Host.EndsWith(".githubusercontent.com",StringComparison.OrdinalIgnoreCase);
            if(current.Scheme!=Uri.UriSchemeHttps||!host||current.Port!=443||!String.IsNullOrEmpty(current.UserInfo))throw new InvalidDataException("The download redirected to an unexpected site.");
            var request=(HttpWebRequest)WebRequest.Create(current);request.UserAgent="DashieRR-Custom-Installer";
            request.Timeout=30000;request.ReadWriteTimeout=120000;request.AllowAutoRedirect=false;
            if(api)request.Accept="application/vnd.github+json";
            var response=(HttpWebResponse)request.GetResponse();int code=(int)response.StatusCode;
            if(code<300||code>=400)return response;
            string location=response.Headers["Location"];response.Dispose();
            if(String.IsNullOrEmpty(location)||!Uri.TryCreate(current,location,out current))throw new InvalidDataException("The download redirect is invalid.");
        }
        throw new InvalidDataException("The download redirected too many times.");
    }
    public static ReleaseAsset Latest(string owner,string repo){
        var url=new Uri("https://api.github.com/repos/"+owner+"/"+repo+"/releases/latest");
        using(var response=Request(url,true))using(var stream=response.GetResponseStream())using(var reader=new StreamReader(stream,Encoding.UTF8)){
            var text=new StringBuilder();char[] buffer=new char[8192];int n;
            while((n=reader.Read(buffer,0,buffer.Length))>0){text.Append(buffer,0,n);if(text.Length>2000000)throw new InvalidDataException("The release response is too large.");}
            return ParseLatest(text.ToString(),owner,repo);
        }
    }
    public static string HashFile(string path){using(var f=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
    public static bool ValidCached(string path,ReleaseAsset asset){
        return File.Exists(path)&&new FileInfo(path).Length==asset.Size&&Eq(HashFile(path),asset.Sha256);
    }
    public static void CopyVerified(Stream input,string destination,ReleaseAsset asset,Action<long,long> progress,CancellationToken cancel){
        string parent=Path.GetDirectoryName(destination);Directory.CreateDirectory(parent);
        string temp=Path.Combine(parent,".download-"+Guid.NewGuid().ToString("N")+".part");bool committed=false;
        try{
            using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var sha=SHA256.Create()){
                byte[] buffer=new byte[128*1024];long total=0;int n;
                while((n=input.Read(buffer,0,buffer.Length))>0){
                    cancel.ThrowIfCancellationRequested();total+=n;if(total>asset.Size)throw new InvalidDataException("The download is larger than expected.");
                    output.Write(buffer,0,n);sha.TransformBlock(buffer,0,n,null,0);progress(total,asset.Size);
                }
                sha.TransformFinalBlock(new byte[0],0,0);string hash=BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();
                if(total!=asset.Size||!Eq(hash,asset.Sha256))throw new InvalidDataException("The download did not match GitHub's checksum.");
            }
            cancel.ThrowIfCancellationRequested();
            if(File.Exists(destination)){
                string backup=Path.Combine(parent,"previous-download-"+Guid.NewGuid().ToString("N")+".exe");File.Replace(temp,destination,backup,true);
            }else File.Move(temp,destination);
            committed=true;
        }finally{if(!committed&&File.Exists(temp))File.Delete(temp);}
    }
    public static string Acquire(ReleaseAsset asset,Action<long,long> progress,CancellationToken cancel){
        string vault=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DashieRRCustom");
        string installed=Path.Combine(vault,"DashieRR-Custom-Setup.exe");
        if(ValidCached(installed,asset))return installed;
        string cache=Path.Combine(vault,"downloads","package-"+asset.Sha256+".exe");
        if(ValidCached(cache,asset))return cache;
        using(var response=Request(asset.Url,false)){
            if(response.ContentLength>=0&&response.ContentLength!=asset.Size)throw new InvalidDataException("The download size differs from the release.");
            using(var stream=response.GetResponseStream())CopyVerified(stream,cache,asset,progress,cancel);
        }
        return cache;
    }
}

public sealed class OnlineWindow : Form {
    readonly Button install=new Button(),cancel=new Button();readonly Label status=new Label(),detail=new Label();readonly ProgressBar bar=new ProgressBar();
    CancellationTokenSource cancellation;bool busy;
    public OnlineWindow(){
        Text="DashieRR Custom Setup";ClientSize=new Size(540,225);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
        var title=new Label{Text="DashieRR Custom Setup",Font=new Font("Segoe UI",16,FontStyle.Bold),AutoSize=true,Left=20,Top=18};Controls.Add(title);
        detail.Text="Downloads the latest custom emotes, props and installer.";detail.SetBounds(24,58,492,35);Controls.Add(detail);
        status.Text="Ready to download.";status.SetBounds(24,101,492,25);Controls.Add(status);bar.SetBounds(24,132,492,18);Controls.Add(bar);
        install.Text="Download and install";install.SetBounds(335,170,180,32);install.Click+=Install;Controls.Add(install);
        cancel.Text="Cancel";cancel.SetBounds(235,170,90,32);cancel.Click+=(s,e)=>{if(busy)cancellation.Cancel();else Close();};Controls.Add(cancel);
        FormClosing+=(s,e)=>{if(busy){cancellation.Cancel();e.Cancel=true;}};
    }
    async void Install(object sender,EventArgs e){
        busy=true;install.Enabled=false;cancellation=new CancellationTokenSource();status.Text="Checking the latest release...";bar.Style=ProgressBarStyle.Marquee;
        try{
            var token=cancellation.Token;
            string file=await Task.Run(()=>{
                ReleaseAsset asset=OnlineDownload.Latest(InstallerConfig.Owner,InstallerConfig.Repository);
                token.ThrowIfCancellationRequested();
                return OnlineDownload.Acquire(asset,(done,total)=>{
                    if(!IsDisposed)BeginInvoke((Action)(()=>{bar.Style=ProgressBarStyle.Continuous;bar.Value=(int)(100*done/total);status.Text="Downloading: "+bar.Value+"%";}));
                },token);
            },token);
            status.Text="Verified. Opening the custom installer...";
            Process.Start(new ProcessStartInfo(file){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(file)});busy=false;Close();
        }catch(OperationCanceledException){status.Text="Download canceled.";}
         catch(Exception ex){status.Text="Unable to download the custom package.";MessageBox.Show(this,ex.Message,"DashieRR Custom Setup",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        finally{busy=false;install.Enabled=true;bar.Style=ProgressBarStyle.Continuous;cancellation.Dispose();}
    }
}
public static class OnlineProgram {
    [STAThread] public static int Main(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new OnlineWindow());return 0;}
}
