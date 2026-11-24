(() => {
  const roomDialog=$('room-dialog');
  let room=null,generation=0,library=null;
  function syncRoom(){
    if(!model)return;
    const h=model.home;
    for(const [id,on,label] of [['room-lamp',h.lamp,'스탠드'],['room-ac',h.ac,'냉방']]){
      $(id).textContent=label+' · '+(on?'켜짐':'꺼짐');$(id).setAttribute('aria-pressed',String(on));
    }
    if(!room)return;
    room.light.intensity=h.lamp?9*h.brightness/100:0;
    room.bulb.material.emissive.setHex(h.lamp?0xffcf69:0x222818);
    room.glow.visible=h.lamp;room.glow.material.opacity=h.brightness*.0023;
    room.acLed.material.color.setHex(h.ac?0x9dc986:0xb6beb0);
    requestRoomRender();
  }
  $('close-room').onclick=()=>roomDialog.close();
  $('room-lamp').onclick=()=>send('home',{id:'lamp'});
  $('room-ac').onclick=()=>send('home',{id:'ac'});
  document.addEventListener('visibilitychange',()=>{if(!document.hidden)requestRoomRender()});
  window.addEventListener('pagehide',disposeRoom);
  window.roomView={sync:syncRoom};
  function requestRoomRender() {
    if (!room || !roomDialog.open || document.hidden || room.pending) return;
    room.pending = requestAnimationFrame(() => {
      if (!room) return;
      room.pending = 0;
      if (!roomDialog.open || document.hidden) return;
      const stage = document.getElementById('room-stage');
      const w = stage.clientWidth, h = stage.clientHeight;
      room.renderer.setSize(w, h, false);
      if(!w||!h)return;const aspect = w / h, span = Math.max(6.7,7.7/aspect);
      room.camera.left = -span * aspect / 2; room.camera.right = span * aspect / 2;
      room.camera.top = span / 2; room.camera.bottom = -span / 2;
      room.camera.updateProjectionMatrix(); room.renderer.render(room.scene, room.camera);
      roomDialog.dataset.frames = String(Number(roomDialog.dataset.frames || 0) + 1);
      roomDialog.dataset.renderState = 'idle';
    });
  }
  function disposeRoom() {
    if (room) {
      cancelAnimationFrame(room.pending);
      room.observer.disconnect();
      room.renderer.domElement.removeEventListener('click', room.click);
      room.scene.traverse(obj => { if (obj.geometry) obj.geometry.dispose(); if (obj.material) (Array.isArray(obj.material) ? obj.material : [obj.material]).forEach(m => m.dispose()); });
      room.renderer.dispose(); room.renderer.forceContextLoss(); room.renderer.domElement.remove(); room = null;
    }
    roomDialog.dataset.renderState = 'closed';
  }
  async function openRoom() {
    roomDialog.showModal();const epoch=++generation;syncRoom();
    document.getElementById('room-fallback').hidden = true;
    roomDialog.dataset.renderState = 'loading'; roomDialog.dataset.frames = '0';
    let pendingRenderer = null, pendingScene = null;
    try {
      if (!window.THREE) await (library ||= loadScript('vendor/v2-three.min.js').catch(e=>{library=null;throw e}));
      if(!roomDialog.open||epoch!==generation)return;
      const T = window.THREE;
      const stage = document.getElementById('room-stage');
      const renderer = new T.WebGLRenderer({ antialias: true, alpha: false, powerPreference: 'low-power' });
      pendingRenderer = renderer;
      renderer.setPixelRatio(Math.min(devicePixelRatio || 1, 1.5));
      renderer.setClearColor(0xe9ece0); renderer.outputColorSpace = T.SRGBColorSpace;
      const scene = new T.Scene();
      pendingScene = scene;
      const camera = new T.OrthographicCamera(-5, 5, 3, -3, .1, 60);
      camera.position.set(7, 6.5, 8); camera.lookAt(0, .6, 0);
      const ambient = new T.HemisphereLight(0xfff9e8, 0x657d59, 2.5); scene.add(ambient);
      const daylight = new T.DirectionalLight(0xfff7df, 2); daylight.position.set(-3, 7, 4); scene.add(daylight);
      function material(color, extra = {}) { return new T.MeshStandardMaterial({ color, roughness: .85, ...extra }); }
      function box(w, h, d, color, x, y, z, key) {
        const mesh = new T.Mesh(new T.BoxGeometry(w, h, d), material(color)); mesh.position.set(x, y, z); if (key) mesh.userData.action = key; scene.add(mesh); return mesh;
      }
      box(5.6, .16, 4.4, 0xc5d0b5, 0, -.1, 0);
      box(5.6, 2.9, .12, 0xdce2cf, 0, 1.3, -2.18);
      box(.12, 2.9, 4.4, 0xcad6bd, -2.74, 1.3, 0);
      box(1.65, 1.45, .05, 0x94b1a3, -1.2, 1.75, -2.08);
      box(.045, 1.5, .08, 0xf4f0df, -1.2, 1.75, -2.03);
      box(1.7, .045, .08, 0xf4f0df, -1.2, 1.75, -2.03);
      box(3.25, .16, 1.3, 0xaa8b63, -.35, .95, -.5);
      for (const x of [-1.78, 1.08]) for (const z of [-1, .0]) box(.1, .9, .1, 0x365442, x, .43, z);
      box(1.48, .9, .1, 0x244b3c, -.45, 1.62, -.85);
      box(1.32, .72, .025, 0x84a99a, -.45, 1.64, -.787);
      box(.1, .3, .12, 0x365542, -.45, 1.12, -.85);
      box(.55, .045, .32, 0x365542, -.45, 1.05, -.8);
      box(1.03, .055, .34, 0xece8d7, -.45, 1.075, -.12);
      box(.19, .07, .24, 0xf3ca58, .39, 1.09, -.12);
      box(.46, .92, .75, 0x214838, 1.68, .42, -.63);
      box(.035, .55, .42, 0x86a28e, 1.915, .51, -.62);
      box(.8, .12, .75, 0xc0cfae, -.5, .57, 1.02);
      box(.8, .85, .13, 0x6c8867, -.5, 1.03, 1.35);
      box(.12, .5, .12, 0x365442, -.5, .28, 1.04);
      box(1, .07, .7, 0x365442, -.5, .06, 1.04);
      box(.4, .05, .4, 0xd3bc7c, .93, 1.065, -.68, 'lamp');
      box(.045, .56, .045, 0xd3bc7c, .93, 1.35, -.68, 'lamp');
      const shade = new T.Mesh(new T.ConeGeometry(.27, .3, 24, 1, true), material(0xf1d794, { side: T.DoubleSide })); shade.position.set(.93, 1.68, -.68); shade.userData.action = 'lamp'; scene.add(shade);
      const bulb = new T.Mesh(new T.SphereGeometry(.1, 12, 8), material(0xffe7ae, { emissive: 0xffcf69, emissiveIntensity: .7 })); bulb.position.set(.93, 1.58, -.68); bulb.userData.action = 'lamp'; scene.add(bulb);
      const light = new T.PointLight(0xffd087, 6, 4, 2); light.position.set(.93, 1.45, -.68); scene.add(light);
      const glow = new T.Mesh(new T.CircleGeometry(.52, 32), new T.MeshBasicMaterial({ color: 0xffd475, transparent: true, opacity: .18, depthWrite: false })); glow.rotation.x = -Math.PI / 2; glow.position.set(.93, 1.04, -.65); scene.add(glow);
      box(1.45, .4, .26, 0xf1f0e5, 1.38, 2.25, -1.99, 'ac');
      box(1.21, .035, .035, 0xb2beaa, 1.38, 2.14, -1.84, 'ac');
      const acLed = box(.08, .035, .035, 0xb6beb0, 1.94, 2.25, -1.84, 'ac');
      const pot = new T.Mesh(new T.CylinderGeometry(.22, .17, .36, 20), material(0xc89985)); pot.position.set(-2.23, .18, 1.4); scene.add(pot);
      for (let i = 0; i < 5; i++) { const leaf = new T.Mesh(new T.SphereGeometry(.3, 12, 8), material(i % 2 ? 0x54764c : 0x6f8c54)); leaf.scale.set(.45, 1.6, .65); leaf.position.set(-2.23 + Math.sin(i * 2) * .15, .63 + i * .025, 1.4 + Math.cos(i * 2) * .13); leaf.rotation.z = Math.sin(i * 2) * .5; scene.add(leaf); }
      renderer.domElement.setAttribute('role', 'img'); renderer.domElement.setAttribute('aria-label', '작업실 3D 배치 예시. 조명과 에어컨을 클릭하거나 아래 버튼으로 조작할 수 있습니다.');
      const raycaster = new T.Raycaster();
      const click = event => {
        if (!model) return;
        const rect = renderer.domElement.getBoundingClientRect();
        raycaster.setFromCamera(new T.Vector2((event.clientX - rect.left) / rect.width * 2 - 1, -(event.clientY - rect.top) / rect.height * 2 + 1), camera);
        const hit = raycaster.intersectObjects(scene.children).find(i => i.object.userData.action);
        if (hit) send('home',{id:hit.object.userData.action});
      };
      renderer.domElement.addEventListener('click', click); stage.prepend(renderer.domElement);
      const observer = new ResizeObserver(requestRoomRender); observer.observe(stage);
      room = { scene, renderer, camera, light, bulb, glow, acLed, pending: 0, observer, click, side: 0 };
      pendingRenderer = null; pendingScene = null;
      document.getElementById('room-status').textContent = '필요할 때만 화면을 그려요. 닫으면 3D 자원을 정리합니다.';
      syncRoom();
    } catch (error) {
      if (pendingScene) pendingScene.traverse(obj => { if (obj.geometry) obj.geometry.dispose(); if (obj.material) (Array.isArray(obj.material) ? obj.material : [obj.material]).forEach(m => m.dispose()); });
      if (pendingRenderer) { pendingRenderer.dispose(); pendingRenderer.forceContextLoss(); }
      disposeRoom(); document.getElementById('room-stage').querySelectorAll('canvas').forEach(c => c.remove());
      document.getElementById('room-fallback').hidden = false; roomDialog.dataset.renderState = 'fallback';
      document.getElementById('room-status').textContent = '3D를 사용할 수 없어 간단한 배치와 같은 제어 버튼을 보여드려요.';
    }
  }
  document.getElementById('open-room').addEventListener('click', openRoom);
  roomDialog.addEventListener('close', disposeRoom);
  function viewpoint(direction) {
    if (!room) return;
    room.side = Math.max(-2, Math.min(2, room.side + direction));
    const a = .72 + room.side * .18;
    room.camera.position.set(Math.sin(a) * 11, 6.5, Math.cos(a) * 11); room.camera.lookAt(0, .6, 0); requestRoomRender();
  }
  document.getElementById('room-view-left').addEventListener('click', () => viewpoint(-1));
  document.getElementById('room-view-right').addEventListener('click', () => viewpoint(1));

})();
