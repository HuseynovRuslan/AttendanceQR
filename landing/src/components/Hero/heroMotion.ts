// Hero hərəkəti (Hero.astro-dan çağırılır): giriş, siçan izləmə, skan ardıcıllığı.
// Skript yüklənməsə, hero tam görünür qalır; gizli başlanğıc yalnız burada qoyulur.
import { animate, inView, motionValue, stagger } from "motion";

function playEntrance(hero: HTMLElement) {
  const texts = hero.querySelectorAll("[data-hero-in]");
  const scene = hero.querySelector("[data-hero-scene]");

  animate(
    texts,
    { opacity: [0, 1], y: [18, 0] },
    { duration: 0.6, ease: "easeOut", delay: stagger(0.09) },
  );

  if (scene) {
    animate(
      scene,
      { opacity: [0, 1], scale: [0.96, 1] },
      { duration: 0.8, ease: "easeOut", delay: 0.35 },
    );
  }
}

// Qatın dərinliyi: shift = ən çox piksel sürüşmə, tilt = ən çox dərəcə əyilmə (kart ən yaxın, poster ən uzaq).
const LAYERS = {
  poster: { shift: 6, tilt: 6 },
  phone: { shift: 9, tilt: 4 },
  card: { shift: 12, tilt: 0 },
} as const;

// Siçan yoxkən qatlar bu nöqtələrə (-1…1) yavaş sürüşür.
const DRIFT: [number, number][] = [
  [0.3, -0.2],
  [-0.25, 0.15],
  [0.2, 0.25],
  [-0.3, -0.1],
];
const DRIFT_STEP_SECONDS = 5;

const SPRING = { type: "spring", stiffness: 70, damping: 16 } as const;

type Layer = { el: HTMLElement; shift: number; tilt: number };

function setupPointer(hero: HTMLElement) {
  const layers: Layer[] = [];
  hero.querySelectorAll<HTMLElement>("[data-hero-layer]").forEach((el) => {
    const cfg = LAYERS[el.dataset.heroLayer as keyof typeof LAYERS];
    if (cfg) layers.push({ el, ...cfg });
  });

  // px, py — siçanın vəziyyəti, -1 (sol/yuxarı) ilə 1 (sağ/aşağı) arası.
  const px = motionValue(0);
  const py = motionValue(0);

  let running = false; // pointer sistemi (media şərti) açıqdır?
  let visible = false; // hero ekranda görünür?
  let inside = false; // siçan hero-nun üstündədir?
  let driftIndex = 0;
  let driftAnims: { pause(): void; play(): void; stop(): void }[] = [];
  let idleTimer = 0;
  let queued = false;

  // `transform` yazılır: kartlarda üzmə `translate`, hover isə `scale` xassəsindədir — bu ikisi toqquşmur.
  function render() {
    queued = false;
    const x = px.get();
    const y = py.get();
    for (const { el, shift, tilt } of layers) {
      const move = `translate3d(${(x * shift).toFixed(2)}px, ${(y * shift).toFixed(2)}px, 0)`;
      // Siçan sağdadırsa üz sağa dönür (rotateY +), aşağıdadırsa aşağı baxır (rotateX −).
      el.style.transform = tilt
        ? `perspective(900px) ${move} rotateX(${(-y * tilt).toFixed(2)}deg) rotateY(${(x * tilt).toFixed(2)}deg)`
        : move;
    }
  }

  // px və py ardıcıl dəyişir, ona görə növbəti kadrda bir dəfə çəkilir.
  function queueRender() {
    if (queued) return;
    queued = true;
    requestAnimationFrame(render);
  }
  px.on("change", queueRender);
  py.on("change", queueRender);

  function stopDrift() {
    clearTimeout(idleTimer);
    driftAnims.forEach((a) => a.stop());
    driftAnims = [];
  }

  function driftStep() {
    const [tx, ty] = DRIFT[driftIndex % DRIFT.length];
    driftIndex++;
    const opts = { duration: DRIFT_STEP_SECONDS, ease: "easeInOut" } as const;
    driftAnims = [
      animate(px, tx, { ...opts, onComplete: driftStep }),
      animate(py, ty, opts),
    ];
  }

  // Sürüşmə yalnız üçü birlikdə doğrudursa işləyir: sistem açıq, hero görünür, tab açıq.
  function sync() {
    const active = running && visible && !document.hidden;
    if (!active) {
      driftAnims.forEach((a) => a.pause());
      return;
    }
    if (inside) return;
    if (driftAnims.length) driftAnims.forEach((a) => a.play());
    else driftStep();
  }

  function onMove(e: PointerEvent) {
    const r = hero.getBoundingClientRect();
    const nx = ((e.clientX - r.left) / r.width) * 2 - 1;
    const ny = ((e.clientY - r.top) / r.height) * 2 - 1;
    inside = true;
    stopDrift();
    animate(px, Math.max(-1, Math.min(1, nx)), SPRING);
    animate(py, Math.max(-1, Math.min(1, ny)), SPRING);
  }

  function onLeave() {
    inside = false;
    animate(px, 0, SPRING);
    animate(py, 0, SPRING);
    clearTimeout(idleTimer);
    idleTimer = window.setTimeout(sync, 1500);
  }

  function start() {
    running = true;
    hero.addEventListener("pointermove", onMove);
    hero.addEventListener("pointerleave", onLeave);
    sync();
  }

  function stop() {
    running = false;
    hero.removeEventListener("pointermove", onMove);
    hero.removeEventListener("pointerleave", onLeave);
    stopDrift();
    px.set(0);
    py.set(0);
    layers.forEach(({ el }) => (el.style.transform = ""));
  }

  // Yalnız böyük ekran və həqiqi siçan; toxunma cihazlarda və ≤860px-də açılmır.
  const canFollow = matchMedia(
    "(hover: hover) and (pointer: fine) and (min-width: 861px)",
  );
  const apply = () => (canFollow.matches ? start() : stop());
  canFollow.addEventListener("change", apply);
  apply();

  inView(hero, () => {
    visible = true;
    sync();
    return () => {
      visible = false;
      sync();
    };
  });
  document.addEventListener("visibilitychange", sync);
}

