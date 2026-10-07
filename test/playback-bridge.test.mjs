import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import {readFileSync} from 'node:fs';

const source = readFileSync(new URL('../native/YouTubeBridge.js', import.meta.url), 'utf8');
const ids = ['2qfoSxRRCJc', 'dQw4w9WgXcQ', 'aaaaaaaaaaa'];
function fixture(options = {}) {
  let now = 100000, interval;
  const timers = [], events = new Map(), messages = [], storage = options.storage || new Map();
  const count = {skip:0, next:0, nextClick:0, play:0, navigations:[]};
  const state = {ad:false, skipVisible:false, skipEnabled:true, nativeNext:false, buttonNext:false, index:0, devices:[{kind:'audiooutput',deviceId:'default',label:'Default'},{kind:'audiooutput',deviceId:'speaker-1',label:'USB Speaker'}]};
  let current = new URL(`https://www.youtube.com/watch?v=${ids[0]}&list=RD2qfoSxRRCJc&index=1&t=40`);
  const location = {
    get hostname(){return current.hostname}, get origin(){return current.origin}, get pathname(){return current.pathname},
    get href(){return current.href}, set href(value){current=new URL(value)},
    assign(value){count.navigations.push(value);if(!options.blockNavigation){current=new URL(value);state.index=Number(current.searchParams.get('index'))-1}},
    replace(value){this.assign(value)}
  };
  class MediaElement {
    play(){count.play++;this.paused=false;return Promise.resolve()}
    pause(){this.paused=true}
    async setSinkId(id){if(id && !state.devices.some(d=>d.deviceId===id)){const e=new Error('missing');e.name='NotFoundError';throw e}this.sinkId=id}
  }
  const video = Object.assign(new MediaElement(),{tagName:'VIDEO',paused:!!options.gate, ended:false, readyState:4, currentTime:10, duration:120, volume:.65, muted:false, sinkId:''});
  const skip = {isConnected:true, get disabled(){return !state.skipEnabled},getAttribute(){return null},getClientRects(){return state.skipVisible?[{}]:[]},getBoundingClientRect(){return {x:100,y:100,width:50,height:30}},click(){count.skip++}};
  const advance = () => {if(state.index<ids.length-1){state.index++;location.href=`https://www.youtube.com/watch?v=${ids[state.index]}&list=RD2qfoSxRRCJc&index=${state.index+1}`;video.ended=false;video.paused=true;video.currentTime=0}};
  const next = {isConnected:true,getAttribute(){return null},click(){count.nextClick++;if(state.buttonNext)advance()}};
  const tracks = (options.ids || ids).map((id,index)=>({querySelector(selector){
    if(selector.startsWith('a'))return {href:`https://www.youtube.com/watch?v=${id}&list=RD2qfoSxRRCJc&index=${index+1}&t=99`,title:id};
    return {textContent:String(index+1)}
  },hasAttribute(){return state.index===index}}));
  const player = {classList:{contains(name){return name==='ad-showing'&&state.ad}},getVideoData(){return {video_id:state.playerVideoId ?? current.searchParams.get('v'),title:'Fixture'}},setVolume(){},unMute(){},mute(){},
    nextVideo(){count.next++;if(state.nativeNext)advance()},previousVideo(){},
    querySelectorAll(selector){return selector.startsWith('.ytp-ad')?[skip]:[]}
  };
  const document = {readyState:'complete',createElement(){return {isConnected:false}},documentElement:{append(el){el.isConnected=true},classList:{toggle(){}}},
    getElementById(){return player},querySelector(selector){if(selector.startsWith('video'))return options.noVideo ? null : video;if(selector==='.ytp-next-button')return next;return null},
    querySelectorAll(selector){return selector==='ytd-playlist-panel-video-renderer'?tracks:[]},
    addEventListener(name,fn){events.set(name,fn)}
  };
  const window = {__famicomplayerGatePlayback:!!options.gate,chrome:{webview:{postMessage(message){messages.push(message)}}}};window.top=window;
  const mediaEvents=new Map();
  vm.runInNewContext(source,{window,document,location,URL,HTMLMediaElement:MediaElement,Date:class{static now(){return now}},
    navigator:{mediaDevices:{enumerateDevices:async()=>state.devices,addEventListener(name,fn){mediaEvents.set(name,fn)}}},
    localStorage:{getItem:key=>storage.has(key)?storage.get(key):null,setItem:(key,value)=>storage.set(key,value)},
    getComputedStyle(){return {display:'block',visibility:'visible'}},
    setInterval(fn){interval=fn},setTimeout(fn,ms){timers.push({fn,at:now+ms})}
  });
  function tick(ms=400){now+=ms;const due=timers.filter(t=>t.at<=now);for(const item of due){timers.splice(timers.indexOf(item),1);item.fn()}interval()}
  return {state,count,video,location,messages,storage,events,mediaEvents,api:window.famicomplayerNative,tick,
    end(){video.ended=true;video.paused=true;video.currentTime=video.duration},advance,
    noErrors(){assert.deepEqual(messages.filter(m=>m.type==='bridge-error'),[])}};
}

