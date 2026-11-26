"""Serve the production WebView UI with an isolated fake native bridge for UI QA.

This harness cannot launch AI apps or execute PC commands. Native persistence is
covered by ProjectSkillTests.cs; this bridge tests browser rendering/events only.
"""
import argparse
import functools
import json
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import re

parser = argparse.ArgumentParser()
parser.add_argument('--port', type=int, default=8942)
args = parser.parse_args()
ui = Path(__file__).resolve().parent.parent / 'ui'
catalog = []
for root in (Path.home() / '.codex' / 'skills', Path.home() / '.codex' / 'skills' / '.system',
             Path.home() / '.agents' / 'skills', Path.home() / '.claude' / 'skills'):
    for file in sorted(root.glob('*/SKILL.md')):
        text = file.read_text(encoding='utf-8-sig')
        description = re.search(r'(?m)^description:[ \t]*(.*)', text)
        catalog.append(dict(id=str(len(catalog)), name=file.parent.name,
                            description=description.group(1).strip('"\'') if description else '설치된 스킬',
                            source='내 스킬 · 화면 시험', path=str(file)))

bridge = r'''
'use strict';
(() => {
 const catalog=__CATALOG__;
 const projects=[{id:'cycle',name:'work-cycle · 화면 시험',folder:'C:\\Projects\\work-cycle'},{id:'hub',name:'Workstation Hub · 화면 시험',folder:'C:\\Projects\\Workstation_Hub'}];
 let saved;try{saved=JSON.parse(localStorage.getItem('skills-native-ui-test-v1'));}catch{}
 saved=saved||{project:'cycle',start:'both',buttons:{cycle:[],hub:[]}};
 let handler;
 function state(){return {kind:'state',mode:'대기',home:{lamp:true,bar:true,ac:false},active:[],enabled:{home:true},macro:{muted:false,recording:null,busy:[]},skills:{readOnly:false,loading:false,status:'화면 시험 · 실제 AI 앱에는 전송하지 않습니다.',runningId:null,selectedProjectId:saved.project,startApps:saved.start,projects,buttons:saved.buttons[saved.project],catalog,warnings:[]}};}
 function emit(data){handler?.({data});}
 window.chrome=window.chrome||{};
 window.chrome.webview={addEventListener:(name,callback)=>handler=callback,postMessage:message=>queueMicrotask(()=>{
   const p=message.projectId;
   if(message.type==='skill-project-select')saved.project=p;
   if(message.type==='skill-start-apps')saved.start=message.value;
   if(message.type==='skill-save'){
     const skill=catalog.find(s=>s.id===message.skillId);
     const button={id:message.buttonId||crypto.randomUUID(),skillId:skill.id,skillName:skill.name,label:message.label,target:message.target,instruction:message.instruction};
     const index=saved.buttons[p].findIndex(b=>b.id===button.id);
     if(index<0)saved.buttons[p].push(button);else saved.buttons[p][index]=button;
   }
   if(message.type==='skill-remove')saved.buttons[p]=saved.buttons[p].filter(b=>b.id!==message.buttonId);
   localStorage.setItem('skills-native-ui-test-v1',JSON.stringify(saved));
   emit(state());emit({kind:'skills-result',action:message.type});
 })};
 document.addEventListener('DOMContentLoaded',()=>{
   const banner=document.createElement('p');banner.textContent='실제 앱 UI 검증 · 테스트 프로젝트 · PC 명령과 AI 전송은 실행되지 않습니다.';banner.style.cssText='margin:0;padding:8px;background:#ffe7a0;color:#23422e;text-align:center;font-size:12px';document.body.prepend(banner);
 });
})();
'''.replace('__CATALOG__', json.dumps(catalog, ensure_ascii=False))


class Handler(SimpleHTTPRequestHandler):
    def do_GET(self):
        path = self.path.split('?', 1)[0]
        if path == '/__fixture.js':
            content = bridge.encode('utf-8')
            mime = 'text/javascript; charset=utf-8'
        elif path == '/desk.html':
            content = (ui / 'desk.html').read_text(encoding='utf-8').replace(
                '<script src="vendor/gsap.min.js">', '<script src="__fixture.js"></script><script src="vendor/gsap.min.js">').encode('utf-8')
            mime = 'text/html; charset=utf-8'
        else:
            return super().do_GET()
        self.send_response(200)
        self.send_header('Content-Type', mime)
        self.send_header('Content-Length', str(len(content)))
        self.send_header('Cache-Control', 'no-store')
        self.end_headers()
        self.wfile.write(content)


print(f'UI QA: http://127.0.0.1:{args.port}/desk.html?panel=macro', flush=True)
ThreadingHTTPServer(('127.0.0.1', args.port), functools.partial(Handler, directory=str(ui))).serve_forever()

