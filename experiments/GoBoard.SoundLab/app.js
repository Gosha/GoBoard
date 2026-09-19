'use strict';
const $ = id => document.getElementById(id);
const STORAGE = 'goboard-sound-lab-favorites-v1';
let manifest, family, selected, ctx, master, loaded, generation = 0, serial = 0;
let nodes = new Set(), timers = new Set(), cache = new Map(), favorites = [];
const held = new Map();
const activeHoldButtons = new Set();
const settingIds = ['start','end','fade','pitch','volume','match','overlap','paired','split','releaseStart','releaseGain','holdMs'];
try { const saved = JSON.parse(localStorage.getItem(STORAGE) || '[]'); if (Array.isArray(saved)) favorites = saved; } catch {}

function status(text, error = false) { $('status').textContent = text; $('status').classList.toggle('error',error); }
function ensureAudio(resume=true) {
  if (!ctx) { ctx = new AudioContext(); master = ctx.createGain(); master.connect(ctx.destination); }
  master.gain.value = Number($('volume').value)/100;
  return resume ? ctx.resume() : Promise.resolve();
}
async function decode(url) {
  if (!cache.has(url)) cache.set(url,(async()=>{
    const response = await fetch(url); if (!response.ok) throw Error('Audio file unavailable');
    return ctx.decodeAudioData(await response.arrayBuffer());
  })().catch(e=>{cache.delete(url);throw e;}));
  return cache.get(url);
}
function controls() { return Object.fromEntries(settingIds.map(id=>[id,$(id).type==='checkbox'?$(id).checked:Number($(id).value)])); }
function stop() {
  serial++;
  held.clear();for(const button of activeHoldButtons)button.setAttribute('aria-pressed','false');activeHoldButtons.clear();
  for (const node of nodes) { try{node.stop();}catch{} }
  nodes.clear(); for(const timer of timers)clearTimeout(timer); timers.clear(); status('Stopped');
}
function later(fn,ms) { const timer=setTimeout(()=>{timers.delete(timer);fn();},ms);timers.add(timer); }
function activity(samples) {
  let energy=0,peak=0;for(const v of samples){energy+=v*v;peak=Math.max(peak,Math.abs(v));}
  const rms=Math.sqrt(energy/Math.max(1,samples.length));
  const peakDb=20*Math.log10(Math.max(peak,1e-12)),rmsDb=20*Math.log10(Math.max(rms,1e-12));
  return {energy,peak,peakDb,rmsDb,weak:peakDb < -30 || peakDb-rmsDb < 14};
}
function makeCut(buffer,settings,whole=false,applyFades=true,sharedGain=null) {
  const sr=buffer.sampleRate, data=buffer.getChannelData(0);
  const a=whole?0:Math.round(settings.start/1000*sr), b=whole?data.length:Math.min(data.length,Math.round(settings.end/1000*sr));
  if(b<=a)throw Error('Choose an end after the start');
  const result=ctx.createBuffer(1,b-a,sr), samples=result.getChannelData(0); samples.set(data.subarray(a,b));
  const attack=Math.min(Math.round(sr*.001),samples.length>>1),tail=Math.min(Math.round(sr*(whole?6:settings.fade)/1000),samples.length>>1);
  if(applyFades){for(let i=0;i<attack;i++)samples[i]*=i/Math.max(1,attack-1);
  for(let i=0;i<tail;i++)samples[samples.length-1-i]*=i/Math.max(1,tail-1);}
  const {energy,peak,weak}=activity(samples);
  // Never boost a range that fails the attack screen; allow quiet raw audition.
  const gain=sharedGain??(settings.match?Math.min(weak?1:Infinity,Math.sqrt((.6197242718860959*.78**2*sr/48000)/Math.max(energy,1e-12)),.24/Math.max(peak,1e-9)):Math.min(1,.65/Math.max(peak,1e-9)));
  for(let i=0;i<samples.length;i++)samples[i]*=gain;
  return result;
}
function parts(buffer,s) {
  if(!s.paired)return {press:makeCut(buffer,s),release:null};
  // One gain for the entire pair preserves the recorded press/release balance.
  const raw=buffer.getChannelData(0).subarray(Math.round(s.start/1000*buffer.sampleRate),Math.round(s.end/1000*buffer.sampleRate));
  const {energy,peak,weak}=activity(raw);
  const gain=s.match?Math.min(weak?1:Infinity,Math.sqrt((.6197242718860959*.78**2*buffer.sampleRate/48000)/Math.max(energy,1e-12)),.24/Math.max(peak,1e-9)):Math.min(1,.65/Math.max(peak,1e-9));
  return {press:makeCut(buffer,{...s,end:s.split},false,true,gain),
    release:makeCut(buffer,{...s,start:s.releaseStart},false,true,gain*s.releaseGain/100)};
}
function playParts(p,s,when=ctx.currentTime) {
  let duration=playBuffer(p.press,s.pitch,when,p.release&&!s.overlap?s.holdMs/1000:null);
  if(p.release){const delay=s.holdMs/1000;duration=Math.max(duration,delay+playBuffer(p.release,s.pitch,when+delay));}
  return duration;
}
function playBuffer(buffer,pitch=0,when=ctx.currentTime,limit=null) {
  const source=ctx.createBufferSource(); source.buffer=buffer; source.playbackRate.value=2**(pitch/12); source.connect(master);
  nodes.add(source);source.onended=()=>{nodes.delete(source);source.disconnect();};source.start(when);
  if(limit!==null)source.stop(when+limit);
  return buffer.duration/source.playbackRate.value;
}
async function audition(kind) {
  stop(); const token=serial;
  try {
    await ensureAudio(); if(token!==serial)return;
    const sample=selected, settings=controls(), buffer=await decode(sample.url); if(token!==serial)return;
    const pair=parts(buffer,settings),cut=kind==='context'?makeCut(buffer,settings,true):pair.press;
    if(kind==='ab') {
      const reference=family.samples.find(s=>s.id===$('reference').value);
      if(!reference?.currentUrl){status('No shipped baseline for this pack');return;}
      const old=await decode(reference.currentUrl);if(token!==serial)return;
      // Apply the same energy target to both sides when matching is selected.
      const a=makeCut(old,{...settings,start:0,end:old.duration*1000},false,false);
      const length=playBuffer(a);status(`A · current ${reference.id}`);
      const delay=Math.max(.85,length+.35);const duration=playParts(pair,settings,ctx.currentTime+delay);
      later(()=>status(`B · ${sample.id} variation`),delay*1000);
      later(()=>status('Ready'),(delay+duration+.1)*1000);
    } else if(kind==='burst') {
      const gap=Number($('spacing').value)/1000, now=ctx.currentTime+.03;
      const sequence=[];
      for(let i=0;i<12;i++){
        sequence.push({time:now+i*gap,buffer:pair.press});
        if(pair.release)sequence.push({time:now+i*gap+settings.holdMs/1000,buffer:pair.release});
      }
      sequence.sort((a,b)=>a.time-b.time);
      for(let i=0;i<sequence.length;i++){const e=sequence[i];playBuffer(e.buffer,settings.pitch,e.time,settings.overlap||i===sequence.length-1?null:sequence[i+1].time-e.time);}
      status(`12 ${settings.paired?'press/release pairs':'presses'} · ${sample.id}`);later(()=>status('Ready'),(12*gap+(settings.paired?settings.holdMs/1000:0)+buffer.duration+.1)*1000);
    } else {
      const seconds=kind==='context'?playBuffer(cut):kind==='press'?playBuffer(pair.press,settings.pitch):kind==='release'?playBuffer(pair.release,settings.pitch):playParts(pair,settings);
      status(`${sample.id} · ${kind==='context'?'source context':kind==='single'?(settings.paired?'press → release':'variation'):kind}`);later(()=>status('Ready'),seconds*1000+80);
    }
  } catch(e) {status(e.message,true);}
}
function update() {
  const start=Number($('start').value),end=Number($('end').value);
  $('split').min=start+5;$('split').max=end-5;
  $('split').value=Math.max(start+5,Math.min(end-5,Number($('split').value)));
  $('releaseStart').min=$('split').value;$('releaseStart').max=end-5;
  $('releaseStart').value=Math.max(Number($('split').value),Math.min(end-5,Number($('releaseStart').value)));
  for(const id of ['split','releaseStart','holdMs'])$(id+'Value').textContent=`${$(id).value} ms`;
  $('releaseGainValue').textContent=`${$('releaseGain').value}%`;
  $('splitControls').hidden=!$('paired').checked;
  $('play').textContent=$('paired').checked?'▶ Play press → release':'▶ Play variation';
  $('typingHint').textContent=$('paired').checked?'Key down = press · key up = release':'Only this field makes key sounds.';
  $('waveHint').textContent=selected?.premade?'Separate source files · mint: press · amber: release · 40 ms display gap':$('paired').checked?'Mint: press · amber: release · drag to split':'Source context';
  $('startValue').textContent=`${$('start').value} ms`; $('endValue').textContent=`${$('end').value} ms`;
  $('fadeValue').textContent=`${$('fade').value} ms`;$('pitchValue').textContent=`${Number($('pitch').value)>0?'+':''}${$('pitch').value} st`;
  $('volumeValue').textContent=`${$('volume').value}%`;if(master)master.gain.value=Number($('volume').value)/100;
  $('duration').textContent=`${Math.round(Number($('end').value)-Number($('start').value))} ms selected / ${Math.round(selected?.durationMs||0)} ms context`;
  draw();
}
function trim(mode) {
  if(selected.defaultSettings){applySettings(selected.defaultSettings);update();return;}
  const offsets={short:[-3,10],natural:[-12,45],long:[-25,110]}[mode];
  $('start').value=Math.max(0,Math.round(selected.startMs+offsets[0]));
  $('end').value=Math.min(Math.floor(selected.durationMs),Math.round(selected.endMs+offsets[1]));
  document.querySelectorAll('[data-trim]').forEach(b=>b.classList.toggle('selected',b.dataset.trim===mode));update();
}
function applySettings(settings) {
  for(const id of settingIds)if(id in settings){if($(id).type==='checkbox')$(id).checked=settings[id];else $(id).value=settings[id];}
}
async function choose(sample,settings=null) {
  stop();const ticket=++generation;selected=sample;loaded=null;
  $('sampleTitle').textContent=`${sample.id} · ${sample.label||(sample.existing?'Current source cut':'New candidate')}`;
  $('kind').textContent=sample.premade?'Ready-made pair':sample.existing?'Baseline context':'New cut';
  $('note').value='';$('start').max=$('end').max=Math.floor(sample.durationMs);
  trim('natural');$('fade').value=6;$('pitch').value=0;
  if(settings)$('paired').checked=false;
  for(const id of ['split','releaseStart']){$(id).min=0;$(id).max=Math.floor(sample.durationMs);}
  $('split').value=Math.round((Number($('start').value)+Number($('end').value))/2);$('releaseStart').value=$('split').value;$('releaseGain').value=70;$('holdMs').value=200;
  if(sample.defaultSettings)applySettings(sample.defaultSettings);
  if(settings)applySettings(settings);
  document.querySelectorAll('[data-trim]').forEach(b=>{b.disabled=!!sample.premade;if(settings||sample.premade)b.classList.remove('selected');});
  if(sample.existing)$('reference').value=sample.id;
  renderCandidates();update();status('Loading waveform…');
  try{await ensureAudio(false);const b=await decode(sample.url);if(ticket!==generation)return;loaded=b;draw();status('Ready');}
  catch(e){if(ticket===generation)status(e.message,true);}
}
function setFamily(id,sampleId=null,settings=null) {
  family=manifest.families.find(f=>f.id===id);$('familyLabel').textContent=family.name;
  $('source').href=family.source;$('source').textContent=`${family.author} · source ↗`;
  $('license').textContent=family.license||manifest.license;$('license').href=family.licenseUrl||manifest.licenseUrl;
  $('sourceHint').textContent=family.premade?'Existing press/release files from kbsim, with complete decoded tails. Ordinary 0–4 share one generic release; Space, Enter and Backspace have their own. The waveform joins the files for editing; live release follows key-up.':'Some wider cuts contain both press and release, or neighboring taps. Use the source context to judge them.';
  $('splitHint').textContent=family.premade?'The boundaries already separate the original press and release files. Hold the button or type to try them; Reset controls restores the full files.':'Place the split between the two sounds. Mint is press; amber is release. A second attack may also be another keystroke—listen to each part.';
  $('ab').disabled=$('reference').disabled=!family.samples.some(s=>s.currentUrl);
  $('context').textContent=family.premade?'Hear assembled pair':'Hear source context';
  $('families').querySelectorAll('button').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.family===id)));
  $('reference').replaceChildren(...family.samples.filter(s=>s.existing).map(s=>new Option(`${s.id} · current ${s.id.slice(-1)}`,s.id)));
  choose(family.samples.find(s=>s.id===sampleId)||family.samples[0],settings);
}
function renderCandidates() {
  $('candidates').replaceChildren(...family.samples.map(s=>{
    const button=document.createElement('button');button.className='candidate'+(s.existing?' old':'');
    button.setAttribute('aria-pressed',String(s.id===selected.id));button.setAttribute('aria-label',`Select ${s.id}, ${s.existing?'current':'new'} cut`);
    const title=document.createElement('strong');title.textContent=s.id;
    const detail=document.createElement('small');detail.textContent=s.premade?s.label:`${s.existing?'Current':'New'} · ${Math.round(s.endMs-s.startMs)} ms`;
    const time=document.createElement('small');time.textContent=s.premade?'Press + release':`Source ${s.sourceCoreSeconds.toFixed(2)} s`;
    button.append(title,detail,time);button.onclick=()=>choose(s);return button;
  }));
}
function draw() {
  const canvas=$('waveform'), rect=canvas.getBoundingClientRect(), dpr=window.devicePixelRatio||1;
  canvas.width=Math.round(rect.width*dpr);canvas.height=Math.round(150*dpr);
  const g=canvas.getContext('2d');g.scale(dpr,dpr);const width=rect.width,height=150;
  g.clearRect(0,0,width,height);if(!selected)return;
  const left=Number($('start').value)/selected.durationMs*width,right=Number($('end').value)/selected.durationMs*width;
  const split=Number($('split').value)/selected.durationMs*width,release=Number($('releaseStart').value)/selected.durationMs*width,paired=$('paired').checked;
  g.fillStyle='#193c38';g.fillRect(left,0,(paired?split:right)-left,height);
  if(paired){g.fillStyle='#44341c';g.fillRect(release,0,right-release,height);}
  g.strokeStyle='#465f70';g.beginPath();g.moveTo(0,height/2);g.lineTo(width,height/2);g.stroke();
  $('levels').textContent='Loading source levels…';$('activityWarning').textContent='';
  if(loaded){const data=loaded.getChannelData(0);
    const a=Math.round(Number($('start').value)/1000*loaded.sampleRate),b=Math.round(Number($('end').value)/1000*loaded.sampleRate);
    const level=activity(data.subarray(a,b));
    $('levels').textContent=`Selected source · peak ${level.peakDb.toFixed(1)} dBFS · RMS ${level.rmsDb.toFixed(1)} dBFS`;
    $('activityWarning').textContent=level.weak?'Weak or missing attack — this range may be background noise. Energy matching will not boost weak audio.':'';
    const scale=$('autofit').checked?1/Math.max(activity(data).peak,1e-6):1;
    $('scaleLabel').textContent=$('autofit').checked?`Visual zoom ${scale.toFixed(1)}× · audio unchanged`:'Fixed scale ±1';
    $('partLevels').textContent='';
    if(paired){const press=activity(data.subarray(a,Math.round(Number($('split').value)/1000*loaded.sampleRate))),up=activity(data.subarray(Math.round(Number($('releaseStart').value)/1000*loaded.sampleRate),b));
      $('partLevels').textContent=`Press peak ${press.peakDb.toFixed(1)} dBFS${press.weak?' (weak attack)':''} · Release peak ${up.peakDb.toFixed(1)} dBFS${up.weak?' (weak attack)':''}`;}
    g.lineWidth=1;for(let x=0;x<width;x++){const a=Math.floor(x/width*data.length),b=Math.max(a+1,Math.floor((x+1)/width*data.length));let low=0,high=0;for(let j=a;j<b;j++){low=Math.min(low,data[j]);high=Math.max(high,data[j]);}g.strokeStyle=paired&&x>=release&&x<=right?'#f4c47e':x>=left&&x<=(paired?split:right)?'#87e5cf':'#465f70';g.beginPath();g.moveTo(x,height/2-high*62*scale);g.lineTo(x,height/2-low*62*scale);g.stroke();}}
  g.strokeStyle='#8de9d4';for(const x of[left,right]){g.beginPath();g.moveTo(x,0);g.lineTo(x,height);g.stroke();}
  if(paired){g.strokeStyle='#f4c47e';for(const x of[split,release]){g.beginPath();g.moveTo(x,0);g.lineTo(x,height);g.stroke();}}
}
function persist() {try{localStorage.setItem(STORAGE,JSON.stringify(favorites));return true;}catch{status('Browser storage unavailable. Export favorites before closing.',true);return false;}}
function renderFavorites() {
  if(activeHoldButtons.size)stop();
  $('favoriteCount').textContent=favorites.length;$('favorites').replaceChildren();
  if(!favorites.length){const p=document.createElement('p');p.className='small';p.textContent='Keep a few variations to compare. Saved in this browser.';$('favorites').append(p);return;}
  for(const favorite of favorites){
    const row=document.createElement('div');row.className='favorite';const description=document.createElement('div');description.className='description';
    const title=document.createElement('span');title.textContent=`${favorite.sampleId} · ${favorite.settings.paired?'press/release · ':''}${Math.round(favorite.settings.end-favorite.settings.start)} ms · ${favorite.settings.pitch} st`;
    const current=manifest.families.find(f=>f.id===favorite.family)?.samples.find(s=>s.id===favorite.sampleId);
    const available=!!current&&(!favorite.contextHash||favorite.contextHash===current.sha256);
    const note=document.createElement('small');note.textContent=(favorite.note||favorite.family)+(available?'':' · Retired or changed source; saved details remain exportable');description.append(title,note);
    const load=document.createElement('button');load.textContent=available?'Load':'Unavailable';load.disabled=!available;load.onclick=()=>{if(!available)return;setFamily(favorite.family,favorite.sampleId,favorite.settings);$('note').value=favorite.note;};
    const preview=document.createElement('button');preview.type='button';preview.className='saved-hold';preview.disabled=!available;
    preview.textContent=favorite.settings.paired?'Hold to press · let go to release':'Hold to press · no release saved';
    preview.setAttribute('aria-label',`${favorite.sampleId}: ${preview.textContent}`);
    bindHold(preview,`favorite:${favorite.id}`,id=>{if(!available)return;stop();return beginHeld(id,current,favorite.settings);});
    const remove=document.createElement('button');remove.textContent='Remove';remove.onclick=()=>{favorites=favorites.filter(f=>f.id!==favorite.id);persist();renderFavorites();};row.append(description,load,remove,preview);$('favorites').append(row);
  }
}
$('play').onclick=()=>audition('single');$('context').onclick=()=>audition('context');$('ab').onclick=()=>audition('ab');$('burst').onclick=()=>audition('burst');$('stop').onclick=stop;
$('pressOnly').onclick=()=>audition('press');$('releaseOnly').onclick=()=>audition('release');
$('autofit').oninput=draw;
function placeSplit(e){
  if(!$('paired').checked)return;
  const rect=$('waveform').getBoundingClientRect();
  $('split').value=Math.round((e.clientX-rect.left)/rect.width*selected.durationMs);
  update();$('releaseStart').value=$('split').value;update();
}
$('waveform').onpointerdown=e=>{if(e.button!==0)return;$('waveform').setPointerCapture(e.pointerId);placeSplit(e);};
$('waveform').onpointermove=e=>{if(e.buttons===1&&$('waveform').hasPointerCapture(e.pointerId))placeSplit(e);};
document.querySelectorAll('[data-trim]').forEach(b=>b.onclick=()=>trim(b.dataset.trim));
$('reset').onclick=()=>{stop();if(selected.defaultSettings){applySettings(selected.defaultSettings);update();return;}trim('natural');$('fade').value=6;$('pitch').value=0;$('match').checked=true;$('split').value=Math.round((Number($('start').value)+Number($('end').value))/2);$('releaseStart').value=$('split').value;$('releaseGain').value=70;$('holdMs').value=200;update();};
for(const id of settingIds)$(id).oninput=()=>{
  if(id==='paired')stop();
  if(id==='start'&&Number($('start').value)>Number($('end').value)-20)$('start').value=Number($('end').value)-20;
  if(id==='end'&&Number($('end').value)<Number($('start').value)+20)$('end').value=Number($('start').value)+20;
  if(id==='start'||id==='end')document.querySelectorAll('[data-trim]').forEach(b=>b.classList.remove('selected'));
  update();
};
function interruptVoices(s){if(!s.overlap){for(const node of nodes){try{node.stop();}catch{}}nodes.clear();}}
function releaseHeld(entry){
  if(entry.token!==serial||!entry.parts?.release)return;
  interruptVoices(entry.settings);playBuffer(entry.parts.release,entry.settings.pitch);status(`${entry.sample.id} · release`);
}
async function beginHeld(id,sample=selected,settings=controls()){
  if(held.has(id)||!sample)return;
  const entry={token:serial,sample,settings:{...settings},released:false,parts:null};held.set(id,entry);
  try{
    await ensureAudio();const buffer=await decode(entry.sample.url);if(entry.token!==serial)return;
    master.gain.value=entry.settings.volume/100;
    entry.parts=parts(buffer,entry.settings);
    interruptVoices(entry.settings);playBuffer(entry.parts.press,entry.settings.pitch);status(`${entry.sample.id} · press`);
    if(entry.released)releaseHeld(entry);
  }catch(error){if(held.get(id)===entry)held.delete(id);status(error.message,true);}
}
function endHeld(id){const entry=held.get(id);if(!entry)return;held.delete(id);entry.released=true;if(entry.parts)releaseHeld(entry);}
$('typing').onkeydown=e=>{
  if(e.repeat||e.ctrlKey||e.metaKey||e.altKey||['Shift','Control','Alt','Meta','CapsLock','Tab','Escape'].includes(e.key))return;
  return beginHeld(`key:${e.code||e.key}`);
};
$('typing').onkeyup=e=>endHeld(`key:${e.code||e.key}`);
$('typing').onblur=stop;
function bindHold(button,prefix,begin=beginHeld){
  const down=id=>{const pending=begin(id);button.setAttribute('aria-pressed','true');activeHoldButtons.add(button);return pending;};
  const up=id=>{endHeld(id);button.setAttribute('aria-pressed','false');activeHoldButtons.delete(button);};
  button.setAttribute('aria-pressed','false');
  button.onpointerdown=e=>{if(button.disabled||e.button!==0)return;e.preventDefault();button.setPointerCapture(e.pointerId);return down(`${prefix}:pointer:${e.pointerId}`);};
  button.onpointerup=e=>up(`${prefix}:pointer:${e.pointerId}`);
  button.onpointercancel=stop;button.onlostpointercapture=e=>up(`${prefix}:pointer:${e.pointerId}`);
  button.onkeydown=e=>{if(button.disabled||![' ','Enter'].includes(e.key))return;e.preventDefault();if(e.repeat)return;return down(`${prefix}:key:${e.key}`);};
  button.onkeyup=e=>{if(![' ','Enter'].includes(e.key))return;e.preventDefault();up(`${prefix}:key:${e.key}`);};
  button.onblur=stop;
}
bindHold($('hold'),'editor');
document.addEventListener('keydown',e=>{if(e.key==='Escape')stop();});
$('save').onclick=()=>{
  if(!selected)return;
  const settings=controls();favorites.push({id:crypto.randomUUID(),family:family.id,sampleId:selected.id,settings,
    note:$('note').value.trim(),source:family.source,sourceHash:family.sourceHash,
    ...(selected.premade?{sourceFiles:selected.sourceFiles,assembly:selected.assembly,license:family.license,licenseUrl:family.licenseUrl,sourceKind:'separate-press-release-files'}:{
    sourceStartSeconds:selected.sourceStartSeconds+settings.start/1000,
    sourceEndSeconds:selected.sourceStartSeconds+settings.end/1000,contextHash:selected.sha256,
    ...(settings.paired?{pressSourceEndSeconds:selected.sourceStartSeconds+settings.split/1000,releaseSourceStartSeconds:selected.sourceStartSeconds+settings.releaseStart/1000}: {})}),contextHash:selected.sha256});
  const stored=persist();renderFavorites();if(stored)status(`Saved ${selected.id}`);
};
$('export').onclick=()=>{
  const blob=new Blob([JSON.stringify({schemaVersion:2,created:new Date().toISOString(),favorites},null,2)],{type:'application/json'});
  const url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download='goboard-sound-favorites.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),10000);status('Favorites exported');
};
new ResizeObserver(draw).observe($('waveform'));
window.addEventListener('pagehide',stop);
window.addEventListener('blur',stop);
document.addEventListener('visibilitychange',()=>{if(document.hidden)stop();});
async function init(){
  try{
    const response=await fetch('samples.json',{cache:'no-store'});if(!response.ok)throw Error('Cannot load sample catalog');manifest=await response.json();
    const extra=await fetch('premade-samples.json',{cache:'no-store'});if(!extra.ok)throw Error('Cannot load ready-made pairs');manifest.families.push(...(await extra.json()).families);
    favorites=favorites.filter(f=>f&&f.settings&&typeof f.family==='string'&&typeof f.sampleId==='string');
    $('families').replaceChildren(...manifest.families.map(f=>{const b=document.createElement('button');b.dataset.family=f.id;b.textContent=f.name;const small=document.createElement('small');small.textContent=f.premade?'8 ready-made press/release pairs':'8 new cuts + 4 current';b.append(small);b.onclick=()=>setFamily(f.id);return b;}));
    renderFavorites();setFamily('gateron-yellow','G03');
  }catch(error){status(error.message,true);$('sampleTitle').textContent='Could not load samples';}
}
init();
