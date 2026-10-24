import { gsap, ScrollTrigger } from "../../scripts/motion/gsap";
import {
  CLEARED_PROPS,
  hasMotion,
  isLoaderGone,
  pageRevealed,
  revertOnPageSwap,
  whenTitleFontReady,
} from "../../scripts/motion/shared";
import { splitIntoLines } from "../../scripts/motion/splitLines";

// ---------- Intro timing (seconds after the loader leaves) ----------
const TITLE_START = 0.2;
const TITLE_SHIFT_PERCENT = 105;
const TITLE_DURATION = 1.1;
const TITLE_STAGGER = 0.12;

const SCENE_START = 0.35;
const SCENE_SHIFT_X = 140;
const SCENE_TURN_Y = -14;
const SCENE_PERSPECTIVE = 1200;
const SCENE_DURATION = 1.3;
const SCENE_TURN_MEDIA = "(min-width: 768px)";

const TEXT_START = 0.55;
const TEXT_SHIFT_X = -48;
const TEXT_DURATION = 0.9;
const TEXT_STAGGER = 0.1;

const CARDS_START = 1;
const CARDS_SCALE = 0.82;
const CARDS_DURATION = 0.7;
const CARDS_STAGGER = 0.12;
const CARDS_EASE = "back.out(1.6)";

// ---------- Pointer parallax: depth of each layer (shift in px, tilt in degrees) ----------
const LAYERS = {
  poster: { shift: 6, tilt: 6 },
  phone: { shift: 9, tilt: 4 },
  card: { shift: 12, tilt: 0 },
} as const;

const FOLLOW_DURATION = 0.35;
const FOLLOW_EASE = "power3.out";
const LAYER_PERSPECTIVE = 900;
const POINTER_MEDIA = "(hover: hover) and (pointer: fine) and (min-width: 861px)";
const IDLE_RESTART_SECONDS = 1.5;

// ---------- Idle drift: pointer-like points from -1 to 1 ----------
const DRIFT_POINTS: [number, number][] = [
  [0.3, -0.2],
  [-0.25, 0.15],
  [0.2, 0.25],
  [-0.3, -0.1],
];
const DRIFT_STEP_SECONDS = 5;

// ---------- Scan sequence ----------
const SCAN_FIRST_DELAY = 0.4;
const SCAN_NO_INTRO_DELAY = 1.8;
const SCAN_EVERY_SECONDS = 8;

// ---------- Intro: scene, texts and floating cards ----------
function sceneStartState(): gsap.TweenVars {
  const startState: gsap.TweenVars = { opacity: 0, x: SCENE_SHIFT_X };
  if (window.matchMedia(SCENE_TURN_MEDIA).matches) {
    startState.rotationY = SCENE_TURN_Y;
    startState.transformPerspective = SCENE_PERSPECTIVE;
  }
  return startState;
}

function addScene(intro: gsap.core.Timeline, hero: HTMLElement): void {
  const scene = hero.querySelector<HTMLElement>("[data-hero-scene]");
  if (!scene) return;
  intro.fromTo(
    scene,
    sceneStartState(),
    { opacity: 1, x: 0, rotationY: 0, duration: SCENE_DURATION, ease: "expo.out", clearProps: CLEARED_PROPS },
    SCENE_START
  );
}

function addTexts(intro: gsap.core.Timeline, hero: HTMLElement): void {
  const texts = hero.querySelectorAll<HTMLElement>("[data-hero-in]");
  intro.fromTo(
    texts,
    { opacity: 0, x: TEXT_SHIFT_X, transition: "none" },
    { opacity: 1, x: 0, duration: TEXT_DURATION, ease: "expo.out", stagger: TEXT_STAGGER, clearProps: CLEARED_PROPS },
    TEXT_START
  );
}

