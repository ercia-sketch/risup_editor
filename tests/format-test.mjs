import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import {webcrypto} from 'node:crypto';
import {spawnSync} from 'node:child_process';
import vm from 'node:vm';
const browserScope={TextEncoder,TextDecoder,Uint8Array,Uint16Array,Uint32Array,Int8Array,Int16Array,Int32Array,Float32Array,Float64Array,BigInt64Array,BigUint64Array,Uint8ClampedArray,ArrayBuffer,DataView,Date,Map,Set,Error,RegExp,console};
vm.runInNewContext(fs.readFileSync(new URL('./vendor/msgpackr-browser.js',import.meta.url),'utf8'),browserScope);
const {encode,decode}=browserScope.msgpackr;
import fflate from './vendor/fflate.cjs';
const {compressSync,decompressSync,zlibSync,deflateSync}=fflate;
import {fileURLToPath} from 'node:url';
const dir=path.dirname(fileURLToPath(import.meta.url));
const root=path.dirname(dir);
const map=fs.readFileSync(path.join(root,'src','rpack_map.bin'));
const exe=process.env.RISUP_TEST_EXE || path.join(root,'src','bin','Release','net8.0-windows','win-x64','RisupEditor.exe');
const key=await webcrypto.subtle.importKey('raw',await webcrypto.subtle.digest('SHA-256',new TextEncoder().encode('risupreset')),'AES-GCM',false,['encrypt','decrypt']);
const preset={
 name:'달빛 서재 · 테스트', aiModel:'example-model',subModel:'other-model',temperature:73,maxContext:32000,maxResponse:1200,
 jailbreakToggle:true,chainOfThought:false,promptSettings:{sendName:true,postEndInnerFormat:'마지막 {{char}}',customChainOfThought:true},
 unknownFutureField:{nested:[true,false,null,undefined,1.25,-2.5,NaN,Infinity],binary:new Uint8Array([0,128,255]),date:new Date('2026-09-09T00:00:00Z'),big:1234567890123456789n},
 promptTemplate:[
 {type:'plain',type2:'main',role:'system',name:'이야기의 시작',text:'당신은 몰입감 있는 이야기를 만드는 작가입니다.\n\n{{char}}의 성격과 말투를 유지하고, {{user}}의 선택을 존중하세요.\n\n장면의 분위기와 감각적인 묘사를 바탕으로 자연스럽게 대화를 이어갑니다.',futureBlockOption:{enabled:true}},
 {type:'description',role2:'system',innerFormat:'<character>\n{{slot}}\n</character>'},
 {type:'lorebook'},
 {type:'plain',type2:'normal',role:'system',name:'문체와 진행',text:'대화와 묘사의 균형을 맞추세요.\n인물의 감정은 행동과 말투로 드러냅니다.\n\n{{#if {{getvar::detail}}}}구체적으로 묘사하세요.{{/if}}'},
 {type:'persona',role2:'user'},
 {type:'authornote',role2:'system',defaultText:'원본 작가의 노트',innerFormat:'{{slot}}'},
 {type:'memory',role2:'system'},
 {type:'chat',rangeStart:-1000,rangeEnd:'end',chatAsOriginalOnSystem:true},
 {type:'jailbreak',type2:'normal',role:'system',text:'테스트 지침'},
 {type:'cot',type2:'normal',role:'system',text:'사고 지침'},
 {type:'chatML',text:'<|im_start|>system\n한국어로 작성하세요.\n<|im_end|>'},
 {type:'postEverything'},
 {type:'cache',name:'cache',depth:1,role:'all'},
 {type:'futureBlock',name:'미래 블록',payload:{x:1}}
 ]
};
async function writeFixture(name, data=preset, version=2, compression=compressSync, rpack=true){
 const encrypted=await webcrypto.subtle.encrypt({name:'AES-GCM',iv:new Uint8Array(12)},key,encode(data));
 const outer={presetVersion:version,type:'preset',preset:encrypted,futureEnvelope:{keep:'yes'}};
 const bytes=compression(encode(outer));fs.writeFileSync(path.join(dir,name),rpack?Uint8Array.from(bytes,b=>map[b]):bytes);
}
async function read(file){
 const bytes=fs.readFileSync(file);const outer=decode(decompressSync(Uint8Array.from(bytes,b=>map[256+b])));
 assert.ok((outer.presetVersion===0||outer.presetVersion===2)&&outer.type==='preset','RisuAI import version predicate');
 const plain=await webcrypto.subtle.decrypt({name:'AES-GCM',iv:new Uint8Array(12)},key,outer.preset??outer.pres);
 return {outer,data:decode(new Uint8Array(plain))};
}
function run(args,expected=0){const r=spawnSync(exe,args,{timeout:30000,windowsHide:true});assert.equal(r.status,expected,`${args[0]} exit: ${r.status} ${r.error??''}`);}
const report=[];
run(['--preview-test',dir]);assert.match(fs.readFileSync(path.join(dir,'preview-result.txt'),'utf8'),/^PASS/);report.push('PASS RisuAI-compatible prompt preview engine');
for(const [name,compression,rpack,version] of [['reference.risup',compressSync,true,2],['zlib.risup',zlibSync,true,2],['raw-deflate.risup',deflateSync,true,0],['legacy.risupreset',compressSync,false,0]]){
 await writeFixture(name,preset,version,compression,rpack);const dest=path.join(dir,name+'.out.risup');run(['--roundtrip',path.join(dir,name),dest]);
 const {outer,data}=await read(dest);assert.deepStrictEqual(data,decode(encode(preset)));assert.deepStrictEqual(outer.futureEnvelope,decode(encode({keep:'yes'})));report.push('PASS full preservation: '+name);
}
const edit=path.join(dir,'edited.risup');run(['--fixture-edit',path.join(dir,'reference.risup'),edit]);
const edited=(await read(edit)).data;const expected=decode(encode(preset));expected.promptTemplate[0].text='수정 완료\n{{char}}와 {{user}}\nUnicode: 🌿';expected.promptTemplate.reverse();assert.deepStrictEqual(edited,expected);report.push('PASS edited text/order and non-prompt preservation');
await writeFixture('unsupported.risup',preset,99);run(['--roundtrip',path.join(dir,'unsupported.risup'),path.join(dir,'must-not-exist.risup')],1);
fs.writeFileSync(path.join(dir,'truncated.risup'),fs.readFileSync(path.join(dir,'reference.risup')).subarray(0,80));run(['--roundtrip',path.join(dir,'truncated.risup'),path.join(dir,'must-not-exist.risup')],1);
assert.ok(!fs.existsSync(path.join(dir,'must-not-exist.risup')));report.push('PASS unsupported/truncated files fail without output');
if(process.argv.includes('--ui')){
 run(['--self-test',dir]);assert.match(fs.readFileSync(path.join(dir,'ui-test-result.txt'),'utf8'),/^PASS/);
 const ui=(await read(path.join(dir,'ui-output.risup'))).data;assert.equal(ui.promptTemplate[0].text,'UI 편집 테스트');assert.deepStrictEqual(ui.unknownFutureField,decode(encode(preset)).unknownFutureField);report.push('PASS UI commands + independent decode of UI export');
}
fs.writeFileSync(path.join(dir,'format-test-result.txt'),report.join('\n'));console.log(report.join('\n'));



