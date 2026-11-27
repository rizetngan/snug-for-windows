# Browser zoom native transport smoke test

Run while no installed Snug 0.2.53+ instance is listening on this user's zoom pipe.
Build SnugZoomHost.exe from tools/SnugZoomHost.cs using the .NET Framework C# compiler,
or use the candidate installer staging copy. Build this project with .NET 10, then run
its DLL with dotnet and the path to SnugZoomHost.exe as the sole argument.
The probe launches the real host, exchanges framed JSON with the real server,
and checks handshake, explicit browser window/tab IDs, exact zoom-factor readback
and a refused request. It does not connect to a browser or read browser data.