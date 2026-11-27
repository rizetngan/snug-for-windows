using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Snug.Windows.Services;

static async Task Write(Stream stream,object message,CancellationToken ct)
{
    var body=JsonSerializer.SerializeToUtf8Bytes(message);
    await stream.WriteAsync(BitConverter.GetBytes(body.Length),ct);
    await stream.WriteAsync(body,ct);
    await stream.FlushAsync(ct);
}
static async Task<JsonDocument> Read(Stream stream,CancellationToken ct)
{
    var header=new byte[4];await stream.ReadExactlyAsync(header,ct);
    var length=BitConverter.ToInt32(header);
    if(length is <=0 or >32768)throw new IOException("bad frame length");
    var body=new byte[length];await stream.ReadExactlyAsync(body,ct);
    return JsonDocument.Parse(body);
}
static void Check(bool pass,string label)
{
    Console.WriteLine((pass?"PASS ":"FAIL ")+label);
    if(!pass)throw new Exception(label);
}
var type=typeof(WindowManager).Assembly.GetType("Snug.Windows.Services.BrowserZoomBridge")!;
using var bridge=(IDisposable)Activator.CreateInstance(type,nonPublic:true)!;
type.GetMethod("Start")!.Invoke(bridge,null);
var psi=new ProcessStartInfo(args[0]){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
using var host=Process.Start(psi)!;
using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
var ct=timeout.Token;
try
{
    await Write(host.StandardInput.BaseStream,new {kind="hello",version=1,browser="chrome"},ct);
    using var ready=await Read(host.StandardOutput.BaseStream,ct);
    Check(ready.RootElement.GetProperty("kind").GetString()=="ready","real native relay and current-user pipe handshake");
    object? client=null;
    var clients=type.GetField("_clients",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(bridge)!;
    for(int i=0;i<20 && client is null;i++)
    {
        var values=(IEnumerable)clients.GetType().GetProperty("Values")!.GetValue(clients)!;
        client=values.Cast<object>().FirstOrDefault();
        if(client is null)await Task.Delay(50,ct);
    }
    Check(client!=null,"bridge registered the connected browser profile");
    var method=client!.GetType().GetMethod("AskAsync")!;
    var request=(Task)method.Invoke(client,new object?[]{"read",10,null,null,ct})!;
    using var req=await Read(host.StandardOutput.BaseStream,ct);
    var root=req.RootElement;
    Check(root.GetProperty("op").GetString()=="read" && root.GetProperty("windowId").GetInt32()==10,"request frame targets bound browser window");
    await Write(host.StandardInput.BaseStream,new {id=root.GetProperty("id").GetInt32(),ok=true,windowId=10,tabId=102,factor=1.0,focused=true},ct);
    await request.WaitAsync(ct);
    var result=request.GetType().GetProperty("Result")!.GetValue(request);
    Check(result!=null && (double)result.GetType().GetProperty("Factor")!.GetValue(result)! ==1.0,"actual 100% response returns through native relay");
    Check((int)result!.GetType().GetProperty("TabId")!.GetValue(result)! ==102,"response retains exact tab ID");
    var set=(Task)method.Invoke(client,new object?[]{"set",10,102,2.0,ct})!;
    using var req2=await Read(host.StandardOutput.BaseStream,ct);
    var r2=req2.RootElement;
    Check(r2.GetProperty("op").GetString()=="set" && r2.GetProperty("tabId").GetInt32()==102 && r2.GetProperty("factor").GetDouble()==2.0,"zoom write addresses explicit tab");
    await Write(host.StandardInput.BaseStream,new {id=r2.GetProperty("id").GetInt32(),ok=false},ct);
    await set.WaitAsync(ct);
    var refused=set.GetType().GetProperty("Result")!.GetValue(set);
    Check(refused!=null && !(bool)refused.GetType().GetProperty("Ok")!.GetValue(refused)!,"negative reply preserved");
    Console.WriteLine("TRANSPORT_PASS");
}
finally
{
    host.StandardInput.Close();
    if(!host.HasExited)host.WaitForExit(2500);
}