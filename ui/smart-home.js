(() => {
  let state=null,selected='';
  const chosenDays={};
  try { selected=localStorage.getItem('smart-room')||''; } catch {}
  const labels={cleaning:'청소 중',docked:'충전대',returning:'복귀 중',paused:'일시정지',idle:'대기',error:'기기 확인 필요',playing:'재생 중',off:'꺼짐',on:'켜짐'};
  const text=value=>labels[value]||value;
  function element(tag,content,className) {
    const node=document.createElement(tag);
    if(content!=null)node.textContent=content;
    if(className)node.className=className;
    return node;
  }
  function request(action,itemId) { send('smart',{action,roomId:selected,itemId}); }
  function button(label,action,enabled=true,itemId=null) {
    const node=element('button',label);
    node.disabled=!enabled||!!state?.busy;
    node.onclick=()=>{press(node);request(action,itemId);};
    return node;
  }
  function status(entity,empty) { return !entity?empty:entity.available?text(entity.state):'확인 불가'; }
  function card(title,description) {
    const node=element('article',null,'device-card');
    node.append(element('h2',title),element('p',description));
    return node;
  }
  function controls(node,buttons) {
    const row=element('div',null,'device-controls');
    row.append(...buttons);node.append(row);
  }
  function render(snapshot) {
    if(!snapshot)return;
    state=snapshot;
    $('smart-settings').disabled=!!state.busy;
    if(!state.rooms.some(room=>room.id===selected))selected=state.rooms[0]?.id||'';
    const room=state.rooms.find(item=>item.id===selected);
    $('smart-status').textContent=state.status;
    $('smart-updated').textContent=state.updatedUtc?'HA 확인 · '+new Date(state.updatedUtc).toLocaleTimeString('ko-KR'):'설정에서 집과 기기를 연결하세요.';
    $('smart-status').classList.toggle('connected',state.connected);
    $('smart-error').textContent=state.error||'';
    $('smart-rooms').replaceChildren();
    for(const item of state.rooms) {
      const tab=element('button',item.name);
      tab.setAttribute('aria-pressed',String(item.id===selected));
      tab.onclick=()=>{selected=item.id;try{localStorage.setItem('smart-room',selected);}catch{}render(state);};
      $('smart-rooms').append(tab);
    }
    const cards=$('smart-cards');cards.replaceChildren();
    if(!room)return;
    const vacuum=card('청소',status(room.vacuum,'청소기를 연결하세요.'));
    controls(vacuum,[button('시작','start',room.start),button('일시정지','pause',room.pause),button('복귀','return',room.home)]);
    controls(vacuum,[button('이 방 다시 청소','room-clean',room.roomClean)]);
    if(!room.roomClean)vacuum.append(element('small','방별 청소는 HA에서 검증한 스크립트를 연결하면 켜집니다.'));
    const music=card('음악',status(room.speaker,'Music Assistant의 스피커를 연결하세요.'));
    controls(music,[button('클래식 ▶','classic',room.classic),button('인기가요 ▶','pop',room.pop),button('정지','music-stop',room.stopMusic)]);
    const sensors=card('집 상태','');
    const windowState=!room.window?'연결 전':!room.window.available?'확인 불가':room.window.state==='on'?'열림':room.window.state==='off'?'닫힘':text(room.window.state);
    sensors.append(element('div','창문 · '+windowState,'sensor-line'),element('div','건조기 · '+status(room.dryer,'연결 전'),'sensor-line'));
    const archive=card('영상 보관',state.archive?.status||'영상 기록을 확인하세요.');
    controls(archive,[button('영상 가져오기','import'),button('전송 폴더','open-inbox'),button('보관 폴더','open-archive')]);
    archive.append(element('small',[state.watch?'폴더 수집 켜짐':'폴더 수집 꺼짐',state.daily?'예약 편집 켜짐':'예약 편집 꺼짐',state.autoUpload?'자동 업로드 켜짐':'업로드 확인 후 실행'].join(' · ')));
    const photos=(state.archive?.photoCounts||[]).filter(item=>item.roomId===room.id);
    for(const collection of photos.slice(-3))archive.append(element('p',`${collection.day} · 사진 ${collection.count.toLocaleString()}장 · 최대 수집 간격 ${Math.round(collection.longestGapSeconds)}초`,'archive-note'));
    const items=(state.archive?.items||[]).filter(item=>item.roomId===room.id);
    for(const item of items.slice(0,8)) {
      const row=element('div',null,'archive-row');
      row.append(element('b',item.name),element('small',`${item.day} · ${item.state} · ${(item.bytes/1048576).toFixed(1)}MB`));
      if(!item.dateConfirmed)row.append(button('촬영 시각 확인','date',true,item.id));
      archive.append(row);
    }
    const days=[...new Set([...items.filter(item=>item.dateConfirmed).map(item=>item.day),...photos.map(item=>item.day)])].sort().reverse();
    if(days.length) {
      const date=document.createElement('select');date.setAttribute('aria-label','편집할 촬영 날짜');
      for(const day of days) { const option=element('option',day);option.value=day;date.append(option); }
      if(days.includes(chosenDays[room.id]))date.value=chosenDays[room.id];
      date.onchange=()=>{chosenDays[room.id]=date.value;};
      const prepare=element('button','선택한 날짜 편집');prepare.disabled=!!state.busy;prepare.onclick=()=>request('prepare',date.value);
      const row=element('div',null,'device-controls');row.append(date,prepare);archive.append(row);
    }
    for(const job of (state.archive?.jobs||[]).filter(item=>item.roomId===room.id).slice(0,8)) {
      const row=element('div',null,'archive-row');
      row.append(element('b',job.day+' · 원본 '+job.count+'개'),element('p',job.detail));
      if(job.extra>0)row.append(element('small','늦게 도착한 원본 '+job.extra+'개 · 기존 작업에는 자동 추가되지 않습니다.'));
      const actions=[];
      if(job.extra>0)actions.push(button('늦게 온 원본만 추가 편집','prepare-extra',true,job.id));
      if(['ready','processing','upload-retry','uploading','done'].includes(job.state))actions.push(button(job.state==='done'?'비공개 영상 보기':'완성 영상 보기','open-job',true,job.id));
      if(job.state==='ready')actions.push(button('비공개 업로드','upload',state.youtubeConnected,job.id));
      if(['error','upload-retry','processing'].includes(job.state))actions.push(button('재시도','retry',true,job.id));
      if(job.state==='review')actions.push(button('업로드 결과 확인','resolve-upload',true,job.id));
      if(actions.length)controls(row,actions);
      archive.append(row);
    }
    if(!items.length&&!photos.length)archive.append(element('p','가져온 영상이 없습니다. U+ 앱에서 갤러리에 저장한 파일을 전송 폴더에 넣거나 직접 선택하세요.','archive-note'));
    cards.append(vacuum,music,sensors,archive);
  }
  $('smart-settings').onclick=()=>send('smart-settings');
  window.smartHomeView={render};
})();
