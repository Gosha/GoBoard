// Browser-free checks for sample integrity and UI/audio control logic.
// Does not claim visual inspection or native browser audio playback.
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import vm from 'node:vm';
import {fileURLToPath} from 'node:url';
import {randomUUID,createHash} from 'node:crypto';
const root=path.dirname(fileURLToPath(import.meta.url));
const html=await fs.readFile(path.join(root,'index.html'),'utf8');
const catalog=JSON.parse(await fs.readFile(path.join(root,'samples.json'),'utf8'));
const storage=new Map(),events=[],timeouts=new Map();let timer=0;
class Element {
  constructor(){this.value='';this.checked=false;this.type='';this.children=[];this.dataset={};this.attributes={};this.classList={toggle(){},remove(){}};}
  setAttribute(k,v){this.attributes[k]=v;}
  append(...children){this.children.push(...children);}
  replaceChildren(...children){this.children=children;if(this.id==='reference')this.value=children[0]?.value||'';}
  querySelectorAll(){return this.children;}
  getBoundingClientRect(){return {width:700,left:0};}
  setPointerCapture(id){this.pointer=id;}
  hasPointerCapture(id){return this.pointer===id;}
  getContext(){return new Proxy({}, {get:()=>()=>{},set:()=>true});}
  click(){events.push({action:'download',href:this.href});this.onclick?.();}
}
const elements=new Map();
for(const tag of html.matchAll(/<[^>]+\bid="([^"]+)"[^>]*>/g)){
  const element=new Element();element.id=tag[1];element.value=tag[0].match(/\bvalue="([^"]*)"/)?.[1]||'';
  element.type=tag[0].match(/\btype="([^"]*)"/)?.[1]||'';element.checked=/\bchecked\b/.test(tag[0]);elements.set(element.id,element);
}
elements.get('spacing').value='150';
const trimButtons=['short','natural','long'].map(trim=>{const e=new Element();e.dataset.trim=trim;return e;});
class AudioBuffer {
  constructor(length,rate){this.sampleRate=rate;this.duration=length/rate;this.data=new Float32Array(length);}
  getChannelData(){return this.data;}
}
function decodeWav(bytes){
  const data=Buffer.from(bytes);assert.equal(data.toString('ascii',0,4),'RIFF');assert.equal(data.readUInt16LE(22),1);
  assert.equal(data.readUInt16LE(34),16);assert.equal(data.readUInt32LE(24),44100);
  assert.equal(data.readUInt32LE(40)+44,data.length);
  const result=new AudioBuffer((data.length-44)/2,44100);for(let i=0;i<result.data.length;i++)result.data[i]=data.readInt16LE(44+i*2)/32768;
  return result;
}
class AudioContext {
  currentTime=0;destination={};
  createGain(){return {gain:{value:1},connect(){}};}
  resume(){return Promise.resolve();}
  createBuffer(_,length,rate){return new AudioBuffer(length,rate);}
  decodeAudioData(bytes){return Promise.resolve(decodeWav(bytes));}
  createBufferSource(){const source={playbackRate:{value:1},connect(){},disconnect(){},start(t){events.push({action:'start',time:t,source});},stop(t){events.push({action:'stop',time:t});if(t===undefined)this.onended?.();}};return source;}
}
const context=vm.createContext({console,AudioContext,ResizeObserver:class{observe(){}},Option:class extends Element{constructor(text,value){super();this.textContent=text;this.value=value;}},
  document:{getElementById:id=>{assert.ok(elements.has(id),`Missing element ${id}`);return elements.get(id);},querySelectorAll:()=>trimButtons,createElement:()=>new Element(),addEventListener(){}},
  window:{devicePixelRatio:1,addEventListener(){}},localStorage:{getItem:k=>storage.get(k),setItem:(k,v)=>storage.set(k,v)},
  crypto:{randomUUID},Blob,URL,setTimeout:(fn,ms)=>{timeouts.set(++timer,{fn,ms});return timer;},clearTimeout:id=>timeouts.delete(id),
  fetch:async url=>{const data=await fs.readFile(path.join(root,url));return {ok:true,json:async()=>JSON.parse(data),arrayBuffer:async()=>data.buffer.slice(data.byteOffset,data.byteOffset+data.byteLength)};}});
