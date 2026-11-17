using System;
using System.IO;
// The GitHub CLI release exists but intentionally has no DashieRR package.
public static class NetworkSmoke {
    public static int Main(){
        try{OnlineDownload.Latest("cli","cli");Console.Error.WriteLine("Unexpected asset match.");return 1;}
        catch(InvalidDataException e){
            if(e.Message.Contains("does not contain the custom package")){Console.WriteLine("GitHub HTTPS release metadata is reachable and unrelated assets are rejected.");return 0;}
            Console.Error.WriteLine(e.Message);return 1;
        }catch(Exception e){Console.Error.WriteLine(e.GetType().Name+": "+e.Message);return 1;}
    }
}