test('skips enabled visible ads, retries, then requests native input once',()=>{
  const f=fixture();f.state.ad=true;f.state.skipVisible=true;f.tick();assert.equal(f.count.skip,1);
  for(let i=0;i<15;i++)f.tick();assert.equal(f.count.skip,4);assert.equal(f.messages.filter(m=>m.type==='skip-input').length,1);
  f.state.ad=false;f.tick();assert.equal(f.count.skip,4);f.noErrors();
});
test('never clicks hidden/disabled skip controls or ordinary content',()=>{
  const f=fixture();f.state.ad=true;f.tick();assert.equal(f.count.skip,0);
  f.state.skipVisible=true;f.state.skipEnabled=false;f.tick();assert.equal(f.count.skip,0);
  f.state.skipEnabled=true;f.state.ad=false;f.tick();assert.equal(f.count.skip,0);f.noErrors();
});
test('ad end cannot advance the content playlist',()=>{
  const f=fixture();f.state.ad=true;f.end();for(let i=0;i<30;i++)f.tick();assert.equal(f.count.next,0);f.noErrors();
});
test('native automatic navigation wins without a second next',()=>{
  const f=fixture();f.tick();f.end();f.tick();f.advance();for(let i=0;i<20;i++)f.tick();assert.equal(f.count.next,0);assert.equal(f.count.nextClick,0);f.noErrors();
});
test('ended list falls back from ineffective API to button to verified URL',()=>{
  const f=fixture();f.tick();f.end();for(let i=0;i<22;i++)f.tick();
  assert.equal(f.count.next,1);assert.equal(f.count.nextClick,1);assert.equal(f.count.navigations.length,1);
  const target=new URL(f.count.navigations[0]);assert.equal(target.searchParams.get('v'),ids[1]);assert.equal(target.searchParams.get('list'),'RD2qfoSxRRCJc');assert.equal(target.searchParams.get('index'),'2');assert.equal(target.searchParams.has('t'),false);f.noErrors();
});
test('effective internal next does not also click or navigate',()=>{
  const f=fixture();f.tick();f.state.nativeNext=true;f.api.next();for(let i=0;i<20;i++)f.tick();
  assert.equal(f.count.next,1);assert.equal(f.count.nextClick,0);assert.equal(f.count.navigations.length,0);f.noErrors();
});
test('repeated next requests are coalesced until a transition completes',()=>{
  const f=fixture();f.tick();f.api.next();f.api.next();f.api.next();assert.equal(f.count.next,1);f.noErrors();
});
test('last playlist item does not fall through to an unrelated recommendation',()=>{
  const f=fixture();f.state.index=2;f.location.href=`https://www.youtube.com/watch?v=${ids[2]}&list=RD2qfoSxRRCJc&index=3`;f.tick();f.end();for(let i=0;i<30;i++)f.tick();assert.equal(f.count.next,0);f.noErrors();
});
test('standalone video is not automatically advanced',()=>{
  const f=fixture();f.location.href=`https://www.youtube.com/watch?v=${ids[0]}`;f.tick();f.end();for(let i=0;i<30;i++)f.tick();assert.equal(f.count.next,0);f.noErrors();
});
test('explicit pause survives SPA navigation',()=>{
  const f=fixture();f.tick();f.api.pause();f.advance();for(let i=0;i<15;i++)f.tick();assert.equal(f.count.play,0);assert.equal(f.video.paused,true);f.noErrors();
});
test('playing intent resumes a reused video element after SPA navigation',()=>{
  const f=fixture();f.tick();f.advance();for(let i=0;i<3;i++)f.tick();assert.equal(f.count.play,1);assert.equal(f.video.paused,false);f.noErrors();
});
test('duplicate videos in a playlist advance by occurrence index',()=>{
  const f=fixture({ids:[ids[0],ids[0],ids[2]]});f.tick();f.end();for(let i=0;i<22;i++)f.tick();
  assert.equal(f.count.navigations.length,1);assert.equal(new URL(f.count.navigations[0]).searchParams.get('v'),ids[0]);assert.equal(new URL(f.count.navigations[0]).searchParams.get('index'),'2');f.noErrors();
});
test('failed navigation stops retrying the same ended track',()=>{
  const f=fixture({blockNavigation:true});f.tick();f.end();for(let i=0;i<100;i++)f.tick();
  assert.equal(f.count.next,1);assert.equal(f.count.nextClick,1);assert.equal(f.count.navigations.length,1);assert.ok(f.messages.some(m=>m.type==='notice'));f.noErrors();
});
test('selected speaker persists and unplugging it falls back to default',async()=>{
  const f=fixture();f.tick();f.api.setAudioOutput('speaker-1');await new Promise(setImmediate);
  assert.equal(f.video.sinkId,'speaker-1');assert.equal(f.storage.get('famicomplayer.outputDevice'),'speaker-1');
  const next=fixture({storage:f.storage});next.tick();await new Promise(setImmediate);assert.equal(next.video.sinkId,'speaker-1');
  f.state.devices=f.state.devices.filter(d=>d.deviceId!=='speaker-1');await f.api.listAudioOutputs();await new Promise(setImmediate);
  assert.equal(f.video.sinkId,'');assert.ok(f.messages.some(m=>m.type==='audio-output'&&m.fallback));f.noErrors();next.noErrors();
});

