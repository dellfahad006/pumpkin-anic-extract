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
  }
});