// Skan ardıcıllığı: poster skan olunur → giriş qeydə alınır → "canlı" kart yenilənir; ~8 saniyədən bir təkrarlanır.

const SCAN_FIRST_DELAY_MS = 1800; // giriş animasiyası bitsin, sonra başlasın
const SCAN_EVERY_MS = 8000;

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

function setupScan(hero: HTMLElement) {
  const qr = hero.querySelector<HTMLElement>("[data-hero-qr]");
  const line = hero.querySelector("[data-hero-scan-line]");
  const glow = hero.querySelector("[data-hero-scan-glow]");
  const scanned = hero.querySelector("[data-hero-scanned]");
  const check = hero.querySelector("[data-hero-check]");
  const pills = hero.querySelectorAll("[data-hero-pill]");
  const avatars = hero.querySelectorAll("[data-hero-avatar]");
  if (!qr || !line || !glow || !scanned || !check) return;

  let visible = false;
  let wake: (() => void) | null = null;
  const canRun = () => visible && !document.hidden;

  // Hero görünmürsə və ya tab gizlidirsə, ardıcıllıq burada gözləyir.
  function untilActive() {
    if (canRun()) return Promise.resolve();
    return new Promise<void>((resolve) => (wake = resolve));
  }
  function resume() {
    if (canRun() && wake) {
      wake();
      wake = null;
    }
  }
  inView(hero, () => {
    visible = true;
    resume();
    return () => {
      visible = false;
    };
  });
  document.addEventListener("visibilitychange", resume);

  // Gözləmə yalnız hero aktiv olanda sayılır (250 ms addımlarla).
  async function activeSleep(ms: number) {
    for (let waited = 0; waited < ms; waited += 250) {
      await untilActive();
      await sleep(250);
    }
  }

  async function playOnce() {
    // 1) Nəticələr "gözləmə" vəziyyətinə düşür: hələ heç nə qeydə alınmayıb.
    await untilActive();
    await Promise.all([
      animate(check, { opacity: 0.2, scale: 0.5 }, { duration: 0.25 }),
      animate(pills, { opacity: 0.3, y: 4 }, { duration: 0.25 }),
      animate(avatars, { opacity: 0.2, scale: 0.8 }, { duration: 0.25 }),
    ]);

    // 2) Mavi xətt QR-ın üstündən enir.
    await untilActive();
    const h = qr!.clientHeight;
    await animate(
      line!,
      { y: [0, 0, h, h], opacity: [0, 1, 1, 0] },
      { duration: 1.3, ease: "easeInOut", times: [0, 0.1, 0.9, 1] },
    );

    // 3) Poster işıqlanır, skan kartındakı dairə "cavab verir".
    animate(glow!, { opacity: [0, 1, 0] }, { duration: 0.8 });
    animate(scanned!, { scale: [1, 1.15, 1] }, { duration: 0.5 });
    await sleep(250);

    // 4) Giriş qeydə alınır: yaşıl dairə yay kimi çıxır, dörd yoxlama ardıcıl yanır.
    await untilActive();
    animate(check, { opacity: 1, scale: 1 }, { type: "spring", stiffness: 300, damping: 14 });
    await animate(
      pills,
      { opacity: 1, y: 0 },
      { duration: 0.4, delay: stagger(0.08, { startDelay: 0.15 }) },
    );

    // 5) "Canlı" kart: işdə olanların dairələri ardıcıl dolur.
    await untilActive();
    await animate(
      avatars,
      { opacity: 1, scale: 1 },
      { type: "spring", stiffness: 300, damping: 16, delay: stagger(0.08) },
    );
  }

  async function loop() {
    await activeSleep(SCAN_FIRST_DELAY_MS);
    while (true) {
      const startedAt = performance.now();
      await playOnce();
      await activeSleep(Math.max(1000, SCAN_EVERY_MS - (performance.now() - startedAt)));
    }
  }
  loop();
}

export function initHeroMotion() {
  const hero = document.querySelector<HTMLElement>("[data-hero]");
  if (!hero) return;

  // "Hərəkəti azalt" seçilibsə heç nə oynamır, hər şey son vəziyyətdə qalır.
  if (matchMedia("(prefers-reduced-motion: reduce)").matches) return;

  playEntrance(hero);
  setupPointer(hero);
  setupScan(hero);
}