test('rapid speaker changes cannot persist a stale asynchronous result',async()=>{
  const f=fixture();f.tick();
  let finish;
  f.video.setSinkId=id=>new Promise(resolve=>{finish=()=>{f.video.sinkId=id;resolve()}});
  f.api.setAudioOutput('speaker-1');f.api.setAudioOutput('');
  finish();await new Promise(setImmediate);
  assert.equal(f.messages.some(m=>m.type==='audio-output'&&m.deviceId==='speaker-1'),false);
  finish();await new Promise(setImmediate);
  assert.equal(f.video.sinkId,'');assert.equal(f.storage.get('famicomplayer.outputDevice'),'');f.noErrors();
});

test('connecting playback reports buffering but explicit pause and the homepage do not',()=>{
  const f=fixture();
  f.video.readyState=1;f.tick();
  assert.equal(f.messages.at(-1).buffering,true);
  assert.equal(f.messages.at(-1).playbackRequested,true);
  f.api.pause();f.tick();
  assert.equal(f.messages.at(-1).buffering,false);
  assert.equal(f.messages.at(-1).playbackRequested,false);
  f.api.play();f.video.readyState=4;f.tick();
  assert.equal(f.messages.at(-1).buffering,false);
  f.location.href='https://www.youtube.com/';f.video.readyState=0;f.tick();
  assert.equal(f.messages.at(-1).buffering,false);
  assert.equal(f.messages.at(-1).playbackRequested,false);f.noErrors();
});

test('pause cancels pending playback even before the video element exists',()=>{
  const f=fixture({noVideo:true});f.tick();
  assert.equal(f.messages.at(-1).buffering,true);
  f.api.pause();for(let i=0;i<8;i++)f.tick();
  assert.equal(f.messages.at(-1).buffering,false);
  assert.equal(f.messages.at(-1).playbackRequested,false);
  f.api.play();f.tick();assert.equal(f.messages.at(-1).buffering,true);f.noErrors();
});

