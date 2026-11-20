mergeInto(LibraryManager.library, {
  // Copy text to the browser clipboard (WebGL cannot use GUIUtility.systemCopyBuffer).
  LookLab_Copy: function (ptr) {
    var text = UTF8ToString(ptr);
    try {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text);
        return;
      }
    } catch (e) {}
    try {
      var ta = document.createElement('textarea');
      ta.value = text;
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.focus();
      ta.select();
      document.execCommand('copy');
      document.body.removeChild(ta);
    } catch (e2) {}
  },
  // Download text as a file (works even where the clipboard is blocked).
  LookLab_Download: function (namePtr, textPtr) {
    var name = UTF8ToString(namePtr);
    var text = UTF8ToString(textPtr);
    var blob = new Blob([text], { type: 'text/plain' });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = name;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(function () { URL.revokeObjectURL(url); }, 2000);
  }
});
