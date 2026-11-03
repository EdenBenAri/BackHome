// Local-only Unity Web smoke/performance probe. Desktop CPU throttling is NOT a phone benchmark.
// node tools/validation/performance-browser.cjs <build-directory> <output-directory> [1,4,6]
// Set PLAYWRIGHT_MODULE to the installed playwright package directory if it is not on NODE_PATH.
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const zlib = require('node:zlib');
const crypto = require('node:crypto');
let playwright;
try { playwright = require(process.env.PLAYWRIGHT_MODULE || 'playwright'); }
catch { playwright = require('C:/Program Files/WindowsApps/OpenAI.CodexPrimaryRuntime.v26-1026-487-0_26.1026.487.0_x64__3k8sg7r9htsxt/dependencies/node/node_modules/playwright'); }
const root = path.resolve(process.argv[2] || 'Builds/performance-audit-2026-11-03');
const out = path.resolve(process.argv[3] || 'Logs/performance-audit-2026-11-03/browser');
const rates = (process.argv[4] || '1,4,6').split(',').map(Number);
// Short functional smoke run; its durations must not be compared to a full audit.
const quick = process.env.AUDIT_QUICK === '1';
if (rates.some(r => !Number.isFinite(r) || r < 1)) throw new Error('Invalid CPU rates');
fs.mkdirSync(out,{recursive:true});
const types={'.html':'text/html; charset=utf-8','.js':'application/javascript','.wasm':'application/wasm','.css':'text/css','.png':'image/png','.ico':'image/x-icon','.data':'application/octet-stream','.json':'application/json'};
const server=http.createServer((req,res)=>{
 let file; try {file=path.resolve(root,'.'+decodeURIComponent(new URL(req.url,'http://localhost').pathname));} catch {res.writeHead(400).end();return;}
 if(file===root) file=path.join(root,'index.html');
 if(!file.startsWith(root+path.sep)) {res.writeHead(403).end();return;}
 if(!fs.existsSync(file)||!fs.statSync(file).isFile()){res.writeHead(404).end();return;}
 const br=file.endsWith('.br'), ext=path.extname(br?file.slice(0,-3):file);
 let data=fs.readFileSync(file);
 // Expose the returned test instance, only in the response; the build files stay byte-identical.
 if(ext==='.html') data=Buffer.from(data.toString().replace('.then((unityInstance) => {','.then((unityInstance) => { window.__auditUnity = unityInstance; window.__auditReady = performance.now();'));
 res.writeHead(200,{'Content-Type':types[ext]||'application/octet-stream','Content-Length':data.length,'Cache-Control':'no-store',...(br?{'Content-Encoding':'br'}:{})});
 res.end(req.method==='HEAD'?undefined:data);
});
const save=(name,value)=>fs.writeFileSync(path.join(out,name),JSON.stringify(value,null,2));
function stats(frames) {
 if(!frames.length) throw new Error('No rendered frames captured');
 const sorted=frames.map(f=>f.ms).sort((a,b)=>a-b),duration=frames.reduce((s,f)=>s+f.ms,0),q=p=>sorted[Math.min(sorted.length-1,Math.ceil(sorted.length*p)-1)];
 const seconds=new Map(); let t=0;
 for(const f of frames){t+=f.ms;const b=Math.floor(t/1000);seconds.set(b,(seconds.get(b)||0)+1);}
 const fullSeconds=[...seconds.entries()].filter(([k])=>k>0&&k<Math.floor(t/1000)).map(([,v])=>v);
 return {frames:frames.length,durationSeconds:duration/1000,fps:frames.length*1000/duration,p50ms:q(.5),p95ms:q(.95),p99ms:q(.99),maxMs:sorted.at(-1),over40ms:sorted.filter(x=>x>40).length,over50ms:sorted.filter(x=>x>50).length,over100ms:sorted.filter(x=>x>100).length,minimumFullSecondFPS:fullSeconds.length?Math.min(...fullSeconds):null,averageDrawCalls:frames.reduce((s,f)=>s+f.draws,0)/frames.length,averageSubmittedTriangles:frames.reduce((s,f)=>s+f.triangles,0)/frames.length};
}
async function snapshot(page){return page.evaluate(()=>{
 const c=document.querySelector('canvas'),b=c.getBoundingClientRect(),gl=c.getContext('webgl2'),debug=gl?.getExtension('WEBGL_debug_renderer_info'),m=window.__auditUnity?.Module;
 return {canvas:[c.width,c.height],css:[b.x,b.y,b.width,b.height],viewport:[innerWidth,innerHeight],dpr:devicePixelRatio,renderer:debug?gl.getParameter(debug.UNMASKED_RENDERER_WEBGL):gl?.getParameter(gl.RENDERER),unityHeapBytes:m?.HEAPU8?.byteLength??null,wasmMemoriesBytes:(window.__auditMemories||[]).map(x=>x.buffer.byteLength),jsHeap:performance.memory?{used:performance.memory.usedJSHeapSize,total:performance.memory.totalJSHeapSize}:null,contextLost:gl?.isContextLost(),visibility:document.visibilityState};
});}
let browser;
(async()=>{
 if(!fs.existsSync(path.join(root,'index.html'))) throw new Error('Build index.html is missing');
 const files=fs.readdirSync(path.join(root,'Build')).map(name=>{
  const b=fs.readFileSync(path.join(root,'Build',name));
  return {name,bytes:b.length,decodedBytes:name.endsWith('.br')?zlib.brotliDecompressSync(b).length:b.length,sha256:crypto.createHash('sha256').update(b).digest('hex')};
 });save('build-files.json',files);
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 const url='http://127.0.0.1:'+server.address().port+'/';
 browser=await playwright.chromium.launch({channel:'msedge',headless:true});
 const results=[];
 for(const rate of rates){
  const context=await browser.newContext({viewport:{width:390,height:844},deviceScaleFactor:3,isMobile:true,hasTouch:true});
  const page=await context.newPage(),cdp=await context.newCDPSession(page);
  const errors=[],logs=[];
  page.on('pageerror',e=>errors.push(String(e)));page.on('requestfailed',r=>errors.push(r.url()+': '+r.failure()?.errorText));
  page.on('dialog',async d=>{errors.push('dialog: '+d.message());await d.dismiss();});
  page.on('console',m=>{logs.push({type:m.type(),text:m.text()});});
  await page.addInitScript(()=>{
   window.__auditMemories=[];
   WebAssembly.Memory=new Proxy(WebAssembly.Memory,{construct(target,args){const m=Reflect.construct(target,args);window.__auditMemories.push(m);return m;}});
   // New Unity/Emscripten builds export their memory instead of constructing it in JS.
   for(const method of ['instantiate','instantiateStreaming'])if(WebAssembly[method]){
    const original=WebAssembly[method];
    WebAssembly[method]=async function(...args){const result=await original.apply(WebAssembly,args);const instance=result.instance||result;for(const value of Object.values(instance.exports||{}))if(value instanceof WebAssembly.Memory&&!window.__auditMemories.includes(value))window.__auditMemories.push(value);return result;};
   }
   window.__probe={draws:0,triangles:0,frames:[],capture:false,previous:0,lastDraws:0,lastTriangles:0,longTasks:[],firstDraw:null};
   for(const Type of [window.WebGLRenderingContext,window.WebGL2RenderingContext])if(Type)for(const name of ['drawArrays','drawElements','drawArraysInstanced','drawElementsInstanced']){
    const original=Type.prototype[name];if(!original)continue;
    Type.prototype[name]=function(...a){const p=window.__probe;p.firstDraw??=performance.now();p.draws++;const count=name.startsWith('drawArrays')?a[2]:a[1],instances=name.endsWith('Instanced')?a.at(-1):1;p.triangles+=(a[0]===4?count/3:(a[0]===5||a[0]===6?Math.max(0,count-2):0))*instances;return original.apply(this,a);};
   }
   try{new PerformanceObserver(list=>{for(const e of list.getEntries())window.__probe.longTasks.push({start:e.startTime,ms:e.duration});}).observe({entryTypes:['longtask']});}catch{}
   function tick(t){const p=window.__probe;if(p.draws!==p.lastDraws){if(p.capture&&p.previous)p.frames.push({ms:t-p.previous,draws:p.draws-p.lastDraws,triangles:p.triangles-p.lastTriangles});p.previous=t;p.lastDraws=p.draws;p.lastTriangles=p.triangles;}requestAnimationFrame(tick);}requestAnimationFrame(tick);
  });
  // CPU throttling is a sensitivity test on this desktop GPU, not a simulated device model.
  await cdp.send('Emulation.setCPUThrottlingRate',{rate});
  if (process.env.AUDIT_NETWORK_MBPS) {
   const mbps=Number(process.env.AUDIT_NETWORK_MBPS);
   if (!Number.isFinite(mbps) || mbps<=0) throw new Error('Invalid network bandwidth');
   await cdp.send('Network.enable');
   await cdp.send('Network.emulateNetworkConditions',{offline:false,latency:80,downloadThroughput:mbps*1000000/8,uploadThroughput:1000000/8});
  }
  const wall=Date.now();
  await page.goto(url,{waitUntil:'domcontentloaded'});
  await page.waitForFunction(()=>window.__auditReady && window.__probe.draws>100,null,{timeout:180000});
  const startup=await page.evaluate(()=>({readyMs:window.__auditReady,firstDrawMs:window.__probe.firstDraw}));
  console.log(JSON.stringify({rate,startup,wallMs:Date.now()-wall}));
  await page.waitForTimeout(5000);
  const initial=await snapshot(page),scenarios=[];
  if(process.env.AUDIT_LOAD_ONLY==='1') {
   await page.screenshot({path:path.join(out,'cpu'+rate+'-load.png')});
   const viewports=[];
   if(process.env.AUDIT_VIEWPORTS==='1')for(const [width,height] of [[360,780],[540,1200],[900,1600],[844,390]]){
    await page.setViewportSize({width,height});await page.waitForTimeout(1200);viewports.push(await snapshot(page));
   }
   const resources=await page.evaluate(()=>performance.getEntriesByType('resource').map(x=>({name:x.name.split('/').at(-1),transferSize:x.transferSize,encodedBodySize:x.encodedBodySize,decodedBodySize:x.decodedBodySize,durationMs:x.duration})));
   results.push({rate,networkMbps:Number(process.env.AUDIT_NETWORK_MBPS)||null,networkLatencyMs:process.env.AUDIT_NETWORK_MBPS?80:null,startup,initial,viewports,errors,consoleErrors:logs.filter(x=>x.type==='error'),resources});
   save('summary.json',results);await context.close();continue;
  }
  async function sample(name,ms,action){
   await page.evaluate(()=>{const p=window.__probe;p.frames=[];p.previous=0;p.capture=true;});
   if(action)await action();else await page.waitForTimeout(ms);
   const frames=await page.evaluate(()=>{window.__probe.capture=false;return window.__probe.frames;});
   const result={name,...stats(frames),state:await snapshot(page)};
   save('cpu'+rate+'-'+name+'-frames.json',frames);scenarios.push(result);console.log(JSON.stringify({rate,...result}));
   await page.screenshot({path:path.join(out,'cpu'+rate+'-'+name+'.png')});
  }
  await sample('idle',quick?5000:20000);
  await sample('movement',quick?6000:30000,async()=>{
   await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:185,y:560}]});
   for(const [x,y] of [[190,480],[235,535],[185,615],[130,545],[190,470],[230,525]]){
    await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x,y}]});await page.waitForTimeout(quick?1000:5000);
   }
   await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
  });
  await sample('after_movement',quick?10000:(rate===1?60000:20000));
  if(rate===1 || rate===6) {
   await sample('ship_transition',7000,async()=>{
    await page.waitForTimeout(1000);
    await page.evaluate(()=>window.__auditUnity.SendMessage('FullShip','TakeOff'));
    await page.waitForTimeout(6000);
   });
   await sample('ship_idle',quick?5000:15000);
   await sample('galaxy_transition',7000,async()=>{
    await page.waitForTimeout(1000);
    await page.evaluate(()=>window.__auditUnity.SendMessage('ChapterOneHologram','Travel'));
    await page.waitForTimeout(6000);
   });
   await sample('galaxy_return_idle',quick?5000:15000);
  }
  await page.setViewportSize({width:844,height:390});await page.waitForTimeout(1500);const landscape=await snapshot(page);
  await page.setViewportSize({width:390,height:844});await page.waitForTimeout(1000);
  const telemetry=await page.evaluate(()=>({longTasks:window.__probe.longTasks,resources:performance.getEntriesByType('resource').map(x=>({name:x.name.split('/').at(-1),transferSize:x.transferSize,encodedBodySize:x.encodedBodySize,decodedBodySize:x.decodedBodySize,durationMs:x.duration}))}));
  const result={rate,quick,method:'Edge desktop, mobile viewport, real desktop GPU; WebGL submission intervals via RAF observer',startup,initial,landscape,scenarios,errors,consoleErrors:logs.filter(x=>x.type==='error'),telemetry};
  results.push(result);save('cpu'+rate+'-result.json',result);save('cpu'+rate+'-console.json',logs);save('summary.json',results);await context.close();
 }
 await browser.close();server.close();
})().catch(async e=>{console.error(e);save('failure.json',{error:String(e),stack:e.stack});if(browser)await browser.close();server.close();process.exitCode=1;});