function addCards(intro: gsap.core.Timeline, hero: HTMLElement): void {
  const cards = hero.querySelectorAll<HTMLElement>('[data-hero-layer="card"]');
  intro.fromTo(
    cards,
    { opacity: 0, scale: CARDS_SCALE },
    {
      opacity: 1,
      scale: 1,
      duration: CARDS_DURATION,
      ease: CARDS_EASE,
      stagger: CARDS_STAGGER,
      clearProps: "opacity",
    },
    CARDS_START
  );
}

// ---------- Intro: title lines (split only while the loader still covers the page; rebuilt by SplitText on resize) ----------
function addTitle(intro: gsap.core.Timeline, hero: HTMLElement): void {
  const title = hero.querySelector<HTMLElement>("[data-hero-title]");
  if (!title) return;
  let titleTween: gsap.core.Tween | undefined;
  let isTitleDue = false;

  const playTitle = () => {
    isTitleDue = true;
    titleTween?.play();
  };
  intro.call(playTitle, [], TITLE_START);

  const buildTitleTween = (lines: HTMLElement[]) => {
    titleTween = gsap.fromTo(
      lines,
      { yPercent: TITLE_SHIFT_PERCENT },
      { yPercent: 0, duration: TITLE_DURATION, ease: "expo.out", stagger: TITLE_STAGGER, paused: !isTitleDue }
    );
    return titleTween;
  };

  whenTitleFontReady().then(() => {
    if (isLoaderGone()) return;
    splitIntoLines(title, buildTitleTween, "[data-hero-scan]");
  });
}

// ---------- Intro: build under the loader, play when it leaves ----------
function playIntro(hero: HTMLElement, onComplete: () => void): void {
  const intro = gsap.timeline({ paused: true, onComplete });
  addScene(intro, hero);
  addTexts(intro, hero);
  addCards(intro, hero);
  addTitle(intro, hero);
  pageRevealed.then(() => intro.play());
}

// ---------- Pointer parallax: layers ----------
interface Layer {
  shift: number;
  tilt: number;
  moveX: gsap.QuickToFunc;
  moveY: gsap.QuickToFunc;
  turnX: gsap.QuickToFunc;
  turnY: gsap.QuickToFunc;
}

function createLayers(hero: HTMLElement): Layer[] {
  const layers: Layer[] = [];
  const follow = { duration: FOLLOW_DURATION, ease: FOLLOW_EASE };
  hero.querySelectorAll<HTMLElement>("[data-hero-layer]").forEach((element) => {
    const depth = LAYERS[element.dataset.heroLayer as keyof typeof LAYERS];
    if (!depth) return;
    if (depth.tilt) {
      gsap.set(element, { transformPerspective: LAYER_PERSPECTIVE });
    }
    layers.push({
      shift: depth.shift,
      tilt: depth.tilt,
      moveX: gsap.quickTo(element, "x", follow),
      moveY: gsap.quickTo(element, "y", follow),
      turnX: gsap.quickTo(element, "rotationX", follow),
      turnY: gsap.quickTo(element, "rotationY", follow),
    });
  });
  return layers;
}

// ---------- Pointer parallax: pointerX / pointerY from -1 (left, top) to 1 (right, bottom) ----------
function moveLayers(layers: Layer[], pointerX: number, pointerY: number): void {
  for (const layer of layers) {
    layer.moveX(pointerX * layer.shift);
    layer.moveY(pointerY * layer.shift);
    if (layer.tilt) {
      layer.turnX(-pointerY * layer.tilt);
      layer.turnY(pointerX * layer.tilt);
    }
  }
}

function pointerPosition(hero: HTMLElement, event: PointerEvent): [number, number] {
  const box = hero.getBoundingClientRect();
  const x = ((event.clientX - box.left) / box.width) * 2 - 1;
  const y = ((event.clientY - box.top) / box.height) * 2 - 1;
  return [gsap.utils.clamp(-1, 1, x), gsap.utils.clamp(-1, 1, y)];
}

// ---------- Pointer parallax: idle drift ----------
interface MotionControls {
  update: () => void;
  stop: () => void;
}

