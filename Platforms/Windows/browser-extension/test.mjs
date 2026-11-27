import test from "node:test";
import assert from "node:assert/strict";
import {readFileSync} from "node:fs";
import {createHash} from "node:crypto";
import vm from "node:vm";

const manifest = JSON.parse(readFileSync(new URL("./manifest.json",import.meta.url),"utf8"));
const source = readFileSync(new URL("./background.js",import.meta.url),"utf8");

function fixture() {
  const windows = new Map([[10,{id:10,focused:true,active:101}],[20,{id:20,focused:false,active:201}]]);
  const zoom = new Map([[101,2],[102,1],[201,.75]]);
  const calls = [];
  let focusId = 10;
  let afterRead = null;
  let ready = null;
  const event = () => ({addListener(){}});
  const port = {onMessage:{addListener(fn){ready=fn;}},onDisconnect:event(),postMessage(msg){calls.push(msg);}};
  const chrome = {
    runtime:{connectNative(name){assert.equal(name,"com.rizetngan.snug.zoom");return port;},onInstalled:event(),onStartup:event()},
    windows:{onFocusChanged:event(),async getLastFocused(){return {...windows.get(focusId)};},async get(id){if(!windows.has(id))throw Error("closed");return {...windows.get(id)};}},
    tabs:{onActivated:event(),onZoomChange:event(),async query({windowId,active}){assert.equal(active,true);return [{id:windows.get(windowId).active}];},
      async getZoom(id){const factor=zoom.get(id);if(afterRead){const fn=afterRead;afterRead=null;fn();}return factor;},
      async setZoom(id,factor){calls.push({tabId:id,factor});zoom.set(id,factor===0?1.25:factor);}},
    action:{async setBadgeText(){},async setTitle(){}}
  };
  const context = vm.createContext({chrome,navigator:{userAgent:"Chrome"},setTimeout,clearTimeout,console});
  vm.runInContext(source,context);
  const ask = async request => {
    context.request=request;
    return JSON.parse(JSON.stringify(await vm.runInContext("handleRequest(request)",context)));
  };
  return {ask,windows,zoom,calls,switchTab(id){windows.get(10).active=id;},
    focus(id){for(const w of windows.values())w.focused=w.id===id;focusId=id;},
    switchDuringRead(fn){afterRead=fn;}};
}

test("extension requests native messaging only; stable public ID",()=>{
  assert.deepEqual(manifest.permissions,["nativeMessaging"]);
  assert.equal(manifest.host_permissions,undefined);
  const hex=createHash("sha256").update(Buffer.from(manifest.key,"base64")).digest("hex").slice(0,32);
  const id=[...hex].map(c=>String.fromCharCode(97+parseInt(c,16))).join("");
  assert.equal(id,"pcefdmjkmhlpdlgamnhajbpjkpffagbk");
});
test("A 200%, B 100%, back A 200% without carrying history",async()=>{
  const f=fixture();
  assert.deepEqual(await f.ask({id:1,op:"focus"}),{id:1,ok:true,windowId:10,tabId:101,factor:2,focused:true});
  f.switchTab(102);
  assert.equal((await f.ask({id:2,op:"read",windowId:10})).factor,1);
  f.switchTab(101);
  assert.equal((await f.ask({id:3,op:"read",windowId:10})).factor,2);
});
test("external zoom changes and multiple windows read actual factors",async()=>{
  const f=fixture();
  f.zoom.set(101,1.75);
  assert.equal((await f.ask({id:1,op:"read",windowId:10})).factor,1.75);
  f.focus(20);
  assert.equal((await f.ask({id:2,op:"focus"})).tabId,201);
  assert.equal((await f.ask({id:3,op:"read",windowId:10})).factor,1.75);
});
test("stale tab or background window rejects writes",async()=>{
  const f=fixture();f.switchTab(102);
  assert.equal((await f.ask({id:1,op:"set",windowId:10,tabId:101,factor:1.75})).ok,false);
  assert.equal(f.zoom.get(101),2);assert.equal(f.zoom.get(102),1);
  f.focus(20);
  assert.equal((await f.ask({id:2,op:"set",windowId:10,tabId:102,factor:2})).ok,false);
  assert.equal(f.zoom.get(102),1);
});
test("explicit set and native reset target correct tab, preserve configured default",async()=>{
  const f=fixture();f.switchTab(102);
  assert.equal((await f.ask({id:1,op:"set",windowId:10,tabId:102,factor:1.5})).factor,1.5);
  assert.equal((await f.ask({id:2,op:"reset",windowId:10,tabId:102})).factor,1.25);
  assert.equal(f.zoom.get(101),2);
  assert.deepEqual(f.calls.at(-1),{tabId:102,factor:0});
});
test("tab change during native API read returns unknown, not stale zoom",async()=>{
  const f=fixture();f.switchDuringRead(()=>f.switchTab(102));
  assert.equal((await f.ask({id:1,op:"read",windowId:10})).ok,false);
  assert.equal((await f.ask({id:2,op:"read",windowId:10})).factor,1);
});
test("invalid requests cannot operate browser",async()=>{
  const f=fixture();
  assert.equal((await f.ask({id:1,op:"set",windowId:10,tabId:101,factor:99})).ok,false);
  assert.equal((await f.ask({id:2,op:"other"})).ok,false);
  assert.equal((await f.ask({id:3,op:"read",windowId:999})).ok,false);
  assert.equal(f.zoom.get(101),2);
});