const run=code=>vm.runInContext(code,context);
vm.runInContext(await fs.readFile(path.join(root,'app.js'),'utf8'),context);
for(let i=0;i<100 && !run('loaded');i++)await new Promise(resolve=>setTimeout(resolve,10));
assert.ok(run('loaded'),'Initial waveform should load without requiring an audio gesture');
assert.equal(run('selected.id'),'G03');assert.equal(run('family.id'),'gateron-yellow');
let fresh=0,old=0;
for(const family of catalog.families){
  assert.equal(family.samples.length,12);
  for(const sample of family.samples){
    const bytes=await fs.readFile(path.join(root,sample.url));const audio=decodeWav(bytes);
    assert.equal(createHash('sha256').update(bytes).digest('hex'),sample.sha256);
    assert.ok(sample.startMs>=0&&sample.endMs>sample.startMs&&sample.endMs<audio.duration*1000);
    const core=audio.data.subarray(Math.round(sample.startMs/1000*44100),Math.round(sample.endMs/1000*44100));
    const level=run('activity')(core);
    assert.ok(!level.weak,`${sample.id} must contain a measurable attack`);
    assert.ok(Math.abs(level.peakDb-sample.activity.peakDbfs)<.1);
    sample.existing?old++:fresh++;
    if(sample.currentUrl)decodeWav(await fs.readFile(path.join(root,sample.currentUrl)));
  }
}
assert.equal(fresh,24);assert.equal(old,12);
for(const id of ['C01','C07','C08']){
  const clear=catalog.families.find(f=>f.id==='cherry-mx-clear');
  assert.ok(!clear.samples.some(s=>s.id===id),'Rejected IDs must not be reused');
  const retired=clear.retired.find(s=>s.id===id);assert.ok(retired);
  const buffer=decodeWav(await fs.readFile(path.join(root,`audio/cherry-mx-clear/${id}.wav`)));
  const a=Math.round(.08*44100),b=a+Math.round((retired.endSeconds-retired.startSeconds)*44100);
  assert.ok(run('activity')(buffer.data.subarray(a,b)).weak,`${id} regression fixture must fail screening`);
  context.rejectedBuffer=buffer;
  const cut=run(`makeCut(rejectedBuffer,{start:80,end:${80+(b-a)/44100*1000},fade:6,match:true})`);
  assert.ok(Math.max(...cut.data.map(Math.abs))<=Math.max(...buffer.data.subarray(a,b).map(Math.abs))+1e-6,'Noise must not be boosted');
}
assert.ok(run('activity')(new Float32Array(4410)).weak,'Digital silence must fail screening');
for(const mode of ['short','natural','long']){
  run(`trim('${mode}')`);assert.ok(+elements.get('end').value>+elements.get('start').value);
  const cut=run('makeCut(loaded,controls())');assert.ok(cut.duration>0);assert.ok(cut.data[0]===0);assert.ok(cut.data.at(-1)===0);
  assert.ok(cut.data.every(Number.isFinite));assert.ok(Math.max(...cut.data.map(Math.abs))<=.24001);
}
let before=events.length;await run("audition('ab')");assert.equal(events.slice(before).filter(e=>e.action==='start').length,2);
elements.get('overlap').checked=false;before=events.length;await run("audition('burst')");
assert.equal(events.slice(before).filter(e=>e.action==='start').length,12);
assert.equal(events.slice(before).filter(e=>e.action==='stop'&&e.time!==undefined).length,11);
run('stop()');assert.equal(run('nodes.size'),0);assert.equal(timeouts.size,0);
before=events.length;await elements.get('typing').onkeydown({key:'a'});assert.equal(events.slice(before).filter(e=>e.action==='start').length,1);
before=events.length;await elements.get('typing').onkeydown({key:'a',ctrlKey:true});assert.equal(events.length,before);
elements.get('note').value='Test tail';elements.get('save').onclick();assert.equal(run('favorites.length'),1);
assert.equal(JSON.parse(storage.get('goboard-sound-lab-favorites-v1'))[0].note,'Test tail');
const saved=run('favorites[0]');assert.ok(saved.sourceEndSeconds>saved.sourceStartSeconds);
elements.get('favorites').children[0].children[1].onclick();
for(let i=0;i<100&&!run('loaded');i++)await new Promise(resolve=>setTimeout(resolve,10));
assert.equal(run('selected.id'),saved.sampleId);assert.equal(Number(elements.get('start').value),saved.settings.start);
elements.get('export').onclick();assert.ok(events.some(e=>e.action==='download'));
elements.get('favorites').children[0].children[2].onclick();assert.equal(run('favorites.length'),0);
// Separate press/release ranges, common gain, live holds and cancellation.
const zoomLabel=elements.get('scaleLabel').textContent;
assert.match(zoomLabel,/Visual zoom/);
elements.get('autofit').checked=false;run('draw()');assert.match(elements.get('scaleLabel').textContent,/Fixed scale/);
elements.get('autofit').checked=true;run('draw()');assert.equal(elements.get('scaleLabel').textContent,zoomLabel);
elements.get('paired').checked=true;elements.get('paired').oninput();
elements.get('releaseGain').value=100;elements.get('holdMs').value=300;run('update()');
assert.equal(elements.get('splitControls').hidden,false);
elements.get('waveform').onpointerdown({button:0,pointerId:1,clientX:350});
assert.equal(+elements.get('split').value,+elements.get('releaseStart').value);
elements.get('waveform').onpointermove({buttons:1,pointerId:1,clientX:-20});
assert.equal(+elements.get('split').value,+elements.get('start').value+5);
elements.get('waveform').onpointermove({buttons:1,pointerId:1,clientX:350});
context.synthetic=new AudioBuffer(4410,44100);context.synthetic.data[500]=.2;context.synthetic.data[3000]=.1;
const split=run('parts(synthetic,{paired:true,start:0,end:100,split:40,releaseStart:60,releaseGain:100,fade:6,match:true})');
assert.ok(Math.abs(split.press.duration-.04)<1/44100);assert.ok(Math.abs(split.release.duration-.04)<1/44100);
assert.ok(Math.abs(Math.max(...split.press.data)/Math.max(...split.release.data)-2)<.0001,'A shared gain must retain source balance');
assert.equal(split.press.data.at(-1),0);assert.equal(split.release.data[0],0);
before=events.length;await run("audition('single')");
let starts=events.slice(before).filter(e=>e.action==='start');assert.equal(starts.length,2);assert.ok(Math.abs(starts[1].time-starts[0].time-.3)<1e-6);
before=events.length;await run("audition('press')");assert.equal(events.slice(before).filter(e=>e.action==='start').length,1);
before=events.length;await run("audition('release')");assert.equal(events.slice(before).filter(e=>e.action==='start').length,1);
run('stop()');before=events.length;
await elements.get('typing').onkeydown({key:'x',code:'KeyX'});assert.equal(events.slice(before).filter(e=>e.action==='start').length,1);
await elements.get('typing').onkeydown({key:'x',code:'KeyX',repeat:true});assert.equal(events.slice(before).filter(e=>e.action==='start').length,1);
// Releasing uses the settings captured on keydown even if controls moved.
const heldRelease=run("held.get('key:KeyX').parts.release");elements.get('releaseGain').value=0;
elements.get('typing').onkeyup({key:'x',code:'KeyX'});starts=events.slice(before).filter(e=>e.action==='start');assert.equal(starts.length,2);assert.equal(starts[1].source.buffer,heldRelease);
await elements.get('typing').onkeydown({key:'a',code:'KeyA'});await elements.get('typing').onkeydown({key:'s',code:'KeyS'});assert.equal(run('held.size'),2);
elements.get('typing').onblur();before=events.length;elements.get('typing').onkeyup({key:'a',code:'KeyA'});assert.equal(events.length,before);assert.equal(run('held.size'),0);
// Keyup before async decode/resume resolves still releases exactly once.
before=events.length;const pending=elements.get('typing').onkeydown({key:'q',code:'KeyQ'});elements.get('typing').onkeyup({key:'q',code:'KeyQ'});await pending;
assert.equal(events.slice(before).filter(e=>e.action==='start').length,2);
run('stop()');before=events.length;await elements.get('hold').onpointerdown({button:0,pointerId:9,preventDefault(){}});
elements.get('hold').onpointerup({pointerId:9});elements.get('hold').onlostpointercapture({pointerId:9});
assert.equal(events.slice(before).filter(e=>e.action==='start').length,2,'Pointer release must play once even after capture loss');
before=events.length;const canceled=elements.get('typing').onkeydown({key:'z',code:'KeyZ'});run('stop()');await canceled;assert.equal(events.slice(before).filter(e=>e.action==='start').length,0);
before=events.length;await run("audition('burst')");starts=events.slice(before).filter(e=>e.action==='start');assert.equal(starts.length,24);assert.ok(starts.every((e,i)=>!i||e.time>=starts[i-1].time));
run('stop()');elements.get('releaseGain').value=70;elements.get('save').onclick();
const pairedSaved=run('favorites[0]');assert.ok(pairedSaved.settings.paired);assert.ok(pairedSaved.releaseSourceStartSeconds>=pairedSaved.pressSourceEndSeconds);
elements.get('favorites').children[0].children[1].onclick();for(let i=0;i<100&&!run('loaded');i++)await new Promise(resolve=>setTimeout(resolve,10));
assert.equal(elements.get('paired').checked,true);assert.equal(+elements.get('split').value,pairedSaved.settings.split);assert.equal(+elements.get('releaseStart').value,pairedSaved.settings.releaseStart);
// Saved hold previews must use the saved sample/settings, leaving the editor alone.
const savedHold=elements.get('favorites').children[0].children[3];
await run('choose(family.samples[0])');
elements.get('pitch').value=-3;elements.get('volume').value=1;elements.get('releaseGain').value=0;
const editorSample=run('selected.id');before=events.length;
await savedHold.onpointerdown({button:0,pointerId:17,preventDefault(){}});
const entry=run('[...held.values()][0]');assert.equal(entry.sample.id,pairedSaved.sampleId);assert.equal(entry.settings.pitch,pairedSaved.settings.pitch);
assert.equal(run('master.gain.value'),pairedSaved.settings.volume/100);assert.equal(run('selected.id'),editorSample);assert.equal(+elements.get('volume').value,1);
savedHold.onpointerup({pointerId:17});savedHold.onlostpointercapture({pointerId:17});
starts=events.slice(before).filter(e=>e.action==='start');assert.equal(starts.length,2);assert.equal(starts[1].source.buffer,entry.parts.release);
assert.equal(savedHold.attributes['aria-pressed'],'false');
before=events.length;await savedHold.onkeydown({key:' ',preventDefault(){}});await savedHold.onkeydown({key:' ',repeat:true,preventDefault(){}});
savedHold.onkeyup({key:' ',preventDefault(){}});assert.equal(events.slice(before).filter(e=>e.action==='start').length,2);
await savedHold.onpointerdown({button:0,pointerId:18,preventDefault(){}});savedHold.onblur();before=events.length;savedHold.onpointerup({pointerId:18});assert.equal(events.length,before);
assert.equal(savedHold.attributes['aria-pressed'],'false');
elements.get('favorites').children[0].children[2].onclick();
context.testFavorite=saved;
run("favorites=[{...testFavorite,family:'cherry-mx-clear',sampleId:'C01'},{...testFavorite,contextHash:'changed'}];persist()");
await run('init()');assert.equal(run('favorites.length'),2,'Retired and changed favorites must survive catalog reload');
for(const row of elements.get('favorites').children){assert.ok(row.children[1].disabled);assert.ok(row.children[3].disabled);assert.match(row.children[0].children[1].textContent,/exportable/);}
assert.equal(JSON.parse(storage.get('goboard-sound-lab-favorites-v1')).length,2);
run('stop()');
// The selected native preset must reproduce the browser's pair processing.
const savedExport=JSON.parse(await fs.readFile(path.join(root,'../GoBoard.SoundSamples/selected-favorites.json'),'utf8')).favorites;
const exported=[...savedExport.slice(-4),savedExport.find(f=>f.sampleId==='G05')];
for(const [index,favorite] of exported.entries()){
  const source=catalog.families.find(f=>f.id===favorite.family).samples.find(s=>s.id===favorite.sampleId);
  context.exportSource=decodeWav(await fs.readFile(path.join(root,source.url)));context.exportSettings=favorite.settings;
  const pair=run('parts(exportSource,exportSettings)');
  for(const edge of ['press','release']){
    const wave=await fs.readFile(path.join(root,`../../assets/audio/mechanical/gateron-pairs/${edge}-${index+1}.wav`));
    assert.equal((wave.length-44)/2,pair[edge].data.length);
    for(let i=0;i<pair[edge].data.length;i++)assert.ok(Math.abs(wave.readInt16LE(44+i*2)-Math.round(pair[edge].data[i]*favorite.settings.volume/100*32767))<=1,`${favorite.sampleId} ${edge}: browser/export PCM mismatch`);
  }
}
// The selected Cherry Blue exports reproduce their exact saved lab processing.
const blueExport=JSON.parse(await fs.readFile(path.join(root,'../GoBoard.SoundSamples/selected-blue-favorites.json'),'utf8')).favorites;
for(const [index,id] of ['BO3','B01','B03','B07'].entries()){
  const favorite=blueExport.find(f=>f.sampleId===id);
  const source=catalog.families.find(f=>f.id===favorite.family).samples.find(s=>s.id===id);
  const bytes=await fs.readFile(path.join(root,source.url));assert.equal(createHash('sha256').update(bytes).digest('hex'),favorite.contextHash);
  context.exportSource=decodeWav(bytes);context.exportSettings=favorite.settings;
  const pair=run('parts(exportSource,exportSettings)');
  for(const edge of ['press','release']){
    const wave=await fs.readFile(path.join(root,`../../assets/audio/mechanical/cherry-blue-pairs/${edge}-${index+1}.wav`));
    assert.equal((wave.length-44)/2,pair[edge].data.length);
    for(let i=0;i<pair[edge].data.length;i++)assert.ok(Math.abs(wave.readInt16LE(44+i*2)-Math.round(pair[edge].data[i]*favorite.settings.volume/100*32767))<=1,`${id} ${edge}: browser/export PCM mismatch`);
  }
}
// Ready-made pairs retain independent source provenance and full-file boundaries.
const premade=JSON.parse(await fs.readFile(path.join(root,'premade-samples.json'),'utf8'));
assert.equal(premade.families.length,3);
for(const pack of premade.families){
  assert.equal(pack.samples.length,8);assert.equal(pack.license,'MIT');
  run(`setFamily('${pack.id}')`);await run('choose(family.samples[0])');
  assert.ok(elements.get('ab').disabled);assert.equal(elements.get('paired').checked,true);
  assert.equal(+elements.get('releaseGain').value,100);assert.equal(+elements.get('fade').value,1);
  for(const sample of pack.samples){
    const bytes=await fs.readFile(path.join(root,sample.url));
    assert.equal(createHash('sha256').update(bytes).digest('hex'),sample.sha256);
    const buffer=decodeWav(bytes);context.preparedBuffer=buffer;context.preparedSettings=sample.defaultSettings;
    const pair=run('parts(preparedBuffer,preparedSettings)');
    for(const edge of ['press','release']){
      assert.ok(pair[edge].data.every(Number.isFinite));assert.ok(pair[edge].data.some(v=>Math.abs(v)>.0001),`${sample.id} ${edge} non-silent`);
      const source=sample.sourceFiles[edge];assert.equal(createHash('sha256').update(await fs.readFile(path.join(root,source.path))).digest('hex'),source.sha256);
      assert.ok(pair[edge].data.length>=source.frames,`${sample.id} preserves complete ${edge} file`);
    }
    const gap=buffer.data.subarray(Math.round(sample.defaultSettings.split*44.1),Math.round(sample.defaultSettings.releaseStart*44.1));
    assert.ok(gap.every(v=>v===0),'Assembly gap must be silent');
  }
  const settings=run('controls()');elements.get('split').value=5;elements.get('releaseGain').value=0;elements.get('reset').onclick();
  assert.equal(+elements.get('split').value,settings.split);assert.equal(+elements.get('releaseGain').value,100);
  before=events.length;await elements.get('hold').onpointerdown({button:0,pointerId:27,preventDefault(){}});
  assert.equal(events.slice(before).filter(e=>e.action==='start').length,1);
  elements.get('hold').onpointerup({pointerId:27});assert.equal(events.slice(before).filter(e=>e.action==='start').length,2);
  elements.get('save').onclick();const favorite=run('favorites.at(-1)');
  assert.equal(favorite.sourceKind,'separate-press-release-files');assert.ok(!('sourceStartSeconds' in favorite));assert.ok(favorite.sourceFiles.release.sha256);
  const row=elements.get('favorites').children.at(-1);row.children[1].onclick();await run('choose(selected,favorites.at(-1).settings)');
  assert.equal(+elements.get('releaseStart').value,settings.releaseStart);
  before=events.length;await row.children[3].onpointerdown({button:0,pointerId:28,preventDefault(){}});row.children[3].onpointerup({pointerId:28});
  assert.equal(events.slice(before).filter(e=>e.action==='start').length,2);
}
run("setFamily('gateron-yellow','G03')");assert.ok(!elements.get('ab').disabled);run('stop()');
console.log('PASS: original 48 WAVs + 3 rejected fixtures; original playback/favorite/export checks; 24 ready-made pairs (hashes, complete non-silent edges, assembly gaps, preset/reset boundaries, hold/release, saved preview and source provenance). Browser audio and visual layout require manual acceptance.');