function createDrift(layers: Layer[], canDrift: () => boolean): MotionControls {
  const pointer = { x: 0, y: 0 };
  let pointIndex = 0;
  let tween: gsap.core.Tween | undefined;

  const driftToNextPoint = () => {
    const [x, y] = DRIFT_POINTS[pointIndex % DRIFT_POINTS.length];
    pointIndex += 1;
    tween = gsap.to(pointer, {
      x,
      y,
      duration: DRIFT_STEP_SECONDS,
      ease: "sine.inOut",
      onUpdate: () => moveLayers(layers, pointer.x, pointer.y),
      onComplete: driftToNextPoint,
    });
  };

  const update = () => {
    if (!canDrift()) {
      tween?.pause();
    } else if (tween) {
      tween.resume();
    } else {
      driftToNextPoint();
    }
  };

  const stop = () => {
    tween?.kill();
    tween = undefined;
    pointer.x = 0;
    pointer.y = 0;
  };

  return { update, stop };
}

// ---------- Pointer parallax: follow the mouse, drift when it is away ----------
function startPointerParallax(hero: HTMLElement, isHeroActive: () => boolean): MotionControls {
  const layers = createLayers(hero);
  let isPointerInside = false;
  let idleCall: gsap.core.Tween | undefined;
  const drift = createDrift(layers, () => !isPointerInside && isHeroActive());

  const onPointerMove = (event: PointerEvent) => {
    isPointerInside = true;
    idleCall?.kill();
    drift.stop();
    const [x, y] = pointerPosition(hero, event);
    moveLayers(layers, x, y);
  };

  const onPointerLeave = () => {
    isPointerInside = false;
    moveLayers(layers, 0, 0);
    idleCall?.kill();
    idleCall = gsap.delayedCall(IDLE_RESTART_SECONDS, drift.update);
  };

  hero.addEventListener("pointermove", onPointerMove);
  hero.addEventListener("pointerleave", onPointerLeave);
  drift.update();

  const stop = () => {
    hero.removeEventListener("pointermove", onPointerMove);
    hero.removeEventListener("pointerleave", onPointerLeave);
    idleCall?.kill();
    drift.stop();
  };
  return { update: drift.update, stop };
}

// ---------- Pointer parallax: only with a real mouse on wide screens ----------
function watchPointer(hero: HTMLElement): { setActive: (isActive: boolean) => void } {
  let isHeroActive = false;
  let parallax: MotionControls | undefined;

  gsap.matchMedia().add(POINTER_MEDIA, () => {
    parallax = startPointerParallax(hero, () => isHeroActive);
    return () => {
      parallax?.stop();
      parallax = undefined;
    };
  });

  const setActive = (isActive: boolean) => {
    isHeroActive = isActive;
    parallax?.update();
  };
  return { setActive };
}

// ---------- Scan sequence: poster scanned → check-in recorded → live card fills up ----------
interface ScanParts {
  qr: HTMLElement;
  line: HTMLElement;
  glow: HTMLElement;
  scanned: HTMLElement;
  check: HTMLElement;
  pills: HTMLElement[];
  avatars: HTMLElement[];
}

function findScanParts(hero: HTMLElement): ScanParts | null {
  const qr = hero.querySelector<HTMLElement>("[data-hero-qr]");
  const line = hero.querySelector<HTMLElement>("[data-hero-scan-line]");
  const glow = hero.querySelector<HTMLElement>("[data-hero-scan-glow]");
  const scanned = hero.querySelector<HTMLElement>("[data-hero-scanned]");
  const check = hero.querySelector<HTMLElement>("[data-hero-check]");
  if (!qr || !line || !glow || !scanned || !check) {
    return null;
  }
  const pills = Array.from(hero.querySelectorAll<HTMLElement>("[data-hero-pill]"));
  const avatars = Array.from(hero.querySelectorAll<HTMLElement>("[data-hero-avatar]"));
  return { qr, line, glow, scanned, check, pills, avatars };
}

