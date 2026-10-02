(() => {
  'use strict';
  const canvas = document.querySelector('#canvas');
  const ctx = canvas.getContext('2d', { alpha: false });
  const game = document.querySelector('#game');
  const intro = document.querySelector('#intro');
  const startButton = document.querySelector('#start');
  const soundButton = document.querySelector('#sound');
  const scoreNode = document.querySelector('#score');
  const STORAGE = { score: 'viniLine.score', sound: 'viniLine.sound', hundred: 'viniLine.hundred', seen: 'viniLine.seen' };
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const safeGet = (key, fallback) => { try { return localStorage.getItem(key) ?? fallback; } catch { return fallback; } };
  const safeSet = (key, value) => { try { localStorage.setItem(key, value); } catch { /* private browsing */ } };

  let W = 390, H = 844, dpr = 1, running = false, paused = false, last = 0, elapsed = 0;
  let total = Math.max(0, parseInt(safeGet(STORAGE.score, '0'), 10) || 0);
  let hundredSeen = safeGet(STORAGE.hundred, '0') === '1';
  let audioOn = safeGet(STORAGE.sound, '0') === '1', audio = null;
  let spawnIn = .65, specialMode = 0, shake = 0, targetX = W / 2;
  const hearts = [], particles = [], ripples = [], stars = [], skyline = [];
  const basket = { x: W / 2, y: H - 92, w: 112, h: 48, squash: 0 };
  const vini = { x: W / 2, y: 125, phase: 0, action: 0, look: 0 };
  scoreNode.textContent = total;
  game.classList.toggle('audio-on', audioOn);
  startButton.textContent = safeGet(STORAGE.seen, '0') === '1' ? 'continuar' : 'toque para começar';

  function resize() {
    const r = game.getBoundingClientRect(); W = r.width; H = r.height;
    dpr = Math.min(devicePixelRatio || 1, 2);
    canvas.width = Math.round(W * dpr); canvas.height = Math.round(H * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    basket.y = H - Math.max(76, 62 + parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--sab') || 0));
    basket.x = Math.max(basket.w / 2 + 10, Math.min(W - basket.w / 2 - 10, basket.x)); targetX = basket.x;
    vini.x = W / 2; vini.y = Math.max(105, Math.min(148, H * .16));
    makeScene(); draw(0);
  }
  function makeScene() {
    const seed = (n) => { const x = Math.sin(n * 91.73) * 43758.5; return x - Math.floor(x); };
    stars.length = 0; skyline.length = 0;
    for (let i = 0; i < 75; i++) stars.push({ x: seed(i) * W, y: seed(i + 99) * H * .72, r: .35 + seed(i + 7) * 1.25, a: .12 + seed(i + 2) * .55, p: seed(i + 18) * 6 });
    let x = 0, i = 0; while (x < W + 20) { const w = 18 + seed(i + 300) * 34; skyline.push({ x, w, h: 18 + seed(i + 400) * 62 }); x += w + 3; i++; }
  }
  function heartPath(c, x, y, s, rotation = 0) {
    c.save(); c.translate(x, y); c.rotate(rotation); c.scale(s / 30, s / 30); c.beginPath();
    c.moveTo(0, 10); c.bezierCurveTo(-4, 5, -15, -1, -15, -10); c.bezierCurveTo(-15, -22, -1, -25, 0, -14); c.bezierCurveTo(1, -25, 15, -22, 15, -10); c.bezierCurveTo(15, -1, 4, 5, 0, 10); c.closePath(); c.restore();
  }
  function spawnHeart(forceGold = false) {
    const roll = Math.random(); let type = forceGold ? 3 : roll < .012 ? 3 : roll < .07 ? 2 : roll < .27 ? 1 : 0;
    const size = (type === 3 ? 24 : 20) + Math.random() * 9;
    hearts.push({ x: vini.x + 17, y: vini.y + 25, vx: (Math.random() - .5) * 34, vy: 34 + Math.random() * 20, size, type, rot: -.2 + Math.random() * .4, spin: (Math.random() - .5) * .8, sway: 12 + Math.random() * 20, phase: Math.random() * 6, age: 0, alpha: 1 });
    vini.action = .42;
  }
  function burst(x, y, type) {
    const color = ['#ff6f91','#ff9fbd','#fff0f6','#ffd477'][type];
    for (let i = 0; i < (reduced ? 4 : 11); i++) particles.push({ x, y, vx: (Math.random() - .5) * 90, vy: -25 - Math.random() * 85, life: .45 + Math.random() * .35, max: .8, r: 1.2 + Math.random() * 2.4, color });
    ripples.push({ x, y, life: .35, color }); basket.squash = 1;
  }
  function catchHeart(h) {
    const points = [1, 1, 3, 5][h.type]; total += points; scoreNode.textContent = total; safeSet(STORAGE.score, total);
    burst(h.x, basket.y - basket.h * .35, h.type); tone(h.type); shake = h.type > 1 ? 3 : 0;
    if (total >= 100 && !hundredSeen) beginHundred();
  }
  function beginHundred() { hundredSeen = true; safeSet(STORAGE.hundred, '1'); specialMode = 5.6; shake = 2; }
  function update(dt) {
    elapsed += dt; vini.phase += dt;
    const slow = specialMode > 0 ? .38 : 1; const step = dt * slow;
    basket.x += (targetX - basket.x) * Math.min(1, dt * 13); basket.squash = Math.max(0, basket.squash - dt * 4.5);
    vini.action = Math.max(0, vini.action - dt);
    if (specialMode > 0) {
      specialMode -= dt;
      vini.look = specialMode > 3.4 ? 1 : specialMode > 2.3 ? -1 : specialMode > 1.25 ? 2 : 0;
      if (specialMode <= 1.25 && specialMode + dt > 1.25) spawnHeart(true);
    } else { vini.look = 0; spawnIn -= dt; if (spawnIn <= 0) { spawnHeart(); spawnIn = .72 + Math.random() * .72; } }
    for (let i = hearts.length - 1; i >= 0; i--) {
      const h = hearts[i]; h.age += step; h.vy += 22 * step; h.y += h.vy * step; h.x += (h.vx + Math.sin(h.age * 2.4 + h.phase) * h.sway) * step; h.rot += h.spin * step;
      const bx = basket.x, by = basket.y - basket.h * .32;
      if (h.vy > 0 && h.y + h.size * .35 > by && h.y < basket.y + 5 && Math.abs(h.x - bx) < basket.w * .48) { catchHeart(h); hearts.splice(i, 1); continue; }
      if (h.y > H + 55) hearts.splice(i, 1);
    }
    for (let i = particles.length - 1; i >= 0; i--) { const p = particles[i]; p.life -= dt; p.x += p.vx * dt; p.y += p.vy * dt; p.vy += 120 * dt; if (p.life <= 0) particles.splice(i, 1); }
    for (let i = ripples.length - 1; i >= 0; i--) { ripples[i].life -= dt; if (ripples[i].life <= 0) ripples.splice(i, 1); }
    shake = Math.max(0, shake - dt * 12);
  }
  function draw(t) {
    ctx.save(); if (shake) ctx.translate((Math.random()-.5)*shake, (Math.random()-.5)*shake);
    const warmth = Math.min(total / 130, 1);
    const bg = ctx.createLinearGradient(0,0,0,H); bg.addColorStop(0, `rgb(${9+warmth*7},${10+warmth*4},${36+warmth*7})`); bg.addColorStop(.65,'#11112d'); bg.addColorStop(1,'#1b132d'); ctx.fillStyle=bg;ctx.fillRect(-5,-5,W+10,H+10);
    drawScene(t, warmth); drawVini(); hearts.forEach(drawHeart); drawBasket(); drawEffects(); ctx.restore();
  }
  function drawScene(t, warmth) {
    const moonX=W*.79,moonY=H*.13,moonR=Math.min(W,H)*.075; let g=ctx.createRadialGradient(moonX,moonY,2,moonX,moonY,moonR*2.8);g.addColorStop(0,'#ffe7b930');g.addColorStop(1,'#ffe7b900');ctx.fillStyle=g;ctx.beginPath();ctx.arc(moonX,moonY,moonR*2.8,0,7);ctx.fill();ctx.fillStyle='#f7dfb8';ctx.globalAlpha=.82;ctx.beginPath();ctx.arc(moonX,moonY,moonR,0,7);ctx.fill();ctx.fillStyle='#d7c6b8';ctx.globalAlpha=.16;ctx.beginPath();ctx.arc(moonX-moonR*.3,moonY-moonR*.15,moonR*.2,0,7);ctx.fill();ctx.globalAlpha=1;
    const visible = Math.floor(34 + warmth * 41); stars.slice(0,visible).forEach(s=>{ctx.globalAlpha=s.a*(.72+.28*Math.sin(t*.001+s.p));ctx.fillStyle=s.r>1.25?'#ffdfae':'#d8d8fa';ctx.beginPath();ctx.arc(s.x,s.y,s.r,0,7);ctx.fill()});ctx.globalAlpha=1;
    ctx.fillStyle='#080b20aa'; skyline.forEach(b=>ctx.fillRect(b.x,H*.76-b.h,b.w,b.h));
    ctx.fillStyle='#ffc87933'; skyline.forEach((b,i)=>{if((i+Math.floor(t/1800))%3===0)ctx.fillRect(b.x+5,H*.76-b.h+9,2,3)});
    ctx.fillStyle='#09091d';ctx.beginPath();ctx.moveTo(0,H*.77);ctx.quadraticCurveTo(W*.2,H*.7,W*.43,H*.77);ctx.quadraticCurveTo(W*.72,H*.68,W,H*.76);ctx.lineTo(W,H);ctx.lineTo(0,H);ctx.fill();
  }
  function drawVini() {
    const bob = reduced ? 0 : Math.sin(vini.phase*2)*2; const x=vini.x,y=vini.y+bob; ctx.save();ctx.translate(x,y);
    if(vini.look===1)ctx.rotate(.09); if(vini.look===-1)ctx.rotate(-.07); if(vini.look===2)ctx.rotate(Math.sin(vini.phase*10)*.045);
    const throwT=vini.action/.42; if(throwT>0)ctx.rotate(-Math.sin(throwT*Math.PI)*.09);
    ctx.fillStyle='#17122d';ctx.globalAlpha=.35;ctx.beginPath();ctx.ellipse(0,49,40,9,0,0,7);ctx.fill();ctx.globalAlpha=1;
    ctx.fillStyle='#5d4169';roundRect(-24,18,48,34,15);ctx.fill();ctx.fillStyle='#f0c2a7';ctx.beginPath();ctx.arc(0,2,22,0,7);ctx.fill();
    ctx.fillStyle='#2b1d2d';ctx.beginPath();ctx.arc(0,-2,23,Math.PI,Math.PI*2);ctx.quadraticCurveTo(17,-25,8,-17);ctx.quadraticCurveTo(-7,-27,-23,-3);ctx.fill();
    ctx.fillStyle='#312238';ctx.beginPath();ctx.arc(-8,3,1.8,0,7);ctx.arc(8,3,1.8,0,7);ctx.fill();ctx.strokeStyle='#9d6170';ctx.lineWidth=1.4;ctx.beginPath();ctx.arc(0,7,6,.25,Math.PI-.25);ctx.stroke();
    ctx.fillStyle='#8f678c';ctx.beginPath();ctx.roundRect(-32,25,15,8,4);ctx.roundRect(17,25,15,8,4);ctx.fill();
    ctx.fillStyle='#eadce9';ctx.font='600 9px system-ui';ctx.textAlign='center';ctx.fillText('VINI',0,39);ctx.restore();
  }
  function drawHeart(h) { const colors=['#f35478','#ff81a7','#ffb0c9','#ffd064'];ctx.save();ctx.shadowColor=colors[h.type];ctx.shadowBlur=h.type>1?14:7;heartPath(ctx,h.x,h.y,h.size,h.rot);const g=ctx.createLinearGradient(h.x-h.size,h.y-h.size,h.x+h.size,h.y+h.size);g.addColorStop(0,h.type===3?'#fff0aa':'#ff9ab4');g.addColorStop(1,colors[h.type]);ctx.fillStyle=g;ctx.fill();if(h.type>1){ctx.fillStyle='#fff';ctx.globalAlpha=.8;ctx.beginPath();ctx.arc(h.x-h.size*.16,h.y-h.size*.25,1.8,0,7);ctx.fill()}ctx.restore(); }
  function drawBasket() {
    const fill=Math.min(total,120)/120, squash=basket.squash;ctx.save();ctx.translate(basket.x,basket.y);ctx.scale(1+squash*.08,1-squash*.12);
    const count=Math.min(18,Math.floor(total/6)); for(let i=0;i<count;i++){const row=Math.floor(i/7),col=i%7;const px=(col-3)*13+(row%2)*5,py=-24-row*12;heartPath(ctx,px,py,15+(i%3),((i%4)-2)*.08);ctx.fillStyle=i%5===0?'#ff9bb5':'#ed587e';ctx.shadowColor='#ff6388';ctx.shadowBlur=5;ctx.fill()}
    if(fill>.82){const overflow=Math.min(8,Math.floor((total-98)/4));for(let i=0;i<overflow;i++){heartPath(ctx,(i-3.5)*14,-48-(i%2)*5,16,(i-3)*.08);ctx.fillStyle=i%3?'#f75b80':'#ffd06c';ctx.fill()}}
    ctx.shadowColor='#000';ctx.shadowBlur=14;const grad=ctx.createLinearGradient(0,-20,0,28);grad.addColorStop(0,'#c58b57');grad.addColorStop(1,'#70452f');ctx.fillStyle=grad;ctx.beginPath();ctx.moveTo(-basket.w/2,-18);ctx.quadraticCurveTo(-basket.w*.43,25,-basket.w*.31,30);ctx.quadraticCurveTo(0,38,basket.w*.31,30);ctx.quadraticCurveTo(basket.w*.43,25,basket.w/2,-18);ctx.closePath();ctx.fill();
    ctx.shadowBlur=0;ctx.strokeStyle='#f3c47b';ctx.lineWidth=4;ctx.beginPath();ctx.moveTo(-basket.w/2,-18);ctx.quadraticCurveTo(0,-10,basket.w/2,-18);ctx.stroke();ctx.globalAlpha=.28;ctx.lineWidth=1;for(let i=-40;i<=40;i+=16){ctx.beginPath();ctx.moveTo(i,-11);ctx.lineTo(i*.75,27);ctx.stroke()}ctx.globalAlpha=1;ctx.fillStyle='#ffe6bc';ctx.font='600 10px Georgia';ctx.textAlign='center';ctx.letterSpacing='2px';ctx.fillText('LINE  ♡',0,15);ctx.restore();
  }
  function drawEffects(){particles.forEach(p=>{ctx.globalAlpha=Math.max(0,p.life/p.max);ctx.fillStyle=p.color;ctx.beginPath();ctx.arc(p.x,p.y,p.r,0,7);ctx.fill()});ripples.forEach(r=>{ctx.globalAlpha=r.life/.35;ctx.strokeStyle=r.color;ctx.lineWidth=1.5;ctx.beginPath();ctx.arc(r.x,r.y,(1-r.life/.35)*32,0,7);ctx.stroke()});ctx.globalAlpha=1}
  function roundRect(x,y,w,h,r){ctx.beginPath();ctx.roundRect(x,y,w,h,r)}
  function loop(now){if(!running)return;if(paused){last=now;requestAnimationFrame(loop);return}const dt=Math.min(.034,(now-last)/1000||0);last=now;update(dt);draw(now);requestAnimationFrame(loop)}
  function setTarget(clientX){const rect=canvas.getBoundingClientRect();targetX=Math.max(basket.w/2+10,Math.min(W-basket.w/2-10,clientX-rect.left));ripples.push({x:targetX,y:basket.y+32,life:.22,color:'#ffffff55'})}
  canvas.addEventListener('pointerdown',e=>{if(!running)return;canvas.setPointerCapture?.(e.pointerId);setTarget(e.clientX)});canvas.addEventListener('pointermove',e=>{if(running&&e.buttons)setTarget(e.clientX)});canvas.addEventListener('contextmenu',e=>e.preventDefault());
  function initAudio(){if(audio)return;audio=new (window.AudioContext||window.webkitAudioContext)()}
  function tone(type){if(!audioOn)return;initAudio();const now=audio.currentTime,o=audio.createOscillator(),gain=audio.createGain();o.type='sine';o.frequency.setValueAtTime(type>1?660:520,now);o.frequency.exponentialRampToValueAtTime(type>1?990:720,now+.09);gain.gain.setValueAtTime(.0001,now);gain.gain.exponentialRampToValueAtTime(.055,now+.012);gain.gain.exponentialRampToValueAtTime(.0001,now+.19);o.connect(gain).connect(audio.destination);o.start(now);o.stop(now+.2)}
  soundButton.addEventListener('click',()=>{audioOn=!audioOn;safeSet(STORAGE.sound,audioOn?'1':'0');game.classList.toggle('audio-on',audioOn);soundButton.setAttribute('aria-pressed',audioOn);soundButton.setAttribute('aria-label',audioOn?'Desativar som':'Ativar som');if(audioOn){initAudio();audio.resume();tone(1)}});
  startButton.addEventListener('click',()=>{safeSet(STORAGE.seen,'1');intro.classList.add('out');game.classList.add('playing');running=true;last=performance.now();if(audioOn){initAudio();audio.resume()}requestAnimationFrame(loop)});
  document.addEventListener('visibilitychange',()=>{paused=document.hidden;if(!paused)last=performance.now()});window.addEventListener('blur',()=>paused=true);window.addEventListener('focus',()=>{paused=false;last=performance.now()});window.addEventListener('resize',resize,{passive:true});
  if('serviceWorker'in navigator&&location.protocol!=='file:')window.addEventListener('load',()=>navigator.serviceWorker.register('./service-worker.js').catch(()=>{}));
  resize();
})();