test('failed next stops buffering until a fresh playback request',()=>{
  const f=fixture({blockNavigation:true});f.video.readyState=1;f.tick();f.api.next();
  for(let i=0;i<40;i++)f.tick();
  assert.ok(f.messages.some(m=>m.type==='notice'));
  assert.equal(f.messages.at(-1).buffering,false);
  f.api.next();f.tick();assert.equal(f.messages.at(-1).buffering,true);f.noErrors();
});

test('play rejection ends playback intent, while navigation AbortError keeps it',async()=>{
  for(const name of ['NotAllowedError','AbortError']){
    const f=fixture();f.video.readyState=1;
    f.video.play=()=>Promise.reject(Object.assign(new Error('fixture rejection'),{name}));
    f.api.play();await new Promise(setImmediate);f.tick();
    assert.equal(f.messages.at(-1).playbackRequested,name==='AbortError');
    assert.equal(f.messages.at(-1).buffering,name==='AbortError');
    assert.equal(f.messages.some(m=>m.type==='notice'),name!=='AbortError');f.noErrors();
  }
});

test('playback bridge never modifies Google sign-in or session-transfer pages',()=>{
  for(const hostname of ['accounts.google.com','accounts.youtube.com','www.google.com','consent.youtube.com']){
    const window={};window.top=window;
    vm.runInNewContext(source,{window,location:{hostname}});
    assert.equal(window.famicomplayerNative,undefined);
  }
});

test('cartridge gate blocks real media play until native insertion approval',async()=>{
  const f=fixture({gate:true});f.video.readyState=1;
  const playback=f.video.play();f.tick();
  assert.equal(f.count.play,0);assert.equal(f.video.muted,true);
  assert.equal(f.messages.some(m=>m.type==='cartridge-ready'),false);
  assert.equal(f.messages.at(-1).buffering,true);
  f.video.readyState=3;f.tick();
  const request=f.messages.find(m=>m.type==='cartridge-ready');
  assert.equal(request.videoId,ids[0]);assert.equal(f.count.play,0);
  assert.equal(f.messages.at(-1).playing,false);
  assert.equal(f.messages.at(-1).insertionPending,true);
  assert.equal(f.messages.at(-1).insertionRequestId,request.requestId);
  assert.equal(f.messages.at(-1).buffering,false);
  for(let i=0;i<15;i++)f.tick();
  assert.equal(f.messages.filter(m=>m.type==='cartridge-ready').length,1);
  assert.equal(f.count.play,0);
  assert.equal(f.api.releaseCartridge(request.requestId),true);
  await playback;f.tick();
  assert.equal(f.count.play,1);assert.equal(f.video.muted,false);
  assert.equal(f.messages.at(-1).playing,true);f.noErrors();
});

test('cartridge autoplay events are silenced, and stale player metadata cannot request insertion',()=>{
  const f=fixture({gate:true});f.state.playerVideoId=ids[1];
  f.video.paused=false;f.events.get('play')({target:f.video});
  assert.equal(f.video.paused,true);assert.equal(f.video.muted,true);
  f.tick();assert.equal(f.messages.some(m=>m.type==='cartridge-ready'),false);
  f.state.playerVideoId=ids[0];f.tick();
  assert.equal(f.messages.filter(m=>m.type==='cartridge-ready').length,1);f.noErrors();
});

test('next-track and pause cancellation reject stale cartridge approvals',async()=>{
  const f=fixture({gate:true});f.tick();
  const first=f.messages.find(m=>m.type==='cartridge-ready');
  f.state.nativeNext=true;f.api.next();f.tick();
  const second=f.messages.filter(m=>m.type==='cartridge-ready').at(-1);
  assert.notEqual(first.requestId,second.requestId);assert.equal(second.videoId,ids[1]);
  assert.equal(f.api.releaseCartridge(first.requestId),false);
  assert.equal(f.count.play,0);
  f.api.pause();assert.equal(f.api.releaseCartridge(second.requestId),false);f.tick();
  assert.equal(f.messages.at(-1).playbackRequested,false);
  f.api.play();f.tick();
  const third=f.messages.filter(m=>m.type==='cartridge-ready').at(-1);
  assert.notEqual(second.requestId,third.requestId);
  assert.equal(f.api.releaseCartridge(third.requestId),true);
  await new Promise(setImmediate);assert.equal(f.count.play,1);f.noErrors();
});