function buildScan(parts: ScanParts): gsap.core.Timeline {
  const scan = gsap.timeline({ paused: true, repeat: -1, repeatRefresh: true });

  // ---------- 1) results go back to "waiting" ----------
  scan.to(parts.check, { opacity: 0.2, scale: 0.5, duration: 0.25 }, 0);
  scan.to(parts.pills, { opacity: 0.3, y: 4, duration: 0.25 }, 0);
  scan.to(parts.avatars, { opacity: 0.2, scale: 0.8, duration: 0.25 }, 0);

  // ---------- 2) the blue line sweeps over the QR ----------
  scan.set(parts.line, { y: 0, opacity: 0 });
  scan.to(parts.line, { opacity: 1, duration: 0.13 });
  scan.to(parts.line, { y: () => parts.qr.clientHeight, duration: 1.04, ease: "power1.inOut" });
  scan.to(parts.line, { opacity: 0, duration: 0.13 });

  // ---------- 3) the poster glows, the scan card answers ----------
  scan.addLabel("scanned");
  scan.to(parts.glow, { opacity: 1, duration: 0.4, yoyo: true, repeat: 1 }, "scanned");
  scan.to(parts.scanned, { scale: 1.15, duration: 0.25, yoyo: true, repeat: 1 }, "scanned");

  // ---------- 4) check-in recorded, the four checks light up ----------
  scan.to(parts.check, { opacity: 1, scale: 1, duration: 0.5, ease: "back.out(2.2)" }, "scanned+=0.25");
  scan.to(parts.pills, { opacity: 1, y: 0, duration: 0.4, stagger: 0.08 }, "scanned+=0.4");

  // ---------- 5) live card: people at work fill in ----------
  scan.to(parts.avatars, { opacity: 1, scale: 1, duration: 0.45, ease: "back.out(2)", stagger: 0.08 }, ">");

  scan.repeatDelay(Math.max(1, SCAN_EVERY_SECONDS - scan.duration()));
  return scan;
}

// ---------- Scan sequence: runs only while the hero is on screen ----------
function createScanLoop(hero: HTMLElement): { start: () => void; setActive: (isActive: boolean) => void } {
  const parts = findScanParts(hero);
  const scan = parts ? buildScan(parts) : undefined;
  let isStarted = false;
  let isHeroActive = false;

  const update = () => {
    if (!scan || !isStarted) return;
    if (isHeroActive) {
      scan.resume();
    } else {
      scan.pause();
    }
  };

  const start = () => {
    isStarted = true;
    update();
  };

  const setActive = (isActive: boolean) => {
    isHeroActive = isActive;
    update();
  };
  return { start, setActive };
}

// ---------- On screen and tab visible ----------
function watchHeroActivity(hero: HTMLElement, onChange: (isActive: boolean) => void): void {
  let isOnScreen = false;
  const notify = () => onChange(isOnScreen && !document.hidden);

  ScrollTrigger.create({
    trigger: hero,
    start: "top bottom",
    end: "bottom top",
    onToggle: (self) => {
      isOnScreen = self.isActive;
      notify();
    },
  });
  document.addEventListener("visibilitychange", notify);
}

// ---------- Start ----------
export function initHeroMotion(): void {
  const hero = document.querySelector<HTMLElement>("[data-hero]");
  if (!hero) return;

  const context = gsap.context(() => {
    const scanLoop = createScanLoop(hero);
    if (hasMotion()) {
      playIntro(hero, () => gsap.delayedCall(SCAN_FIRST_DELAY, scanLoop.start));
    } else {
      pageRevealed.then(() => gsap.delayedCall(SCAN_NO_INTRO_DELAY, scanLoop.start));
    }

    const pointer = watchPointer(hero);
    watchHeroActivity(hero, (isActive) => {
      scanLoop.setActive(isActive);
      pointer.setActive(isActive);
    });
  });
  revertOnPageSwap(context);
}
