// Rostr site script. One file for both designs.
//
// index.html has no design of its own: this script sends the visitor to windows.html or linux.html.
// windows.html and linux.html load the same script to wire up the download button.
// A visitor who clicks a "Windows" or "Linux" link is remembered, so they can override the detection.
(function () {
  var PLATFORMS = {
    windows: {
      name: "Windows", page: "windows.html",
      note: "Windows 10 and 11",
      // url: where the download button goes. Leave it empty and the button does nothing.
      url: "https://github.com/stepak2321/rostr.trebka.net/releases/latest/download/Rostr.exe"
    },
    linux: {
      name: "Linux", page: "linux.html",
      note: "Linux version coming soon",
      readyNote: "64-bit Linux",
      // No Linux build yet. Put the release file link here when it exists: the button starts working.
      url: ""
    }
  };

  function detect() {
    var ua = navigator.userAgent || "";
    var hint = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || "";
    var all = hint + " " + ua;
    // Android and ChromeOS report Linux in their user agent, but they are not desktop Linux.
    if (/android|cros/i.test(all)) return "windows";
    if (/linux|x11/i.test(all)) return "linux";
    return "windows";
  }

  function saved() {
    try { var v = localStorage.getItem("rostr-os"); return PLATFORMS[v] ? v : null; } catch (e) { return null; }
  }
  function save(os) {
    try { localStorage.setItem("rostr-os", os); } catch (e) {}
  }

  var page = document.documentElement.getAttribute("data-os");

  // index.html: pick a design and go there. ?os=linux or ?os=windows forces one.
  if (!page) {
    var q = /[?&]os=(windows|linux)\b/.exec(location.search);
    var chosen = q ? q[1] : (saved() || detect());
    location.replace(PLATFORMS[chosen].page + location.hash);
    return;
  }

  // windows.html / linux.html
  var os = PLATFORMS[page];
  if (!os) return;

  // The button only does something once the platform has a download url.
  var buttons = document.querySelectorAll("[data-download]");
  for (var i = 0; i < buttons.length; i++) {
    if (os.url) buttons[i].addEventListener("click", function () { window.location.href = os.url; });
  }

  var note = document.getElementById("os-note");
  if (note) note.textContent = os.url && os.readyNote ? os.readyNote : os.note;

  var links = document.querySelectorAll("[data-os]");
  for (var j = 0; j < links.length; j++) {
    links[j].addEventListener("click", function (e) { save(e.currentTarget.getAttribute("data-os")); });
  }
})();
