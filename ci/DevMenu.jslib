mergeInto(LibraryManager.library, {
  // size of the WebAssembly memory Unity is using, in MB (it grows but never shrinks)
  DevMenu_HeapMB: function () {
    return HEAPU8.length / 1048576;
  },
  // JavaScript heap in use, in MB (Chrome only, 0 elsewhere)
  DevMenu_JSHeapMB: function () {
    if (typeof performance !== 'undefined' && performance.memory) {
      return performance.memory.usedJSHeapSize / 1048576;
    }
    return 0;
  },

  // size of the game canvas as shown on the page, in CSS pixels
  DevMenu_CanvasW: function () {
    var c = Module.canvas;
    return c ? c.clientWidth : 0;
  },
  DevMenu_CanvasH: function () {
    var c = Module.canvas;
    return c ? c.clientHeight : 0;
  },
  // retro mode: enlarge the (small) drawing buffer with hard pixel edges and keep the on-page size fixed
  DevMenu_SetRetro: function (on, cssW, cssH) {
    var c = Module.canvas;
    if (!c) { return; }
    if (on) {
      if (c.__devRetroSaved === undefined) {
        c.__devRetroSaved = { w: c.style.width, h: c.style.height };
      }
      c.style.imageRendering = 'pixelated';
      if (cssW > 0 && cssH > 0) {
        c.style.width = cssW + 'px';
        c.style.height = cssH + 'px';
      }
    } else {
      c.style.imageRendering = '';
      if (c.__devRetroSaved !== undefined) {
        c.style.width = c.__devRetroSaved.w;
        c.style.height = c.__devRetroSaved.h;
        c.__devRetroSaved = undefined;
      }
    }
  }
});