test('resuming a paused cartridge requires a fresh insertion approval',async()=>{
  const f=fixture({gate:true});f.tick();
  const first=f.messages.find(m=>m.type==='cartridge-ready');
  f.api.releaseCartridge(first.requestId);f.tick();assert.equal(f.count.play,1);
  f.api.pause();f.api.play();f.tick();
  const second=f.messages.filter(m=>m.type==='cartridge-ready').at(-1);
  assert.notEqual(first.requestId,second.requestId);
  assert.equal(f.count.play,1);assert.equal(f.messages.at(-1).insertionPending,true);
  f.api.releaseCartridge(second.requestId);await new Promise(setImmediate);
  assert.equal(f.count.play,2);f.noErrors();
});

test('automatic playlist advance waits for the next cartridge insertion',()=>{
  const f=fixture({gate:true});f.tick();
  f.api.releaseCartridge(f.messages.find(m=>m.type==='cartridge-ready').requestId);
  f.state.nativeNext=true;f.end();for(let i=0;i<12;i++)f.tick();
  const requests=f.messages.filter(m=>m.type==='cartridge-ready');
  assert.equal(requests.length,2);assert.equal(requests[1].videoId,ids[1]);
  assert.equal(f.count.play,1);assert.equal(f.messages.at(-1).playing,false);
  assert.equal(f.api.releaseCartridge(requests[1].requestId),true);assert.equal(f.count.play,2);f.noErrors();
});

test('ads bypass cartridge gating and content waits for insertion after the ad',async()=>{
  const f=fixture({gate:true});f.state.ad=true;await f.video.play();f.tick();
  assert.equal(f.count.play,1);assert.equal(f.messages.some(m=>m.type==='cartridge-ready'),false);
  f.state.ad=false;f.tick();
  const request=f.messages.find(m=>m.type==='cartridge-ready');
  assert.equal(f.video.paused,true);assert.equal(f.video.muted,true);
  assert.equal(f.count.play,1);assert.equal(f.messages.at(-1).insertionPending,true);
  f.api.releaseCartridge(request.requestId);assert.equal(f.count.play,2);f.noErrors();
});

test('browser mode bypasses a pending gate and returning to a playing widget does not insert again',()=>{
  const f=fixture({gate:true});f.tick();
  const request=f.messages.find(m=>m.type==='cartridge-ready');
  f.api.setWidgetMode(false);f.tick();assert.equal(f.count.play,1);
  assert.equal(f.api.releaseCartridge(request.requestId),false);
  assert.equal(f.video.muted,false);assert.equal(f.messages.at(-1).insertionPending,false);
  f.api.setWidgetMode(true);f.tick();
  assert.equal(f.messages.filter(m=>m.type==='cartridge-ready').length,1);
  assert.equal(f.messages.at(-1).playing,true);f.noErrors();
});

test('navigation aborts a held media play even before cartridge metadata becomes ready',async()=>{
  const f=fixture({gate:true});f.video.readyState=1;
  let rejected;
  const oldPlayback=f.video.play().catch(error=>{rejected=error.name});
  f.advance();f.tick();await oldPlayback;
  assert.equal(rejected,'AbortError');assert.equal(f.count.play,0);
  f.video.readyState=3;f.tick();
  const request=f.messages.filter(m=>m.type==='cartridge-ready').at(-1);
  assert.equal(request.videoId,ids[1]);assert.equal(f.api.releaseCartridge(request.requestId),true);f.noErrors();
});

test('an ad cancels a prepared cartridge approval and content gets a fresh token afterward',()=>{
  const f=fixture({gate:true});f.tick();
  const beforeAd=f.messages.find(m=>m.type==='cartridge-ready');
  f.state.ad=true;f.tick();
  assert.equal(f.api.releaseCartridge(beforeAd.requestId),false);
  assert.equal(f.messages.at(-1).insertionPending,false);
  f.state.ad=false;f.tick();
  const afterAd=f.messages.filter(m=>m.type==='cartridge-ready').at(-1);
  assert.notEqual(beforeAd.requestId,afterAd.requestId);
  assert.equal(f.messages.at(-1).insertionPending,true);f.noErrors();
});
