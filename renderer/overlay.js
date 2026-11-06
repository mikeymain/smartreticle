// Draws the crosshair at an exact device pixel. The canvas is sized in device pixels so nothing is resampled.
const canvas = document.getElementById('c');
const ctx = canvas.getContext('2d');
let crosshair = null;
let aim = null;

function render() {
  const dpr = window.devicePixelRatio || 1;
  const w = Math.round(window.innerWidth * dpr), h = Math.round(window.innerHeight * dpr);
  if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; }
  ctx.clearRect(0, 0, w, h);
  if (!crosshair || !aim) return;
  canvas.style.opacity = String(crosshair.opacity / 100);
  ctx.imageSmoothingEnabled = false;
  window.Crosshair.draw(ctx, crosshair, aim.x, aim.y);
}

window.overlayBridge.onCrosshair((c) => { crosshair = c; render(); });
window.overlayBridge.onPlace((p) => { aim = p; render(); });
window.overlayBridge.onVisible((v) => canvas.classList.toggle('hidden', !v));
window.addEventListener('resize', render);
