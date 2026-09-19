// Reproduce the four regular pairs plus the saved G05 pair for larger keys.
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
import assert from 'node:assert/strict';
const root=path.dirname(fileURLToPath(import.meta.url)),repo=path.resolve(root,'../..');
const lab=path.join(repo,'experiments/GoBoard.SoundLab');
const catalog=JSON.parse(fs.readFileSync(path.join(lab,'samples.json')));
const blue=process.argv.includes('--blue');
const saved=JSON.parse(fs.readFileSync(path.join(root,blue?'selected-blue-favorites.json':'selected-favorites.json'))).favorites;
const ids=blue?['BO3','B01','B03','B07']:['G02','GO1','GO3','GO4','G05'];
const favorites=ids.map(id=>{const matches=saved.filter(f=>f.sampleId===id);assert.equal(matches.length,1,`Expected one saved ${id}`);return matches[0];});
const hash=b=>createHash('sha256').update(b).digest('hex');
const dir=path.join(repo,'assets/audio/mechanical',blue?'cherry-blue-pairs':'gateron-pairs');fs.mkdirSync(dir,{recursive:true});
const manifest=[];
for(const [index,f] of favorites.entries()){
  const family=catalog.families.find(g=>g.id===f.family),sample=family.samples.find(s=>s.id===f.sampleId),s=f.settings;
  assert.equal(f.sourceHash,family.sourceHash);assert.equal(f.contextHash,sample.sha256);
  assert.ok(s.paired&&s.overlap&&s.pitch===0&&s.volume===65);
  const bytes=fs.readFileSync(path.join(lab,sample.url));assert.equal(hash(bytes),f.contextHash);
  assert.equal(bytes.readUInt32LE(24),44100);assert.equal(bytes.readUInt16LE(22),1);assert.equal(bytes.length,44+bytes.readUInt32LE(40));
  const rate=44100,data=new Float32Array((bytes.length-44)/2);
  for(let i=0;i<data.length;i++)data[i]=bytes.readInt16LE(44+i*2)/32768;
  const frame=ms=>Math.round(ms/1000*rate),start=frame(s.start),end=frame(s.end),split=frame(s.split),up=frame(s.releaseStart);
  assert.ok(0<=start&&start<split&&split<=up&&up<end&&end<=data.length);
  let energy=0,peak=0;for(const v of data.subarray(start,end)){energy+=v*v;peak=Math.max(peak,Math.abs(v));}
  const rms=Math.sqrt(energy/(end-start)),weak=20*Math.log10(peak)<-30||20*Math.log10(peak/rms)<14;
  const gain=s.match?Math.min(weak?1:Infinity,Math.sqrt((.6197242718860959*.78**2*rate/48000)/energy),.24/peak):Math.min(1,.65/peak);
  const outputs=[];
  for(const [edge,a,b,level] of [['press',start,split,1],['release',up,end,s.releaseGain/100]]){
    const values=data.slice(a,b),attack=Math.min(Math.round(rate*.001),values.length>>1),tail=Math.min(Math.round(rate*s.fade/1000),values.length>>1);
    for(let i=0;i<attack;i++)values[i]*=i/Math.max(1,attack-1);
    for(let i=0;i<tail;i++)values[values.length-1-i]*=i/Math.max(1,tail-1);
    // Include the saved lab master volume; GoBoard's shared volume remains an extra trim.
    for(let i=0;i<values.length;i++)values[i]*=gain*level;
    const wave=Buffer.alloc(44+values.length*2);wave.write('RIFF');wave.writeUInt32LE(wave.length-8,4);wave.write('WAVEfmt ',8);wave.writeUInt32LE(16,16);wave.writeUInt16LE(1,20);wave.writeUInt16LE(1,22);wave.writeUInt32LE(rate,24);wave.writeUInt32LE(rate*2,28);wave.writeUInt16LE(2,32);wave.writeUInt16LE(16,34);wave.write('data',36);wave.writeUInt32LE(values.length*2,40);
    for(let i=0;i<values.length;i++)wave.writeInt16LE(Math.round(Math.max(-1,Math.min(1,values[i]*s.volume/100))*32767),44+i*2);
    assert.equal(wave.readInt16LE(44),0);assert.equal(wave.readInt16LE(wave.length-2),0);
    assert.ok(values.some(v=>Math.abs(v)>.0001));
    const file=`${edge}-${index+1}.wav`;fs.writeFileSync(path.join(dir,file),wave);
    outputs.push({file,sha256:hash(wave),frames:values.length,sourceStartSeconds:sample.sourceStartSeconds+a/rate,sourceEndSeconds:sample.sourceStartSeconds+b/rate});
  }
  manifest.push({role:index===(blue?3:4)?'large-keys':'regular',favoriteId:f.id,sampleId:f.sampleId,source:f.source,sourceHash:f.sourceHash,contextHash:f.contextHash,settings:s,gain,outputs});
}
fs.writeFileSync(path.join(root,blue?'blue-paired-manifest.json':'paired-manifest.json'),JSON.stringify({preset:blue?'CherryMxBlue':'GateronYellowPairs',license:catalog.license,variants:manifest},null,2)+'\n');
console.log(`Exported ${ids.join(', ')}: saved master volume 65%, release 70%, exact lab fades.`);
