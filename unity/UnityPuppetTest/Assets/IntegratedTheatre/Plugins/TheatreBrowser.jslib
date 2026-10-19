mergeInto(LibraryManager.library, {
  TheatreStatus: function (json) {
    if (window.onTheatreStatus) window.onTheatreStatus(JSON.parse(UTF8ToString(json)));
  }
});
